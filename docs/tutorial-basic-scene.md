# Tutorial: a basic interactive PS1 scene

**What you'll build:** a minimal slice of what the demo ships —
a floor, a player with third-person camera, and a cube you walk up to
and press Triangle on to see a line of dialog. You'll end with a scene
that boots on PCSX-Redux and demonstrates the five core node types
(`PS1Scene`, `PS1MeshInstance`, `PS1Player`, `PS1UICanvas`, plus a Lua
script).

**Prerequisites:** finish [`QUICKSTART.md`](../QUICKSTART.md) (or at
least confirm the shipped demo runs on your machine).

**Scope:** we skip music, cutscenes, rooms/portals, skinned meshes, and
animations. Those are all real features in the demo — start with the
basics here, then read `demo/demo.tscn` to see how the bigger ones
attach.

**Time budget:** ~20 minutes once the environment is set up.

---

## 1. Create a new scene

1. In Godot: **Scene → New Scene → Other Node**. Pick **Node3D**.
2. Save as `res://my_scene/my_scene.tscn` (create the folder).
3. Select the root node, and in the inspector click the script icon
   → **Extends → PS1Scene** (type `PS1Scene` in the search). This
   upgrades the root to a `PS1Scene` instance — it now carries fog,
   player physics config, audio clip list, and the music/sub-scene
   arrays.

Rename the root node to `Scene` for consistency.

### Tune the scene defaults (optional)

With the `Scene` node selected, look at the inspector:

- **Fog** — toggle on, pick a soft color (e.g. RGB `0.65, 0.75, 0.85`).
  Fog hides the PSX's short draw distance; turn it on unless you
  specifically want the hard cut.
- **GteScaling** — leave at `4`. Roughly "Godot units per PSX unit"
  (see `CLAUDE.md` for the math).
- **Player** — `Height = 1.7`, `Radius = 0.3`, `MoveSpeed = 3`,
  `JumpHeight = 1.2`, `Gravity = 20` is a reasonable starting set.

## 2. Add a floor

1. Add a **MeshInstance3D** child under `Scene`. Rename it `Floor`.
2. In the inspector, set **Mesh** → `New PlaneMesh`. Click the mesh,
   set **Size** to `(30, 30)`.
3. Click the script icon → **Extends → PS1MeshInstance**. This makes
   it exportable to the splashpack.
4. On the `PS1MeshInstance` component section, set **Collision** to
   `Static`. Otherwise the player walks right through it.
5. Optional: drag a PS1-friendly material onto **Material Override**.
   The plugin ships `addons/ps1godot/shaders/ps1_default.tres` —
   that's what auto-assigns to any new `PS1MeshInstance`, so you get
   the PS1 look for free.

Save. You should see a dim grey plane. Vertices will jitter if you
orbit — that's the shader working.

> **Scale baking:** if you scale a `PS1MeshInstance` via its
> `transform`, the exporter bakes that scale into the exported
> triangles. Stick to `(1, 1, 1)` scale + author the mesh at the
> intended size when possible.

## 3. Add the player

1. Add a **Node3D** child of `Scene`. Rename it `PS1Player`.
2. Position it at `(0, 1, 5)` — the spawn point. The Y coordinate is
   roughly the player's hip height; the physics body is anchored to
   feet, so 1 unit up puts them standing on the floor.
3. Script icon → **Extends → PS1Player**. The node now drives the
   runtime's first-person / third-person camera + input.

### Add a camera rig offset (third-person)

Drop a **Camera3D** child under `PS1Player`. Position it at
`(0, 1, 3)` — this is the authored offset from the player's origin in
player-local space. The runtime rotates this offset by the player's yaw
each frame, so the camera trails behind them.

> `(0, 1, 3)` = "1 unit above the player's feet-to-head midpoint,
> 3 units behind them." Tweak to taste.

### Add a visible avatar mesh (optional but nice)

Drop a **MeshInstance3D** child under `PS1Player`. Use a BoxMesh or
import an FBX humanoid — whatever you want the player to look like.
Set **Extends → PS1MeshInstance**. The runtime auto-tracks any
`PS1MeshInstance` child of `PS1Player` (position + yaw), so you don't
need any Lua to move the avatar with the player.

## 4. Add an interactive cube

1. Drop a **MeshInstance3D** child under `Scene`. Rename it `Cube`.
2. Set **Mesh** → `New BoxMesh`, size `(2, 2, 2)`. Position at
   `(-3, 1, 0)` so it sits in front of the player spawn.
3. **Extends → PS1MeshInstance**, then in the component section:
   - **Collision** → `Static`.
   - **Interactable** → `true`. Reveals interaction fields.
   - **Interact Radius** → `2.5` (meters).
   - **Show Prompt** → `true` + **Prompt Canvas Name** → `interact_prompt`
     (we'll create a canvas with that name in a second).
   - **Script File** → pick `res://my_scene/cube.lua` (we'll create
     this file next).

## 5. Create the Lua script

1. In the FileSystem dock: right-click `my_scene/` → **New → Script**.
   In the dialog, switch the **Language** dropdown to **PS1Lua** (make
   sure the plugin is enabled — PS1Lua only appears when
   `addons/ps1godot/plugin.cfg` is active).
2. Save as `cube.lua`.
3. Paste:

   ```lua
   -- Runs once when the scene's Lua VM boots and this GameObject's script is bound.
   function onCreate(self)
       Debug.Log("cube ready")
   end

   -- Runs every frame while the scene is live.
   function onUpdate(self, dt)
       -- dt is fp12 (4096 = one 30fps frame). Left empty here.
   end

   -- Runs when the player presses the Interact button (Triangle) within InteractRadius.
   function onInteract(self)
       Debug.Log("cube interacted")
       local canvas = UI.FindCanvas("dialog")
       if canvas >= 0 then
           local body = UI.FindElement(canvas, "body")
           UI.SetText(body, "Hello from the cube!")
           UI.SetCanvasVisible(canvas, true)
       end
   end
   ```

The runtime dispatches `onCreate`, `onUpdate`, and `onInteract` by name
— no base class to inherit, no wiring. Other callbacks:
`onTriggerEnter` / `onTriggerExit` (for `PS1TriggerBox`),
`onSceneCreationStart` / `onSceneCreationEnd` (for scripts attached to
the `PS1Scene` root).

## 6. Add a dialog canvas

This is the `UI.FindCanvas("dialog")` the Lua script looks up.

1. Drop a **Node** child under `Scene` (plain `Node`, not Node3D —
   UI canvases aren't spatial). Rename to `Dialog`.
2. **Extends → PS1UICanvas**. In the inspector:
   - **Canvas Name** → `dialog`
   - **Visible On Load** → `false` (hidden until the Lua script shows
     it)
   - **Residency** → `MenuOnly` (keeps it out of the gameplay-resident
     budget until it's actually shown)

3. Drop a **Node** child under `Dialog`. Rename to `Background`.
   **Extends → PS1UIElement**:
   - **Type** → `Box`
   - **X, Y, W, H** → `16, 168, 288, 56` (PS1 screen is 320×240)
   - **Color** → dark blue-ish `20, 20, 60`

4. Drop a second **Node** child, `body`. **Extends → PS1UIElement**:
   - **Type** → `Text`
   - **X, Y, W, H** → `24, 176, 272, 40`
   - **Color** → white `240, 240, 240`
   - **Text** → leave empty; the Lua script sets it at runtime.

> Element placement is pixel-exact. PS1 screen is 320×240 — author
> against that reference. The runtime's current text renderer word-wraps
> on **W** and honors `\n` for explicit line breaks.

### (Optional) interact prompt canvas

Duplicate the `Dialog` canvas, rename to `InteractPrompt`, set
**Canvas Name** → `interact_prompt`, shrink it to a small box in a
corner, and set its `body` element text to "Press △". The runtime
auto-shows this canvas whenever the player is within `InteractRadius`
of any `Interactable` with **ShowPrompt = true** + matching
**PromptCanvasName**. No Lua needed for the prompt.

## 7. Export + run

1. Save all.
2. In Godot's top menu: **Scene → Set as Main Scene** (or edit
   `project.godot` → `run/main_scene` to
   `res://my_scene/my_scene.tscn`).
3. In the **PS1Godot** dock: hit **▶ Run on PSX**.

You should see:

- The floor under fog.
- The cube a few units in front of you.
- Press forward (D-pad or left stick) to walk toward it.
- Within ~2.5 units, the interact prompt shows (if you added one).
- Press **Triangle** (F on keyboard by default in PCSX-Redux). The
  dialog canvas appears with "Hello from the cube!".

If the dialog never closes, that's expected — this tutorial doesn't
schedule a hide. See `demo/scripts/test_logger.lua` for the
audio-aware-auto-hide pattern the demo uses.

---

## Where to go from here

The shipped demo (`demo/demo.tscn`) is the reference for everything
beyond the basics. Open it and compare:

| Feature in the demo | Node / file to study |
|---|---|
| Intro cutscene with narration | `IntroCutscene` node (`PS1Cutscene`) + `test_logger.lua` |
| Spinning / bouncing animated cubes | `BounceAnim` + `SpinAnim` nodes (`PS1Animation`) |
| Checkered realm sub-scene + teleport | `PS1Scene.SubScenes` on the root + `teleport_to_realm.lua` |
| Interior room with portal culling | `PS1Room_A` + `PS1Room_B` + `PS1PortalLink_AB` |
| Sequenced music | `PS1Scene.MusicSequences` array on the root (points at `RetroAdventureSong.mid`); Lua triggers in `test_logger.lua`'s `onCreate` |
| Skinned mesh + walk animation | `SkinnedTest` subtree (`PS1SkinnedMesh`) |
| Dialog with audio-aware auto-hide | `test_logger.lua` + `Dialog` canvas + dialogue WAV clips under `demo/audio/dialogue/` |

The [`ROADMAP.md`](../ROADMAP.md) is the source of truth for what each
feature does + what's still pending.

## Quick Lua API reference

The runtime binds a handful of global tables. These are the ones you'll
use most often:

- **`Debug.Log(msg)`** — prints to PCSX-Redux console.
- **`UI.FindCanvas(name)` / `UI.FindElement(canvas, name)`** — resolve
  authored canvases + elements.
- **`UI.SetText(element, str)` / `UI.SetCanvasVisible(canvas, bool)`**
  — mutate UI.
- **`Audio.Play(clipName, vol, pan)`** — play a `PS1AudioClip` by name.
  `vol` 0–127, `pan` 0 (left) … 64 (center) … 127 (right).
- **`Audio.GetClipDuration(clipName)`** — returns clip length in 60 Hz
  frames. Useful for timing dialog against the voice clip.
- **`Music.Play(seqName, vol)` / `Music.Stop()` / `Music.SetVolume(v)`
  / `Music.GetBeat()`** — sequenced music control.
- **`Scene.Load(N)`** — swap to sub-scene N (indices into
  `PS1Scene.SubScenes`).
- **`Camera.SetMode("first" | "third")`** — flip the camera rig.
- **`Controls.SetEnabled(bool)`** — freeze input during cutscenes.
- **`Input.IsPressed(Input.TRIANGLE)` / `Input.IsHeld(...)`** — raw
  button queries.

The full set lives in `psxsplash-main/src/luaapi.cpp` + mirrored in
`luaapi.hh`. As the system grows we'll ship EmmyLua stubs for Rider /
VSCode autocomplete (Phase 3 roadmap item).

Good luck — and if you get stuck, the demo is the best reference.
