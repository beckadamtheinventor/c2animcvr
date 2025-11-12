import os, sys, json
from py_expression_eval import *

class Struct:
    """ members is a str:str dictionary where the values are single-letter type names """
    def __init__(self, name: str, members: dict):
        self.name = name
        self.symbols = {k:members[k]+name+"_"+k for k in members.keys()}
    
    def __getitem__(self, member: str):
        if member not in self.symbols:
            return None
        return self.symbols[member]

    def __setitem__(self, member: str, value):
        raise NotImplementedError()


class Compiler:
    def __init__(self, source: str, debug: bool = False):
        self.debug = debug
        self.source = source
        self.debug_tokens = []
        self.outputters = []
        self.layers = []
        self.vars = {}
        self.labels = []
        self.anon_label_count = 0
        self.assembly = []
        self.lineno = 0
        self.parser = parser = Parser()
        self.structs = {}
        parser.values["shift_right"] = parser.values["shr"] = lambda a,b: int(a)//(2**int(b))
        parser.values["shift_left"] = parser.values["shl"] = lambda a,b: int(a)*(2**int(b))
        parser.values["rotate_right"] = parser.values["ror"] = lambda a,b: int(a)//(2**int(b)) + int(a)*(2**int(31 - b))
        parser.values["rotate_left"] = parser.values["rol"] = lambda a,b: int(a)*(2**int(b)) + int(a)//(2**int(31 - b))
        parser.values["bitwise_and"] = parser.values["band"] = lambda a,b: int(a)&int(b)
        parser.values["bitwise_or"] = parser.values["bor"] = lambda a,b: int(a)|int(b)
        parser.values["bitwise_xor"] = parser.values["bxor"] = lambda a,b: int(a)^int(b)
        self.builtins_assembly = [
            "var f#BuiltinTmp",
            "var f#BuiltinTmpA",
            "macro sqrt, $A, $B",
            "  pow $A, $B, -1",
            "end macro",
            "macro ceil, $A, $B",
            "  fpart f#BuiltinTmp, $B",
            "  ipart $A, $B",
            "  cond f#BuiltinTmp, f#BuiltinTmp, 0, 1",
            "  add $A, $A, f#BuiltinTmp",
            "end macro",
            "; note: AnimatorDriver conditional is true if the condition is >= 0.5.",
            "macro round, $A, $B",
            "  fpart f#BuiltinTmp, $B",
            "  ipart $A, $B",
            "  cond f#BuiltinTmp, f#BuiltinTmp, 0, -1",
            "  add $A, $A, f#BuiltinTmp",
            "end macro",
            "macro abs, $A, $B",
            "  negate $A, $B",
            "  ge f#BuiltinTmp, $A, 0",
            "  cond $A, f#BuiltinTmp, $B, $A",
            "end macro",
            "macro negate, $A, $B",
            "  sub $A, 1, $B",
            "end macro",
            "macro not, $A, $B",
            "  xor $A, $B, 0xffffffff",
            "end macro",
        ]

    def InternalError(self, m="Unknown"):
        print(f"Internal Error: {m}")
        raise RuntimeError(f"Internal Error: {m}")

    def Error(self, m="Syntax"):
        print(f"Error on line {self.lineno}: {m}")
        raise RuntimeError(f"Error: {m}")

    def Finalize(self):
        if self.debug:
            with open("debug_tokens.json", "w") as f:
                json.dump(self.debug_tokens, f)
        return "\n".join(self.assembly)

    def serialize(self, v):
        if type(v) is Token:
            if v.type_ == TOP1:
                return ["OP1", v.toString()]
            elif v.type_ == TOP2:
                return ["OP2", v.toString()]
            elif v.type_ == TVAR:
                return ["VAR", v.toString()]
            return v.toString()
        elif type(v) is Expression:
            return [self.serialize(t) for t in v.tokens]
        elif type(v) is str:
            return v
        elif type(v) is list or type(v) is tuple:
            return [self.serialize(vv) for vv in v]
        return v

    def SymbolToOperator(self, sym: str) -> str:
        operatormap = {
            'sqrt': "sqrt",
            'abs': "abs",
            'ceil': "ceil",
            'floor': "ipart",
            'round': "round",
            'not': "not",
            'exp': "pow",
            '+': "add",
            '-': "sub",
            '*': "mul",
            '/': "div",
            '%': "mod",
            '^': "pow",
            '**': "pow",
            "==": "eq",
            "!=": "ne",
            ">": "gt",
            "<": "lt",
            ">=": "ge",
            "<=": "le",
            "and": "land",
            "or": "lor",
            "xor": "xor",
            "D": "diceroll",
            'random': "random",
            'log': "log",
            'min': "min",
            'max': "max",
            'pow': "pow",
            'if': "cond",
            'band': "and",
            'bor': "or",
            'bxor': "xor",
            'shr': "shr",
            'shl': "shl",
            'ror': "ror",
            'rol': "rol",
            'bitwise_and': "and",
            'bitwise_or': "or",
            'bitwise_xor': "xor",
            'shift_right': "shr",
            'shift_left': "shl",
            'rotate_right': "ror",
            'rotate_left': "rol",
        }
        if sym in operatormap:
            return operatormap[sym]
        return ""

    def TokenToArg(self, t):
        if type(t) is not Token:
            return str(t)
        if t.type_ == TNUMBER:
            return str(t.number_)
        if t.type_ == TVAR:
            return "f" + str(t.index_)
        self.InternalError(f"Unexpected Token used as Argument: {t.toString()}")

    def BuildAssembly(self):
        self.assembly.extend(self.builtins_assembly)

        for var in self.vars.keys():
            if not self.vars[var]["constant"]:
                self.assembly.append(f"var f{var}")

        for opt in self.outputters:
            self.assembly.append(f"output f{opt['var']}, {','.join(opt['dest'])}")

        for layer in self.layers:
            if "name" in layer:
                self.assembly.append(f"layer {layer['name']}")
                if "states" in layer:
                    for statename in layer["states"].keys():
                        state = layer["states"][statename]
                        self.assembly.append(f"state {statename}")
                        for instr in state["instructions"]:
                            self.assembly.append(instr)
                        for goto in state["gotos"]:
                            if "condition" in goto:
                                if goto["unless"]:
                                    self.assembly.append(f"goto_unless {goto['condition']}, {goto['state']}")
                                else:
                                    self.assembly.append(f"goto_if {goto['condition']}, {goto['state']}")
                            else:
                                self.assembly.append(f"goto {goto['state']}")
                        self.assembly.append(f"end state")
                self.assembly.append(f"end layer")

    def _Compile(self, source, parsed, depth=0, withinif=False):
        ln = 0
        while ln < len(source):
            line = source[ln]
            ln += 1
            line = line.strip(" \n\t")
            self.lineno += 1
            if line.startswith("label "):
                name = "L"+line.split(" ", maxsplit=1)[1]
                self.labels.append(name)
                parsed.append(["@LABEL", name])
            elif line.startswith("goto "):
                name = "L"+line.split(" ", maxsplit=1)[1]
                if name not in self.labels:
                    self.Error("Unknown label \"{name}\"")
                parsed.append(["@GOTO", name])
            elif line.startswith("end"):
                if depth <= 0:
                    self.Error("Unexpected \"end\"")
                return ln
            elif line.startswith("if "):
                cond = line.split(" ", maxsplit=1)[1].replace("[","(").replace("]",")")
                var = f"#I{depth}"
                parsed.append([var, self.parser.parse(cond)])
                inner = []
                innerElse = []
                # compile if block
                ln += self._Compile(source[ln:], inner, depth+1)
                if source[ln].startswith("else"):
                    ln += 1
                    # compile else block
                    ln += self._Compile(source[ln:], innerElse, depth+1)
                iback = []
                # setup if block ternary expressions
                for line in inner:
                    if line[0].startswith("@"):
                        continue
                    if len(line[1].tokens) > 1:
                        line[1].tokens.extend([
                            Token(TVAR, 'if', 0, 0),
                            Token(TVAR, var, 0, 0),
                            Token(TVAR, "#BuiltinTmpA", 0, 0),
                            Token(TOP2, ',', 0, 0),
                            Token(TVAR, line[0], 0, 0),
                            Token(TOP2, ',', 0, 0),
                            Token(TFUNCALL, 0, 0, 0),
                        ])
                    elif len(line[1].tokens) > 0:
                        line[1].tokens = [
                            Token(TVAR, 'if', 0, 0),
                            Token(TVAR, var, 0, 0),
                            line[1].tokens[0],
                            Token(TOP2, ',', 0, 0),
                            Token(TVAR, line[0], 0, 0),
                            Token(TOP2, ',', 0, 0),
                            Token(TFUNCALL, 0, 0, 0),
                        ]
                # setup else block ternary expressions
                for line in innerElse:
                    if line[0].startswith("@"):
                        continue
                    if len(line[1].tokens) > 1:
                        line[1].tokens.extend([
                            Token(TVAR, 'if', 0, 0),
                            Token(TVAR, var, 0, 0),
                            Token(TVAR, line[0], 0, 0),
                            Token(TOP2, ',', 0, 0),
                            Token(TVAR, "#BuiltinTmpA", 0, 0),
                            Token(TOP2, ',', 0, 0),
                            Token(TFUNCALL, 0, 0, 0),
                        ])
                    elif len(line[1].tokens) > 0:
                        line[1].tokens = [
                            Token(TVAR, 'if', 0, 0),
                            Token(TVAR, var, 0, 0),
                            Token(TVAR, line[0], 0, 0),
                            Token(TOP2, ',', 0, 0),
                            line[1].tokens[0],
                            Token(TOP2, ',', 0, 0),
                            Token(TFUNCALL, 0, 0, 0),
                        ]
                # backup original values of parameters touched by if/else blocks
                for ib in iback:
                    parsed.append(["#IB"+ib, Expression([Token(TVAR, ib, 0, 0)], [], [], [])])
                # if block
                parsed.extend(inner)
                # else block
                parsed.extend(innerElse)
            elif line.startswith("while "):
                if withinif:
                    self.Error("Loops within if statements are not currently supported")
                cond = line.split(" ", maxsplit=1)[1].replace("[","(").replace("]",")")
                self.anon_label_count += 1
                wlblname = f"W{self.anon_label_count}"
                wlblnameLoop = f"W{self.anon_label_count}Loop"
                wlblnameEnd = f"W{self.anon_label_count}End"
                parsed.append(["@LABEL", wlblname])
                var = f"#W{depth}"
                parsed.append([var, self.parser.parse(cond)])
                parsed.append(["@GOTO_UNLESS", wlblnameEnd, "f"+var])
                parsed.append(["@GOTO", wlblnameLoop])
                parsed.append(["@LABEL", wlblnameLoop])
                ln += self._Compile(source[ln:], parsed, depth+1)
                parsed.append(["@GOTO", wlblname])
                parsed.append(["@LABEL", wlblnameEnd])
            elif line.startswith("repeat "):
                if withinif:
                    self.Error("Loops within if statements are not currently supported")
                cond = line.split(" ", maxsplit=1)[1]
                cond = cond.replace("[","(").replace("]",")")
                self.anon_label_count += 1
                wlblname = f"R{self.anon_label_count}"
                parsed.append(["@LABEL", wlblname])
                ln += self._Compile(source[ln:], parsed, depth+1)
                var = f"#R{depth}"
                parsed.append([var, self.parser.parse(cond)])
                parsed.append(["@GOTO", wlblname, "f"+var])
            elif line.startswith("for "):
                if withinif:
                    self.Error("Loops within if statements are not currently supported")
                if ln + 2 >= len(source):
                    self.Error("Invalid for loop")
                init = line.split(" ", maxsplit=1)[1]
                if '=' not in init:
                    self.Error("Invalid for loop init")
                init = [i.strip(" \t\n") for i in init.split("=", maxsplit=1)]
                cond = source[ln]
                ln += 1
                inc = source[ln]
                if '=' not in inc:
                    self.Error("Invalid for loop increment")
                inc = [i.strip(" \t\n") for i in inc.split("=", maxsplit=1)]
                ln += 1
                var = f"#F{depth}"
                self.anon_label_count += 1
                wlblname = f"F{self.anon_label_count}"
                wlblnameLoop = f"F{self.anon_label_count}Loop"
                wlblnameEnd = f"F{self.anon_label_count}End"
                parsed.append([init[0], self.parser.parse(init[1])])
                parsed.append(["@LABEL", wlblname])
                parsed.append([var, self.parser.parse(cond)])
                parsed.append(["@GOTO_UNLESS", wlblnameEnd, "f"+var])
                parsed.append(["@GOTO", wlblnameLoop])
                parsed.append(["@LABEL", wlblnameLoop])
                ln += self._Compile(source[ln:], parsed, depth+1)
                parsed.append([inc[0], self.parser.parse(inc[1])])
                parsed.append(["@GOTO", wlblname])
                parsed.append(["@LABEL", wlblnameEnd])
            elif line.startswith("output "):
                dest = "material._Value"
                src = line.split(" ", maxsplit=1)[1]
                if "->" in src:
                    src, dest = [a.strip(" \t\n") for a in src.split("->", maxsplit=1)]
                parsed.append(["@OUTPUT", src, dest])
            # elif line.startswith("struct "):
            #     members = {}
            #     sname = line.split(" ", maxsplit=1)[1]
            #     while ln < len(source):
            #         line = source[ln]
            #         ln += 1
            #         if line == "end":
            #             break
            #         elif ":" in line:
            #             svname, svtype = line.split(":", maxsplit=1)
            #             members[svname] = svtype
            #     self.structs[sname] = Struct(sname, members)
            elif "=" in line:
                var, expr = [a.strip(" \t\n") for a in line.split("=", maxsplit=1)]
                expr = expr.replace("[","(").replace("]",")")
                parsed.append([var, self.parser.parse(expr)])
        return len(source)

    def _CompileExpr(self, line):
        instructions = []
        i = 0
        acc = []
        expr = line[1]
        if line[0] not in self.vars:
            self.vars[line[0]] = {"constant": False}
        while i < len(expr.tokens):
            token = expr.tokens[i]
            i += 1
            lastToken = i >= len(expr.tokens)
            if lastToken:
                destparam = line[0]
            else:
                destparam = "#BuiltinTmpA"
            if type(token) is Token:
                if token.type_ == TNUMBER:
                    if lastToken:
                        instructions.append(f"set f{line[0]}, {token.number_}")
                    else:
                        acc.append(token)
                elif token.type_ == TVAR:
                    if lastToken:
                        instructions.append(f"set f{line[0]}, f{token.index_}")
                    else:
                        acc.append(token)
                elif token.type_ == TOP1:
                    if token.index_ == '-':
                        instructions.append(f"negate f{destparam}, f{line[0]}")
                        acc.append(Token(TVAR, destparam, 0, 0))
                    elif len(acc) >= 1:
                        arg = acc.pop()
                        if arg.type_ == TNUMBER:
                            val = self.parser.ops1[token.index_](arg.number_)
                            acc.append(Token(TNUMBER, 0, 0, val))
                        elif arg.type_ == TVAR:
                            op = self.SymbolToOperator(token.index_)
                            if not len(op):
                                self.InternalError(f"Unknown operator \"{token.index_}\"")
                            instructions.append(f"{op} f{destparam}, {self.TokenToArg(arg)}")
                            acc.append(Token(TVAR, destparam, 0, 0))
                        else:
                            self.InternalError()
                    else:
                        self.InternalError()
                elif token.type_ == TOP2:
                    if len(acc) >= 2:
                        arg2 = acc.pop()
                        arg1 = acc.pop()
                        if token.index_ == ',':
                            if type(arg1) is list:
                                arg1.append(arg2)
                                acc.append(arg1)
                            else:
                                acc.append([arg1, arg2])
                            continue
                        elif token.index_ == '||':
                            if type(arg1) is not list:
                                arg1 = [arg1]
                            if type(arg2) is not list:
                                arg2 = [arg2]
                            arg1.extend(arg2)
                            acc.append(arg1)
                            continue

                        if arg2.type_ == TNUMBER and arg1.type_ == TNUMBER:
                            val = self.parser.ops2[token.index_](arg1.number_, arg2.number_)
                            acc.append(Token(TNUMBER, 0, 0, val))
                        elif arg1.type_ == TVAR or arg2.type_ == TVAR:
                            op = self.SymbolToOperator(token.index_)
                            if not len(op):
                                self.InternalError(f"Unknown operator \"{token.index_}\"")
                            instructions.append(f"{op} f{destparam}, {self.TokenToArg(arg1)}, {self.TokenToArg(arg2)}")
                            acc.append(Token(TVAR, destparam, 0, 0))
                        else:
                            self.InternalError()
                    else:
                        self.InternalError()
                elif token.type_ == TFUNCALL:
                    if len(acc) >= 2:
                        arg = acc.pop()
                        op = acc.pop()
                        if op.index_ in self.parser.functions:
                            opf = self.parser.functions[op.index_]
                        elif op.index_ in self.parser.ops1:
                            opf = self.parser.ops1[op.index_]
                        elif op.index_ in self.parser.ops2:
                            opf = self.parser.ops2[op.index_]
                        elif op.index_ in self.parser.values:
                            opf = self.parser.values[op.index_]
                        else:
                            self.InternalError(f"Unknown function \"{op.index_}\"")
                        op = self.SymbolToOperator(op.index_)
                        if len(op):
                            if type(arg) is list:
                                if all([a.type_ == TNUMBER for a in arg]):
                                    arg = [a.number_ for a in arg]
                                    acc.append(Token(TNUMBER, 0, 0, opf(*arg)))
                                else:
                                    instructions.append(f"{op} f{destparam}," + ", ".join([self.TokenToArg(a) for a in arg]))
                                    acc.append(Token(TVAR, destparam, 0, 0))
                            elif arg.type_ == TNUMBER and type(arg.number_) is list:
                                arg = arg.number_
                                if all([a.type_ == TNUMBER for a in arg]):
                                    arg = [a.number_ for a in arg]
                                    acc.append(Token(TNUMBER, 0, 0, opf(*arg)))
                                else:
                                    instructions.append(f"{op} f{destparam}," + ", ".join([self.TokenToArg(a) for a in arg]))
                                    acc.append(Token(TVAR, destparam, 0, 0))
                            else:
                                if arg.type_ == TNUMBER:
                                    acc.append(Token(TNUMBER, 0, 0, opf(arg)))
                                else:
                                    instructions.append(f"{op} f{destparam}, {self.TokenToArg(arg)}")
                                    acc.append(Token(TVAR, destparam, 0, 0))
                        else:
                            self.InternalError(f"Unknown function: \"{token.index_}\"")
                    else:
                        self.InternalError()
            else:
                pass
            if lastToken:
                if len(acc) > 0:
                    arg = self.TokenToArg(acc.pop())
                    if f"f{destparam}" != arg:
                        instructions.append(f"set f{destparam}, {arg}")
        return instructions

    def Compile(self):
        # remove line comments
        source = "\n".join([l if not l.startswith("//") else "" for l in self.source.split("\n")])
        # remove block comments
        while "/*" in source:
            head, tail = source.split("/*", maxsplit=1)
            if "*/" not in tail:
                self.Error("Missing end comment \"*/\"")
            ignore, tail = tail.split("*/")
            source = head + tail
        # add implied semicolons to ends
        while "end\n" in source:
            head, tail = source.split("end\n", maxsplit=1)
            source = head + "end;\n" + tail
        # replace placeholder
        # split code lines
        source = source.split(";")
        parsed = []
        self._Compile(source, parsed)
        if self.debug:
            self.debug_tokens.extend([
                [str(p[0])]+[self.serialize(token) for token in p[1].tokens] if type(p[1]) is Expression else self.serialize(p) for p in parsed
            ])

        current_layer = {
            "name": "Main Layer",
            "states": {"entry": {"instructions": [], "gotos": []}}
        }
        current_state = "entry"
        instructions = []
        gotos = []
        for line in parsed:
            if not len(line):
                continue
            if line[0] == "@OUTPUT":
                dest = line[2]
                if ":" in dest:
                    dest = dest.split(":", maxsplit=1)
                else:
                    dest = [dest]
                self.outputters.append({"var": line[1], "dest": dest})
            elif line[0] == "@LABEL":
                if not any([len(g)<=2 for g in gotos]):
                    gotos.append({"state": line[1]})
                current_layer["states"][current_state] = {
                    "instructions": instructions,
                    "gotos": gotos,
                }
                current_state = line[1]
                instructions = []
                gotos = []
            elif line[0] in ("@GOTO", "@GOTO_UNLESS"):
                gtst = line[1]
                if line[1] == current_state:
                    gtst = line[1]+"#loop"
                    current_layer["states"][gtst] = {
                        "instructions": [],
                        "gotos": [{"state": current_state}]
                    }
                gotos.append({"state": gtst, "unless": line[0]=="@GOTO_UNLESS"})
                if len(line) >= 3:
                    gotos[-1]["condition"] = line[2]
            else:
                instructions.extend(self._CompileExpr(line))
        if len(instructions):
            current_layer["states"][current_state] = {
                "instructions": instructions,
                "gotos": gotos,
            }
        
        if not any([len(g)<=2 for g in gotos]):
            gotos.append({"state": "end"})
        current_layer["states"][current_state] = {
            "instructions": [],
            "gotos": gotos,
        }

        current_layer["states"]["end"] = {
            "instructions": [],
            "gotos": [],
        }

        self.layers.append(current_layer)



if __name__ == '__main__':
    if len(sys.argv) < 2:
        print(f"Usage: {sys.argv[0]} source.txt [-d] [-o output.asm]")
        exit(0)

    ifile = sys.argv[1]
    ofile = ifile + ".asm"
    debug = False
    for i in range(2, len(sys.argv)):
        if sys.argv[i] in ("-d", "--debug"):
            debug = True
        elif sys.argv[i] in ("-o", "--output") and i+1 < len(sys.argv):
            ofile = sys.argv[i+1]

    with open(ifile) as f:
        source = f.read()

    comp = Compiler(source, debug)
    comp.Compile()
    comp.BuildAssembly()
    data = comp.Finalize()

    with open(ofile, "w") as f:
        f.write(data)
