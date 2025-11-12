# c2animcvr

A simple expression based language targeting Unity Animators and ChilloutVR Animator Drivers. (state behavior)

Requires python 3 to be installed.
On Linux you will need to alias python to run python3 or apt install python-is-python3.

This compiler is not yet fully featured and may not be entirely accurate.



# Usage

Create a text file or duplicate one of the examples.

Add the "Animator Compiler" script to a Game Object in the scene.

Add an animator script to the same Game Object.

Create a new Animator Controller, attach it to the Animator Compiler and Animator attached to the Game Object.

Attach the text asset to the Animator Compiler's "Source" field.

Click "Build" to compile the source to the targeted Animator Controller.

