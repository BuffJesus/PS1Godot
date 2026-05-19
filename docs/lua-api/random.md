<!-- gen_lua_api_docs:generated -->
# `Random`

!!! info "Generated"
    This page is auto-generated from `psxsplash-main/src/luaapi.hh` by `scripts/py/gen_lua_api_docs.py`. Edits won't survive the next build — fix the source comments instead.

5 entries, 0 with worked examples mined from `godot-ps1/demo/scripts/` and the bundled templates.

## Methods

### `Random.Number(max) -> integer` { #random-number }

One-shot dice roll in the range [1, max]. The seed is auto-mixed
with the current frame count, so consecutive same-frame calls in
different scripts still differ. Use for "throw-away" randomness
(sparkle jitter, hit-flash variations) where determinism doesn't
matter. For reproducible sequences, use Random.GeneratorNumber.

### `Random.GeneratorNumber(max) -> integer` { #random-generatornumber }

Deterministic dice roll in [1, max] from the seedable generator.
Pair with Random.Seed for reproducible sequences (replays, daily-
challenge dungeons, save-respecting loot tables).

### `Random.Range(min, max) -> integer` { #random-range }

One-shot integer in [min, max]. Same frame-mixed pool as
Random.Number — convenient when you don't want a +1 offset.

### `Random.GeneratorRange(min, max) -> integer` { #random-generatorrange }

Deterministic integer in [min, max] from the seedable generator.

### `Random.Seed(seed)` { #random-seed }

Re-seeds the deterministic generator used by Random.Generator*.
A seed of 0 is silently rewritten to 108 (avoids the all-zero
degenerate state). Doesn't affect Random.Number / Random.Range,
which always frame-mix. Call once at scene start for reproducible
runs; call again to reset between attempts.
