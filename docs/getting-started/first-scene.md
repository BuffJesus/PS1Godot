# Your first PS1 scene

**What you'll build:** a scene with a floor, a third-person player,
and a cube you walk up to and press Triangle on to see a line of
dialog. Along the way you'll get the PS1 look-and-feel — vertex
jitter, nearest-neighbor texturing, the 2× color modulate, and fog —
configured the way every other scene in the project will use it.

By the end you'll have something that boots on PCSX-Redux, plus a
working mental model of the five core node types
(`PS1Scene`, `PS1MeshInstance`, `PS1Player`, `PS1UICanvas`, plus a
Lua script).

**Prerequisites:** [`installation.md`](installation.md) through
Phase 0, and ideally [`quickstart.md`](quickstart.md) so the
shipped demo runs on your machine.

**Time budget:** ~40 minutes.

---

## 1. Open the project and look at the demo

```bat
scripts\launch-editor.cmd
```

(Use `scripts/launch-editor.sh` on Linux/macOS.)

Godot builds the C# assembly on first open (30–60s). Once it's done,
the Output dock (bottom panel) should show:

```
[PS1Godot] Plugin enabled.
```

If you see `The type 'PS1Scene' could not be resolved`, the C# build
hasn't finished or errored. Hit the **hammer icon** (top-right of the
editor) to rebuild, then close and reopen the tab.

In the FileSystem dock (bottom-left), double-click `demo/demo.tscn`.
Press **F** on the camera node to focus the viewport, then orbit by
holding the middle mouse button.

**What to notice:**

- The cube's edges **jitter** when you orbit slowly. That's the
  vertex snap from `ps1.gdshader` pulling clip-space xy onto a
  320×240 grid.
- Vertices **pop** as they cross grid boundaries. This is the
  artifact, not a bug — it's the thing you're here to capture.
- Textures are crisp-pixelated, not bilinear-filtered.
- Far geometry fades into fog.

The demo is the reference. Browse it whenever you want to see how a
feature is wired up.

## 2. Create a new scene

1. **Scene → New Scene → Other Node**. Pick **Node3D**.
2. Save as `res://my_scene/my_scene.tscn` (create the folder).
3. Select the root, click the script icon in the inspector
   → **Extends → PS1Scene** (type `PS1Scene` in the search). This
   upgrades the root to a `PS1Scene` instance — it now carries fog,
   player physics config, audio clip list, and the music/sub-scene
   arrays.
4. Rename the root node to `Scene` for consistency.

### Tune the scene defaults

With the `Scene` node selected, in the inspector:

- **Fog** — toggle on, pick a soft color (e.g. RGB
  `0.65, 0.75, 0.85`). Fog hides the PSX's short draw distance;
  turn it on unless you specifically want the hard cut.
- **GteScaling** — leave at `4`. Roughly "Godot units per PSX unit"
  (see [`CLAUDE.md`](https://github.com/BuffJesus/PS1Godot/blob/main/CLAUDE.md){ target="_blank" }
  for the math).
- **Player** — `Height = 1.7`, `Radius = 0.3`, `MoveSpeed = 3`,
  `JumpHeight = 1.2`, `Gravity = 20` is a reasonable starting set.

## 3. Add a floor

1. Add a **MeshInstance3D** child under `Scene`. Rename it `Floor`.
2. **Mesh** → `New PlaneMesh`. Click the mesh, set **Size** to
   `(30, 30)`.
3. Script icon → **Extends → PS1MeshInstance**. This makes it
   exportable to the splashpack.
4. On the `PS1MeshInstance` component section, set **Collision** to
   `Static`. Otherwise the player walks right through it.

Save. You should see a dim grey plane. Vertices will jitter if you
orbit — that's the shader working.

The plugin ships `addons/ps1godot/shaders/ps1_default.tres` as the
material that auto-assigns to any new `PS1MeshInstance` at runtime.
For editor-time preview, drag it onto **Material Override** in the
inspector.

!!! note "Scale baking"
    If you scale a `PS1MeshInstance` via its `transform`, the
    exporter bakes that scale into the exported triangles. Stick to
    `(1, 1, 1)` scale + author the mesh at the intended size when
    possible.

## 4. Texture the floor and tune the look

Drop a PNG into `godot-ps1/` — e.g., a small 64×64 tile or concrete
texture. For this tutorial assume it's at `godot-ps1/my_scene/tile.png`.

!!! info "PS1 texture constraint primer"
    The real PS1 supports 4bpp (16 colors), 8bpp (256 colors), and
    16bpp direct-color textures. Anything bigger gets quantized at
    export time. For preview, use whatever — full quality renders in
    the editor. Stay ≤128×128 to stay in the spirit.

In the editor:

1. Click the **Floor** node.
2. In the inspector, expand **Material Override → Shader Parameters
   → texture**.
3. Drag `tile.png` onto the **Albedo Tex** slot.

The floor now shows the tile texture, crisply pixelated because the
shader forces `filter_nearest`.

### Tweak the PS1 feel

Select the material and play with:

- **Snap → Snap Resolution** — drop to `Vector2(160, 120)` for
  *heavy* jitter (PSX Demakes territory). Raise to `Vector2(640, 480)`
  to almost disable it.
- **Modulate → Modulate Scale** — `2.0` is PS1-correct; `1.0` looks
  like a normal Godot material with nearest filtering.
- **Snap → Snap Enabled** — toggle off to confirm the shader is
  doing the work.

### Preview the fog

The `PS1Scene` node stores fog settings as export-time metadata (for
the splashpack header), not as live shader uniforms. To visually
preview the fog right now, open the material in the inspector and set:

- **Fog → Fog Enabled** ✔
- **Fog → Fog Color** — match the scene's fog color
- **Fog → Fog Near** `5`
- **Fog → Fog Far** `25`

!!! warning "Phase 1 limitation"
    Right now you have to set fog on both the `PS1Scene` (for export
    metadata) and the material (for preview). Phase 1's subviewport
    work will auto-propagate scene fog to all PS1 materials. For
    now, keep them in sync manually.

## 5. Add the player

1. Add a **Node3D** child of `Scene`. Rename it `PS1Player`.
2. Position it at `(0, 1, 5)` — the spawn point. Y is roughly the
   player's hip height; the physics body is anchored to feet.
3. Script icon → **Extends → PS1Player**. The node now drives the
   runtime's first-person / third-person camera + input.

### Add a camera rig offset (third-person)

Drop a **Camera3D** child under `PS1Player`. Position at
`(0, 1, 3)` — the authored offset from the player's origin in
player-local space. The runtime rotates this offset by the player's
yaw each frame, so the camera trails behind them.

> `(0, 1, 3)` = "1 unit above the player's feet-to-head midpoint,
> 3 units behind them." Tweak to taste.

Configure the camera while it's selected:

- **FOV** `72` (PS1 games typically ran 60–90°)
- **Near** `0.2`
- **Far** `60`

Click the camera-preview icon in the viewport toolbar (small camera
glyph) to see the scene through the camera.

### Add a visible avatar mesh (optional but nice)

Drop a **MeshInstance3D** child under `PS1Player`. Use a `BoxMesh`
or import an FBX humanoid. Set **Extends → PS1MeshInstance**. The
runtime auto-tracks any `PS1MeshInstance` child of `PS1Player`
(position + yaw), so no Lua needed to move the avatar with the
player.

## 6. Add an interactive cube

1. **MeshInstance3D** child under `Scene`. Rename to `Cube`.
2. **Mesh** → `New BoxMesh`, size `(2, 2, 2)`. Position at
   `(-3, 1, 0)` so it sits in front of the player spawn.
3. **Extends → PS1MeshInstance**, then in the component section:
    - **Collision** → `Static`.
    - **Interactable** → `true`. Reveals interaction fields.
    - **Interact Radius** → `2.5` (meters).
    - **Show Prompt** → `true` + **Prompt Canvas Name** →
      `interact_prompt` (we'll create that canvas in step 9).
    - **Script File** → pick `res://my_scene/cube.lua` (we'll create
      this file next).

## 7. Create the Lua script

1. In the FileSystem dock: right-click `my_scene/` → **New →
   Script**. In the dialog, switch the **Language** dropdown to
   **PS1Lua** (only visible when the plugin is enabled).
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

The runtime dispatches `onCreate`, `onUpdate`, and `onInteract` by
name — no base class to inherit, no wiring. Other callbacks:
`onTriggerEnter` / `onTriggerExit` (for `PS1TriggerBox`),
`onSceneCreationStart` / `onSceneCreationEnd` (for scripts attached
to the `PS1Scene` root).

See the [Lua API reference](../lua-api/index.md) for the full set.

## 8. Add a dialog canvas

This is what `UI.FindCanvas("dialog")` resolves in the Lua script.

1. Drop a **Node** child under `Scene` (plain `Node`, not Node3D —
   UI canvases aren't spatial). Rename to `Dialog`.
2. **Extends → PS1UICanvas**:
    - **Canvas Name** → `dialog`
    - **Visible On Load** → `false` (hidden until the Lua script
      shows it)
    - **Residency** → `MenuOnly` (keeps it out of the
      gameplay-resident budget until shown)
3. Drop a **Node** child under `Dialog`, rename to `Background`.
   **Extends → PS1UIElement**:
    - **Type** → `Box`
    - **X, Y, W, H** → `16, 168, 288, 56` (PS1 screen is 320×240)
    - **Color** — dark blue-ish `20, 20, 60`
4. Drop a second **Node** child, `body`. **Extends → PS1UIElement**:
    - **Type** → `Text`
    - **X, Y, W, H** → `24, 176, 272, 40`
    - **Color** — white `240, 240, 240`
    - **Text** — leave empty; the Lua script sets it at runtime.

!!! tip "PS1 screen is 320×240"
    Element placement is pixel-exact. Author against that reference.
    The runtime's current text renderer word-wraps on **W** and
    honors `\n` for explicit line breaks.

### Optional: an interact prompt canvas

Duplicate `Dialog`, rename to `InteractPrompt`, set **Canvas Name**
→ `interact_prompt`, shrink to a small box in a corner, set its
`body` element text to "Press △". The runtime auto-shows this canvas
whenever the player is within `InteractRadius` of any `Interactable`
with **ShowPrompt = true** + matching **PromptCanvasName**. No Lua
needed for the prompt.

## 9. Run on PSX

1. Save all.
2. **Scene → Set as Main Scene** (or edit `project.godot` →
   `run/main_scene` to `res://my_scene/my_scene.tscn`).
3. In the **PS1Godot** dock: hit **▶ Run on PSX**.

You should see:

- The textured floor under fog.
- The cube a few units in front of you.
- Press forward (D-pad or left stick) to walk toward it.
- Within ~2.5 units, the interact prompt shows (if you added one).
- Press **Triangle** (F on keyboard by default in PCSX-Redux). The
  dialog canvas appears with "Hello from the cube!".

If the dialog never closes, that's expected — this tutorial doesn't
schedule a hide. See `demo/scripts/test_logger.lua` for the
audio-aware auto-hide pattern.

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
| Sequenced music | `PS1Scene.MusicSequences` on the root + `RetroAdventureSong.mid` |
| Skinned mesh + walk animation | `SkinnedTest` subtree (`PS1SkinnedMesh`) |
| Dialog with audio-aware auto-hide | `test_logger.lua` + `Dialog` canvas + `demo/audio/dialogue/` |

For the bigger picture:

- The [Lua API reference](../lua-api/index.md) lists all 24
  runtime-bound namespaces with worked examples mined from the demo.
- [`ROADMAP.md`](https://github.com/BuffJesus/PS1Godot/blob/main/ROADMAP.md){ target="_blank" }
  tracks what's shipped vs pending.
- [`GLOSSARY.md`](https://github.com/BuffJesus/PS1Godot/blob/main/GLOSSARY.md){ target="_blank" }
  if PS1 terms (CLUT, TPage, GTE, OT) are new — you'll encounter
  them constantly.

## Lua API quick reference

The runtime binds a handful of global tables. These are the ones
you'll use most often:

- **`Debug.Log(msg)`** — prints to PCSX-Redux console.
- **`UI.FindCanvas(name)` / `UI.FindElement(canvas, name)`** —
  resolve authored canvases + elements.
- **`UI.SetText(element, str)` / `UI.SetCanvasVisible(canvas, bool)`**
  — mutate UI.
- **`Audio.Play(clipName, vol, pan)`** — play a `PS1AudioClip` by
  name. `vol` 0–127, `pan` 0 (left) … 64 (center) … 127 (right).
- **`Audio.GetClipDuration(clipName)`** — returns clip length in
  60 Hz frames. Useful for timing dialog against a voice clip.
- **`Music.Play(seqName, vol)` / `Music.Stop()`
  / `Music.SetVolume(v)` / `Music.GetBeat()`** — sequenced music
  control.
- **`Scene.Load(N)`** — swap to sub-scene N (indices into
  `PS1Scene.SubScenes`).
- **`Camera.SetMode("first" | "third")`** — flip the camera rig.
- **`Controls.SetEnabled(bool)`** — freeze input during cutscenes.
- **`Input.IsPressed(Input.TRIANGLE)` / `Input.IsHeld(...)`** — raw
  button queries.

Full surface: [Lua API → Overview](../lua-api/index.md). Source of
truth lives in `psxsplash-main/src/luaapi.hh`; the docs pages are
auto-generated from those signature comments.

## Troubleshooting

**The cube has no vertex jitter.**
Check that the material's shader is `ps1.gdshader`, not the Godot
default. Confirm `snap_enabled` is ✔ in shader parameters.

**The cube is way too bright or way too dark.**
PSYQo treats vertex-color 128 as neutral; our shader compensates with
a 2× multiply (`modulate_scale`). If your mesh doesn't have vertex
colors set, Godot feeds `(1,1,1,1)`, which gets *doubled*, blowing
out the image. Either set `modulate_scale` to `1.0` for untreated
meshes, or bake vertex colors (Phase 2 will automate this).

**Textures look bilinearly filtered.**
Texture import → **Import dock → Flags → Filter → Off**. The shader
itself uses `filter_nearest`, but Godot's import pipeline can
re-upsample upstream.

**"PS1Scene" doesn't appear in Add Node.**
`[GlobalClass]` requires a built C# assembly. Build the solution
first (hammer icon). If that doesn't help, open `PS1Godot.sln` in
Rider and build from there.

**`Run on PSX` errors out before launching PCSX-Redux.**
The export gate refuses to launch if any `PS1MeshInstance` is
non-`Static` collision but missing its mesh, or any `PS1UICanvas`
has duplicate `Canvas Name`s. Read the export-error output in the
Godot console — every gate failure points at the offending node.
