# PS1 Lua Scripting, Cross-Entity State, and Runtime Architecture Plan

**Project target:** PS1Godot / psxsplash / PS1-style chunked action RPG  
**Focus:** Lua scripting architecture, cross-entity variables, state ownership, performance, tool support, and how this should fit with PS1-style runtime constraints.

This document combines the current PS1Godot scripting direction with broader lessons from PSYQo, PSn00bSDK, and the existing project docs.

---

## 1. Core Principle

Lua should be used as the **gameplay orchestration layer**, not as an unrestricted global dumping ground.

For a larger RPG, the scripting model should be:

```text
Small per-object scripts
+ deliberate shared modules
+ event/message passing
+ chunk-owned state
+ save-friendly logical IDs
+ strict update budgets
```

Avoid:

```text
random globals
every object scanning every other object
per-frame Entity.Find calls
long-lived object handles in save data
large monolithic god scripts
unbounded event queues
hidden cross-script mutations
```

The PS1 runtime should stay predictable. The scripting layer should make behavior authorable without destroying frame time or memory discipline.

---

## 2. Why This Matters More on PS1

A modern engine can sometimes hide messy scripting behind CPU headroom. A PS1-style runtime cannot.

The main scripting risks are:

- too many `onUpdate` calls
- too much string lookup
- too many entity scans
- excessive table allocation
- unbounded event chains
- scattered global state
- unclear save ownership
- accidental references to unloaded chunks
- APIs drifting away from documentation/stubs

PSYQo’s design philosophy is useful here: keep the mandatory core small and instantiate only what is needed. PSn00bSDK examples and docs also reinforce explicit buffers, predictable rendering work, and controlled resource ownership. Bring that same mindset to Lua:

```text
Only load/run what the current chunk needs.
Only update actors that matter.
Only keep state where it belongs.
Only expose APIs that actually exist.
```

---

## 3. Recommended Script Layers

Use four main state layers.

```text
1. Entity-local state
2. Chunk / scene state
3. Global game state
4. Persistent save state
```

### 3.1 Entity-local state

Lives on `self`.

Use for data that only matters to that object instance:

```lua
self.hp
self.cooldown
self.isOpen
self.dialogueId
self.homeChunk
self.timer
self.state
```

Example:

```lua
function onEnable(self)
    self.hp = 10
    self.cooldown = 0
    self.state = "idle"
end

function onUpdate(self, dt)
    if self.cooldown > 0 then
        self.cooldown = self.cooldown - 1
    end
end
```

Important pattern:

```text
Use onEnable for pooled-object reset.
Use onCreate for one-time setup only.
```

This matches the project’s existing pooled entity guidance: spawned objects should reset in `onEnable`, because `onCreate` fires once at scene initialization.

---

### 3.2 Chunk / scene state

Lives in a named module such as `Chunk`, `SceneState`, or `ChunkState`.

Use for:

- current chunk ID
- local flags
- active camera mode
- local weather/fog state
- loaded NPC set
- local puzzle state
- local encounter state
- temporary scene progression

Example:

```lua
Chunk = Chunk or {
    id = "unknown",
    localFlags = {},
    activeActors = {},
    mode = "explore"
}

function Chunk.SetFlag(name, value)
    Chunk.localFlags[name] = value == true
end

function Chunk.GetFlag(name)
    return Chunk.localFlags[name] == true
end
```

Do not store permanent quest progress only in chunk-local state. If it must survive leaving the chunk or saving the game, promote it to `Quest` or save state.

---

### 3.3 Global game state

Use deliberately named modules.

Recommended global modules:

```text
GameState
Party
Quest
Inventory
Dialogue
AudioState
CameraState
Chunk
Bus
```

This is good:

```lua
GameState.mode = "dialogue"
Quest.SetFlag("met_blacksmith", true)
Party.gold = Party.gold + 10
```

This is bad:

```lua
talking = true
metBlacksmith = true
gold = gold + 10
```

Globals are not evil. Accidental globals are evil.

Use a small number of intentional global module tables.

---

### 3.4 Persistent save state

Save data should contain logical IDs and primitive values, not runtime handles.

Good save data:

```lua
SaveData = {
    currentChunk = "town_square_north",
    currentDiscRequired = 1,
    party = {
        gold = 120,
        hp = 34
    },
    questFlags = {
        met_blacksmith = true,
        opened_north_gate = true
    },
    inventory = {
        potion = 3,
        rusty_key = 1
    }
}
```

Bad save data:

```text
entity handle
raw memory pointer
temporary channel ID
current Lua closure
transient animation handle
temporary object index unless guaranteed stable
```

If the current runtime only supports `Persist.*`, treat that as a temporary/session persistence layer. Long-term RPG saves should eventually use a real memory-card-style `Save.*` abstraction.

---

## 4. Cross-Entity Communication

## 4.1 Direct lookup is okay for tiny cases

For one-off simple interactions:

```lua
local door = Entity.Find("blacksmith_door")
Entity.SetActive(door, false)
```

This is fine if:

- it happens rarely
- it is not inside a hot `onUpdate`
- the object is guaranteed to exist in the current chunk
- the relationship is obvious

Do not use direct lookup everywhere.

---

## 4.2 Prefer event messages for RPG behavior

For scalable RPG logic, use events:

```lua
Bus.Emit("door_opened", {
    doorId = "north_gate",
    openedBy = "player"
})
```

Then systems listen:

```lua
Bus.On("door_opened", function(data)
    if data.doorId == "north_gate" then
        Quest.SetFlag("north_gate_opened", true)
    end
end)
```

This keeps scripts less tangled.

The door does not need to know about the quest system.  
The quest system does not need to know about the door script internals.

---

## 4.3 Recommended event categories

Use events for:

```text
door_opened
door_locked
item_picked_up
npc_spoken_to
quest_flag_set
enemy_killed
chunk_entered
chunk_exited
battle_started
battle_finished
cutscene_started
cutscene_finished
save_point_used
dialogue_choice_selected
party_member_joined
shop_opened
```

Avoid using events for tight per-frame data flow.

Bad event use:

```text
player_position_changed every frame
camera_yaw_changed every frame
npc_distance_updated every frame
```

That belongs in direct state or lower-level systems.

---

## 5. Minimal Event Bus

A simple pure-Lua bus is enough to start.

```lua
Bus = Bus or {
    listeners = {},
    queue = {}
}

function Bus.On(eventName, fn)
    if Bus.listeners[eventName] == nil then
        Bus.listeners[eventName] = {}
    end
    table.insert(Bus.listeners[eventName], fn)
end

function Bus.Emit(eventName, data)
    table.insert(Bus.queue, { name = eventName, data = data })
end

function Bus.Flush(maxEvents)
    local limit = maxEvents or 32
    local count = 0

    while #Bus.queue > 0 and count < limit do
        local evt = table.remove(Bus.queue, 1)
        local listeners = Bus.listeners[evt.name]

        if listeners ~= nil then
            for i = 1, #listeners do
                listeners[i](evt.data)
            end
        end

        count = count + 1
    end

    if #Bus.queue > 0 then
        Debug.Log("Bus queue still has events; capped flush this frame")
    end
end
```

A scene-level controller should call:

```lua
function onUpdate(self, dt)
    Bus.Flush(32)
end
```

### Important PS1 rule

Cap event processing per frame.

Never allow infinite chains:

```text
event A emits event B
event B emits event C
event C emits event A
```

Use capped flushing and log overflow.

---

## 6. Better Event Bus for Larger RPGs

Eventually add:

```text
Bus.On(eventName, listenerId, fn)
Bus.Off(eventName, listenerId)
Bus.Emit(eventName, data)
Bus.EmitImmediate(eventName, data)       -- use sparingly
Bus.Flush(maxEvents)
Bus.ClearChunkListeners(chunkId)
```

Why listener IDs matter:

- chunks unload
- NPCs despawn
- pooled objects deactivate
- temporary cutscene listeners must be removed
- old callbacks must not keep firing

Recommended listener ID pattern:

```text
chunk:towngate
npc:blacksmith
system:quest
system:dialogue
cutscene:intro
```

---

## 7. GameState Module

Use `GameState` for broad runtime state.

```lua
GameState = GameState or {
    frame = 0,
    mode = "explore",
    currentChunk = "unknown",
    currentRegion = "unknown",
    currentDisc = 1
}

function GameState.SetMode(mode)
    GameState.mode = mode
    Bus.Emit("game_mode_changed", { mode = mode })
end

function GameState.IsGameplayActive()
    return GameState.mode == "explore" or GameState.mode == "battle"
end
```

Suggested modes:

```text
boot
loading
explore
dialogue
battle
cutscene
menu
paused
disc_swap
game_over
```

---

## 8. Quest Module

Use a dedicated `Quest` module early.

```lua
Quest = Quest or {
    flags = {}
}

function Quest.GetFlag(name)
    return Quest.flags[name] == true
end

function Quest.SetFlag(name, value)
    local v = value == true
    if Quest.flags[name] == v then
        return
    end

    Quest.flags[name] = v
    Bus.Emit("quest_flag_changed", { flag = name, value = v })
end
```

Example NPC:

```lua
function onInteract(self)
    if Quest.GetFlag("north_gate_opened") then
        Dialogue.Start("guard_after_gate")
    else
        Dialogue.Start("guard_before_gate")
    end
end
```

Do not scatter permanent story flags across NPC scripts.

---

## 9. Blackboard Module

A blackboard is useful for temporary AI/NPC memory.

```lua
Blackboard = Blackboard or {}

function Blackboard.For(id)
    if Blackboard[id] == nil then
        Blackboard[id] = {}
    end
    return Blackboard[id]
end
```

Example:

```lua
local bb = Blackboard.For("npc_blacksmith")
bb.mood = "annoyed"
bb.lastTalkFrame = GameState.frame
bb.homeChunk = "blacksmith_shop"
```

Use blackboards for:

```text
NPC mood
temporary AI targets
recent interaction cooldowns
local schedule state
combat target preferences
```

Do not use blackboards as the primary permanent save system.

---

## 10. Dialogue Module

A dialogue helper matters because UI primitives are low-level.

Suggested API:

```lua
Dialogue.Start("blacksmith_intro")
Dialogue.StartChoice("guard_gate", {
    { text = "Open the gate.", id = "open" },
    { text = "Never mind.", id = "leave" }
})
Dialogue.IsActive()
Dialogue.Close()
```

Internally, this can use current shipped UI APIs:

```text
UI.FindCanvas
UI.FindElement
UI.SetText
UI.SetVisible
UI.SetCanvasVisible
```

But RPG scripts should not all hand-roll UI element plumbing.

---

## 11. Inventory Module

Keep inventory data simple.

```lua
Inventory = Inventory or {
    items = {}
}

function Inventory.Add(itemId, count)
    count = count or 1
    Inventory.items[itemId] = (Inventory.items[itemId] or 0) + count
    Bus.Emit("item_added", { itemId = itemId, count = count })
end

function Inventory.Has(itemId, count)
    count = count or 1
    return (Inventory.items[itemId] or 0) >= count
end

function Inventory.Remove(itemId, count)
    count = count or 1
    if not Inventory.Has(itemId, count) then
        return false
    end

    Inventory.items[itemId] = Inventory.items[itemId] - count
    Bus.Emit("item_removed", { itemId = itemId, count = count })
    return true
end
```

Save item IDs and counts, not item object handles.

---

## 12. Party Module

Simple starting shape:

```lua
Party = Party or {
    gold = 0,
    members = {},
    hp = 10,
    maxHp = 10
}

function Party.AddGold(amount)
    Party.gold = Party.gold + amount
    Bus.Emit("gold_changed", { gold = Party.gold })
end
```

Later this can expand into party members, stats, equipment, etc.

Keep combat numbers compact and fixed-point-safe.

---

## 13. Chunk Module

Chunk state should bridge gameplay and streaming.

```lua
Chunk = Chunk or {
    current = "unknown",
    previous = "unknown",
    localFlags = {}
}

function Chunk.Enter(chunkId)
    Chunk.previous = Chunk.current
    Chunk.current = chunkId
    GameState.currentChunk = chunkId
    Bus.Emit("chunk_entered", { chunkId = chunkId })
end

function Chunk.Exit(chunkId)
    Bus.Emit("chunk_exited", { chunkId = chunkId })
end
```

Future `PS1Chunk` metadata should define:

```text
ChunkId
RegionId
DiscId
AreaArchiveId
LightingProfile
FogProfile
AudioProfile
CameraProfile
NeighborChunks
ActorBudget
EffectBudget
TextureBudget
```

Lua should refer to chunk IDs, not file paths.

---

## 14. Script Roles

For a larger RPG, standardize file roles.

```text
scene_boot.lua
  Loads/registers shared systems.

game_state.lua
  GameState module.

bus.lua
  Event queue and message dispatch.

quest.lua
  Quest flags and story progression.

party.lua
  Party/global player state.

inventory.lua
  Items and counts.

dialogue.lua
  Dialogue UI flow.

audio_router.lua
  SPU/XA/CDDA routing helpers.

camera_controller.lua
  Camera mode / zone behavior.

chunk_controller.lua
  Chunk enter/exit, local flags, transition hooks.

interaction_controller.lua
  Finds nearby interactables and dispatches interaction.

npc_<name>.lua
  Specific NPC behavior.

enemy_<type>.lua
  Enemy behavior.

door.lua
  Reusable door script.

chest.lua
  Reusable chest script.

pickup.lua
  Reusable item pickup script.
```

Avoid naming everything `manager.lua`. Use names that describe ownership.

---

## 15. Central Controllers vs Per-Object Scripts

Use both.

### Per-object scripts are best for:

```text
door behavior
chest behavior
pickup behavior
enemy behavior
NPC interaction
trigger volumes
simple props
```

### Central controllers are best for:

```text
input mode
UI flow
dialogue
quest flags
chunk transitions
audio routing
camera controller
event bus flush
save/load coordination
```

This matches the existing project guidance: small per-object scripts are preferred, with a few intentional central controllers for scene-level systems.

---

## 16. Update Frequency Strategy

Do not run everything every frame.

Use update tiers.

```text
Every frame:
  player
  camera
  active enemies
  active interactables
  dialogue/menu UI
  event bus flush

Every 4 frames:
  passive nearby NPC logic
  ambient emitters
  simple proximity checks

Every 16 frames:
  offscreen schedules
  low-priority triggers
  symbolic world state

On chunk load:
  NPC placement
  local quest checks
  area state reconstruction
  audio/fog/camera profile application
```

Example:

```lua
function onUpdate(self, dt)
    GameState.frame = GameState.frame + 1

    PlayerController.Update(dt)
    CameraController.Update(dt)
    Bus.Flush(32)

    if GameState.frame % 4 == 0 then
        NPCController.UpdateSlow()
    end

    if GameState.frame % 16 == 0 then
        Schedule.UpdateSymbolic()
    end
end
```

---

## 17. Avoid Hot-Loop Entity Searches

Bad:

```lua
function onUpdate(self, dt)
    local door = Entity.Find("north_gate")
    local guard = Entity.Find("guard")
end
```

Better:

```lua
function onCreate(self)
    self.door = Entity.Find("north_gate")
    self.guard = Entity.Find("guard")
end
```

For pooled or chunk-loaded objects, cache on `onEnable` instead.

If handles can become invalid after chunk unload, store logical IDs and reacquire on chunk enter.

---

## 18. Stable IDs vs Runtime Handles

Every important object should have a stable logical ID.

```text
north_gate
npc_blacksmith
chest_old_well
chunk_town_square_north
quest_missing_cart
item_rusty_key
```

Use runtime handles only for active objects.

Good:

```lua
self.doorId = "north_gate"
local door = Entity.Find(self.doorId)
```

Bad:

```lua
SaveData.northGateHandle = door
```

---

## 19. Cross-Chunk References

Cross-chunk references should be logical.

Example:

```lua
Transition.Request("forest_gate_02")
```

Not:

```lua
Entity.SetPosition(Entity.Find("player"), Vec3.new(...)) -- into unloaded space
```

The transition system should:

1. validate target chunk
2. check disc/content availability
3. unload or suspend current chunk
4. load target archive
5. restore player spawn point
6. apply fog/sky/audio/camera profiles
7. emit `chunk_entered`

---

## 20. Save System Shape

Future save module:

```lua
Save = Save or {}

function Save.Build()
    return {
        saveVersion = 1,
        currentChunk = GameState.currentChunk,
        currentDiscRequired = GameState.currentDisc,
        party = Party.Export(),
        inventory = Inventory.Export(),
        questFlags = Quest.Export(),
        chunkFlags = Chunk.ExportPersistent()
    }
end

function Save.Apply(data)
    Party.Import(data.party)
    Inventory.Import(data.inventory)
    Quest.Import(data.questFlags)
    Chunk.ImportPersistent(data.chunkFlags)
    GameState.currentChunk = data.currentChunk
end
```

The save module should reject or migrate mismatched versions.

---

## 21. Memory and Allocation Discipline

Lua tables are convenient, but avoid creating many temporary tables in hot paths.

Avoid:

```lua
function onUpdate(self, dt)
    Bus.Emit("pos", { x = self.x, y = self.y, z = self.z }) -- every frame
end
```

Prefer:

```lua
-- Keep event data for meaningful transitions, not high-frequency telemetry.
```

Rules:

```text
No table allocation spam in per-frame loops.
No debug string concatenation spam in hot paths.
No unbounded arrays for logs/events.
Cap event queues.
Reuse common tables only if safe.
Prefer integers/fixed-point values where possible.
```

---

## 22. Debug Logging Rules

`Debug.Log` is useful but slow.

Use it for:

```text
boot summary
chunk load summary
warnings
failed lookups
invalid state
one-time diagnostics
```

Avoid:

```text
per-frame logs
per-entity logs in large loops
logs inside particle/effect update
logs during event storms
```

Add helper:

```lua
DebugOnce = DebugOnce or {}

function LogOnce(key, msg)
    if DebugOnce[key] then
        return
    end
    DebugOnce[key] = true
    Debug.Log(msg)
end
```

---

## 23. Lua API IDL and Stubs

The project already has generated EmmyLua stubs. This should become more formal as the API grows.

Recommended direction:

```text
lua_api.yaml
  modules
  functions
  parameters
  return values
  docs
  availability
  runtime support status
```

Generate:

```text
C++ registration tables
EmmyLua stubs
Markdown docs
Godot tooltip text
validation tests
```

This prevents drift between:

```text
runtime binding
Godot plugin
docs
IDE autocomplete
agent prompts
```

The current docs already call out that the hand-written Lua API needs a single source of truth. This should become more important as RPG scripting expands.

---

## 24. Lua Profiler / Observability

Add a script budget overlay eventually.

Track:

```text
per-hook time
per-script time
event queue size
entity update count
slowest scripts
number of Entity.Find calls
number of active Lua objects
Debug.Log count
```

Example overlay:

```text
Lua total: 1.20 ms
onUpdate(player_controller): 0.18 ms
onUpdate(camera_controller): 0.12 ms
onUpdate(npc_controller): 0.08 ms
Bus.Flush: 0.05 ms
Entity.Find calls: 2
Event queue: 4 / 32
```

Warnings:

```text
Script npc_schedule.lua used 2.4 ms this frame.
Move schedule checks to a slower tick.
```

```text
Entity.Find called 180 times this frame.
Cache handles or use tags/manager lists.
```

---

## 25. Host-Mode Tests for Lua

A host/test build should eventually validate scripts without booting the emulator.

Test goals:

```text
All Lua files parse.
All required modules load in the intended order.
All referenced API calls exist.
All dialogue IDs resolve.
All quest flags are declared.
All chunk IDs resolve.
All item IDs resolve.
All transitions target valid chunks.
All save data can round-trip.
Event bus cannot infinite-loop.
```

This lines up with the broader host-build/test direction already proposed for psxsplash.

---

## 26. Script Loading Order

Define it explicitly.

Recommended order:

```text
1. core/bus.lua
2. core/gamestate.lua
3. core/quest.lua
4. core/party.lua
5. core/inventory.lua
6. core/dialogue.lua
7. core/audio_router.lua
8. core/camera_controller.lua
9. core/chunk.lua
10. scene/chunk-specific controllers
11. per-object scripts
```

If the runtime does not support `require`, have the exporter or scene config order scripts deliberately.

Avoid relying on filesystem order.

---

## 27. Namespacing Rules

Use table namespaces.

Good:

```lua
Quest.SetFlag("met_blacksmith", true)
Inventory.Add("potion", 1)
Dialogue.Start("blacksmith_intro")
```

Bad:

```lua
SetFlag("met_blacksmith", true)
AddItem("potion", 1)
StartDialogue("blacksmith_intro")
```

Namespacing prevents collisions and makes autocomplete useful.

---

## 28. Fixed-Point and Coordinate Gotchas

Keep scripting helpers around PS1-specific math.

The current Lua cheatsheet already highlights:

- decimals are rewritten to fixed-point
- some numeric forms are skipped by the rewriter
- +Y points down on PS1
- third-person `Player.GetPosition()` returns the camera head, not body
- `Input.IsPressed` is edge-triggered, while `Input.IsHeld` is continuous
- `Debug.Log` should not be spammed

Build helpers for common mistakes:

```lua
function IsButtonJustPressed(btn)
    return Input.IsPressed(btn)
end

function IsButtonDown(btn)
    return Input.IsHeld(btn)
end
```

And document camera/player-space helpers clearly.

---

## 29. Data-Driven Definitions

For RPG scale, avoid hardcoding every item/NPC/quest in behavior scripts.

Use definition tables:

```lua
Items = {
    rusty_key = {
        name = "Rusty Key",
        type = "key"
    },
    potion = {
        name = "Potion",
        type = "consumable",
        heal = 20
    }
}
```

```lua
NPCDefs = {
    blacksmith = {
        displayName = "Mara",
        defaultDialogue = "blacksmith_intro",
        homeChunk = "blacksmith_shop"
    }
}
```

```lua
QuestDefs = {
    missing_cart = {
        flags = {
            "missing_cart_started",
            "missing_cart_found",
            "missing_cart_returned"
        }
    }
}
```

Later, these can move to exported data blobs instead of raw Lua tables if memory demands it.

---

## 30. Module Export/Import Pattern

Each persistent module should support export/import.

```lua
function Quest.Export()
    return Quest.flags
end

function Quest.Import(flags)
    Quest.flags = flags or {}
end
```

```lua
function Inventory.Export()
    return Inventory.items
end

function Inventory.Import(items)
    Inventory.items = items or {}
end
```

This makes save/load architecture cleaner.

---

## 31. Disc and Multi-Disc Awareness

For multi-disc support, Lua must never assume all content exists.

Useful helpers:

```lua
Disc = Disc or {
    current = 1,
    count = 1
}

function Disc.Require(discId)
    if Disc.current ~= discId then
        GameState.SetMode("disc_swap")
        Bus.Emit("disc_required", { discId = discId })
        return false
    end
    return true
end
```

Transitions should know required disc:

```lua
Transition.Request("late_game_castle")
```

The transition system checks metadata:

```text
target chunk exists on current disc?
if not, show disc swap UI
```

Do not let random scripts directly request unloaded cross-disc content.

---

## 32. Audio Routing from Lua

Lua should call logical audio helpers instead of knowing backend details.

High-level API:

```lua
AudioRouter.PlaySfx("door_creak")
AudioRouter.PlayMusic("forest_theme")
AudioRouter.PlayAmbient("forest_wind")
AudioRouter.StopMusic()
```

Internal route metadata decides:

```text
SPU
XA
CDDA
Auto
```

If XA/CDDA is scaffolded only, the helper should log a clear unsupported route.

Do not fake a successful playback path.

---

## 33. Camera from Lua

Camera scripts should use camera profiles/zones rather than arbitrary scattered camera math.

Desired high-level helpers:

```lua
CameraController.SetMode("third_person")
CameraController.SetZone("town_square_north")
CameraController.LoadPreset("blacksmith_shop_counter")
CameraController.ResetBehindPlayer()
```

Until runtime APIs exist, keep this as architecture/scaffolding.

---

## 34. UI from Lua

UI primitives are powerful but verbose.

Wrap common flows:

```lua
Toast.Show("Picked up Rusty Key", 90)
Dialog.Show("blacksmith_intro")
HUD.SetGold(Party.gold)
HUD.SetHP(Party.hp, Party.maxHp)
Menu.Open("inventory")
```

Use raw `UI.*` only in wrapper modules and power-user scripts.

This matches the project UI/UX direction: prefer helpers and prefabs for common author needs while keeping primitive APIs available.

---

## 35. Recommended First Lua Files

Add these first:

```text
lua/core/bus.lua
lua/core/gamestate.lua
lua/core/quest.lua
lua/core/party.lua
lua/core/inventory.lua
lua/core/dialogue.lua
lua/core/chunk.lua
lua/core/audio_router.lua
lua/core/debug_helpers.lua
```

Then add scene/game-specific files:

```text
lua/game/player_controller.lua
lua/game/camera_controller.lua
lua/game/interaction_controller.lua
lua/game/chunk_controller.lua
```

Reusable object scripts:

```text
lua/objects/door.lua
lua/objects/chest.lua
lua/objects/pickup.lua
lua/objects/trigger_transition.lua
lua/objects/npc_basic.lua
```

---

## 36. Immediate Implementation Order

### Step 1 — Document the Lua architecture

Add this architecture to the docs so future agent sessions follow the same rules.

### Step 2 — Add core module skeletons

Create:

```text
bus.lua
gamestate.lua
quest.lua
inventory.lua
party.lua
debug_helpers.lua
```

Keep them tiny.

### Step 3 — Add script loading order

Make script order explicit in scene/export metadata.

### Step 4 — Add validation

Exporter should warn on:

```text
missing script file
duplicate script ID
script path typo
unknown module reference if detectable
global module file missing
```

### Step 5 — Add event bus cap

Prevent runaway event loops.

### Step 6 — Add ID/stub discipline

Make sure every helper is documented and reflected in EmmyLua stubs if exposed.

### Step 7 — Add profiling later

Per-script timing is not needed first, but it should be planned early.

---

## 37. IDE-Agent Prompt

Use this when asking an IDE coding agent to implement the next slice.

```text
You are helping me improve the Lua scripting architecture for my PS1Godot / psxsplash project.

Goal:
Create a scalable Lua scripting foundation for a larger chunk-based PS1-style RPG without breaking existing jam/demo scripts.

Important constraints:
- Use only currently shipped runtime APIs unless clearly adding scaffolded helpers.
- Do not invent runtime APIs that do not exist.
- Keep Lua lightweight; PS1 runtime has limited CPU/RAM.
- Avoid accidental globals.
- Prefer entity-local state on self.
- Prefer named global module tables for intentional shared state.
- Prefer event messages over tight cross-entity coupling.
- Do not save runtime handles.
- Save logical IDs and primitive values.
- Cap event processing per frame.
- Do not call Entity.Find in hot update loops.
- Debug.Log is slow; do not spam it.

Implement or scaffold:
1. lua/core/bus.lua
   - Bus.On
   - Bus.Emit
   - Bus.Flush(maxEvents)
   - capped queue processing
   - optional Bus.Clear if safe

2. lua/core/gamestate.lua
   - GameState.mode
   - GameState.currentChunk
   - GameState.frame
   - GameState.SetMode
   - GameState.IsGameplayActive

3. lua/core/quest.lua
   - Quest.flags
   - Quest.GetFlag
   - Quest.SetFlag
   - emits quest_flag_changed

4. lua/core/inventory.lua
   - Inventory.items
   - Inventory.Add
   - Inventory.Remove
   - Inventory.Has

5. lua/core/party.lua
   - Party.gold
   - Party.hp
   - Party.maxHp
   - Party.AddGold

6. lua/core/debug_helpers.lua
   - LogOnce(key, msg)

7. Documentation:
   - explain self-local state
   - explain module state
   - explain event bus
   - explain save-friendly logical IDs
   - explain update frequency tiers
   - explain direct Entity.Find vs event messages

Rules:
- Keep modules tiny and readable.
- Do not require memory-card save implementation yet.
- Do not rewrite existing scripts unless necessary.
- If require/include is unsupported, document how these modules should be loaded by the exporter or scene script order.
- Add EmmyLua-friendly annotations/comments where useful.
- Provide a changed-files list and how to test.
```

---

## 38. Bottom Line

For a PS1 RPG, Lua should provide:

```text
clear behavior
small scripts
controlled communication
save-friendly state
chunk-aware logic
low update cost
good tooling
```

The best scripting architecture is not “no globals.”  
It is:

```text
few intentional global modules
+ entity-local self state
+ event-driven cross-system communication
+ logical IDs
+ strict update budgets
```

That keeps the game authorable without letting the script layer become the slowest and most confusing part of the engine.
