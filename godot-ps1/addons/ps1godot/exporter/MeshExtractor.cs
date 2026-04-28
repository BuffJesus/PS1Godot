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
// Auto-rebind: after writing the .glb, we trigger the editor's import
// pipeline + load the resulting PackedScene + walk it for the first
// MeshInstance3D + assign its Mesh back to the PS1MeshInstance. The
// reference is now path-backed (points at the .glb's sub-resource);
// Godot's import scanner refreshes the Mesh data on every Blender
// re-export, and the PS1MeshInstance picks up the changes
// automatically. Saves the manual "drag mesh onto slot" step.
public static class MeshExtractor
{
    public sealed class Result
    {
        public string OutputPath = "";
        public bool Success = false;
        public string ErrorMessage = "";
        // Rebind step ran + assigned a fresh path-backed Mesh. False
        // when extraction succeeded but auto-rebind didn't (import
        // pipeline may need a moment; author can re-run).
        public bool Rebound = false;
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

        // ── Auto-rebind ────────────────────────────────────────────
        // Trigger Godot's editor import pipeline against the fresh
        // .glb, then load the resulting PackedScene + extract its
        // first MeshInstance3D's Mesh + assign back to pmi.Mesh.
        // The reference is now PATH-BACKED (sub-resource of the
        // imported PackedScene), so Blender re-edits flow through
        // Godot's normal scanner without any manual rebinding.
        try
        {
            string resPath = ProjectSettings.LocalizePath(outputPath);
            if (!resPath.StartsWith("res://"))
            {
                // Output dir was outside res://; nothing to rebind to.
                // Author can manually drop the .glb into the project
                // tree afterwards.
                return result;
            }

            // EditorInterface.GetResourceFilesystem().UpdateFile() +
            // ReimportFiles() is the synchronous-import path. After
            // these return, the .glb has been imported and its
            // PackedScene is loadable.
            var efs = EditorInterface.Singleton.GetResourceFilesystem();
            efs.UpdateFile(resPath);
            efs.ReimportFiles(new[] { resPath });

            // Load + walk for the first Mesh.
            var scene = ResourceLoader.Load<PackedScene>(resPath);
            if (scene == null) return result;
            var instance = scene.Instantiate<Node>();
            try
            {
                var firstMesh = FindFirstMesh(instance);
                if (firstMesh != null)
                {
                    pmi.Mesh = firstMesh;
                    result.Rebound = true;
                }
            }
            finally
            {
                instance.QueueFree();
            }
        }
        catch (Exception ex)
        {
            // Auto-rebind is best-effort; extraction itself succeeded
            // so don't flip Success=false. Surface the diagnostic so
            // the author knows to drag the mesh manually.
            result.ErrorMessage = $"auto-rebind failed: {ex.Message}";
        }

        return result;
    }

    private static Mesh? FindFirstMesh(Node n)
    {
        if (n is MeshInstance3D mi && mi.Mesh != null) return mi.Mesh;
        foreach (var child in n.GetChildren())
        {
            var hit = FindFirstMesh(child);
            if (hit != null) return hit;
        }
        return null;
    }
}
#endif
