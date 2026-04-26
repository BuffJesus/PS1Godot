extends SceneTree

# One-shot helper: walk monitor.tscn, find every PS1MeshGroup whose
# descendants match the UV linter's complaint list (FBX imports with UVs
# scaled past [0, 1]), set TilingUV=true on the group, save the scene.
#
# How to run — pick whichever fits your workflow:
#
#   1. From a shell (recommended; works without opening Godot):
#         "$GODOT_EXE" --headless --path godot-ps1 --script dev/apply_tiling_uv.gd
#      On Windows bash:
#         "$GODOT_EXE" --headless --path godot-ps1 --script dev/apply_tiling_uv.gd
#
#   2. From inside Godot's Script editor:
#      Open this file. In the Script editor's File menu (the menu inside
#      the Script editor pane, separate from the main editor's File
#      menu) -> Run, or shortcut Ctrl+Shift+X.
#
# Idempotent: re-running on a scene already flagged is a no-op (still
# re-saves the file, but the diff is empty).
#
# Why: the export log floods with 21 mesh warnings on every run from the
# Kenney survival-kit + animated-characters packs. Their FBX UVs are
# authored as tiled-atlas coords scaled 5-30x past [0, 1]. The
# MeshLinter correctly flags them; the TilingUV flag mutes the
# diagnostic per authoring intent. Manually clicking 21 boxes in the
# inspector is tedious, hence this helper.
#
# CAVEAT: muting != fixing. PSX still doesn't wrap UVs hardware-side.
# The rendered sampling is whatever per-vertex linear interp lands on.
# True PS1 tiling needs the mesh subdivided at integer UV boundaries —
# out of scope here.

const TARGET_SCENE := "res://scenes/monitor/monitor.tscn"

# Names emitted by the UV linter as out-of-range. Substring match
# against either the mesh-instance name or its parent PS1MeshGroup name.
# Kept in sync with the offenders printed at scene[3] in the export log.
const KENNEY_OFFENDERS := [
	"bookcaseClosed", "chair", "televisionVintage", "coatRackStanding",
	"cardboardBoxClosed", "desk", "bench", "kitchenCoffeeMachine",
	"pallet", "lampWall", "detail-barrier-type-a", "trashcan",
	"chairDesk", "detail-light-single", "computerScreen", "mug",
	"pottedPlant", "drawer", "plant", "door-type-a",
	"detail-dumpster-closed",
]


func _initialize() -> void:
	var ps := load(TARGET_SCENE) as PackedScene
	if ps == null:
		printerr("[apply_tiling_uv] Could not load %s" % TARGET_SCENE)
		quit(1)
		return

	# Instantiate with edit-state so per-instance overrides are visible
	# and re-packable. Without GEN_EDIT_STATE_INSTANCE we'd lose any
	# inspector-set TilingUV that already exists on instanced subtrees.
	var root := ps.instantiate(PackedScene.GEN_EDIT_STATE_INSTANCE)
	_visit(root)
	var changes := _count_groups_marked(root)

	var new_ps := PackedScene.new()
	var pack_err := new_ps.pack(root)
	if pack_err != OK:
		printerr("[apply_tiling_uv] pack() failed: %s" % pack_err)
		root.free()
		quit(1)
		return

	var save_err := ResourceSaver.save(new_ps, TARGET_SCENE)
	if save_err != OK:
		printerr("[apply_tiling_uv] save() failed: %s" % save_err)
		root.free()
		quit(1)
		return

	print("[apply_tiling_uv] %d PS1MeshGroup nodes flagged TilingUV=true. Scene re-saved: %s"
		% [changes, TARGET_SCENE])
	root.free()
	quit(0)


func _visit(n: Node) -> void:
	# PS1MeshGroup is a Node3D that has a script + the TilingUV property.
	# Cheaper than importing the C# class — the duck-type check covers it.
	if n is Node3D and n.get_script() != null and \
			"TilingUV" in n and "ObjectName" in n:
		if _has_offender_descendant(n) and not n.TilingUV:
			n.TilingUV = true
			print("  -> %s: TilingUV=true" % n.get_path())

	for child in n.get_children():
		_visit(child)


func _has_offender_descendant(group: Node) -> bool:
	for child in group.get_children():
		if _name_matches_offender(child.name):
			return true
		if _has_offender_descendant(child):
			return true
	return false


func _name_matches_offender(node_name: String) -> bool:
	for offender in KENNEY_OFFENDERS:
		if offender in node_name:
			return true
	return false


func _count_groups_marked(n: Node) -> int:
	var count: int = 0
	if n is Node3D and n.get_script() != null and \
			"TilingUV" in n and n.TilingUV:
		count += 1
	for child in n.get_children():
		count += _count_groups_marked(child)
	return count
