#if TOOLS
using System;
using System.Collections.Generic;
using Godot;

namespace PS1Godot.Exporter;

// Phase L1 — bake from Godot scene lights into PS1MeshInstance.BakedColors.
//
// Mirrors the Blender side's vc_bake_scene_lights operator (see
// tools/blender-addon/.../operators/vertex_lighting.py), with the same
// formula + the same 0.8 PSX 2x semi-trans ceiling. Authors who
// already lit a Godot scene for editor preview get a one-button bake
// that produces vertex colors matching what's on screen.
//
// Storage: per-mesh-instance Color[] override on PS1MeshInstance.
// SceneCollector / PSXMesh prefer this over the mesh's COLOR channel
// when populated. Per-instance means same mesh in two scenes can have
// two lighting setups — a feature SplashEdit can't match because it
// bakes into the source mesh.
//
// Minimum tier (this file): single-surface meshes only. Multi-surface
// meshes are typically PS1MeshGroups in this codebase, and groups
// don't get baked at this level (they merge children at export).
// Phase L2 will extend to multi-surface once we have AO baking that
// needs the same per-surface storage.
public static class VertexLightingBaker
{
    // Same ceiling as the Blender side + SplashEdit. Bake to 1.0 and
    // any mesh tagged Translucent later white-outs through the PSX
    // hardware 2x semi-trans blend.
    private const float PSXVertexBakeCeiling = 0.8f;

    public sealed class Result
    {
        public int MeshesBaked = 0;
        public int VerticesPainted = 0;
        public int LightsUsed = 0;
        public int Skipped = 0;
        public List<string> SkippedReasons = new();
    }

    /// <summary>
    /// Bake every selected PS1MeshInstance against every visible
    /// DirectionalLight3D / OmniLight3D / SpotLight3D in the scene.
    /// Mutates each PS1MeshInstance.BakedColors. Returns aggregate
    /// stats for the caller to log.
    /// </summary>
    public static Result Bake(Node sceneRoot, IReadOnlyList<Node> selection)
    {
        var result = new Result();

        var lights = CollectLights(sceneRoot);
        result.LightsUsed = lights.Count;
        if (lights.Count == 0)
        {
            // No work to do — but we still mutate selected meshes by
            // clearing their override (so authors can "reset" by
            // baking with all lights off).
            foreach (var node in selection)
            {
                if (node is not PS1MeshInstance pmi) continue;
                pmi.BakedColors = Array.Empty<Color>();
            }
            return result;
        }

        foreach (var node in selection)
        {
            if (node is not PS1MeshInstance pmi)
            {
                // Silent-skip used to be the rule here, but a freshly-
                // imported GLB loads as MeshInstance3D — authors hit the
                // "0 meshes baked / 0 skipped" no-feedback wall and gave
                // up. Surface a real reason + name the fix tool.
                if (node is MeshInstance3D)
                {
                    result.Skipped++;
                    result.SkippedReasons.Add(
                        $"{node.Name}: MeshInstance3D, not PS1MeshInstance. " +
                        $"Run Tools → 'PS1Godot: Convert selected MeshInstance3D to " +
                        $"PS1MeshInstance' first, then re-bake.");
                }
                else if (node is Node3D)
                {
                    result.Skipped++;
                    result.SkippedReasons.Add(
                        $"{node.Name}: {node.GetType().Name} — only PS1MeshInstance " +
                        $"(or MeshInstance3D after conversion) carries BakedColors.");
                }
                // Other selection types (Camera3D, lights, the scene root,
                // etc.) are intentionally ignored — common when the author
                // multi-selects + bakes the whole tree.
                continue;
            }
            if (pmi.Mesh == null)
            {
                result.Skipped++;
                result.SkippedReasons.Add($"{pmi.Name}: no Mesh assigned");
                continue;
            }
            if (pmi.Mesh.GetSurfaceCount() != 1)
            {
                result.Skipped++;
                result.SkippedReasons.Add(
                    $"{pmi.Name}: {pmi.Mesh.GetSurfaceCount()} surfaces — Phase L1 only supports single-surface meshes (Phase L2 extends).");
                continue;
            }

            var arrays = pmi.Mesh.SurfaceGetArrays(0);
            var verts = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var normals = arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
            if (verts.Length == 0)
            {
                result.Skipped++;
                result.SkippedReasons.Add($"{pmi.Name}: surface 0 has 0 vertices");
                continue;
            }
            // Normals can be empty (untextured cube primitives etc.);
            // fall back to Up when missing so we still get sensible
            // lighting from the SUN above.
            bool haveNormals = normals.Length == verts.Length;

            var meshWorld = pmi.GlobalTransform;
            var meshRot = meshWorld.Basis;

            var output = new Color[verts.Length];
            for (int i = 0; i < verts.Length; i++)
            {
                Vector3 wp = meshWorld * verts[i];
                Vector3 wn = haveNormals ? (meshRot * normals[i]).Normalized() : Vector3.Up;

                float r = 0f, g = 0f, b = 0f;
                foreach (var L in lights)
                {
                    float ndotl = L.Lambert(wp, wn);
                    if (ndotl <= 0f) continue;
                    r += L.Color.R * L.Energy * ndotl;
                    g += L.Color.G * L.Energy * ndotl;
                    b += L.Color.B * L.Energy * ndotl;
                }
                output[i] = new Color(
                    Mathf.Clamp(r, 0f, PSXVertexBakeCeiling),
                    Mathf.Clamp(g, 0f, PSXVertexBakeCeiling),
                    Mathf.Clamp(b, 0f, PSXVertexBakeCeiling),
                    1f);
            }

            pmi.BakedColors = output;
            // Also stamp into the Mesh's COLOR vertex array so Godot's
            // editor renderer shows the bake. Without this, BakedColors
            // is invisible in the viewport and only manifests on PSX
            // after export.
            BakedColorMeshHelper.ApplyBakedColorsTo(pmi);
            result.MeshesBaked++;
            result.VerticesPainted += output.Length;
        }

        return result;
    }

    // ── Light collection + per-light contribution ───────────────────

    private readonly struct LightInfo
    {
        public readonly Light3D Node;
        public readonly Color Color;
        public readonly float Energy;
        public readonly Vector3 Position;
        // For DirectionalLight3D: direction the light shines TO (-Z).
        // For Omni/Spot: ignored.
        public readonly Vector3 Forward;
        // For Spot: cos(half_angle), cos(half_angle * (1 - blend))
        public readonly float CosOuter;
        public readonly float CosInner;
        public readonly float Range;
        public readonly LightKind Kind;

        public LightInfo(Light3D n, LightKind kind, Color color, float energy,
                         Vector3 pos, Vector3 forward, float cosOuter, float cosInner, float range)
        {
            Node = n; Kind = kind; Color = color; Energy = energy;
            Position = pos; Forward = forward;
            CosOuter = cosOuter; CosInner = cosInner; Range = range;
        }

        /// <summary>Lambertian contribution at world point + normal,
        /// including falloff / cone for non-directional lights.</summary>
        public float Lambert(Vector3 worldPos, Vector3 worldNormal)
        {
            switch (Kind)
            {
                case LightKind.Directional:
                {
                    // Forward = direction the light shines TO. The
                    // "from-light" direction reaching the surface is
                    // -Forward; the surface receives light from there
                    // when its normal faces back along that vector.
                    float ndotl = -worldNormal.Dot(Forward);
                    return ndotl > 0f ? ndotl : 0f;
                }
                case LightKind.Omni:
                case LightKind.Spot:
                {
                    Vector3 toLight = Position - worldPos;
                    float dist = toLight.Length();
                    if (dist < 1e-6f) return 0f;
                    if (Range > 0f && dist > Range) return 0f;
                    toLight /= dist;

                    float ndotl = worldNormal.Dot(toLight);
                    if (ndotl <= 0f) return 0f;

                    // Soft inverse-square. Same formula as the Blender
                    // side: 1 / (1 + d^2/r^2) where r is the light's
                    // characteristic radius.
                    float r = Range > 0f ? Range : 4f;
                    float falloff = 1f / (1f + (dist * dist) / (r * r));
                    float intensity = ndotl * falloff;

                    if (Kind == LightKind.Spot)
                    {
                        // Forward = spot axis direction (-Z of the light).
                        float coneDot = -toLight.Dot(Forward);
                        if (coneDot <= CosOuter) return 0f;
                        if (coneDot >= CosInner) return intensity;
                        float t = (coneDot - CosOuter) / Mathf.Max(1e-6f, CosInner - CosOuter);
                        return intensity * t;
                    }
                    return intensity;
                }
            }
            return 0f;
        }
    }

    private enum LightKind { Directional, Omni, Spot }

    private static List<LightInfo> CollectLights(Node root)
    {
        var lights = new List<LightInfo>();
        Walk(root, lights);
        return lights;
    }

    private static void Walk(Node n, List<LightInfo> dest)
    {
        if (n is DirectionalLight3D dir && dir.Visible)
        {
            // -Z of the light's basis is the direction it shines TO
            // (Godot convention).
            Vector3 forward = -dir.GlobalBasis.Z.Normalized();
            dest.Add(new LightInfo(
                dir, LightKind.Directional,
                dir.LightColor, dir.LightEnergy,
                Vector3.Zero, forward,
                cosOuter: 0f, cosInner: 0f, range: 0f));
        }
        else if (n is OmniLight3D omni && omni.Visible)
        {
            dest.Add(new LightInfo(
                omni, LightKind.Omni,
                omni.LightColor, omni.LightEnergy,
                omni.GlobalPosition, Vector3.Zero,
                cosOuter: 0f, cosInner: 0f,
                range: omni.OmniRange));
        }
        else if (n is SpotLight3D spot && spot.Visible)
        {
            Vector3 forward = -spot.GlobalBasis.Z.Normalized();
            // Godot SpotLight3D.SpotAngle is the FULL cone angle in
            // degrees, half-angle is what we want for the cosine.
            float halfAngleRad = Mathf.DegToRad(spot.SpotAngle * 0.5f);
            // Attenuation at the cone edge — Godot has spot_angle_attenuation
            // (0..n) but for our PS1-style hard cutoff we just use a
            // small inner cone (90% of the half angle) as the fade
            // start. Authors who want softer fades set their Blender
            // side via spot_blend; on Godot side we keep it simple.
            float cosOuter = Mathf.Cos(halfAngleRad);
            float cosInner = Mathf.Cos(halfAngleRad * 0.9f);
            dest.Add(new LightInfo(
                spot, LightKind.Spot,
                spot.LightColor, spot.LightEnergy,
                spot.GlobalPosition, forward,
                cosOuter, cosInner,
                range: spot.SpotRange));
        }

        foreach (var child in n.GetChildren())
        {
            Walk(child, dest);
        }
    }
}
#endif
