#if UNITY_EDITOR
using System.IO;
using System.Diagnostics;
using System.Collections.Generic;
using UnityEditor.Animations;
using UnityEngine;
using UnityEditor;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;
using ABI.CCK.Components;

namespace Beckadam.C2AnimCVR
{

    public class AnimatorCompiler : MonoBehaviour
    {
        [CustomEditor(typeof(AnimatorCompiler))]
        public class CompilerGUI : Editor
        {
            public override void OnInspectorGUI()
            {
                AnimatorCompiler compiler = (AnimatorCompiler)target;
                GUILayout.Label("Note: this will destroy the targeted animator controller!");
                if (GUILayout.Button("Build"))
                {
                    compiler.Build();
                }
                base.OnInspectorGUI();
            }
        }

        public class Variable {
            public AnimatorDriverTask.ParameterType type;
            public string name;
            public bool isConst=false;
            public float val = 0.0f;
            public AnimatorControllerParameterType AnimatorParameterType()
            {
                switch (type)
                {
                    case AnimatorDriverTask.ParameterType.Float:
                        return AnimatorControllerParameterType.Float;
                    case AnimatorDriverTask.ParameterType.Int:
                        return AnimatorControllerParameterType.Int;
                    case AnimatorDriverTask.ParameterType.Bool:
                        return AnimatorControllerParameterType.Bool;
                    case AnimatorDriverTask.ParameterType.Trigger:
                        return AnimatorControllerParameterType.Trigger;
                    default:
                        break;
                }
                Debug.LogWarning($"Unknown type conversion from AnimatorDriverTask.ParameterType {type} to AnimatorControllerParameterType");
                return AnimatorControllerParameterType.Float;
            }
            public AnimatorDriverTask.SourceType SourceType()
            {
                if (isConst)
                {
                    return AnimatorDriverTask.SourceType.Static;
                }
                return AnimatorDriverTask.SourceType.Parameter;
            }
            public static Variable FromString(string s)
            {
                Variable vv = new();
                if (s.Length < 1)
                {
                    Debug.LogWarning("Encountered zero length variable string (check for double commas?) defaulting to float 0.0f");
                    vv.type = AnimatorDriverTask.ParameterType.Float;
                    vv.val = 0.0f;
                    vv.isConst = true;
                    return vv;
                }
                s = s.Trim(' ', '\t');
                if (s == "true")
                {
                    vv.type = AnimatorDriverTask.ParameterType.Bool;
                    vv.val = 1.0f;
                    vv.isConst = true;
                    return vv;
                }
                else if (s == "false")
                {
                    vv.type = AnimatorDriverTask.ParameterType.Bool;
                    vv.val = 0.0f;
                    vv.isConst = true;
                    return vv;
                }
                if (s.StartsWith("0x"))
                {
                    int num = 0;
                    foreach (char c in s[2..])
                    {
                        if (c >= '0' && c <= '9')
                        {
                            num = num * 16 + c - '0';
                        }
                        else if (c >= 'A' && c <= 'F')
                        {
                            num = num * 16 + c + 10 - 'A';
                        }
                        else if (c >= 'a' && c <= 'f')
                        {
                            num = num * 16 + c + 10 - 'a';
                        }
                    }
                    vv.isConst = true;
                    vv.val = num;
                    vv.type = AnimatorDriverTask.ParameterType.Int;
                    return vv;
                }
                if (s[0] >= '0' && s[0] <= '9' || s[0] == '-' || s[0] == '.')
                {
                    vv.isConst = true;
                    if (int.TryParse(s, out int v))
                    {
                        vv.val = v;
                        vv.type = AnimatorDriverTask.ParameterType.Int;
                    }
                    else
                    {
                        if (float.TryParse(s, out vv.val))
                        {
                            vv.type = AnimatorDriverTask.ParameterType.Float;
                        }
                        else
                        {
                            Debug.LogWarning($"Failed to parse number: {s} (defaulting to 0.0f)");
                            vv.type = AnimatorDriverTask.ParameterType.Float;
                            vv.val = 0.0f;
                        }
                    }
                    return vv;
                }
                if (s.StartsWith("f"))
                {
                    vv.type = AnimatorDriverTask.ParameterType.Float;
                }
                else if (s.StartsWith("i"))
                {
                    vv.type = AnimatorDriverTask.ParameterType.Int;
                }
                else if (s.StartsWith("b"))
                {
                    vv.type = AnimatorDriverTask.ParameterType.Bool;
                }
                else if (s.StartsWith("t"))
                {
                    vv.type = AnimatorDriverTask.ParameterType.Trigger;
                }
                else
                {
                    Debug.LogWarning($"Failed to Determine Variable Type: {s} (defaulting to float)");
                    vv.type = AnimatorDriverTask.ParameterType.Float;
                }
                vv.name = s[1..];
                return vv;
            }
        };

        public class BytecodeAnimatorTransition
        {
            public string dest;
            public string param;
            public bool inverted = false;
            public BytecodeAnimatorTransition(string dst, string v, bool inv=false)
            {
                dest = dst;
                param = v;
                inverted = inv;
            }
        }
        public class BytecodeAnimatorState
        {
            public string name;
            public AnimatorDriverTask[] tasks;
            public string after;
            public List<BytecodeAnimatorTransition> transitions;
            public BytecodeAnimatorState(string n)
            {
                name = n;
                transitions = new();
            }
        }

        public class BytecodeAnimatorLayer
        {
            public string name;
            public GameObject output = null;
			public string outputPath;
            public string outputParam;
            public BytecodeAnimatorState[] states;

            public BytecodeAnimatorLayer(string n)
            {
                name = n;
            }
        }

        public class BytecodeMacro
        {
            public string name;
            public string contents;
            public string[] args;
            public BytecodeMacro(string n, string[] a)
            {
                name = n;
                args = a;
            }
        };

        public TextAsset source;
        public TextAsset sourceAsm;
        public AnimatorController targetController;
        public Animator targetAnimator;
        public bool hideConsoleWindow = true;

        private float state_speed;
        private List<BytecodeAnimatorLayer> layers;
        private List<BytecodeAnimatorLayer> outputLayers;
        private List<BytecodeAnimatorState> states;
        private List<AnimatorDriverTask> tasks;
        private List<Variable> vars;
        private BytecodeAnimatorLayer currentLayer;
        private BytecodeAnimatorState currentState;
        private List<BytecodeMacro> macros;
        private int lineno;

        public void Build()
        {

            if (!AssetDatabase.IsValidFolder("Assets/Beckadam/C2AnimCVR/Generated"))
            {
                AssetDatabase.CreateFolder("Assets/Beckadam/C2AnimCVR", "Generated");
            }

            if (targetController == null)
            {
                if (!targetAnimator.TryGetComponent(out targetController))
                {
                    targetController = new();
                    int t = Mathf.FloorToInt(Time.time);
                    targetController.AddLayer("Empty");
                    AssetDatabase.CreateAsset(targetController, $"Assets/Beckadam/C2AnimCVR/Generated/Controller-{t}.controller");
                    targetAnimator.runtimeAnimatorController = targetController;
                }
            }
            string sourceData = null;
            if (source == null || Compile(out sourceData))
            {
                if (sourceData != null)
                {
                    Assemble(sourceData);
                }
                else
                {
                    Assemble();
                }
                targetAnimator.runtimeAnimatorController = targetController;
            }
        }
        public bool Compile(out string sourceData)
        {
            sourceData = null;
            string dpath = AssetDatabase.GetAssetPath(source) + "_asm.txt";
            if (sourceAsm != null)
            {
                dpath = AssetDatabase.GetAssetPath(sourceAsm);
            }
            Process buildCmd = CreateCmdProcess("Assets/Beckadam/C2AnimCVR/Scripts",
                   $"python c2animcvr.py ../../../../{AssetDatabase.GetAssetPath(source)} -o ../../../../{dpath}",
                   hideConsoleWindow);
            buildCmd.Start();
            buildCmd.WaitForExit();
            try
            {
                sourceData = File.ReadAllText(dpath);
                if (sourceData != null)
                    return true;
            }
            catch { }
            Debug.LogError("Failed to compile! Check the console (make sure to uncheck \"Hide Console Window\")");
            return false;
        }

        private static Process CreateCmdProcess(string workingDirectory, string arguments, bool hideWindow = false)
        {
            return new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = @"C:\Windows\System32\cmd.exe",
                    Arguments = $"{(hideWindow ? "/c" : "/k")} {arguments}",
                    UseShellExecute = !hideWindow,
                    RedirectStandardOutput = hideWindow,
                    CreateNoWindow = hideWindow,
                    WorkingDirectory = workingDirectory,
                },
            };
        }


        private bool Parse(string text)
        {
            int i = 0;
            BytecodeMacro current_macro = null;
            int current_macro_start = 0;
            while (i < text.Length)
            {
                string line = GetLine(text, i).Trim(' ', '\t');
                if (line.Length >= 2 && line[0] != ';')
                {
                    // Debug.Log(line);
                    if (current_macro == null)
                    {
                        string arg0 = line.Split(' ')[0];
                        string[] args = line[(arg0.Length + 1)..].Split(',');
                        if (arg0 == "speed")
                        {
                            if (args.Length != 1)
                            {
                                LogError("Incorrect number of arguments to speed: should be only one");
                                return false;
                            }
                            if (!float.TryParse(args[0], out state_speed))
                            {
                                LogError($"Failed to parse float: {args[0]}");
                                return false;
                            }
                        }
                        else if (arg0 == "memory")
                        {
                            if (args.Length != 6)
                            {
                                LogError("Incorrect number of arguments to memory: must be 6 (base,size,faddrparam,fvalueparam,breadparam,bwriteparam)");
                                return false;
                            }
                            if (!int.TryParse(args[0].Trim(' ', '\t'), out int baseaddr)) {
                                LogError("Incorrect argument to memory: arg 0 must be integer");
                                return false;
                            }
                            if (!int.TryParse(args[1].Trim(' ', '\t'), out int size))
                            {
                                LogError("Incorrect argument to memory: arg 1 must be integer");
                                return false;
                            }
                            string addr_param = args[2].Trim(' ', '\t');
                            string value_param = args[3].Trim(' ', '\t');
                            string read_param = args[4].Trim(' ', '\t');
                            string write_param = args[5].Trim(' ', '\t');
                            if (addr_param[0] != 'f' || value_param[0] != 'f' || read_param[0] != 'b' || write_param[0] != 'b')
                            {
                                LogError("Wrong parameter types to memory: must be float, float, bool, bool");
                                return false;
                            }
                            CreateMemoryLayer($"Memory_{baseaddr}", addr_param[1..], value_param[1..], read_param[1..], write_param[1..], baseaddr, size);
                        }
                        else if (arg0 == "macro")
                        {
                            string name = args[0].Trim(' ', '\t');
                            string[] margs = args[1..];
                            for (int j=0; j<margs.Length; j++)
                            {
                                margs[j] = margs[j].Trim(' ', '\t');
                            }
                            current_macro_start = NextLine(text, i);
                            current_macro = new BytecodeMacro(name, margs);
                        }
                        else if (arg0 == "layer")
                        {
                            currentLayer = new(args[0]);
                        }
                        else if (arg0 == "state")
                        {
                            if (currentLayer == null)
                            {
                                LogError("state is only valid within a layer");
                                return false;
                            }
                            currentState = new(args[0]);
                        }
                        else if (arg0 == "goto_if")
                        {
                            if (currentState == null)
                            {
                                LogError("goto_if if only value within a state");
                                return false;
                            }
                            if (args.Length != 2)
                            {
                                LogError("goto_if requires exactly 2 arguments");
                                return false;
                            }
                            currentState.transitions.Add(new BytecodeAnimatorTransition(args[1].Trim(' ', '\t'), args[0][1..]));
                        }
                        else if (arg0 == "output")
                        {
                            if (currentLayer != null)
                            {
                                LogError("output is only valid outside of a layer");
                                return false;
                            }
                            if (args.Length != 2 && args.Length != 3)
                            {
                                LogError("output requires exactly 2 or 3 arguments");
                                return false;
                            }
                            string p = args[0].Trim(' ', '\t')[1..];
                            BytecodeAnimatorLayer l = new($"blend tree {p}");
                            l.outputParam = p;
                            l.output = FindGameObject(args[1].Trim(' ', '\t'));
							if (args.Length >= 3) {
								l.outputPath = args[2].Trim(' ', '\t');
							}
                            outputLayers.Add(l);
                        }
                        else if (arg0 == "goto_unless")
                        {
                            if (currentState == null)
                            {
                                LogError("goto_if if only value within a state");
                                return false;
                            }
                            if (args.Length != 2)
                            {
                                LogError("goto_if requires exactly 2 arguments");
                                return false;
                            }
                            currentState.transitions.Add(new BytecodeAnimatorTransition(args[1].Trim(' ', '\t'), args[0][1..], true));
                        }
                        else if (arg0 == "goto")
                        {
                            if (currentState == null)
                            {
                                LogError("goto is only valid within a state");
                                return false;
                            }
                            currentState.after = args[0];
                        }
                        else if (arg0 == "var")
                        {
                            if (args.Length < 1)
                            {
                                LogError("Missing variable name");
                                return false;
                            }
                            Variable v = Variable.FromString(args[0]);
                            if (v.isConst)
                            {
                                LogError("Variable name cannot be a number or boolean");
                                return false;
                            }
                            vars.Add(v);
                        }
                        else if (arg0 == "end")
                        {
                            if (args.Length < 1)
                            {
                                LogError("Missing end argument");
                            }
                            if (args[0] == "layer")
                            {
                                currentLayer.states = states.ToArray();
                                layers.Add(currentLayer);
                                currentLayer = null;
                                states.Clear();
                            }
                            else if (args[0] == "state")
                            {
                                currentState.tasks = tasks.ToArray();
                                states.Add(currentState);
                                currentState = null;
                                tasks.Clear();
                            }
                            else if (args[0] != "macro")
                            {
                                LogError($"Unknown end argument: {args[0]}");
                                return false;
                            }
                        }
                        else if (TryGetOperator(arg0, out AnimatorDriverTask.Operator op, out int nargs))
                        {
                            if (args.Length != nargs)
                            {
                                LogError($"Incorrect number of arguments to operator {arg0}: should be {nargs}");
                                return false;
                            }
                            tasks.Add(Instruction(
                                op,
                                Variable.FromString(args[0]),
                                Variable.FromString(args[1]),
                                (nargs >= 3) ? Variable.FromString(args[2]) : null,
                                (nargs >= 4) ? Variable.FromString(args[3]) : null
                            ));
                        }
                        else if (TryGetMacro(arg0, out BytecodeMacro m))
                        {
                            // replace macro parameters with arguments verbatum
                            string insert = m.contents;
                            for (int j = 0; j < m.args.Length; j++)
                            {
                                if (j < args.Length)
                                {
                                    insert = insert.Replace(m.args[j], args[j]);
                                }
                                else
                                {
                                    insert = insert.Replace(m.args[j], "");
                                }
                            }
                            // parse the expanded macro
                            Parse(insert);
                        }
                        else
                        {
                            LogError($"Unknown opcode: {arg0}");
                            return false;
                        }
                    }
                    else if (line == "end macro")
                    {
                        current_macro.contents = text[current_macro_start..NextLine(text, i)];
                        macros.Add(current_macro);
                        current_macro = null;
                    }
                }
                i = NextLine(text, i);
                lineno++;
            }
            return true;
        }

        public bool Assemble()
        {
            if (sourceAsm == null)
            {
                return false;
            }
            return Assemble(sourceAsm.text);
        }

        public bool Assemble(string sourceData)
        {
            if (targetController == null)
            {
                return false;
            }
            lineno = 1;
            layers = new();
            states = new();
            tasks = new();
            vars = new();
            macros = new();
            outputLayers = new();
            state_speed = 30.0f;

            currentLayer = null;
            currentState = null;


            // re-create the animator controller
            string path = AssetDatabase.GetAssetPath(targetController);
            AssetDatabase.DeleteAsset(path);
            targetController = AnimatorController.CreateAnimatorControllerAtPath(path);
            // remove initial layer
            targetController.RemoveLayer(0);

            Parse(sourceData);

            // add parameters
            foreach (Variable v in vars)
            {
                targetController.AddParameter(v.name, v.AnimatorParameterType());
            }

            List<List<AnimatorState>> added_states = new();
            int total_tasks = 0;
            int total_states = 0;
            // add layers, states, and drivers
            foreach (BytecodeAnimatorLayer l in layers)
            {
                AnimatorControllerLayer layer = new() { name = l.name, defaultWeight = 1.0f, stateMachine = new() };
                layer.stateMachine.hideFlags = HideFlags.HideInHierarchy;
                AssetDatabase.AddObjectToAsset(layer.stateMachine, targetController);
                targetController.AddLayer(layer);
                AnimatorStateMachine sm = layer.stateMachine;
                List<AnimatorState> _added_states = new();
                foreach (BytecodeAnimatorState st in l.states)
                {
                    AnimatorState state = sm.AddState(st.name);
                    if (st.tasks != null && st.tasks.Length > 0)
                    {
                        AnimatorDriver driver = state.AddStateMachineBehaviour<AnimatorDriver>();
                        driver.localOnly = true;
                        driver.EnterTasks = new List<AnimatorDriverTask>(st.tasks);
                        state.speed = state_speed;
                        total_tasks += driver.EnterTasks.Count;
                    }
                    else
                    {
                        state.speed = 1000000.0f;
                    }
                    _added_states.Add(state);
                }
                total_states += _added_states.Count;
                added_states.Add(_added_states);
            }

            foreach (BytecodeAnimatorLayer l in outputLayers)
            {
                if (l.output != null && l.outputParam != null)
                {
                    string relpath = GetRelativePath(l.output, gameObject);
                    AnimationClip valueMin = new(), valueMax = new();
					string outputPath = "material._Value";
					if (l.outputPath != null)
					{
						outputPath = l.outputPath;
					}
                    valueMin.SetCurve(relpath, typeof(MeshRenderer), outputPath, AnimationCurve.Linear(0, 0, 0, 0));
                    valueMax.SetCurve(relpath, typeof(MeshRenderer), outputPath, AnimationCurve.Linear(0, 9999, 0, 9999));
                    string t = relpath.Replace("/", "_")+" "+outputPath;
                    AssetDatabase.CreateAsset(valueMin, $"Assets/Beckadam/C2AnimCVR/Generated/VMin-{t}.anim");
                    AssetDatabase.CreateAsset(valueMax, $"Assets/Beckadam/C2AnimCVR/Generated/VMax-{t}.anim");
                    AssetDatabase.SaveAssetIfDirty(valueMin);
                    AssetDatabase.SaveAssetIfDirty(valueMax);
                    AnimatorControllerLayer ll = new() { name = l.name, defaultWeight = 1.0f, stateMachine = new() };
                    AssetDatabase.AddObjectToAsset(ll.stateMachine, targetController);
                    ll.stateMachine.hideFlags = HideFlags.HideInHierarchy;
                    targetController.AddLayer(ll);

                    AnimatorState sm = targetController.CreateBlendTreeInController(l.name, out BlendTree tree, targetController.layers.Length - 1);
                    tree.hideFlags = HideFlags.HideInHierarchy;
                    sm.speed = 1000.0f;
                    sm.name = tree.name = l.name;
                    tree.blendParameter = l.outputParam;
                    tree.blendType = BlendTreeType.Simple1D;
                    tree.useAutomaticThresholds = false;
                    tree.AddChild(valueMin, 0);
                    tree.AddChild(valueMax, 9999);
                    continue;
                }
            }

            Debug.Log($"Generated {layers.Count} layers, {outputLayers.Count} output layers, {vars.Count} parameters, {total_states} states total, and {total_tasks} tasks total");

            // add transitions
            for (int j=0; j<layers.Count; j++)
            {
                AnimatorControllerLayer layer = targetController.layers[j];
                BytecodeAnimatorLayer l = layers[j];
                layer.defaultWeight = 1.0f;
                List<AnimatorState> _added_states = added_states[j];
                for (int k=0; k<l.states.Length; k++)
                {
                    BytecodeAnimatorState st = l.states[k];
                    if (st.after != null)
                    {
                        AnimatorState dest = SearchForNamedState(st.after, _added_states);
                        if (dest == null)
                        {
                            Debug.LogError($"Animator state {st.after} is undefined!");
                            return false;
                        }
                        AnimatorStateTransition tr = _added_states[k].AddTransition(dest);
                        tr.hasExitTime = true;
                        tr.duration = 0.0f;
                        tr.exitTime = 1.0f;
                        tr.offset = 0.0f;
                    }
                    if (st.transitions.Count > 0)
                    {
                        foreach (BytecodeAnimatorTransition btr in st.transitions)
                        {
                            AnimatorState dest2 = SearchForNamedState(btr.dest, _added_states);
                            if (dest2 == null)
                            {
                                Debug.LogError($"Animator state {btr.dest} is undefined!");
                                return false;
                            }
                            AnimatorStateTransition tr2 = _added_states[k].AddTransition(dest2);
                            if (btr.inverted)
                            {
                                tr2.AddCondition(AnimatorConditionMode.Less, 1.0f, btr.param);
                            }
                            else
                            {
                                tr2.AddCondition(AnimatorConditionMode.Greater, 0.0f, btr.param);
                            }
                            tr2.hasExitTime = false;
                            tr2.duration = 0.0f;
                            tr2.exitTime = 0.0f;
                            tr2.offset = 0.0f;
                        }
                    }
                }
            }
            if (layers.Count + outputLayers.Count == 0)
            {
                // add empty layer to avoid spamming console errors due to the animator trying to show the old state machine
                targetController.AddLayer("Empty");
            }
            foreach (AnimatorControllerLayer l in targetController.layers)
            {
                l.defaultWeight = 1.0f;
            }
            return true;
        }

        private void CreateMemoryLayer(string name, string addr_param, string value_param, string read_param, string write_param, int addr, int size)
        {
            AnimatorControllerLayer layer = new()
            {
                name = name,
                defaultWeight = 1.0f,
                stateMachine = new() { hideFlags = HideFlags.HideInHierarchy }
            };
            AnimatorStateMachine sm = layer.stateMachine;
            AnimatorState st_init = sm.AddState("init");
            AnimatorState st_read = sm.AddState("read");
            AnimatorState st_write = sm.AddState("write");
            st_init.speed = st_read.speed = st_write.speed = 1000000.0f;
            AnimatorStateTransition tr_read = sm.AddAnyStateTransition(st_read);
            AnimatorStateTransition tr_write = sm.AddAnyStateTransition(st_write);
            tr_read.AddCondition(AnimatorConditionMode.If, 1.0f, read_param);
            tr_write.AddCondition(AnimatorConditionMode.If, 1.0f, write_param);
            AnimatorDriver driver_read = st_read.AddStateMachineBehaviour<AnimatorDriver>();
            AnimatorDriver driver_write = st_write.AddStateMachineBehaviour<AnimatorDriver>();
            driver_read.EnterTasks.Add(new()
            {
                op = AnimatorDriverTask.Operator.Set,
                targetName = read_param,
                targetType = AnimatorDriverTask.ParameterType.Bool,
                aType = AnimatorDriverTask.SourceType.Static,
                aValue = 0.0f,
            });
            driver_write.EnterTasks.Add(new()
            {
                op = AnimatorDriverTask.Operator.Set,
                targetName = write_param,
                targetType = AnimatorDriverTask.ParameterType.Bool,
                aType = AnimatorDriverTask.SourceType.Static,
                aValue = 0.0f,
            });
            targetController.AddParameter($"#{name}_tmp", AnimatorControllerParameterType.Bool);
            for (int i=addr; i<addr+size; i++)
            {
                targetController.AddParameter($"#{name}_{i}", AnimatorControllerParameterType.Float);
                driver_read.EnterTasks.Add(new()
                {
                    op = AnimatorDriverTask.Operator.Equal,
                    targetName = $"#{name}_tmp",
                    targetType = AnimatorDriverTask.ParameterType.Bool,
                    aType = AnimatorDriverTask.SourceType.Parameter,
                    aName = addr_param,
                    bType = AnimatorDriverTask.SourceType.Static,
                    bValue = i,
                });
                driver_read.EnterTasks.Add(new()
                {
                    op = AnimatorDriverTask.Operator.Conditional,
                    targetName = value_param,
                    targetType = AnimatorDriverTask.ParameterType.Float,
                    aType = AnimatorDriverTask.SourceType.Parameter,
                    aName = $"#{name}_{i}",
                    bType = AnimatorDriverTask.SourceType.Parameter,
                    bName = value_param,
                    cType = AnimatorDriverTask.SourceType.Parameter,
                    cName = $"#{name}_tmp",
                });
                driver_write.EnterTasks.Add(new()
                {
                    op = AnimatorDriverTask.Operator.Equal,
                    targetName = $"#{name}_tmp",
                    targetType = AnimatorDriverTask.ParameterType.Bool,
                    aType = AnimatorDriverTask.SourceType.Parameter,
                    aName = addr_param,
                    bType = AnimatorDriverTask.SourceType.Static,
                    bValue = i,
                });
                driver_write.EnterTasks.Add(new()
                {
                    op = AnimatorDriverTask.Operator.Conditional,
                    targetName = $"#{name}_{i}",
                    targetType = AnimatorDriverTask.ParameterType.Float,
                    aType = AnimatorDriverTask.SourceType.Parameter,
                    aName = $"#{name}_{i}",
                    bType = AnimatorDriverTask.SourceType.Parameter,
                    bName = value_param,
                    cType = AnimatorDriverTask.SourceType.Parameter,
                    cName = $"#{name}_tmp",
                });
            }
            AssetDatabase.AddObjectToAsset(layer.stateMachine, targetController);
            targetController.AddLayer(layer);
        }

        private void LogError(string s)
        {
            Debug.LogError($"{s}\nError on line {lineno}");
        }

        private static string GetRelativePath(GameObject dest, GameObject src)
        {
            return AnimationUtility.CalculateTransformPath(dest.transform, src.transform);
        }

        private GameObject FindGameObject(string name)
        {
            for (int i=0; i<gameObject.transform.childCount; i++)
            {
                GameObject obj = gameObject.transform.GetChild(i).gameObject;
                if (obj.name == name)
                {
                    return obj;
                }
            }
            return null;
        }

        private AnimatorState SearchForNamedState(string name, List<AnimatorState> states)
        {
            foreach (AnimatorState st in states)
            {
                if (st.name == name)
                {
                    return st;
                }
            }
            return null;
        }
        private bool TryGetMacro(string name, out BytecodeMacro mac)
        {
            foreach (BytecodeMacro m in macros)
            {
                if (m.name == name)
                {
                    mac = m;
                    return true;
                }
            }
            mac = null;
            return false;
        }
        private string GetLine(string s, int i)
        {
            int nl = s.IndexOf('\n', i);
            if (nl == -1)
                return s[i..];
            if (nl < i + 1)
                return "";
            return s[i .. (nl-1)];
        }

        private int NextLine(string s, int i)
        {
            int j = s.IndexOf('\n', i);
            if (j == -1)
            {
                return s.Length;
            }
            return j + 1;
        }

        private bool TryGetOperator(string word, out AnimatorDriverTask.Operator op, out int nargs)
        {
            string[] words = "set add sub mul div mod pow log eq ne lt le gt ge ip fp land lor and or xor shl shr rol ror cond".Split(' ');
            AnimatorDriverTask.Operator[] ops = {
                AnimatorDriverTask.Operator.Set,
                AnimatorDriverTask.Operator.Addition,
                AnimatorDriverTask.Operator.Subtraction,
                AnimatorDriverTask.Operator.Multiplication,
                AnimatorDriverTask.Operator.Division,
                AnimatorDriverTask.Operator.Modulo,
                AnimatorDriverTask.Operator.Power,
                AnimatorDriverTask.Operator.Log,
                AnimatorDriverTask.Operator.Equal,
                AnimatorDriverTask.Operator.NotEqual,
                AnimatorDriverTask.Operator.LessThen,
                AnimatorDriverTask.Operator.LessEqual,
                AnimatorDriverTask.Operator.MoreThen,
                AnimatorDriverTask.Operator.MoreEqual,
                AnimatorDriverTask.Operator.IPart,
                AnimatorDriverTask.Operator.FPart,
                AnimatorDriverTask.Operator.LogicalAnd,
                AnimatorDriverTask.Operator.LogicalOr,
                AnimatorDriverTask.Operator.BitwiseAnd,
                AnimatorDriverTask.Operator.BitwiseOr,
                AnimatorDriverTask.Operator.BitwiseXor,
                AnimatorDriverTask.Operator.LeftShift,
                AnimatorDriverTask.Operator.RightShift,
                AnimatorDriverTask.Operator.RotateLeft,
                AnimatorDriverTask.Operator.RotateRight,
                AnimatorDriverTask.Operator.Conditional
            };
            op = AnimatorDriverTask.Operator.Set;
            bool found = false;
            for (int i=0; i<words.Length; i++)
            {
                if (word == words[i])
                {
                    op = ops[i];
                    found = true;
                    break;
                }
            }
            if (op == AnimatorDriverTask.Operator.Set || op == AnimatorDriverTask.Operator.FPart ||
                op == AnimatorDriverTask.Operator.IPart || op == AnimatorDriverTask.Operator.Log)
            {
                nargs = 2;
            }
            else if (op == AnimatorDriverTask.Operator.Conditional)
            {
                nargs = 4;
            }
            else
            {
                nargs = 3;
            }
            return found;
        }

        private AnimatorDriverTask Instruction(AnimatorDriverTask.Operator op, Variable t, Variable a, Variable b = null, Variable c = null)
        {
            AnimatorDriverTask task = new();
            task.op = op;
            task.aType = a.SourceType();
            task.aName = a.name;
            task.aValue = a.val;
            task.aParamType = a.type;
            task.targetName = t.name;
            task.targetType = t.type;
            if (b != null)
            {
                task.bType = b.SourceType();
                task.bName = b.name;
                task.bValue = b.val;
                task.bParamType = b.type;
            }
            if (c != null)
            {
                task.cType = c.SourceType();
                task.cName = c.name;
                task.cValue = c.val;
                task.cParamType = c.type;
            }
            return task;
        }

    }

}
#endif
