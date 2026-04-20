# Tutorial: your first PS1 scene

**What you'll build:** a small PS1-looking scene — a textured crate on a tiled
floor under fog, viewed through a PS1 camera — with vertex jitter, nearest-
neighbor texturing, and the 2× color modulate that gives PS1 games their
characteristic punch.

**What you need first:** complete [`SETUP.md`](../SETUP.md) through Phase 0.

**Scope:** this tutorial ends at a preview-quality scene in the Godot
viewport. Exporting the scene to PSX is a separate workflow — once the
plugin's dock is visible, the **▶ Run on PSX** button builds a
splashpack and launches PCSX-Redux for you. Everything you author here
ends up in the splashpack unchanged.

**Time budget:** ~20 minutes.

---

## 1. Open the project

```bat
scripts\launch-editor.cmd
```

Godot builds the C# assembly on first open (30–60s). Once it's done, the
Output dock (bottom panel) should show:

```
[PS1Godot] Plugin enabled.
```

If you see `The type 'PS1Scene' could not be resolved`, the C# build hasn't
finished or errored. Hit the **hammer icon** (top-right of the editor) to
rebuild, then close and reopen the tab.

## 2. Look at the demo scene

In the FileSystem dock (bottom-left), double-click `demo/demo.tscn`.

You should see a cube sitting on a plane at a fixed angle, all rendered with
flat white shading. Press **F** on the camera node to focus the viewport.
Orbit the view by holding the middle mouse button.

**What to notice:**

- The cube's edges **jitter** when you orbit slowly. That's the vertex snap
  from `ps1.gdshader` pulling clip-space xy onto a 320×240 grid.
- Vertices **pop** as they cross grid boundaries. This is the artifact, not a
  bug — it's the thing you're here to capture.
- Without a texture, everything looks flat white. We'll fix that now.

## 3. Add a texture

Drop a PNG into `godot-ps1/` — e.g., a small 64×64 crate texture. For this
tutorial I'll assume it's at `godot-ps1/demo/crate.png`.

> **PS1 constraint primer.** The real PS1 supports 4bpp (16 colors), 8bpp (256
> colors), and 16bpp direct-color textures. Anything more will be quantized at
> export time (Phase 2). For now, use whatever — the preview renders full
> quality. Use small textures (≤128×128) to stay in the spirit.

In the editor:

1. Click the **Cube** node.
2. In the Inspector, expand **Material Override → Shader Parameters → texture**.
3. Drag `crate.png` onto the **Albedo Tex** slot.

The cube now shows the crate texture, crisply pixelated because the shader
forces `filter_nearest`.

Repeat for **Floor** with a different texture if you like (e.g., a tile or
concrete image), or share the same one.

## 4. Turn on fog

In **Scene** (the root node), the inspector shows a **Fog** group. Toggle:

- `Fog Enabled`: ✔
- `Fog Color`: `#404060` (dark blue-grey — SplashEdit's default)
- `Fog Density`: 5

Nothing happens in the viewport yet. **Why?** The `PS1Scene` node stores fog
settings as export-time metadata (for the splashpack header), not as live
shader uniforms. To visually preview the fog right now, open the cube's
material in the inspector and set:

- `Fog → Fog Enabled`: ✔
- `Fog → Fog Color`: match the scene's
- `Fog → Fog Near`: 5
- `Fog → Fog Far`: 25

Then orbit backwards. The farther cube fades to the fog color.

> **Phase 1 limitation.** Right now you have to set fog on both the PS1Scene
> (for export metadata) and the material (for preview). Phase 1's subviewport
> work will auto-propagate scene fog to all PS1 materials in the scene. For
> now, keep them in sync manually.

## 5. Tweak the PS1 feel

Select a material and play with:

- **Snap → Snap Resolution**: drop to `Vector2(160, 120)` for *heavy* jitter
  (PSX Demakes territory). Raise to `Vector2(640, 480)` to almost disable it.
- **Modulate → Modulate Scale**: `2.0` is PS1-correct; `1.0` looks like a
  normal Godot material with nearest filtering.
- **Snap → Snap Enabled**: toggle off to confirm the shader is doing the work.

## 6. Move the camera

Select the **Camera** node. It's a `PS1Camera` — right now just a tagged
`Camera3D`. Set:

- **FOV**: `72` (PS1 games typically ran 60–90°)
- **Near**: `0.2`
- **Far**: `60`

Click the camera-preview icon in the viewport toolbar (looks like a small
camera) to see the scene from the camera's perspective. Move the camera
with G/R/S (Godot transform gizmos).

## 7. Add a new mesh

From the Add Node dialog (`Ctrl+A`):

1. Add a `Node3D` under the Scene if you want a group, or just add to Scene.
2. Add a `MeshInstance3D` child.
3. In the inspector, set **Mesh** to a new `BoxMesh` (or import a `.glb`).
4. Attach the PS1 script: drag
   `res://addons/ps1godot/nodes/PS1MeshInstance.cs` onto the **Script** slot.
5. Leave **Material Override** empty. When the scene plays, `PS1MeshInstance._Ready`
   will auto-apply `ps1_default.tres`. For editor-time preview, drag
   `ps1_default.tres` onto the slot manually.

> Once you have a few meshes, `Ctrl+S` to save. If Godot grumbles about
> unknown types, rebuild C# with the hammer icon.

## 8. What a Lua script will look like (preview)

When Phase 2 step 5 lands, you'll attach a `.lua` file directly to a
`PS1MeshInstance` via the inspector's **Script File** property. The file will
use the psxsplash Lua API verbatim. For a door that opens on interact:

```lua
-- crate.lua
function onEnable(self)
    self.broken = false
end

function onInteract(self)
    if not self.broken then
        Audio.Play("crate_break", 100, 0)
        Entity.SetActive(self, false)
        self.broken = true
    end
end
```

See [the API reference](../psxsplash-main/src/luaapi.hh) for all functions.

You can write and version-control `.lua` files today — they just won't run
in-editor until the `ScriptLanguageExtension` lands.

## 9. Save your scene

`Ctrl+S` → `res://demo/my_first_scene.tscn`. Commit it when you're happy.

## 10. Where to next

- **Phase 1 is still in progress.** Low-resolution subviewport rendering is
  the biggest visual win still coming; when it lands you'll see the scene
  rendered at true 320×240 and upscaled.
- **Phase 2 (exporter) is what makes the scene *run*.** Without it, scenes are
  Godot-only art assets. Follow `ROADMAP.md` for status.
- **Read [`GLOSSARY.md`](../GLOSSARY.md)** if PS1 terms (CLUT, TPage, GTE,
  OT) are new — you'll encounter them constantly in the next phases.

## Troubleshooting

**The cube has no vertex jitter.**
Check that the material's shader is `ps1.gdshader`, not the Godot default.
Also confirm `snap_enabled` is ✔ in shader parameters.

**The cube is way too bright or way too dark.**
PSYQo treats vertex-color 128 as neutral; our shader compensates with a 2×
multiply (`modulate_scale`). If your mesh doesn't have vertex colors set,
Godot feeds `(1,1,1,1)`, which gets *doubled*, blowing out the image. Either:
(a) set `modulate_scale` to `1.0` for untreated meshes, or (b) bake vertex
colors (Phase 2 will automate this via a Godot equivalent of
`PSXLightingBaker.cs`).

**Textures look bilinearly filtered.**
Check the texture import settings: **Import dock → Flags → Filter → Off**.
Or edit the material's shader parameter — the shader itself uses
`filter_nearest`, but Godot's texture import can re-upsample upstream.

**"PS1Scene" doesn't appear in Add Node.**
`[GlobalClass]` requires a built C# assembly. Build the solution first
(hammer icon). If that doesn't help, open `PS1Godot.sln` in Rider and build
from there.
