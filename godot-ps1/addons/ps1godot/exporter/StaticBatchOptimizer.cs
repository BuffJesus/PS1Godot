#if TOOLS
using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace PS1Godot.Exporter;

// Slot D1 — automatic static-mesh batching at export time.
//
// Walks SceneData.Objects, finds every PS1MeshInstance with
// MeshRole == StaticWorld + ExportMode == MergeStatic that's not
// gameplay-bound (no Lua / Tag / interactable / starts-inactive),
// buckets by (DrawPhase, ShadingMode, AlphaMode, AtlasGroup,
// first-material-texture-page-id), and emits one combined GameObject
// per multi-member bucket.
//
// Per-bucket merge mechanics:
//   - Anchor at the AABB-centre of all members so vertex positions
//     stay tight in fp12 range (±8 PSX units = ±32 Godot units at
//     gteScaling=4). Anchor becomes the new GameObject's world
//     position.
//   - For each member: convert each PSXVertex back to Godot units
//     (member-local), apply member's GlobalTransform → world,
//     subtract anchor → anchor-local, re-encode to fp12. Normals
//     rotate-only (anchor basis is identity).
//   - Concatenate Triangles + SurfaceTextureIndices across members.
//   - Per-Tri TextureIndex is preserved verbatim, so the bucket's
//     members can use different specific texture entries as long as
//     they share the same texture_page_id (the bucket key).
//
// What the bucket key intentionally does NOT include:
//   - per-mesh node names (we lose them)
//   - LuaFileIndex (eligibility check excludes scripted meshes)
//   - StartsInactive (eligibility check excludes pre-placed pool meshes)
//   - tags (eligibility check excludes Tag != 0)
//
// What the bucket key intentionally INCLUDES:
//   - DrawPhase (different render-order buckets must not merge)
//   - ShadingMode (Unlit + VertexColor surfaces interpret colors differently)
//   - AlphaMode (Cutout vs Opaque differs at GPU primitive level)
//   - AtlasGroup (soft hint for the packer; bucketing here keeps it intact)
//   - First material's texture_page_id (the only way the packer keeps
//     a coherent atlas page across the merged mesh — see § 3 of
//     docs/ps1_asset_pipeline_plan.md "Atlas dependency ordering" tradeoff (a))
//
// Big tradeoff to flag: once batched, the renderer can't cull the
// pieces independently. Mitigated for typical scenes by the bucket's
// combined AABB still being modest (one chunk's worth of decals is
// ~bounded), but huge open exteriors should leave their meshes
// non-batched (they will: large outdoor scenes typically use
// KeepSeparate for streaming-cell granularity anyway).
public static class StaticBatchOptimizer
{
    // Slot D1 kill-switch. When chasing a regression, flip to `false`
    // to bypass the optimizer entirely so each member exports as its
    // own GameObject. The static-batch GO iteration savings disappear,
    // but you get unambiguous "is this the optimizer or not" signal.
    private const bool s_enabled = false;

    /// <summary>
    /// Bucket eligible static meshes and replace each multi-member
    /// bucket with a single merged SceneObject. Mutates data.Objects
    /// in place. Pinned (ineligible) objects pass through unchanged.
    /// </summary>
    public static void Optimize(SceneData data)
    {
        if (!s_enabled)
        {
            GD.Print("[PS1Godot] Static batch: DISABLED via s_enabled=false (kill-switch).");
            return;
        }
        if (data.Objects == null || data.Objects.Count == 0) return;

        var pinned = new List<SceneObject>();
        var eligible = new List<SceneObject>();
        foreach (var obj in data.Objects)
        {
            if (IsBatchEligible(obj)) eligible.Add(obj);
            else pinned.Add(obj);
        }

        if (eligible.Count <= 1)
        {
            // Nothing to merge — keep data.Objects as-is.
            return;
        }

        // Bucket by key. Dictionary<string, ...> keyed on a stringified
        // tuple keeps the C# code obvious without committing to a
        // record-with-equality. Bucket order doesn't matter for output.
        var buckets = new Dictionary<string, List<SceneObject>>(StringComparer.Ordinal);
        foreach (var obj in eligible)
        {
            string key = MakeBucketKey(obj, data);
            if (!buckets.TryGetValue(key, out var list))
            {
                list = new List<SceneObject>();
                buckets[key] = list;
            }
            list.Add(obj);
        }

        var result = new List<SceneObject>(pinned);
        int batchesCreated = 0;
        int meshesAbsorbed = 0;
        int passedThrough  = 0;

        foreach (var (key, members) in buckets)
        {
            if (members.Count == 1)
            {
                // Single-member bucket — no merge benefit, pass through.
                result.Add(members[0]);
                passedThrough++;
                continue;
            }

            var merged = MergeBucket(members, data, key);
            if (merged != null)
            {
                result.Add(merged);
                batchesCreated++;
                meshesAbsorbed += members.Count;
            }
            else
            {
                // Merge failed (shouldn't happen but defensively) —
                // pass members through individually.
                result.AddRange(members);
                passedThrough += members.Count;
            }
        }

        data.Objects.Clear();
        data.Objects.AddRange(result);

        if (batchesCreated > 0)
        {
            int saved = meshesAbsorbed - batchesCreated;
            GD.Print(
                $"[PS1Godot] Static batch: {meshesAbsorbed} mesh(es) → {batchesCreated} batch(es) " +
                $"(-{saved} GameObject iteration costs).");
        }
    }

    // ── Eligibility ──────────────────────────────────────────────────

    private static bool IsBatchEligible(SceneObject obj)
    {
        if (obj == null || obj.Mesh == null || obj.Mesh.Triangles == null) return false;
        if (obj.Mesh.Triangles.Count == 0) return false;

        // Slot C metadata gates the merge. Any one of these being non-
        // default = explicit author signal that the mesh stays separate.
        if (obj.MeshRole != MeshRole.StaticWorld) return false;
        if (obj.ExportMode != ExportMode.MergeStatic) return false;

        // Gameplay handles. Lua scripts + tags + interactables address
        // their target by name from the runtime; merging breaks that.
        if (obj.LuaFileIndex >= 0) return false;
        if (obj.Tag != 0) return false;
        if (obj.StartsInactive) return false;

        // PS1MeshInstance.Interactable would trigger a per-object
        // hover-prompt at runtime; merging would lump multiple
        // interactables into one detection volume.
        if (obj.Node is PS1MeshInstance pmi && pmi.Interactable) return false;

        return true;
    }

    // ── Bucket key ───────────────────────────────────────────────────

    // Members of one bucket merge into a single batch with vertex
    // positions stored as anchor-local fp12 in int16 (~±32 Godot units
    // of headroom from the anchor at gteScaling=4). If the bucket spans
    // a wider radius than that, far verts clamp and render off-screen.
    // Quantising the world AABB centre to a coarse grid forces members
    // farther apart than the cell size into different buckets. Cell
    // size = 24m gives ~12m of headroom either side of the anchor —
    // safely under the int16 envelope, with room for member-internal
    // mesh extent on top.
    private const float SpatialCellSize = 24.0f;

    /// <summary>
    /// Build a stable string key for the bucket this SceneObject
    /// belongs to. Members with the same key merge into a single
    /// batched GameObject; different keys stay separate.
    /// </summary>
    private static string MakeBucketKey(SceneObject obj, SceneData data)
    {
        // First-material texture_page_id from PS1MaterialMetadata
        // (Slot C). Empty string = "no per-material override" — those
        // cluster together which is fine; the packer puts them on
        // best-fit pages anyway.
        string texturePageId = "";
        if (obj.Node is PS1MeshInstance pmi && pmi.Materials != null && pmi.Materials.Count > 0)
        {
            var firstMeta = pmi.Materials[0];
            if (firstMeta != null) texturePageId = firstMeta.TexturePageId ?? "";
        }
        else if (obj.Node is PS1MeshGroup pmg && pmg.Materials != null && pmg.Materials.Count > 0)
        {
            var firstMeta = pmg.Materials[0];
            if (firstMeta != null) texturePageId = firstMeta.TexturePageId ?? "";
        }

        // Spatial bucket: world AABB centre quantised to SpatialCellSize.
        // Falls back to the node's world position when the AABB is
        // unavailable / degenerate so single-vertex meshes still group.
        Vector3 worldCentre;
        if (obj.LocalAabb.Size != Vector3.Zero && obj.Node.IsInsideTree())
        {
            var memberWorld = obj.Node.GlobalTransform;
            var local = obj.LocalAabb;
            worldCentre = memberWorld * (local.Position + local.Size * 0.5f);
        }
        else
        {
            worldCentre = obj.Node.IsInsideTree()
                ? obj.Node.GlobalPosition
                : obj.Node.Position;
        }
        int gx = Mathf.FloorToInt(worldCentre.X / SpatialCellSize);
        int gy = Mathf.FloorToInt(worldCentre.Y / SpatialCellSize);
        int gz = Mathf.FloorToInt(worldCentre.Z / SpatialCellSize);

        return string.Join("|",
            obj.DrawPhase.ToString(),
            obj.ShadingMode.ToString(),
            obj.AlphaMode.ToString(),
            obj.AtlasGroup.ToString(),
            obj.Translucent ? "T" : "O",   // legacy Translucent bool — must match for batchability
            texturePageId,
            $"{gx},{gy},{gz}");
    }

    // ── Merge ────────────────────────────────────────────────────────

    private static SceneObject? MergeBucket(List<SceneObject> members, SceneData data, string keyForName)
    {
        if (members.Count == 0) return null;

        // Anchor = AABB-centre across all members in WORLD space, so
        // the merged mesh's vertices stay symmetric around 0 in
        // anchor-local space (kindest to fp12 short range).
        Vector3 anchor = ComputeWorldAabbCentre(members);

        // Safety guard: PSX vertex positions are int16 fp12, capping
        // anchor-local extent at ~32 Godot units (32767 / 4096 * gteScaling).
        // The spatial bucket key keeps members within one cell, but a
        // single member with an oversize local AABB (long floor / wall)
        // can still push the combined extent past the envelope. Bail
        // out here and pass members through individually rather than
        // shipping clamped verts that render as scrambled geometry.
        float safeRadius = 32767.0f / PSXTrig.FixedScale * data.GteScaling - 0.5f;
        Vector3 worldMin = new(float.MaxValue, float.MaxValue, float.MaxValue);
        Vector3 worldMax = new(float.MinValue, float.MinValue, float.MinValue);
        foreach (var member in members)
        {
            var mw = member.Node.GlobalTransform;
            var laabb = member.LocalAabb;
            for (int c = 0; c < 8; c++)
            {
                var corner = new Vector3(
                    (c & 1) != 0 ? laabb.Position.X + laabb.Size.X : laabb.Position.X,
                    (c & 2) != 0 ? laabb.Position.Y + laabb.Size.Y : laabb.Position.Y,
                    (c & 4) != 0 ? laabb.Position.Z + laabb.Size.Z : laabb.Position.Z);
                Vector3 wc = mw * corner;
                worldMin = new Vector3(Mathf.Min(worldMin.X, wc.X), Mathf.Min(worldMin.Y, wc.Y), Mathf.Min(worldMin.Z, wc.Z));
                worldMax = new Vector3(Mathf.Max(worldMax.X, wc.X), Mathf.Max(worldMax.Y, wc.Y), Mathf.Max(worldMax.Z, wc.Z));
            }
        }
        float maxExtent = Mathf.Max(Mathf.Max(
            Mathf.Max(Mathf.Abs(worldMax.X - anchor.X), Mathf.Abs(anchor.X - worldMin.X)),
            Mathf.Max(Mathf.Abs(worldMax.Y - anchor.Y), Mathf.Abs(anchor.Y - worldMin.Y))),
            Mathf.Max(Mathf.Abs(worldMax.Z - anchor.Z), Mathf.Abs(anchor.Z - worldMin.Z)));
        if (maxExtent > safeRadius)
        {
            GD.PushWarning(
                $"[PS1Godot] Static batch '{keyForName}' would overflow int16 fp12 envelope " +
                $"(extent {maxExtent:F1}m > safe {safeRadius:F1}m at gteScaling={data.GteScaling}). " +
                $"Passing {members.Count} member(s) through unmerged. " +
                $"Likely cause: an oversize floor/wall mesh; split it or shrink SpatialCellSize.");
            return null;
        }

        var combinedTris = new List<Tri>();
        var combinedTexIndices = new List<int>();
        Vector3 minLocal = new(float.MaxValue, float.MaxValue, float.MaxValue);
        Vector3 maxLocal = new(float.MinValue, float.MinValue, float.MinValue);

        foreach (var member in members)
        {
            var memberWorld = member.Node.GlobalTransform;
            var memberRot = memberWorld.Basis;
            // Transform: world position - anchor → anchor-local. Since
            // anchor's basis is identity, this is straight subtraction.
            // Local rotation is the member's basis (no anchor rotation).

            foreach (var tri in member.Mesh.Triangles)
            {
                combinedTris.Add(new Tri
                {
                    v0 = TransformVertex(tri.v0, memberWorld, memberRot, anchor, data.GteScaling),
                    v1 = TransformVertex(tri.v1, memberWorld, memberRot, anchor, data.GteScaling),
                    v2 = TransformVertex(tri.v2, memberWorld, memberRot, anchor, data.GteScaling),
                    TextureIndex = tri.TextureIndex,
                });
            }
            combinedTexIndices.AddRange(member.SurfaceTextureIndices);

            // Track combined-AABB in anchor-local Godot units. Used for
            // frustum culling at runtime — the whole bucket stands or
            // falls together.
            var memberAabb = member.LocalAabb;
            // Transform 8 corners through member→world→anchor-local
            for (int c = 0; c < 8; c++)
            {
                var corner = new Vector3(
                    (c & 1) != 0 ? memberAabb.Position.X + memberAabb.Size.X : memberAabb.Position.X,
                    (c & 2) != 0 ? memberAabb.Position.Y + memberAabb.Size.Y : memberAabb.Position.Y,
                    (c & 4) != 0 ? memberAabb.Position.Z + memberAabb.Size.Z : memberAabb.Position.Z);
                Vector3 worldCorner = memberWorld * corner;
                Vector3 localCorner = worldCorner - anchor;
                minLocal = new Vector3(Mathf.Min(minLocal.X, localCorner.X),
                                       Mathf.Min(minLocal.Y, localCorner.Y),
                                       Mathf.Min(minLocal.Z, localCorner.Z));
                maxLocal = new Vector3(Mathf.Max(maxLocal.X, localCorner.X),
                                       Mathf.Max(maxLocal.Y, localCorner.Y),
                                       Mathf.Max(maxLocal.Z, localCorner.Z));
            }
        }

        var batchedMesh = new PSXMesh();
        batchedMesh.Triangles.AddRange(combinedTris);

        // Synthetic Node3D as the SceneObject host. Parentless, so
        // GlobalPosition == Position. Free'd by GC when SceneData is
        // disposed; the Editor doesn't keep tool-mode references.
        var firstMember = members[0];
        var batchNode = new Node3D
        {
            Name = $"_StaticBatch_{ShortHash(keyForName):x8}_{members.Count}",
        };
        batchNode.Transform = new Transform3D(Basis.Identity, anchor);

        // Inherit Slot C metadata from first member — every member has
        // the same key, so any consistent member's values work.
        return new SceneObject
        {
            Node = batchNode,
            Mesh = batchedMesh,
            LocalAabb = new Aabb(minLocal, maxLocal - minLocal),
            SurfaceTextureIndices = combinedTexIndices.ToArray(),
            LuaFileIndex = -1,
            Tag = 0,
            StartsInactive = false,
            Translucent = firstMember.Translucent,
            MeshRole       = firstMember.MeshRole,
            ExportMode     = firstMember.ExportMode,
            DrawPhase      = firstMember.DrawPhase,
            ShadingMode    = firstMember.ShadingMode,
            AlphaMode      = firstMember.AlphaMode,
            AtlasGroup     = firstMember.AtlasGroup,
            Residency      = firstMember.Residency,
            // IDs lose their per-member meaning post-merge — the merged
            // GameObject stands for the whole bucket. Empty-string the
            // per-mesh IDs; preserve chunk/region/archive (they're
            // shared across the bucket since members are static peers).
            AssetId        = "",
            MeshId         = batchNode.Name,
            ChunkId        = firstMember.ChunkId,
            RegionId       = firstMember.RegionId,
            AreaArchiveId  = firstMember.AreaArchiveId,
        };
    }

    // ── Transform helpers ────────────────────────────────────────────

    /// <summary>
    /// Decode a PSXVertex's local fp12 position back to Godot units,
    /// transform through member→world, anchor-localize, and re-encode.
    /// Normals: rotate-only via the member's basis (anchor basis is identity).
    /// All other channels (uv, color) are byte/integer — no transform needed.
    /// </summary>
    private static PSXVertex TransformVertex(PSXVertex v, Transform3D memberWorld,
                                             Basis memberRot, Vector3 anchor, float gteScaling)
    {
        // Coordinate frames: PSXMesh.cs stored these verts as
        //   vx =  pos.X,  vy = -pos.Y,  vz = -pos.Z   (PSX is left-handed,
        // Godot right-handed; SplashpackWriter applies the same negation
        // to GO position + AABB at write time). To run a Godot Transform3D
        // through the position we must first flip Y/Z back to Godot space,
        // then flip again on re-encode so the runtime renderer sees the
        // same convention as a non-batched mesh. Skipping these flips
        // happens to cancel for identity-rotation members but mangles
        // anything rotated — walls, doors, props — and the broken verts
        // get clamped off-camera.
        Vector3 memberLocal = new(
             (v.vx / PSXTrig.FixedScale) * gteScaling,
            -(v.vy / PSXTrig.FixedScale) * gteScaling,
            -(v.vz / PSXTrig.FixedScale) * gteScaling);
        Vector3 world = memberWorld * memberLocal;
        Vector3 anchorLocal = world - anchor;

        // Normal: rotate by member basis. fp12 normals are unit-length
        // vectors stored as (component × 4096). Don't normalize after —
        // the member's basis may include uniform scale we want to keep
        // (the existing pipeline already normalizes during PSXMesh
        // construction; this is a re-rotation only). Same Y/Z flip
        // dance as positions.
        Vector3 memberLocalN = new(
             v.nx / PSXTrig.FixedScale,
            -v.ny / PSXTrig.FixedScale,
            -v.nz / PSXTrig.FixedScale);
        Vector3 worldN = memberRot * memberLocalN;

        return new PSXVertex
        {
            vx = PSXTrig.ConvertCoordinateToPSX( anchorLocal.X, gteScaling),
            vy = PSXTrig.ConvertCoordinateToPSX(-anchorLocal.Y, gteScaling),
            vz = PSXTrig.ConvertCoordinateToPSX(-anchorLocal.Z, gteScaling),
            nx = PSXTrig.ConvertToFixed12( worldN.X),
            ny = PSXTrig.ConvertToFixed12(-worldN.Y),
            nz = PSXTrig.ConvertToFixed12(-worldN.Z),
            u = v.u, v = v.v,
            r = v.r, g = v.g, b = v.b,
        };
    }

    private static Vector3 ComputeWorldAabbCentre(List<SceneObject> members)
    {
        // Walk member AABB corners through their world transforms and
        // accumulate min/max in world space.
        Vector3 min = new(float.MaxValue, float.MaxValue, float.MaxValue);
        Vector3 max = new(float.MinValue, float.MinValue, float.MinValue);
        foreach (var m in members)
        {
            var aabb = m.LocalAabb;
            var w = m.Node.GlobalTransform;
            for (int c = 0; c < 8; c++)
            {
                var corner = new Vector3(
                    (c & 1) != 0 ? aabb.Position.X + aabb.Size.X : aabb.Position.X,
                    (c & 2) != 0 ? aabb.Position.Y + aabb.Size.Y : aabb.Position.Y,
                    (c & 4) != 0 ? aabb.Position.Z + aabb.Size.Z : aabb.Position.Z);
                Vector3 wc = w * corner;
                min = new Vector3(Mathf.Min(min.X, wc.X), Mathf.Min(min.Y, wc.Y), Mathf.Min(min.Z, wc.Z));
                max = new Vector3(Mathf.Max(max.X, wc.X), Mathf.Max(max.Y, wc.Y), Mathf.Max(max.Z, wc.Z));
            }
        }
        return (min + max) * 0.5f;
    }

    // FNV-1a 32-bit. Stable across runs so re-exporting the same scene
    // produces the same batch GameObject names — matters for diffing /
    // versioned splashpacks.
    private static uint ShortHash(string s)
    {
        uint h = 2166136261u;
        foreach (char c in s)
        {
            h ^= c;
            h *= 16777619u;
        }
        return h;
    }
}
#endif
