var f#BuiltinTmp
var f#BuiltinTmpA
macro sqrt, $A, $B
  pow $A, $B, -1
end macro
macro ceil, $A, $B
  fpart f#BuiltinTmp, $B
  ipart $A, $B
  cond f#BuiltinTmp, f#BuiltinTmp, 0, 1
  add $A, $A, f#BuiltinTmp
end macro
; note: AnimatorDriver conditional is true if the condition is >= 0.5.
macro round, $A, $B
  fpart f#BuiltinTmp, $B
  ipart $A, $B
  cond f#BuiltinTmp, f#BuiltinTmp, 0, -1
  add $A, $A, f#BuiltinTmp
end macro
macro abs, $A, $B
  negate $A, $B
  ge f#BuiltinTmp, $A, 0
  cond $A, f#BuiltinTmp, $B, $A
end macro
macro negate, $A, $B
  sub $A, 1, $B
end macro
macro not, $A, $B
  xor $A, $B, 0xffffffff
end macro
var fvalueA
var fvalueB
var fvalueC
var fvalueD
var fvalueAxorB
var fvalueAorB
var fvalueAandB
var fvalueCshlD
var fvalueCshrD
var fvalueCrolD
var fvalueCrorD
var fI
var f#F0
var fvalueArolI
layer Main Layer
state entry
set fvalueA, 420
set fvalueB, 1337
set fvalueC, 6969
set fvalueD, 7
xor fvalueAxorB,fvalueA, fvalueB
or fvalueAorB,fvalueA, fvalueB
and fvalueAandB,fvalueA, fvalueB
shl fvalueCshlD,fvalueC, fvalueD
shr fvalueCshrD,fvalueC, fvalueD
rol fvalueCrolD,fvalueC, fvalueD
ror fvalueCrorD,fvalueC, fvalueD
set fI, 0
goto F1
end state
state F1
lt f#F0, fI, 32
goto_unless f#F0, F1End
goto F1Loop
end state
state F1Loop
rol fvalueArolI,fvalueA, fI
add fI, fI, 1
goto F1
end state
state F1End
goto end
end state
state end
end state
end layer