# Demo showcase setup guide

Step-by-step wiring for seeing every 2026-04-22 feature inside the
existing `demo.tscn` + `checkered_realm.tscn`. Each section is
independent — stop after any one if you don't want the rest.

`intro_splash.tscn` already has a PS1UIModel corner preview of the
HeadSpider (committed in 0a29b4d). Boot the demo to see it before
starting — confirms the v23 pipeline works on your PSX target before
you invest more author time.

---

## 1. Combat arena in `demo.tscn` (~15 min) — exercises Entity.Spawn / Destroy / Tag / FindNearest / Physics.Raycast / Physics.OverlapBox / Camera.Shake / Scene.PauseFor

**Goal**: L2 shoots projectiles, R2 swings a melee box, R3 locks onto
the nearest enemy. Red cubes are enemies; they die with hit-stop +
shake on impact.

### 1a. Add enemy templates (3×)

In the Godot scene tree, pick the root `Scene` node. Right-click →
Add Child Node → `MeshInstance3D`. Name it `EnemyA`.

Inspector:
- `Mesh`: New BoxMesh, size `(1, 1, 1)`.
- `Material Override`: click [empty] → New StandardMaterial3D → set
  `albedo_color` to a red (e.g., `(0.85, 0.25, 0.25)`) so enemies
  read visually. Save it as `res://demo/materials/ps1_red.tres` so
  you can reuse it on the other two enemies.
- `Transform`: position `(-6, 1, -6)` (far corner from the green Cube).
- Extension script: in the inspector's top, click the script icon → Load
  → `res://addons/ps1godot/nodes/PS1MeshInstance.cs` (attaches the
  PS1MeshInstance script, giving access to the PS1 / * fields below).
- After the script attaches, set:
  - `Collision` = `Solid` (so OverlapBox + Raycast can find it).
  - `Tag` = `2` (matches `TAG_ENEMY` in combat_showcase.lua).
  - `StartsInactive` = **false** (they spawn visible).

Duplicate the EnemyA node (Ctrl-D) twice; rename to `EnemyB` / `EnemyC`
and move them to different positions: try `(6, 1, -6)` and `(0, 1, -10)`.

### 1b. Add bullet pool templates (5×)

Same as enemies, but:
- Mesh: BoxMesh size `(0.3, 0.3, 0.3)` (smaller).
- Material: a yellow one (albedo `(1, 0.9, 0.3)`). Save as
  `res://demo/materials/ps1_bullet.tres`.
- Position: `(-30, -10, -30)` (far offscreen — they'll spawn from
  Lua, so the author-time position doesn't matter).
- `Collision` = `Solid`.
- `Tag` = `1` (matches `TAG_BULLET`).
- `StartsInactive` = **true**. Critical — otherwise 5 bullets
  render on boot at their author position.

Duplicate 4 times: `BulletA` through `BulletE`. Leave positions
identical (offscreen).

### 1c. Add the CombatManager

Right-click the root Scene → Add Child → `MeshInstance3D`. Name
`CombatManager`. Inspector:
- `Mesh`: leave empty (no render; we just need a GameObject with an
  onUpdate hook). Actually the exporter requires a mesh — give it a
  BoxMesh size `(0.01, 0.01, 0.01)` and position it at `(0, 50, 0)`
  (50m above the play area — invisible to the player).
- Script: `PS1MeshInstance.cs`.
- After script attaches:
  - `ScriptFile`: `res://demo/scripts/combat_showcase.lua`.
  - Leave Collision off.

### 1d. Test

Export + launch. In the demo, walk near the red enemies and press:
- `L2` → yellow bullet spawns in front of you, flies forward. If
  it hits an enemy's AABB, the enemy vanishes with camera shake +
  ~4-frame hit-stop.
- `R2` → melee swing. All enemies within 1.5m in front of you
  vanish with a bigger shake + longer pause.
- `R3` → toggles lock onto the nearest enemy (no visible marker
  yet — the lock just re-tags to TAG_LOCKED=9).

If bullets don't spawn: check your templates' Tag=1 +
StartsInactive=true. The exporter warns at export if StartsInactive
is set with Tag=0.

---

## 2. HUD overlay with layout containers (~10 min) — exercises PS1UIHBox / PS1UIVBox / PS1UISpacer / PS1UIOverlay / PS1UISizeBox

**Goal**: a status bar at the top of the screen reading
`[ L2: shoot ] [ R2: swing ] [ R3: lock ]`, laid out with an HBox.

### 2a. Add the canvas

Right-click root Scene → Add Child → `Node`. Name it `HUD`.
- Script: `res://addons/ps1godot/nodes/PS1UICanvas.cs`.
- After attach:
  - `CanvasName`: `hud`.
  - `VisibleOnLoad` = true.
  - `SortOrder` = 5 (behind dialog which is 20).
  - `Theme`: `res://addons/ps1godot/themes/PS1Theme.tres`.

### 2b. Add the HBox row

Right-click `HUD` → Add Child → `Node`. Name it `StatusRow`.
- Script: `res://addons/ps1godot/nodes/PS1UIHBox.cs`.
- After attach:
  - `Anchor` = `TopCenter`.
  - `X` = 0, `Y` = 8. (Anchor inset — 8 px from the top edge.)
  - `Width` = 280, `Height` = 16.
  - `Spacing` = 16 (pixels between entries).
  - `DefaultVAlign` = `Center`.

### 2c. Add three text elements

Right-click `StatusRow` → Add Child → `Node`. Name `Shoot`.
- Script: `PS1UIElement.cs`.
- `Type` = `Text`, `Text` = `L2: shoot`.
- `Width` = 80, `Height` = 12.
- `ThemeSlot` = `Text`.
- `SlotFlex` = 1 (fill available space proportionally).

Duplicate twice: `Swing` (text `R2: swing`) and `Lock`
(text `R3: lock`).

### 2d. Test

Export + launch. You should see the three entries equally spaced
across the top center of the screen, respecting the 8-px inset.

If you open the PS1 UI bottom-panel tab in Godot while HUD is
selected, you can drag/resize the box in the WYSIWYG designer
directly — changes persist to the scene on save.

---

## 3. Checkered Realm enhancements (~20 min)

**Goal**: the realm scene gets a PS1UIModel of the spinning cube in
the corner + a VBox-laid-out welcome dialog.

### 3a. PS1UIModel of the cube

In `checkered_realm.tscn`, add a PS1UIModel as a child of any existing
PS1UICanvas (or a new one).
- `Target`: NodePath to the spinning Cube in that scene.
- `Anchor` = `TopRight`, `X`=8, `Y`=8, Width/Height 64×64.
- `OrbitDistance` = 3, `OrbitPitchDegrees` = -15.
- Optionally add a tiny Lua script that calls
  `UI.SetModelOrbit("cube_preview", yaw, 0, 3)` in onUpdate for a
  continuous spin.

### 3b. Replace the flat dialog with a VBox layout

Find the existing dialog canvas in the realm. Instead of positioning
`Background` + `Body` with absolute X/Y:

1. Add a `PS1UIVBox` node under the canvas.
   - Anchor = BottomCenter, X=0, Y=16, Width=240, Height=80.
   - Padding = (8, 8, 8, 8), Spacing = 4.
2. Move `Background` and `Body` to be children of the VBox.
3. On each child, set:
   - `SlotFlex`: 0 for fixed labels, 1 for fill.
   - `SlotPadding`: as needed for inset.
4. Delete the absolute `X/Y` values (they'll be overridden by VBox
   anyway) — or leave them since the layout resolver ignores authored
   X/Y for container children.

Compare exports side-by-side — binary output should be identical to
the previous hand-authored version if the VBox positions each child
at the same absolute pixel.

---

## 4. Sanity checks

After each section, do:
1. Save the scene (Ctrl-S).
2. Run `PS1Godot: Export Splashpack` from the tool menu (or the
   dock's Export button). Watch for `GD.PushWarning` messages in
   the Output panel — the exporter flags:
   - `StartsInactive=true but Tag=0` on objects (fix Tag value)
   - `UIModel target '<name>' isn't an exported GameObject` (fix
     the Target NodePath to point at a PS1MeshInstance /
     PS1MeshGroup that's in the scene)
3. Run `PS1Godot: Run on PSX` or the dock's Run button. Confirm the
   demo still boots.

If the demo stops booting at any point, `git diff` the scene file —
spot the problematic node, remove it in Godot, save, re-run. If the
file won't open AT ALL in Godot, `git checkout -- godot-ps1/demo/<scene>.tscn`
to revert that scene specifically.

## 5. Takeaways / patterns to reuse

- **Enemy pool pattern**: N tagged `StartsInactive=true` meshes +
  Lua calls `Entity.Spawn(tag, pos)` to draw from the pool. Reset
  state in the template's `onEnable` hook, not `onCreate`.
- **Melee pattern**: `Physics.OverlapBox` with a tag filter is the
  cleanest swing hitbox. Pair with `Camera.Shake` on hit +
  `Scene.PauseFor(4–8)` for crunch.
- **Projectile pattern**: same pool trick + `Physics.Raycast`
  one-step-ahead in `onUpdate` for hit detection.
- **HUD with containers**: place a PS1UICanvas + one HBox/VBox, let
  the exporter do the layout math. Author in the WYSIWYG dock
  instead of hand-editing X/Y.
- **Model previews**: PS1UIModel + Target NodePath. Tight orbit
  distance (compute via `PS1Godot: Frame Selected Model in
  Viewport` on the target mesh) keeps the model inside the rect.
