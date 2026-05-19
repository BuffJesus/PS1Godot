# API showcase (2026-04-22 additions)

Everything shipped in the 11-commit session on 2026-04-22, in one place,
with copy-paste-ready Lua. See `godot-ps1/demo/scripts/combat_showcase.lua`
for a full working example that exercises most of these together.

## Contents

1. [Bind-pose skinned idle](#bind-pose-skinned-idle)
2. [Physics.Raycast — ray-vs-collider AABBs](#physicsraycast)
3. [Entity.Spawn / Destroy / Tag — pooled dynamic objects](#entityspawn)
4. [Entity.FindByTag / FindNearest](#entityfind)
5. [Physics.OverlapBox — melee hitboxes / area damage](#physicsoverlapbox)
6. [Camera.Shake — screen impact](#camerashake)
7. [Scene.PauseFor — hit-stop / impact crunch](#scenepausefor)
8. [Layout containers (HBox / VBox / SizeBox / Spacer / Overlay)](#layout-containers)
9. [Frame Model in Viewport — author-side helper](#frame-model)
10. [PS1UIModel — 3D model HUD widgets](#ps1uimodel)
11. [UI Designer dock — click/drag/resize/add](#ui-designer)

---

## Bind-pose skinned idle {#bind-pose-skinned-idle}

Skinned meshes default to their bind pose (T-pose) until a clip starts,
so characters no longer show frame 0 of a walk clip (mid-stride) on
load. Call `SkinnedAnim.BindPose` to return to bind at any time —
useful on walk-edge-down:

```lua
if moving and not isWalking then
    SkinnedAnim.Play("Player", "walk", { loop = true })
    isWalking = true
elseif not moving and isWalking then
    SkinnedAnim.BindPose("Player")   -- not Stop (freezes on last frame)
    isWalking = false
end
```

## Physics.Raycast {#physicsraycast}

Slab-method ray test against all active **Solid collider AABBs** in the
scene (not BVH triangles — world geometry itself doesn't block rays
yet). Returns `{ object, distance, point }` or `nil`.

```lua
local origin = Player.GetPosition()
local dir    = Camera.GetForward()
local hit    = Physics.Raycast(origin, dir, 50)  -- maxDist in world units
if hit ~= nil then
    local target = Entity.FindByIndex(hit.object)
    Debug.Log("Ray hit at " .. hit.distance .. " — " .. Entity.GetTag(target))
end
```

- Pass `dir` roughly unit-length so `distance` is in world units.
  (Use `Vec3.normalize` if unsure.)
- Zero dir → explicit `nil` return (no phantom hits).
- Default maxDist if omitted is 10000 world units.

## Entity.Spawn / Destroy / Tag {#entityspawn}

Runtime-pooled dynamic objects. Pattern:

1. Author places N copies of a template (bullet / enemy / pickup) in
   the editor; sets `Tag` (any non-zero u16 per "kind") and
   `StartsInactive = true`.
2. Lua `Entity.Spawn(tag, pos [, rotY])` finds the first inactive
   match and activates it (fires `onEnable` on the attached script).
3. Lua `Entity.Destroy(obj)` returns it to the pool.

```lua
local TAG_BULLET = 1

-- Fire a bullet 1m in front of the player, facing forward.
local p    = Player.GetPosition()
local fwd  = Vec3.normalize(Camera.GetForward())
local spawn = Vec3.new(p.x + fwd.x, p.y - 1, p.z + fwd.z)
local bullet = Entity.Spawn(TAG_BULLET, spawn)
if bullet == nil then
    Debug.Log("pool exhausted")
end

-- Later:
Entity.Destroy(bullet)
```

- `rotY` is in the "pi fraction" convention (1.0 = π = 180°), matching
  `Entity.SetRotationY`.
- Per-spawn reset logic should live in the template's `onEnable` hook,
  not `onCreate` (which fires once at scene init regardless of
  StartsInactive state).
- Tag 0 is the "untagged" sentinel — `Spawn(0, ...)` explicitly returns
  nil, and the exporter warns on `StartsInactive=true + Tag=0`.

`Entity.SetTag` / `GetTag` are also available for runtime tag changes
(useful for "mark as locked" / "mark as boss" variants).

## Entity.FindByTag / FindNearest {#entityfind}

```lua
-- Any active object with tag — first match in GameObject order.
local anyEnemy = Entity.FindByTag(TAG_ENEMY)

-- Closest active object with tag — for lock-on, closest-enemy AI, etc.
local closest = Entity.FindNearest(Player.GetPosition(), TAG_ENEMY)
if closest ~= nil then
    -- ...aim camera at it, apply damage, draw lock-on reticle, etc.
end
```

Tag 0 bails out on both (same sentinel rule as Spawn).

## Physics.OverlapBox {#physicsoverlapbox}

AABB overlap query — the missing half of `Physics.Raycast`. Use for
melee swings, area damage, pickup radii. Optional tag filter. Returns
a Lua array of object handles (hard-capped at 16 per call).

```lua
-- Swing a 1.5×1.5×1.5 m hitbox 1 m in front of the player.
local p    = Player.GetPosition()
local fwd  = Vec3.normalize(Camera.GetForward())
local cx   = p.x + fwd.x
local cz   = p.z + fwd.z
local minV = Vec3.new(cx - 0.75, p.y - 1.5, cz - 0.75)
local maxV = Vec3.new(cx + 0.75, p.y + 0.5, cz + 0.75)

local hits = Physics.OverlapBox(minV, maxV, TAG_ENEMY)
for i = 1, #hits do
    Entity.Destroy(hits[i])
end
```

## Camera.Shake {#camerashake}

Screen shake with linear decay. Intensity is the max per-axis offset in
world units (FP12); keep it small (0.05–0.3) for game feel.

```lua
Camera.Shake(0.15, 14)   -- "got hit" — medium shake over 14 frames
Camera.Shake(0.04, 8)    -- "dialog reveal" — subtle feedback
Camera.Shake(0.3, 30)    -- "boss landed" — heavy thump
```

Render-only: the underlying camera position is never modified, so
camera-follow code (`m_cameraFollowsPlayer`) keeps working during a
shake. Safe to call during `Scene.PauseFor` (the shake still ticks).

## Scene.PauseFor {#scenepausefor}

Hit-stop / impact crunch. Freezes animation / cutscene / skin / Lua
onUpdate / player movement for N frames. Render, music, camera shake,
and button-state tracking keep running. Stacks via
`max(remaining, requested)` so a short impact can't cut short a longer
pause already in flight.

```lua
-- Big enemy dies → 5-frame freeze so the player reads the impact.
Scene.PauseFor(5)
```

Pairs well with `Camera.Shake` for Souls/Hades-style combat juice.
Don't exceed ~10 frames at 60 fps — longer freezes start feeling like
the game hung.

## Layout containers {#layout-containers}

UMG-flavored UI authoring. Containers nest under a `PS1UICanvas`; at
export time the layout resolver bakes everything to absolute X/Y so
the splashpack runtime is unchanged.

Place in scene tree:

```
PS1UICanvas "HUD"
├── PS1UIHBox  (Padding, Spacing, DefaultHAlign/VAlign)
│   ├── PS1UIElement  (Text, "HP:")                SlotFlex=0
│   ├── PS1UIElement  (Box, fill bar)              SlotFlex=1
│   └── PS1UIElement  (Text, "100/100")            SlotFlex=0
├── PS1UIVBox  (anchor=BottomCenter)
│   ├── PS1UISizeBox   (WidthOverride=120)
│   │   └── PS1UIElement  (Text, "Press X")
│   └── PS1UISpacer    (SlotFlex=1)                 "push everything else up"
```

Per-child slot fields live on every widget type: `SlotHAlign`,
`SlotVAlign` (Start/Center/End/Fill/Inherit), `SlotFlex`
(0 = fixed, >0 = proportional), `SlotPadding` (Vector4I: L/T/R/B).

## Frame Model in Viewport {#frame-model}

Editor tool menu: `PS1Godot: Frame Selected Model in Viewport`.
Computes camera position + `projection H` that frames a selected
Node3D's bounding sphere at ~128 px wide on the PSX screen. Prints a
ready-to-paste Lua snippet and, if there's exactly one Camera3D in
scene, moves it so the Godot viewport shows the framing immediately.

Use for splash-screen logos, title-screen models, character-select
screens. Pairs with `PS1UIModel` for animated HUD model previews.

## PS1UIModel {#ps1uimodel}

3D model rendered in a screen-space rect on top of the main scene —
inventory item previews, title-screen rotating logo, character
portraits. Splashpack v23.

Author in Godot:

1. Place your model as a normal `PS1MeshInstance` in the scene (set
   `StartsInactive = true` on it if you don't want it visible in the
   main scene — the HUD pass renders it anyway).
2. Drop a `PS1UIModel` child under your `PS1UICanvas`.
3. Set `Target` (NodePath) to the mesh node.
4. Set screen rect + Anchor and orbit yaw/pitch/distance.
5. Run "Frame Selected Model in Viewport" on the mesh to get a good
   starting distance.

Lua side:

```lua
-- Per-frame rotating preview.
local yaw = 0
function onUpdate(self, dt)
    yaw = yaw + 0.01   -- yaw is in "pi fractions" (1.0 = π)
    if yaw > 2 then yaw = yaw - 2 end
    UI.SetModelOrbit("item_preview", yaw, 0.05)  -- keep pitch constant
end

-- Show / hide the preview widget.
UI.SetModelVisible("item_preview", true)

-- Swap the model shown in the widget (inventory scrolling).
UI.SetModel("item_preview", "sword_model")
```

Current limits: static meshes only (skinned UI models deferred);
`Target` must point at a PS1MeshInstance, not a PS1MeshGroup.

## UI Designer dock {#ui-designer}

Bottom-panel tab: **PS1 UI**. Shows the selected `PS1UICanvas` at
integer zoom (1×–4×) with drag/resize support. No manual X/Y edits
required for most layouts.

- Click any element / container to select it.
- Click empty area to select the canvas.
- Drag the body of a selected element → updates X/Y.
- Drag the bottom-right corner handle → updates Width/Height.
- `+ Add` dropdown adds a child under the selected container, or
  under the canvas if no container is selected. Types: Text
  Element, Box Element, HBox, VBox, SizeBox, Spacer, Overlay, 3D
  Model.
- Delete key on the scene tree removes nodes (standard Godot).

Limits: drag/resize aren't wired into Godot's undo/redo yet (Ctrl-Z
won't revert a drag — use the inspector for precise edits that DO
undo). Inspector-side property edits still undo as usual.
