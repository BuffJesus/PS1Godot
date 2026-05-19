<!-- gen_lua_api_docs:generated -->
# `Math`

!!! info "Generated"
    This page is auto-generated from `psxsplash-main/src/luaapi.hh` by `scripts/py/gen_lua_api_docs.py`. Edits won't survive the next build — fix the source comments instead.

11 entries.

## Methods

### `Math.Clamp(value, min, max) -> number` { #math-clamp }

Returns `value` constrained to the [min, max] range.

### `Math.Lerp(a, b, t) -> number` { #math-lerp }

Linear interpolation: returns `a` when t=0, `b` when t=1, blend
in between. Scalar version of Vec3.lerp.

### `Math.Sign(value) -> number` { #math-sign }

Returns -1 for negative, 0 for zero, +1 for positive. Useful for
flipping facing direction or "which way to step" decisions.

### `Math.Abs(value) -> number` { #math-abs }

Absolute value. Works on both integers and FixedPoint<12>.

### `Math.Min(a, b) -> number` { #math-min }

Smaller of the two values. For lists, fold pair-wise.

### `Math.Max(a, b) -> number` { #math-max }

Larger of the two values.

### `Math.Floor(fp) -> integer` { #math-floor }

Floors a FixedPoint<12> to the next integer toward -infinity.
Accepts either a FixedPoint object or a plain integer (identity).

### `Math.Ceil(fp) -> integer` { #math-ceil }

Ceilings a FixedPoint<12> to the next integer toward +infinity.

### `Math.Round(fp) -> integer` { #math-round }

Rounds a FixedPoint<12> to the nearest integer. Half-values tie
toward +infinity so round(0.5) = 1, round(-0.5) = 0.

### `Math.ToInt(fp) -> integer` { #math-toint }

Truncates a FixedPoint<12> toward zero. Inverse of Math.ToFixed
for non-negative whole values. Use Floor if you want the
round-toward-minus-infinity semantics.

### `Math.ToFixed(integer) -> FixedPoint` { #math-tofixed }

Promotes a plain integer to a FixedPoint<12>. Equivalent to
FixedPoint.new(integer, 0) but shorter.
