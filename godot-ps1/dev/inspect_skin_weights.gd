extends Node

# One-shot diagnostic to inspect character mesh skin weights
# Run with: godot --headless --script inspect_skin_diagnostic.gd

func _ready():
	inspect_character_skin()
	get_tree().quit()

func inspect_character_skin():
	# Load the monitor scene which contains the character mesh
	var monitor = load("res://scenes/monitor/monitor.tscn")
	if monitor == null:
		print("ERROR: Could not load monitor scene")
		return
	
	# Get the ArrayMesh resource from the scene data
	var scene_inst = monitor.instantiate()
	var mesh_instance = find_skinned_mesh(scene_inst)
	
	if mesh_instance == null:
		print("ERROR: Could not find SkinnedMesh in scene")
		return
	
	var mesh = mesh_instance.mesh
	if mesh == null:
		print("ERROR: No mesh assigned")
		return
	
	if not (mesh is ArrayMesh):
		print("ERROR: Mesh is not ArrayMesh, got: ", mesh.get_class())
		return
	
	print("=== KENNEY CHARACTER SKIN WEIGHT DIAGNOSTIC ===")
	print("Mesh: ", mesh.resource_name)
	
	# Get surface count
	var surface_count = mesh.get_surface_count()
	print("Surfaces: ", surface_count)
	
	for surface_idx in range(surface_count):
		analyze_surface(mesh, surface_idx)

func find_skinned_mesh(node):
	if "mesh" in node and node.has_method("get_mesh"):
		var mesh = node.mesh
		if mesh != null and mesh is ArrayMesh:
			return node
	
	for child in node.get_children():
		var result = find_skinned_mesh(child)
		if result != null:
			return result
	
	return null

func analyze_surface(mesh: ArrayMesh, surface_idx: int):
	print("\n--- Surface %d ---" % surface_idx)
	
	# Get surface arrays
	var arrays = mesh.surface_get_arrays(surface_idx)
	
	if arrays.is_empty():
		print("No arrays for surface")
		return
	
	var positions = arrays[Mesh.ARRAY_VERTEX]
	var bones = arrays[Mesh.ARRAY_BONES]
	var weights = arrays[Mesh.ARRAY_WEIGHTS]
	
	if positions == null:
		print("No positions in surface")
		return
	
	var vertex_count = positions.size()
	print("Vertex count: ", vertex_count)
	
	if bones == null or weights == null:
		print("WARNING: No bone/weight data")
		return
	
	# Analyze weights
	var unweighted_verts = 0
	var partial_weight_verts = 0
	var invalid_bone_verts = 0
	var max_bone_idx = 0
	
	# In Godot 4.x, bones and weights are packed as arrays of Color objects
	# For each vertex: bones.color is [bone0, bone1, bone2, bone3]
	#                  weights.color is [w0, w1, w2, w3]
	
	for v_idx in range(min(vertex_count, bones.size())):
		var bone_data = bones[v_idx]  # Vector4 or Color
		var weight_data = weights[v_idx]  # Vector4 or Color
		
		var bone_indices = [
			int(bone_data.x),
			int(bone_data.y),
			int(bone_data.z),
			int(bone_data.w)
		]
		
		var weight_values = [weight_data.x, weight_data.y, weight_data.z, weight_data.w]
		var weight_sum = sum_array(weight_values)
		
		# Track max bone index (should be < 45)
		max_bone_idx = maxi(max_bone_idx, maxii(bone_indices))
		
		# Count issues
		if weight_sum < 0.01:
			unweighted_verts += 1
		elif weight_sum < 0.95:
			partial_weight_verts += 1
		
		if any_invalid(bone_indices, 45):
			invalid_bone_verts += 1
		
		# Print sample verts for debugging
		if v_idx < 5:
			print("  V%d: bones=%s, weights=%.3f, sum=%.3f" % [
				v_idx, bone_indices,
				maxf(weight_values[0], maxf(weight_values[1], maxf(weight_values[2], weight_values[3]))),
				weight_sum
			])
	
	print("\n=== ANALYSIS RESULTS ===")
	print("Total vertices: %d" % vertex_count)
	print("Unweighted (sum ~0): %d (%.1f%%)" % [
		unweighted_verts,
		float(unweighted_verts) / vertex_count * 100
	])
	print("Partially weighted (sum < 0.95): %d (%.1f%%)" % [
		partial_weight_verts,
		float(partial_weight_verts) / vertex_count * 100
	])
	print("Invalid bone indices (>= 45): %d" % invalid_bone_verts)
	print("Max bone index: %d" % max_bone_idx)
	
	# Verdict
	print("\n=== VERDICT ===")
	if unweighted_verts > vertex_count * 0.05:
		print("WARNING: High unweighted vertex count. Likely cause of grey patches.")
	elif partial_weight_verts > vertex_count * 0.2:
		print("WARNING: Significant partial weighting. Skin may be incomplete.")
	else:
		print("OK: Rigging appears complete with good weight coverage.")

func sum_array(arr: Array) -> float:
	var total = 0.0
	for val in arr:
		total += float(val)
	return total

func maxii(arr: Array) -> int:
	var max_val = 0
	for val in arr:
		max_val = maxi(max_val, int(val))
	return max_val

func any_invalid(bone_indices: Array, max_bone: int) -> bool:
	for idx in bone_indices:
		if int(idx) >= max_bone:
			return true
	return false
