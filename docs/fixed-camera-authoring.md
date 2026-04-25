# Fixed Camera Authoring (proposal)

**Status:** unimplemented as of 2026-04-24. Write-up of pain we hit during
"The Monitor" jam game so the next time someone needs N fixed cameras in a
scene, the authoring path is a single click in Godot, not three rounds of
PSX-frame math debugging.

## What we hit

Authoring the 4 CCTV camera positions for `monitor.tscn` cost roughly an
afternoon of debugging *purely* in coordinate-conversion gotchas:

1. **`Camera.SetPosition` takes raw PSX coords.** Mesh verts get `/GteScaling`
   and Y/Z flipped at export (`PSXMesh.cs:221-226`), but Lua camera coords
   pass through unchanged (`luaapi.cpp:1536-1545`). Authoring a cam at the
   Godot-world position next to a mesh dropped the cam 60 units away from
   the geometry. Everything renders fine — just way off-screen.
   See `project_camera_lua_coord_frame.md` memory.

2. **`Camera.LookAt` is a no-op stub.** The runtime reads the target vector,
   computes nothing useful, and returns. The IDE brief shipped describing it
   as a working API. Workaround was hand-computing `pitch = atan2(Δy, h)/π`
   and `yaw = atan2(Δx, Δz)/π`. See `project_camera_lookat_stub.md`.

3. **Pitch sign is inverted vs intuition.** psyqo's `rotX` matrix
   (`pcsx-redux-main/src/mips/psyqo/src/soft-math.cpp:32`) makes positive
   pitch tilt the camera *up* in PSX-Y-down world space. The natural
   `atan2(dy_psx, h)/π` formula gives the opposite of the right answer when
   the cam sits above the floor and you want it to look down at the floor.
   Spent the bulk of the debugging session on a black-screen feed because
   the floor was projected to `sy=225` — one pixel inside the bottom CRT
   bezel. See `project_camera_pitch_sign.md`.

4. **No editor preview of the runtime camera frustum.** A Godot `Camera3D`
   node *can* show a preview gizmo — but it shows the *Godot* projection,
   not the PSX one. There's no way to confirm framing without exporting and
   running PCSX-Redux. Each iteration was 30+ seconds.

These are 4 separate paper cuts that compound. Doing the math on paper with
a clear head is mostly tractable. Doing it twice while three other things
are also broken is brutal.

## What we want

A `PS1FixedCamera` Node3D that authors place visually in Godot world space
and configure with normal Godot conventions (look at a target, set FOV).
The exporter bakes presets into the splashpack with the conversion +
correct sign math built in. Lua just says "use preset N" or "use preset
'hallway'."

### Authoring shape

```gdscript
# PS1FixedCamera.cs (Tool-mode, lives next to PS1Room.cs)
[ExportGroup("Identity")]
[Export] public string PresetName { get; set; } = "";

[ExportGroup("Aim")]
// Optional Node3D the cam looks at. If unset, exporter uses the cam's
// own -Z basis (Godot convention) so authors can rotate the node in the
// editor and see a live frustum gizmo.
[Export] public Node3D LookAtTarget { get; set; }

[ExportGroup("Projection")]
[Export(PropertyHint.Range, "1,1024,1")] public int ProjectionH { get; set; } = 120;

[ExportGroup("Behavior")]
// Optional shake to fire on Camera.LoadPreset, in raw fp12 + frame count.
[Export] public int LoadShakeIntensity { get; set; } = 80;
[Export] public int LoadShakeFrames { get; set; } = 4;
```

The node renders a wireframe frustum gizmo using `ProjectionH` directly so
the author can see in the Godot viewport exactly what the cam will see at
runtime — same FOV, same aspect.

### Exporter

`SceneCollector.CollectCameras(...)` walks the tree for `PS1FixedCamera`
nodes. For each, it computes:

```
pos_psx   = (gtx.X / GteScaling, -gtx.Y / GteScaling, -gtx.Z / GteScaling)

if LookAtTarget != null:
    target_psx = same conversion applied to LookAtTarget.GlobalPosition
    delta      = target_psx - pos_psx
    horiz      = sqrt(delta.x^2 + delta.z^2)
    yaw_pi     =  atan2(delta.x, delta.z) / π
    pitch_pi   = -atan2(delta.y, horiz) / π     # ← negate to fix the sign
else:
    # Read Godot Camera3D-style basis. Godot looks -Z; PSX looks +Z;
    # that's the Y/Z flip handling the orientation.
    fwd_godot = -gtx.basis.Z
    fwd_psx   = (fwd_godot.x, -fwd_godot.y, -fwd_godot.z)
    yaw_pi    =  atan2(fwd_psx.x, fwd_psx.z) / π
    pitch_pi  = -atan2(fwd_psx.y, sqrt(fwd_psx.x^2 + fwd_psx.z^2)) / π
```

Bake `(pos_psx, pitch_pi, yaw_pi, projH, presetName, shakeIntensity,
shakeFrames)` into a new `CameraPresetRecord`. Bump splashpack version,
add a `cameraPresetOffset + cameraPresetCount` to the header, append a
`CameraPresetRecord[]` block.

### Runtime (psxsplash side)

A new `Camera.LoadPreset(name_or_index)` Lua API:

```c++
int LuaAPI::Camera_LoadPreset(lua_State* L) {
    psyqo::Lua lua(L);
    if (!s_sceneManager) return 0;

    const CameraPresetRecord* preset = nullptr;
    if (lua.isString(1)) {
        preset = s_sceneManager->findCameraPresetByName(lua.toString(1));
    } else if (lua.isNumber(1)) {
        preset = s_sceneManager->getCameraPreset((int)lua.toNumber(1));
    }
    if (!preset) return 0;

    auto& cam = s_sceneManager->getCamera();
    cam.SetPosition(preset->posX, preset->posY, preset->posZ);
    cam.SetRotation(preset->pitch, preset->yaw, {0});
    cam.SetH(preset->projH);
    if (preset->shakeFrames > 0) {
        psyqo::FixedPoint<12> intensity;
        intensity.value = preset->shakeIntensity;
        cam.Shake(intensity, preset->shakeFrames);
    }
    return 0;
}
```

### Author code shrinks dramatically

Today (~30 lines for FEEDS table with manual conversion + sign-corrected
pitch/yaw + ShakeRaw):

```lua
FEEDS = {
    { name = "HALLWAY",   camPos = Vec3.new(-20, -0.625, 0.25),
      pitch = 0.03, yaw = 0, projH = 120 },
    -- … 3 more entries with hand-computed PSX coords …
}

local function switchToFeed(n)
    local feed = FEEDS[n]
    Camera.SetPosition(feed.camPos)
    Camera.SetRotation(Vec3.new(feed.pitch, feed.yaw, 0))
    Camera.SetH(feed.projH)
    Camera.ShakeRaw(80, 4)
    -- …
end
```

After:

```lua
local FEED_PRESETS = { "hallway", "storage", "parking", "back_room" }

local function switchToFeed(n)
    Camera.LoadPreset(FEED_PRESETS[n])
    -- …
end
```

The author authored the 4 cameras in the 3D viewport with Godot's normal
gizmo + preview flow. Zero PSX-frame math. Zero pitch-sign gotchas. The
black-screen-because-floor-is-one-pixel-below-bezel debugging episode does
not happen.

## Migration path

1. **Land `PS1FixedCamera.cs`** alongside `PS1Room.cs` / `PS1Camera.cs`.
   Tool-mode, gizmo, no runtime side yet.
2. **Exporter:** add `CollectCameras` + `CameraPresetRecord` writer behind
   a feature flag. Bump splashpack version when the writer side ships.
3. **Runtime:** add `Camera.LoadPreset` + the splashpack reader changes
   in psxsplash. Could land as a single PR; `psxsplash-improvements.md`
   has the upstream channel discussion.
4. **Migration:** for `monitor.tscn`, replace the `FEEDS` table with 4
   `PS1FixedCamera` nodes + a 4-entry array of preset names. Validate
   parity against the current hand-tuned framing before deleting the
   conversion math from `feed_manager.lua`.

## Until then

Document the gotchas everywhere a fresh user is likely to look:
- `docs/the-monitor-ide-brief.md` ← already patched (2026-04-24)
- `docs/api-showcase.md` if/when it covers Camera APIs
- `docs/lua-ps1-cheatsheet.md` ← TODO add a "fixed CCTV cams" recipe block
- Tool memories: `project_camera_lua_coord_frame.md`,
  `project_camera_lookat_stub.md`, `project_camera_pitch_sign.md`

## Out of scope

- **Cutscene-driven camera tracks** — already covered by `PS1Cutscene` /
  `PS1Camera` baked tracks (those go through the exporter's coordinate
  conversion, no manual math required). Fixed cameras are specifically
  for *runtime-switchable* preset selection (CCTV games, classic survival
  horror Resident-Evil-style fixed-cam, dialogue framing presets, etc.).
- **Smooth camera blends between presets** — interesting but separate.
  V1 is "snap to preset on Camera.LoadPreset"; smooth blends can come later.
