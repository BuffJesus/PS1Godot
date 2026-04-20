# UI / UX plan

The plugin is the entire product for most authors. Godot is the "engine" in the
same way Vulkan is the "engine" of Unity — present and necessary, but not what
the user talks about. Everything in this doc is judged against four tenets:

- **Intuitive.** Every action has one obvious place. No hunting.
- **Non-intimidating.** PS1 development is niche and full of constraints.
  Hide the constraints behind defaults; surface them as gentle nudges when
  the author is about to break one.
- **Modern.** Flat, legible, responsive. No 1999 skeuomorphism — the *game*
  is the retro experience, the *editor* isn't.
- **Beautiful.** Deliberate spacing, a coherent accent color, icons that
  communicate rather than decorate.

Every concrete decision below should trace back to at least one of these.

## Principles

### 1. Progressive disclosure
Default to the simplest scene with two or three meshes, a player, and a
camera. Advanced options (BVH settings, CLUT bit-depth, custom colliders)
appear in their inspector sections **collapsed** with a one-line description
of what they do. An author who ignores them should still ship a working
splashpack.

### 2. One action, one place
If the author wants to "run on PSX," there is exactly one big button. Not a
menu item *and* a script *and* a dock button. The dock button is canonical;
the keyboard shortcut and menu item mirror it and say so in their tooltips.

### 3. Sensible defaults > configuration
Every custom node ships with values that work: `PS1MeshInstance` with 8bpp
textures, `PS1Scene` with reasonable budgets, `PS1Player` with third-person
camera at a sane offset. Config exists for when the default is wrong, not
as a mandatory first step.

### 4. Errors in plain English with a fix button
"Texture 1024×1024 exceeds VRAM page (max 256×256). [Auto-downscale]
[Open import settings]" beats "TextureFormatException". Every warning
the exporter emits follows this format.

### 5. Real-time constraint feedback
Budgets (triangle count, VRAM, SPU, texture pages) update live in a
viewport overlay and dock panel. Passing them should feel like free
progress; nearing them should feel like a soft warning in your peripheral
vision, not a popup.

### 6. Build on Godot conventions
Authors who know Godot shouldn't have to relearn anything. PS1 nodes
appear in the Create Node dialog alongside MeshInstance3D, they have
`[Export]` properties, they respect `visible`, they hot-reload. If
Godot does it one way, we do it the same way.

## Visual language

### Accent color: PS1 red
A single branded accent, `#CE2127` (the Sony red from the controller
logo). Used for primary CTAs and brand elements. Everything else is
Godot's editor theme — no palette sprawl.

### Icons
Monochrome, 16×16 @1x, Godot editor style. Every custom node gets one
so it's identifiable at a glance in the scene dock. Use a shared SVG
source; generate @1× / @2× at plugin install.

### Typography
Godot's default UI font. No custom fonts. Type hierarchy comes from
size + weight, not font choice.

### Spacing
8 px grid. Everything lands on it — padding, gaps, icon positions. A
16-pixel-wide gap reads as "these are different things"; an 8-pixel
gap reads as "these belong together."

## Surfaces

### A. First-run panel *(Phase 0.5)*
Auto-opens once when the plugin detects a fresh project or missing deps.
One full-screen panel, no Godot chrome visible behind it. Sections:

1. Welcome message in one sentence.
2. Checklist of dependencies with ✓/✗ and "Install" buttons per row.
3. "Open demo scene" button at the bottom.
4. "Skip (I know what I'm doing)" link, small, bottom-right.

Dismisses itself once everything is green. Never returns unless something
breaks.

### B. PS1Godot dock panel *(Phase 1 starter, Phase 3 complete)*
Always-visible dock, docked bottom-right by default. Sections, top to
bottom:

```
┌─ PS1Godot ─────────────────────────────────┐
│                                            │
│   [ ▶ Run on PSX ]                         │  ← primary CTA, accent red
│                                            │
│   [ Build ]  [ Launch ]  [ Analyze ]       │  ← secondary actions
│                                            │
│  ── Scene ──────────────────────────────── │
│  demo.tscn                                 │
│  Triangles    234 / 2500    ████░░░░░░░░░  │
│  VRAM         198 / 1024 KB ██░░░░░░░░░░░  │
│  SPU          452 / 512 KB  ███████░░░░░░  │
│                                            │
│  ── Setup ─────────────────────────────── │
│  All dependencies ready.  [Re-check]       │
│                                            │
└────────────────────────────────────────────┘
```

- The primary button grows vertically on hover; briefly pulses when an
  export finishes.
- Budget bars use green / amber / red at 0-80 / 80-95 / 95+ %.
- The Setup section collapses to one line when happy; expands to the
  full dependency list when anything is missing.

### C. Viewport overlay *(Phase 3)*
Translucent corner overlay, top-left of the 3D viewport. Four lines
max:

```
134 tris · 182 KB VRAM · 452 KB SPU · 3 texpages
```

Color-coded per budget. Toggleable with a tiny "i" button in the
viewport toolbar.

### D. Node inspectors *(Phase 1 polish, always)*
Every `PS1*` node has a single-sentence description at the top of its
inspector explaining what it is. Advanced options (collision mask, BVH
participation) live in a collapsed "Advanced" section.

### E. Templates *(Phase 3)*
"New PS1 Scene" in the Scene menu offers:

- **Empty** — one PS1Scene, one PS1Player, one Camera3D.
- **Demo** — the current demo scene, ready to edit.
- **Menu** — title screen with one UI canvas.
- **Gameplay** — level with floor, spawn, trigger box.

Templates are `.tscn` files in `addons/ps1godot/templates/`. No code.

### F. Built-in script editor polish *(Phase 3)*
PS1Lua gets Godot's syntax highlighter, autocomplete from EmmyLua stubs,
and F1-on-a-function opens the `luaapi.md` doc page at the right anchor.

### G. UI authoring surfaces *(Phase 3 polish, high leverage)*

Current UI authoring (drop PS1UICanvas + PS1UIElement children, type
absolute X/Y/W/H numbers, hit Run on PSX, squint) violates every
tenet: it's unintuitive (no visual feedback), intimidating (pixel
math), un-modern (numeric inspector only), un-beautiful (you only
see the output after exporting). The items below map each tenet to
concrete changes.

**Intuitive — one obvious place per action:**

- **WYSIWYG canvas editor.** Selecting a PS1UICanvas shows its
  children drawn at 320×240 reference scale in a 2D viewport
  overlay. The PS1 screen is a bordered rectangle; elements render
  inside, with drag-handles for X/Y and resize-handles for W/H.
  Inspector stays in sync.
- **One authoring flow per element kind.** Text, Box, Image,
  9-patch Border, Portrait — each with its own inspector shape and
  editor gizmo. No "generic fields with conditional meaning."
- **Dialog trees as a dedicated editor dock.** Not a prefab of
  nodes in the scene tree — a proper graph editor similar to
  Godot's AnimationTree. Arrows between choice nodes, conditions
  evaluated against quest flags, inline text editing.

**Non-intimidating — progressive disclosure, sensible defaults:**

- **Anchors instead of pixel math.** "Top-right, 8 px inset" is
  the right primitive; authors shouldn't hand-compute `X = 312`.
  Default anchor is TopLeft to match existing content.
- **Auto-wrap text.** Authors type paragraphs; the exporter
  measures against font advance widths and wraps at word
  boundaries. Explicit `\n` stays for dramatic breaks.
- **Prefab dialog box / menu list / HUD bar.** Drop-in resources
  that pre-wire text/cursor/input plumbing. Authors who just want
  "a dialog box" shouldn't have to stack three Box elements and
  a Text element.
- **Lua helpers over primitives.** `Dialog.Show({ text = ... })`
  blocks until choice; `Toast.Show(text, 2)` auto-hides. `UI.*`
  primitives stay available for power users.

**Modern — clear hierarchy, responsive feedback:**

- **Live PS1-quantized preview.** Font glyph bitmaps, 15-bit
  color clamp, dithering — rendered in the editor through a
  matching shader. What you see is what the PSX renders, not
  Godot's anti-aliased approximation.
- **Element list panel.** Alongside the visual preview, a
  reorderable list of elements (like Figma's layers panel) with
  visibility toggles and names. Z-order = list order.
- **Inspector hints.** Below each numeric field, show the actual
  pixel range and a plain-language description ("0–319, horizontal
  position on the PS1 screen"). Combines with anchors so most
  authors never touch raw pixels.

**Beautiful — coherent palette, deliberate spacing:**

- **Central PS1Theme resource.** Colors, font, alignment defaults.
  Every element can opt in. Change theme once → everything
  restyles. Follows the same spirit as the UI/UX plan's "accent
  red" choice — one source of truth, no palette sprawl.
- **Icons for element kinds.** Text, Box, Image, Border each get
  a 16 px icon in the Create Node dialog and scene tree. Visible
  identification at a glance.
- **Consistent padding.** Prefabs land with the same internal
  padding / line-height as the rest of the UI, so mixing an
  authored canvas with a prefab dialog box doesn't jar.

**Out of scope here:** Runtime mutations. Those stay in the
`UI / HUD from Lua` section of ROADMAP — dock vs. Lua API are
different surfaces.

## Non-goals

- **Custom Godot theme.** Godot users already have their own theme
  preference. Don't fight them.
- **Launcher app / wrapper around Godot.** Phase 4 `PS1Godot.zip` is a
  distribution detail, not a UX product. The experience inside Godot is
  what matters.
- **Touch / mobile affordances.** PS1Godot is desktop-only.
- **Gamification.** No "achievements" for exporting a clean splashpack.
  The work is the reward.

## Roadmap mapping

| Roadmap bullet | UX surface |
|---|---|
| Phase 0.5 first-run panel | **A** |
| Phase 0.5 setup panel ongoing | dock **B**'s Setup section |
| Phase 3 F5-to-play | dock **B**'s primary button |
| Phase 3 VRAM viewer dock | becomes its own tab of **B** |
| Phase 3 budget overlays | **C** viewport overlay + **B** Scene section |
| Phase 3 texture reuse auditor | warning rows in **B**'s Setup section, `[Auto-fix]` buttons |
| Phase 3 project templates | **E** templates |
| Phase 3 PS1Lua syntax/autocomplete | **F** script editor polish |
| Phase 3 EmmyLua stubs | feeds **F**'s autocomplete |

## Work order

1. **Dock panel skeleton** with primary button + action buttons (this
   session). Replaces flat menu items as the canonical entry point.
2. **Scene budget section** wired to the real scene data (after
   next exporter run — needs a lightweight "dry-run" path that collects
   stats without writing files).
3. **Setup status** reusing the Phase 0.5 detection logic once it
   exists.
4. **Viewport overlay** after the dock has stabilized.
5. **Inspector polish** in lockstep with any new custom nodes.
6. **First-run panel** as Phase 0.5 proper ships.

Each step is a shippable win. Authors should notice the plugin getting
calmer and more confident as we move through them.
