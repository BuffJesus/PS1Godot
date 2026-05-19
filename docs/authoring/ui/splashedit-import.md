# Swapping Kenney models for the PSX asset packs

The Monitor scene currently uses three Kenney kits + the cartoon
animated-characters pack. Replacing them with the PSX-style packs is
**editor work** — the scene's bone IDs (for skinned meshes) and prop
sub-mesh references can't be safely text-swapped, because Godot bakes
unique IDs into the scene the first time it imports an FBX/GLB.

This doc covers what's already prepped, and the manual swap steps.

## What's already in the project

```
assets/monitor/models/
├── psx_characters/
│   ├── Models/
│   │   ├── Character_Monster.fbx
│   │   ├── Character_Monster_01.fbx
│   │   ├── Character_Monster_03.fbx
│   │   ├── Character_Killer.fbx
│   │   └── Character_Killer_05.fbx
│   ├── Textures/
│   │   └── (matching PNGs per character)
│   ├── ps1_psx_monster_01.tres   ← uses ps1.gdshader + Character_Monster_01.png
│   └── ps1_psx_killer.tres       ← uses ps1.gdshader + Character_Killer.png
└── psx_props/
    ├── Models/
    │   └── models.fbx              ← bundled scene; ALL props as sibling MeshInstances
    └── Textures/
        └── (147 PNG/JPG textures)
```

## Swap recipe — character (`hallway_figure`)

1. Open `scenes/monitor/monitor.tscn` in the Godot editor.
2. Find **Feed01_Hallway / hallway_figure** in the scene tree, expand `Char`.
3. Note the current settings on the inner skinned mesh (the node with
   PS1SkinnedMesh script):
   - Position, scale (current scale = 100×, mesh extent ≈ 1m human)
   - Material override → `ps1_kenney_skin.tres`
   - PS1SkinnedMesh export properties (clips, etc. — currently empty)
4. **Delete** the `hallway_figure/Char` subtree.
5. Drag `assets/monitor/models/psx_characters/Models/Character_Monster_01.fbx`
   from the FileSystem dock onto **hallway_figure** in the scene tree.
   Godot instances it as a child node.
6. Inspect the new instance — find the `MeshInstance3D` (and skeleton)
   that holds the geometry. The PSX pack is rigged differently from
   Kenney; you may need to apply the PS1SkinnedMesh script to the right
   mesh node.
7. Set `material_override` = `ps1_psx_monster_01.tres`.
8. Match the original transform (position relative to room, scale).
   PSX characters export at real-world scale; Kenney was 100× scaled in
   the original FBX. Try scale = 1.0 first; tweak from there.
9. If the figure stands too small / too large, scale by 0.5–2× — the
   PSX pack has consistent cm-units, Kenney was unit-units.
10. Save the scene and re-export.

Repeat for **`parking_figure`** (use a different killer/monster for
variety, e.g. `Character_Monster_03`).

## Swap recipe — props

The new prop pack is **one** `models.fbx` containing every prop as a
sibling. Two options:

### Option A — instance the bundled scene, hide unused

1. Drag `psx_props/Models/models.fbx` into a hidden offstage anchor in
   the scene (e.g. a `PSXPropPalette` Node3D placed at y=-1000).
2. In the editor, expand the imported scene and find the prop you want
   (e.g. `Bookcase`, `Box_01`, etc.).
3. **Duplicate** the specific child node, drag it out onto the room
   parent (Feed02_Storage etc.), reposition.
4. The PSX pack textures live in the bundled FBX's import; they're
   shared via the `Textures/` folder.

Pros: fast, no Blender. Cons: scene gets a deep tree of palette
fragments.

### Option B — Blender split (cleaner, more work)

1. Open `psx_props/Models/models.fbx` in Blender.
2. Select each prop, "Export Selected" as individual `.glb` files.
3. Drop those into `psx_props/Models/` next to the bundled FBX.
4. Reference each one as an ext_resource the same way the Kenney GLBs
   are referenced today.

Pros: clean ext_resource lines, matches the Kenney pattern. Cons:
Blender step before any prop is usable.

## After the swap

Once the figures and props point at the new packs, the entire
`kenney_*` directories under `assets/monitor/models/` become
unreferenced. Re-run the orphan audit (see `docs/sound-macro-plan.md`
or the inline grep we used) to find them, then delete in bulk —
~25-30 MB more cleanup.

## Don't text-swap

What NOT to try (will break the scene):
- Renaming `[ext_resource path="…kenney…fbx"]` to point at a new FBX.
  The scene's `parent_id_path=PackedInt32Array(…)` arrays reference
  bones / sub-meshes by IDs that only exist in the old FBX's import.
  Editor must re-create those references at instance time.
- Mass-replacing material UIDs in tscn. The shader parameter slots
  still need texture references that match the new mesh's UV layout.
