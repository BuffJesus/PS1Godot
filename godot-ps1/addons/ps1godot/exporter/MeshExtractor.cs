#if TOOLS
using System;
using System.Collections.Generic;
using System.IO;
using Godot;

namespace PS1Godot.Exporter;

// Godot → Blender geometry round-trip helper.
//
// The "Edit Mesh in Blender" workflow: author has a PS1MeshInstance
// with a Mesh that wasn't authored in Blender (procedural ArrayMesh,
// primitive BoxMesh, .glb that's been mutated post-import, etc.) and
// wants to push it OUT to Blender for editing. Existing tooling
// already covers the other direction (Blender → Godot via
// PS1GODOT_OT_export_to_godot in the addon). This closes the loop.
//
// Output convention: the extracted .glb lands at
//   <project_root>/<DefaultBlenderMeshDir>/<mesh_id>.glb
// — exactly the path the Blender add-on's "Export to Godot" button
// writes to. So when the author edits in Blender and clicks back, the
// same file is overwritten and Godot's import scanner picks up the
// changed mesh automatically. No manual file shuffling.
//
// Caveat that ships with the operator's user-facing message: the
// PS1MeshInstance.Mesh field still references the OLD Mesh resource.
// After Blender edits + re-export, the .glb's import produces a NEW
// PackedScene with a new Mesh resource. The PS1MeshInstance won't
// auto-rebind — author drags the imported mesh onto the slot manually.
// Auto-rebind via .import config + meshes/save_to_file is Phase 2 of
// this feature; we ship the minimum tier first.
public static class MeshExtractor
{
    public sealed class Result
    {
        public string OutputPath = "";
        public bool Success = false;
        public string ErrorMessage = "";
    }

    public static Result ExtractToGlb(PS1MeshInstance pmi, string outputDir)
    {
        var result = new Result();

        if (pmi == null || pmi.Mesh == null)
        {
            result.ErrorMessage = "PS1MeshInstance has no Mesh assigned.";
            return result;
        }

        // Ensure mesh_id so the output path is stable across exports.
        // Same auto-gen logic the Blender writer uses.
        IdAutoGen.EnsureIds(pmi);

        try
        {
            Directory.CreateDirectory(outputDir);
        }
        catch (Exception ex)
        {
            result.ErrorMessage = $"could not create '{outputDir}': {ex.Message}";
            return result;
        }

        string outputPath = Path.Combine(outputDir, $"{pmi.MeshId}.glb");

        // GLTFDocument needs a Node3D root + a MeshInstance3D child.
        // We use a fresh tree so the source PS1MeshInstance doesn't get
        // moved (Godot would re-parent if we tried to AddChild it).
        // The temp tree lives under SceneTree.Root for the duration of
        // the export — AppendFromScene needs valid tree context for
        // global transforms.
        var tree = ((SceneTree)Engine.GetMainLoop());
        if (tree == null || tree.Root == null)
        {
            result.ErrorMessage = "no SceneTree available — running outside the editor?";
            return result;
        }

        var tempHost = new Node3D { Name = "_PS1Godot_ExtractRoot" };
        var tempMesh = new MeshInstance3D
        {
            Name = pmi.MeshId,
            Mesh = pmi.Mesh,
            // Identity transform so the GLB lands centered on world
            // origin in Blender. The author's per-instance position
            // doesn't travel into the asset (PS1Godot scene placement
            // is what actually drives runtime position).
            Transform = Transform3D.Identity,
        };
        tempHost.AddChild(tempMesh);
        tree.Root.AddChild(tempHost);

        try
        {
            var doc = new GltfDocument();
            var state = new GltfState();
            // AppendFromScene walks tempHost + descendants. flags=0
            // gives default behaviour (export everything).
            var err = doc.AppendFromScene(tempHost, state);
            if (err != Error.Ok)
            {
                result.ErrorMessage = $"GltfDocument.AppendFromScene failed: {err}";
                return result;
            }
            err = doc.WriteToFilesystem(state, outputPath);
            if (err != Error.Ok)
            {
                result.ErrorMessage = $"GltfDocument.WriteToFilesystem failed: {err}";
                return result;
            }
        }
        finally
        {
            // Always clean up the temp host even on partial failure.
            tempHost.QueueFree();
        }

        result.OutputPath = outputPath;
        result.Success = true;
        return result;
    }
}
#endif
