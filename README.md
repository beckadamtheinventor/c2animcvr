# c2animcvr

A simple expression based language targeting Unity Animators and ChilloutVR Animator Drivers. (state behavior)

This compiler is not yet fully featured and may not be entirely accurate.

Note that on Windows since the compile step uses a python script packaged as an executable, you may need to unblock it before it can be run.
Windows defender will most likely see the program as a virus due to how executable packaged python scripts work.


# Usage

Create a text file or duplicate one of the examples to write expressions to compile.

Add the "Animator Compiler" script to a Game Object in the scene.

Add an animator script to the same Game Object.

Create a new Animator Controller, attach it to the Animator Compiler and Animator attached to the Game Object.

Attach the text asset to the Animator Compiler's "Source" field.

Click "Build" to compile the source to the targeted Animator Controller.

