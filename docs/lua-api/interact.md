<!-- gen_lua_api_docs:generated -->
# `Interact`

!!! info "Generated"
    This page is auto-generated from `psxsplash-main/src/luaapi.hh` by `scripts/py/gen_lua_api_docs.py`. Edits won't survive the next build — fix the source comments instead.

2 entries.

## Methods

### `Interact.SetEnabled(object, bool)` { #interact-setenabled }

Toggles whether the given GameObject participates in the "press
X to interact" pipeline. Disabling hides the prompt AND blocks
the on-interact callback. Use to "consume" an interaction
(one-time chests, conversations that shouldn't repeat).

### `Interact.IsEnabled(object) -> boolean` { #interact-isenabled }

True if the object is interactable AND not currently disabled.
False for objects with no Interactable component.
