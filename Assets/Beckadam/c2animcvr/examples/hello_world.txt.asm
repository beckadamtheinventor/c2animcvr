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
var fi
var f#F0
var fhello
var f#I1
var fhello2
output fhello2, display,material._Value
layer Main Layer
state entry
set fi, 0
goto F1
end state
state F1
lt f#F0, fi, 10
goto_unless f#F0, F1End
goto F1Loop
end state
state F1Loop
set fhello, 7
negate f#BuiltinTmpA, fhello
lor fhello, fhello, f#BuiltinTmpA
sub fhello, fhello, fi
lt f#I1, fhello, 0
add f#BuiltinTmpA, fhello, fi
cond fhello,f#I1, f#BuiltinTmpA, fhello
cond fhello2,f#I1, fhello, fhello2
add fi, fi, 1
goto F1
end state
state F1End
goto end
end state
state end
end state
end layer