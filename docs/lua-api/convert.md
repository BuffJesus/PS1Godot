<!-- gen_lua_api_docs:generated -->
# `Convert`

!!! info "Generated"
    This page is auto-generated from `psxsplash-main/src/luaapi.hh` by `scripts/py/gen_lua_api_docs.py`. Edits won't survive the next build — fix the source comments instead.

2 entries, 0 with worked examples mined from `godot-ps1/demo/scripts/` and the bundled templates.

## Methods

### `Convert.IntToFp(intValue) -> FixedPoint` { #convert-inttofp }

Promotes a plain integer to a FixedPoint<12>. `Convert.IntToFp(3)`
= the FP value for 3.0. Same as Math.ToFixed; both names exist
for muscle-memory parity with older PS1 dev conventions.

### `Convert.FpToInt(fpValue) -> integer` { #convert-fptoint }

Truncates a FixedPoint<12> toward zero, returning the integer
part. Use Math.Floor / Math.Ceil / Math.Round when you need
different rounding semantics.
