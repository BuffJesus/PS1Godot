#if TOOLS
using Godot;

namespace PS1Godot.Exporter;

// PS1MeshInstance.BakedColors is a per-instance node property the
// splashpack writer reads at export time. Godot's editor renderer
// uses the underlying Mesh resource's COLOR vertex array — which
// the bakers never touch. Result: meshes with baked Vertex Lighting
// or AO render WHITE in the editor viewport while showing
// correctly on PSX.
//
// This helper closes the loop. After a baker stamps BakedColors,
// it calls ApplyBakedColorsTo(pmi) which:
//   1. Builds an ArrayMesh duplicate from the current Mesh with
//      the BakedColors written into the ARRAY_COLOR slot of every
//      surface.
//   2. Replaces pmi.Mesh with the duplicate.
//
// The original Mesh resource is NOT mutated — pmi gets its own
// per-instance ArrayMesh so two PS1MeshInstance(s) sharing a
// SubResource still bake independently. Side effect: saving the
// .tscn after a bake serialises an inline ArrayMesh sub-resource
// per baked instance instead of the shared reference. That's the
// price for per-instance lighting.
//
// Idempotent — calling on the same pmi twice with the same
// BakedColors produces the same end state (modulo Resource
// identity). The BackgroundBaker's transient mesh-swap path stays
// as a safety net for hand-painted BakedColors that bypass the
// standard bakers.
public static class BakedColorMeshHelper
{
    public static void ApplyBakedColorsTo(PS1MeshInstance pmi)
    {
        if (pmi == null || pmi.Mesh == null) return;
        if (pmi.BakedColors == null || pmi.BakedColors.Length == 0) return;

        var rebuilt = BuildMeshWithColors(pmi.Mesh, pmi.BakedColors);
        if (rebuilt != null) pmi.Mesh = rebuilt;
    }

    // Build an ArrayMesh that mirrors `src` (per-surface arrays
    // preserved) but with `bakedColors` stamped into ARRAY_COLOR.
    // bakedColors is interpreted as concatenated per-surface colors
    // — same convention the splashpack writer uses. Remainder pads
    // with white if shorter than total verts.
    public static ArrayMesh? BuildMeshWithColors(Mesh src, Color[] bakedColors)
    {
        int surfaceCount = src.GetSurfaceCount();
        if (surfaceCount == 0) return null;

        var dst = new ArrayMesh();
        int colorCursor = 0;
        for (int s = 0; s < surfaceCount; s++)
        {
            var arrays = src.SurfaceGetArrays(s);
            if (arrays.Count <= (int)Mesh.ArrayType.Color) continue;

            var verts = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            int vc = verts.Length;
            var colors = new Color[vc];
            for (int i = 0; i < vc; i++)
            {
                colors[i] = colorCursor < bakedColors.Length
                    ? bakedColors[colorCursor]
                    : Colors.White;
                colorCursor++;
            }
            arrays[(int)Mesh.ArrayType.Color] = colors;

            dst.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        }
        return dst;
    }
}
#endif
