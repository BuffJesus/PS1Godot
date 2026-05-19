<!-- gen_lua_api_docs:generated -->
# `Stats`

!!! info "Generated"
    This page is auto-generated from `psxsplash-main/src/luaapi.hh` by `scripts/py/gen_lua_api_docs.py`. Edits won't survive the next build — fix the source comments instead.

9 entries, 0 with worked examples mined from `godot-ps1/demo/scripts/` and the bundled templates.

## Methods

### `Stats.GetHP(object) -> int` { #stats-gethp }

Current HP. 0 if the entity has no PS1Stats authored or HP has
hit 0 (the entity is "dead" from a stats perspective; runtime
does not auto-destroy — your Lua decides what happens then).

### `Stats.SetHP(object, value) -> int` { #stats-sethp }

Clamps value to [0, MaxHP] and stores it as the new current HP.
Returns the stored value. No-op on entities without stats.

### `Stats.GetMaxHP(object) -> int` { #stats-getmaxhp }

0 if the entity has no PS1Stats authored.

### `Stats.GetStamina(object) -> int` { #stats-getstamina }

Current stamina. 0 if entity has no stamina system (MaxStamina = 0).

### `Stats.SetStamina(object, value) -> int` { #stats-setstamina }

Clamps to [0, MaxStamina]. Returns stored value.

### `Stats.GetMaxStamina(object) -> int` { #stats-getmaxstamina }

*No description.*

### `Stats.GetMana(object) -> int` { #stats-getmana }

Current mana. 0 if entity has no mana system (MaxMana = 0).

### `Stats.SetMana(object, value) -> int` { #stats-setmana }

Clamps to [0, MaxMana]. Returns stored value.

### `Stats.GetMaxMana(object) -> int` { #stats-getmaxmana }

*No description.*
