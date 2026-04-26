@tool
extends EditorScript

# One-shot helper: walk a target scene file, find every PS1MeshGroup whose
# descendant MeshInstance3D matches the UV linter's complaint list (FBX
# imports with UVs scaled past [0, 1]), set TilingUV=true on the group,
# save the scene.
#
# Run from Godot:
#   File -> Run...
# or via the editor "Run Editor Script" menu item with this file open.
#
# Idempotent: re-running on a scene that already has TilingUV=true on
# every matching group is a no-op (the file is still re-saved, but the
# tscn diff is empty).
#
# Why: the export log floods with 21 mesh warnings on every run from the
# Kenney survival-kit + animated-characters packs. Their FBX UVs are
# authored as tiled-atlas coords scaled 5-30x past [0, 1]. The MeshLinter
# correctly flags them; the TilingUV flag mutes the diagnostic per
# authoring intent. Manually clicking 21 boxes in the inspector is
# tedious, hence this helper.
#
# CAVEAT: muting != fixing. PSX still doesn't wrap UVs hardware-side. The
# rendered sampling is whatever per-vertex linear interpolation lands on.
# True PS1 tiling needs the mesh subdivided at integer UV boundaries —
# out of scope here.

const TARGET_SCENE := "res://scenes/monitor/monitor.tscn"

# Names emitted by the UV linter as out-of-range. Keep in sync with the
# offenders printed at scene[3] in the export log. Substring match against
# either the mesh-instance name or its parent PS1MeshGroup name.
const KENNEY_OFFENDERS := [
	"bookcaseClosed", "chair", "televisionVintage", "coatRackStanding",
	"cardboardBoxClosed", "desk", "bench", "kitchenCoffeeMachine",
	"pallet", "lampWall", "detail-barrier-type-a", "trashcan",
	"chairDesk", "detail-light-single", "computerScreen", "mug",
	"pottedPlant", "drawer", "plant", "door-type-a",
	"detail-dumpster-closed",
]


func _run() -> void:
	var ps := load(TARGET_SCENE) as PackedScene
	if ps == null:
		push_error("[apply_tiling_uv] Could not load %s" % TARGET_SCENE)
		return

	var root := ps.instantiate()
	var changes: int = 0
	_visit(root, changes)
	# _visit can't return through the closure — recount.
	changes = _count_groups_marked(root)

	# Re-pack and save.
	var new_ps := PackedScene.new()
	var pack_err := new_ps.pack(root)
	if pack_err != OK:
		push_error("[apply_tiling_uv] pack() failed: %s" % pack_err)
		root.queue_free()
		return

	var save_err := ResourceSaver.save(new_ps, TARGET_SCENE)
	if save_err != OK:
		push_error("[apply_tiling_uv] save() failed: %s" % save_err)
		root.queue_free()
		return

	print("[apply_tiling_uv] %d PS1MeshGroup nodes flagged TilingUV=true. Scene re-saved: %s"
		% [changes, TARGET_SCENE])
	root.queue_free()


func _visit(n: Node, _changes_out: int) -> void:
	# PS1MeshGroup is detected by class_name (registered globally via
	# [GlobalClass]) — we don't need to import the C# script type.
	if n.get_class() == "Node3D" and n.get_script() != null and \
			"TilingUV" in n and "ObjectName" in n:
		# Heuristic: does this group own any descendant whose name matches
		# a known offender? Set TilingUV=true if so.
		if _has_offender_descendant(n) and not n.TilingUV:
			n.TilingUV = true
			print("  -> %s: TilingUV=true" % n.get_path())

	for child in n.get_children():
		_visit(child, _changes_out)


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
	if n.get_class() == "Node3D" and n.get_script() != null and \
			"TilingUV" in n and n.TilingUV:
		count += 1
	for child in n.get_children():
		count += _count_groups_marked(child)
	return count
