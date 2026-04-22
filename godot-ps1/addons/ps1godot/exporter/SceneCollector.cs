using System.Collections.Generic;
using Godot;

namespace PS1Godot.Exporter;

// Walks a Godot scene tree and produces a SceneData snapshot for the writer.
//
// Every visible PS1MeshInstance descendant of the root is exportable. To
// exclude a mesh: hide the node or remove the script. (We may add a "Skip on
// export" property later if authors need the granularity.)
public static class SceneCollector
{
    public static SceneData FromRoot(Node root, string scenePath = "")
    {
        var data = new SceneData { ScenePath = scenePath };
        if (root == null) return data;

        // Diagnostic — list the names + types of root's direct children so
        // we can see at a glance whether PS1Player + cubes etc. actually
        // got loaded into Godot's scene tree (vs. silently dropped due to
        // script-attach errors).
        var childTypes = new System.Collections.Generic.List<string>();
        foreach (var c in root.GetChildren())
        {
            childTypes.Add($"{c.Name}({c.GetType().Name})");
        }
        GD.Print($"[PS1Godot] Root '{root.Name}' children: {string.Join(", ", childTypes)}");

        // Pull scene-level settings from the PS1Scene root if present.
        if (root is PS1Scene ps1Scene)
        {
            data.GteScaling = ps1Scene.GteScaling;
            data.PlayerHeightMeters = ps1Scene.PlayerHeight;
            data.PlayerRadiusMeters = ps1Scene.PlayerRadius;
            data.MoveSpeedMps = ps1Scene.MoveSpeed;
            data.SprintSpeedMps = ps1Scene.SprintSpeed;
            data.JumpHeightMeters = ps1Scene.JumpHeight;
            data.GravityMps2 = ps1Scene.Gravity;
            data.SceneType = ps1Scene.SceneType;
            data.FogEnabled = ps1Scene.FogEnabled;
            data.FogColor = ps1Scene.FogColor;
            data.FogDensity = (byte)Mathf.Clamp(ps1Scene.FogDensity, 1, 255);
            GD.Print($"[PS1Godot] Scene params: move={ps1Scene.MoveSpeed}, jump={ps1Scene.JumpHeight}, gravity={ps1Scene.Gravity}, gteScaling={ps1Scene.GteScaling}");
        }
        else
        {
            GD.PushWarning($"[PS1Godot] Scene root '{root.Name}' ({root.GetType().Name}) is not a PS1Scene — using built-in defaults for player physics. Attach PS1Scene.cs to the root to tune.");
        }

        // Lua dedup: multiple nodes pointing at the same .lua resource share a
        // single LuaFile entry (and get the same luaFileIndex stamped on their
        // GameObject).
        var luaCache = new Dictionary<string, int>();
        string sceneLuaPath = (root as PS1Scene)?.SceneLuaFile ?? "";
        data.SceneLuaFileIndex = ResolveLuaScript(root.Name, sceneLuaPath, data, luaCache);

        // Audio clips authored on the PS1Scene get ADPCM-encoded once at
        // export time. Lua resolves them by name via the in-splashpack name
        // table.
        if (root is PS1Scene sceneWithAudio)
        {
            CollectAudioClips(sceneWithAudio, data);
            // Music sequences depend on AudioClips (channels reference clip
            // names) — collect after audio so the lookup table is populated.
            CollectMusicSequences(sceneWithAudio, data);
        }

        // Dedup cache keyed by (resourcePath, bitDepth). A texture used at two
        // different bit depths is intentionally two atlas entries.
        var textureCache = new Dictionary<(string path, PSXBPP bpp), int>();

        WalkAddMeshes(root, data, textureCache, luaCache);

        // After every region (auto-flat + authored) has been collected,
        // scan them for coincident edges and emit portal pairs so the
        // runtime can walk between them.
        StitchNavPortals(data);

        // Interior room/portal culling: assign every exported triangle to a
        // PS1Room volume (or the catch-all), then capture PS1PortalLink
        // connectivity. Runtime uses this when sceneType=Interior. For
        // exterior scenes with no PS1Room nodes, the lists stay empty and
        // the renderer falls back to BVH culling.
        CollectRooms(root, data);

        // Player spawn: prefer an explicit PS1Player node; fall back to the
        // first Camera3D so scenes authored before PS1Player existed keep
        // working. A Camera3D child of PS1Player supplies the initial
        // camera rig offset (behind/above for 3rd person, at head for
        // 1st person). If no Camera3D child, the runtime's default rig is
        // used.
        var player = FindFirstOfType<PS1Player>(root);
        if (player != null)
        {
            data.PlayerPosition = player.GlobalPosition;
            data.PlayerRotation = player.GlobalRotation;

            // Camera3D child → editor-configurable third-person rig offset.
            // Local position is relative to PS1Player. Runtime rotates it
            // by player yaw each frame. Falls back to the SceneData default
            // (3 m behind, 1 m above) if none authored.
            var rig = FindFirstOfType<Camera3D>(player);
            if (rig != null)
            {
                data.CameraRigOffset = rig.Position;
            }

            // MeshInstance3D descendant → player avatar. Runtime tracks +
            // rotates it each frame (no Lua needed). We store the offset in
            // PS1Player's local space so the runtime can add it to the
            // player's world position to place the mesh.
            //
            // Use the full transform chain (PS1Player.inv * avatar.global)
            // rather than avatar.Position — the latter only works when the
            // avatar is a DIRECT child of PS1Player. For nested FBX imports
            // where the mesh lives inside Humanoid/Skeleton3D/Mesh, the
            // innermost Position is usually (0,0,0); we need the cumulative
            // offset from all intermediate transforms instead.
            var avatar = FindFirstOfType<MeshInstance3D>(player);
            if (avatar != null)
            {
                Transform3D local = player.GlobalTransform.AffineInverse() * avatar.GlobalTransform;
                data.PlayerAvatarOffset = local.Origin;
                for (int i = 0; i < data.Objects.Count; i++)
                {
                    if (ReferenceEquals(data.Objects[i].Node, avatar))
                    {
                        data.PlayerAvatarObjectIndex = i;
                        break;
                    }
                }
            }

            string rigInfo = rig != null
                ? $"rig offset={data.CameraRigOffset:F2}"
                : $"default rig offset={data.CameraRigOffset:F2}";
            string avatarInfo = data.PlayerAvatarObjectIndex >= 0
                ? $"avatar='{avatar!.Name}' idx={data.PlayerAvatarObjectIndex} offset={data.PlayerAvatarOffset:F2}"
                : "no avatar mesh";
            GD.Print($"[PS1Godot] Player spawn from PS1Player '{player.Name}' at {player.GlobalPosition}, mode={player.CameraMode}, {rigInfo}, {avatarInfo}");
        }
        else
        {
            var camera = FindFirstCamera(root);
            if (camera != null)
            {
                data.PlayerPosition = camera.GlobalPosition;
                data.PlayerRotation = camera.GlobalRotation;
                GD.PushWarning("[PS1Godot] No PS1Player in scene; using first Camera3D as spawn. Add a PS1Player node to control this explicitly.");
            }
            else
            {
                GD.PushError("[PS1Godot] No PS1Player AND no Camera3D in scene — player will spawn at world origin (0,0,0). Add a PS1Player node and Build the project so Godot picks it up.");
            }
        }

        return data;
    }

    private static T? FindFirstOfType<T>(Node n) where T : Node
    {
        if (n is T t) return t;
        foreach (var child in n.GetChildren())
        {
            var found = FindFirstOfType<T>(child);
            if (found != null) return found;
        }
        return null;
    }

    // Search siblings + ancestors for the first AnimationPlayer. FBX imports
    // typically put AnimationPlayer as a sibling of MeshInstance3D under a
    // common root; if the user re-parented something we still find it.
    private static AnimationPlayer? FindAnimationPlayerNearby(Node from)
    {
        for (var p = from.GetParent(); p != null; p = p.GetParent())
        {
            foreach (var c in p.GetChildren())
            {
                if (c is AnimationPlayer ap) return ap;
            }
        }
        return null;
    }

    private static string[] CollectClipNamesFrom(AnimationPlayer? ap)
    {
        if (ap == null) return System.Array.Empty<string>();
        var result = new System.Collections.Generic.List<string>();
        foreach (var libName in ap.GetAnimationLibraryList())
        {
            var lib = ap.GetAnimationLibrary(libName);
            if (lib == null) continue;
            foreach (var animName in lib.GetAnimationList())
            {
                string s = animName.ToString();
                string qualified = string.IsNullOrEmpty(libName.ToString()) ? s : $"{libName}/{s}";
                result.Add(qualified);
            }
        }
        return result.ToArray();
    }

    // Walk up to find a PS1Player ancestor. If found, this node is part of
    // the player avatar subtree and gets auto-skinning defaults even without
    // an explicit PS1SkinnedMesh script attached.
    private static PS1Player? FindPS1PlayerAncestor(Node n)
    {
        for (var p = n.GetParent(); p != null; p = p.GetParent())
        {
            if (p is PS1Player pp) return pp;
        }
        return null;
    }

    // Shared skinned-mesh emission for both PS1SkinnedMesh-scripted nodes and
    // raw MeshInstance3D descendants of PS1Player. Computes bone count, bone
    // indices per triangle, bakes animation clips, and records the result in
    // data.SkinnedMeshes so the writer picks it up.
    private static void EmitSkinnedMeshData(MeshInstance3D mesh, string[] clipNames,
        NodePath animationPlayerPath, int targetFps, ushort objectIndex, SceneData data)
    {
        int boneCount = DetectBoneCount(mesh);
        if (boneCount == 0)
        {
            GD.PushWarning(
                $"[PS1Godot] Skinned mesh '{mesh.Name}' has no Skeleton3D target " +
                $"(or an empty one). Falling back to 1 bone at the mesh origin.");
            boneCount = 1;
        }
        if (boneCount > 64)
        {
            GD.PushWarning(
                $"[PS1Godot] Skinned mesh '{mesh.Name}' has {boneCount} bones; " +
                $"runtime cap is 64. Bone indices >= 64 will be clamped.");
        }
        int clampedBoneCount = System.Math.Min(boneCount, 64);
        byte[] boneIndices = ComputeBoneIndices(mesh, clampedBoneCount);

        var clips = BakeSkinClips(mesh, clipNames, animationPlayerPath, targetFps,
            clampedBoneCount, data.GteScaling);

        data.SkinnedMeshes.Add(new SkinnedMeshRecord
        {
            Name = mesh.Name,
            GameObjectIndex = objectIndex,
            BoneCount = (byte)clampedBoneCount,
            BoneIndices = boneIndices,
            Clips = clips,
        });
        GD.Print(
            $"[PS1Godot] Skinned mesh '{mesh.Name}': {boneCount} bones, " +
            $"{boneIndices.Length / 3} triangles, {clips.Count} baked clips " +
            $"({System.Linq.Enumerable.Sum(System.Linq.Enumerable.Select(clips, c => (int)c.FrameCount))} total frames).");
    }

    private static void WalkAddMeshes(Node n, SceneData data,
        Dictionary<(string, PSXBPP), int> textureCache,
        Dictionary<string, int> luaCache)
    {
        // PS1MeshGroup: merge every descendant MeshInstance3D's triangles
        // into a single PSXMesh + GameObject. Walk stops at the group —
        // descendants are consumed here, not visited as siblings.
        if (n is PS1MeshGroup group && group.Visible)
        {
            EmitMeshGroup(group, data, textureCache, luaCache);
            return;
        }
        if (n is PS1MeshInstance pmi && pmi.Visible && pmi.Mesh != null)
        {
            int surfaceCount = pmi.Mesh.GetSurfaceCount();
            var surfaceTextureIndices = new int[surfaceCount];
            for (int s = 0; s < surfaceCount; s++)
            {
                surfaceTextureIndices[s] = ResolveSurfaceTexture(pmi, s, data, textureCache);
            }

            Color effectiveFlat = ResolveEffectiveFlatColor(pmi);
            var psxMesh = PSXMesh.FromGodotMesh(
                pmi, data.GteScaling, pmi.VertexColorMode, effectiveFlat,
                surfaceTextureIndices, data.Textures);

            ushort objectIndex = (ushort)data.Objects.Count;
            data.Objects.Add(new SceneObject
            {
                Node = pmi,
                Mesh = psxMesh,
                LocalAabb = pmi.Mesh.GetAabb(),
                SurfaceTextureIndices = surfaceTextureIndices,
                LuaFileIndex = ResolveLuaScript(pmi.Name, pmi.ScriptFile, data, luaCache),
            });

            EmitCollisionFor(pmi, objectIndex, data);
            EmitInteractableFor(pmi, objectIndex, data);

            // Stage 1 skinned-mesh export: if this is a PS1SkinnedMesh,
            // resolve its skeleton, compute per-triangle bone indices from
            // vertex weights, and record it for SplashpackWriter to emit as
            // a SkinTable entry + SkinData block.
            if (pmi is PS1SkinnedMesh ps1Skin)
            {
                EmitSkinnedMeshData(ps1Skin, ps1Skin.ClipNames ?? System.Array.Empty<string>(),
                    ps1Skin.AnimationPlayerPath, ps1Skin.TargetFps, objectIndex, data);
            }
        }
        // Auto-detect raw MeshInstance3D descendants of a PS1Player with a
        // bound Skin + Skeleton. This is what you get when an FBX character
        // is instanced under PS1Player without the user manually attaching
        // PS1SkinnedMesh script to the inner mesh node — most users won't
        // know they need to. Export it with sensible defaults (all clips
        // from the nearest AnimationPlayer, baked at 15 fps) and rename the
        // exported object to "Player" so Lua's SkinnedAnim.Play keys by a
        // stable name regardless of the FBX's internal naming.
        else if (n is MeshInstance3D rawMi && rawMi is not PS1MeshInstance
                 && rawMi.Visible && rawMi.Mesh != null
                 && rawMi.Skin != null && !rawMi.Skeleton.IsEmpty
                 && FindPS1PlayerAncestor(rawMi) != null)
        {
            int surfaceCount = rawMi.Mesh.GetSurfaceCount();
            var surfaceTextureIndices = new int[surfaceCount];
            for (int s = 0; s < surfaceCount; s++)
            {
                surfaceTextureIndices[s] = ResolveSurfaceTextureRaw(rawMi, s, data, textureCache);
            }

            // Rename the node to "Player" so its exported name is stable for
            // Lua SkinnedAnim.Play("Player", ...) calls, regardless of what
            // Mixamo / FBX importer called the inner mesh.
            if (rawMi.Name != "Player") rawMi.Name = "Player";

            var psxMesh = PSXMesh.FromGodotMesh(
                rawMi, data.GteScaling, PS1MeshInstance.ColorMode.FlatColor,
                new Color(1, 1, 1, 1), surfaceTextureIndices, data.Textures);

            ushort objectIndex = (ushort)data.Objects.Count;
            data.Objects.Add(new SceneObject
            {
                Node = rawMi,
                Mesh = psxMesh,
                LocalAabb = rawMi.Mesh.GetAabb(),
                SurfaceTextureIndices = surfaceTextureIndices,
                LuaFileIndex = -1,
            });

            var ap = FindAnimationPlayerNearby(rawMi);
            var clipNames = CollectClipNamesFrom(ap);
            var apPath = ap != null ? rawMi.GetPathTo(ap) : new NodePath();
            EmitSkinnedMeshData(rawMi, clipNames, apPath, 15, objectIndex, data);
        }
        // Auto-detect raw non-skinned MeshInstance3D anywhere in the scene.
        // This is what you get when the author instances an FBX scene (or
        // drops a primitive MeshInstance3D) directly under PS1Scene without
        // wrapping it in a PS1MeshInstance script. Export with sensible
        // defaults (8bpp textures via ResolveSurfaceTextureRaw, no collision,
        // FlatColor) so the mesh appears on PSX without requiring the user
        // to know about the script attachment step. Users who need specific
        // bit depth or collision opt into PS1MeshInstance explicitly.
        else if (n is MeshInstance3D autoMi && autoMi is not PS1MeshInstance
                 && autoMi.Visible && autoMi.Mesh != null
                 && (autoMi.Skin == null || autoMi.Skeleton.IsEmpty)
                 && FindPS1PlayerAncestor(autoMi) == null)
        {
            int surfaceCount = autoMi.Mesh.GetSurfaceCount();
            var surfaceTextureIndices = new int[surfaceCount];
            for (int s = 0; s < surfaceCount; s++)
            {
                surfaceTextureIndices[s] = ResolveSurfaceTextureRaw(autoMi, s, data, textureCache);
            }

            var psxMesh = PSXMesh.FromGodotMesh(
                autoMi, data.GteScaling, PS1MeshInstance.ColorMode.FlatColor,
                new Color(1, 1, 1, 1), surfaceTextureIndices, data.Textures);

            data.Objects.Add(new SceneObject
            {
                Node = autoMi,
                Mesh = psxMesh,
                LocalAabb = autoMi.Mesh.GetAabb(),
                SurfaceTextureIndices = surfaceTextureIndices,
                LuaFileIndex = -1,
            });
            GD.Print($"[PS1Godot]   auto-exported raw mesh '{autoMi.Name}' ({psxMesh.Triangles.Count} tris, {surfaceCount} surf)");
        }
        else if (n is PS1TriggerBox tb && tb.Visible)
        {
            EmitTriggerBox(tb, data, luaCache);
        }
        else if (n is PS1NavRegion nav)
        {
            EmitAuthoredNavRegion(nav, data);
        }
        else if (n is PS1UICanvas canvas)
        {
            EmitUICanvas(canvas, data);
            // Canvas children are consumed here, not re-walked as siblings.
            return;
        }
        else if (n is PS1Animation anim)
        {
            EmitAnimation(anim, data);
            return;
        }
        else if (n is PS1Cutscene cs)
        {
            EmitCutscene(cs, data);
            return;
        }
        foreach (var child in n.GetChildren())
        {
            WalkAddMeshes(child, data, textureCache, luaCache);
        }
    }

    // Walk every MeshInstance3D descendant of `group`, bake each one's
    // local-to-group transform into its triangle verts, concatenate into
    // one PSXMesh, and emit as a single GameObject. Preserves per-sub-mesh
    // texture indices so (e.g.) body + eye atlases still split correctly.
    //
    // Skinned sub-meshes are refused — rigged characters belong to
    // PS1SkinnedMesh / PS1Player's auto-skinning pipeline, which expects
    // one object per skeleton. A group with a skinned child gets a warning
    // and falls back to skipping that child.
    private static void EmitMeshGroup(PS1MeshGroup group, SceneData data,
        Dictionary<(string, PSXBPP), int> textureCache,
        Dictionary<string, int> luaCache)
    {
        var subMeshes = new List<MeshInstance3D>();
        CollectMergableMeshes(group, subMeshes);

        if (subMeshes.Count == 0)
        {
            GD.PushWarning(
                $"[PS1Godot] PS1MeshGroup '{group.Name}' has no MeshInstance3D descendants — skipping.");
            return;
        }

        // Author-facing name: explicit ObjectName wins, else node name.
        string displayName = string.IsNullOrWhiteSpace(group.ObjectName)
            ? group.Name.ToString()
            : group.ObjectName;
        if (group.Name.ToString() != displayName)
        {
            group.Name = displayName;
        }

        byte rByte = PSXTrig.ColorChannelToPSX(group.FlatColor.R);
        byte gByte = PSXTrig.ColorChannelToPSX(group.FlatColor.G);
        byte bByte = PSXTrig.ColorChannelToPSX(group.FlatColor.B);

        var merged = new PSXMesh();
        var surfaceTextureIndices = new List<int>();

        // Bake the group's SCALE into the merged verts (PSX GameObjects
        // only carry position + rotation at runtime — no scale). We do
        // that by computing each sub's local transform relative to a
        // rotation-only copy of the group: the group's scale then lives
        // inside subToGroup.basis and flows through AppendFromGodotSurface
        // into vertex positions. SplashpackWriter emits the group's
        // GlobalBasis.GetRotationQuaternion() (rotation only, no scale)
        // as the runtime rotation, which pairs correctly.
        var groupRotOnly = new Transform3D(
            group.GlobalTransform.Basis.Orthonormalized(),
            group.GlobalTransform.Origin);
        Transform3D groupInv = groupRotOnly.AffineInverse();

        // Accumulate the group-local AABB from every transformed vertex.
        // Used by WriteWorldAabb at write time — no runtime equivalent of
        // Mesh.GetAabb() exists for a multi-source aggregate.
        Vector3 aabbMin = new(float.MaxValue, float.MaxValue, float.MaxValue);
        Vector3 aabbMax = new(float.MinValue, float.MinValue, float.MinValue);

        foreach (var sub in subMeshes)
        {
            if (sub.Mesh == null) continue;
            Transform3D subToGroup = groupInv * sub.GlobalTransform;
            int surfaceCount = sub.Mesh.GetSurfaceCount();
            for (int s = 0; s < surfaceCount; s++)
            {
                int texIdx = ResolveSurfaceTextureCore(sub, group.BitDepth, s, data, textureCache);
                surfaceTextureIndices.Add(texIdx);
                PSXTexture? tex = (texIdx >= 0 && texIdx < data.Textures.Count)
                    ? data.Textures[texIdx]
                    : null;
                merged.AppendFromGodotSurface(sub, s, subToGroup, texIdx, tex,
                    data.GteScaling, rByte, gByte, bByte);
            }

            // Extend the AABB with this sub-mesh's footprint in group space.
            Aabb subAabb = sub.Mesh.GetAabb();
            for (int c = 0; c < 8; c++)
            {
                var corner = new Vector3(
                    (c & 1) != 0 ? subAabb.Position.X + subAabb.Size.X : subAabb.Position.X,
                    (c & 2) != 0 ? subAabb.Position.Y + subAabb.Size.Y : subAabb.Position.Y,
                    (c & 4) != 0 ? subAabb.Position.Z + subAabb.Size.Z : subAabb.Position.Z);
                Vector3 p = subToGroup * corner;
                aabbMin = new Vector3(Mathf.Min(aabbMin.X, p.X), Mathf.Min(aabbMin.Y, p.Y), Mathf.Min(aabbMin.Z, p.Z));
                aabbMax = new Vector3(Mathf.Max(aabbMax.X, p.X), Mathf.Max(aabbMax.Y, p.Y), Mathf.Max(aabbMax.Z, p.Z));
            }
        }

        data.Objects.Add(new SceneObject
        {
            Node = group,
            Mesh = merged,
            LocalAabb = new Aabb(aabbMin, aabbMax - aabbMin),
            SurfaceTextureIndices = surfaceTextureIndices.ToArray(),
            LuaFileIndex = ResolveLuaScript(displayName, group.ScriptFile, data, luaCache),
        });

        GD.Print(
            $"[PS1Godot] PS1MeshGroup '{displayName}': merged {subMeshes.Count} mesh(es) → " +
            $"{merged.Triangles.Count} tris across {surfaceTextureIndices.Count} surfaces.");
    }

    // Gather every MeshInstance3D descendant that qualifies for merging.
    // Skips: skinned meshes (belong to the PS1SkinnedMesh pipeline), hidden
    // nodes, and nodes without a Mesh resource. Nested PS1MeshGroup trees
    // aren't recursed into — inner groups stay self-contained.
    private static void CollectMergableMeshes(Node n, List<MeshInstance3D> outList)
    {
        foreach (var child in n.GetChildren())
        {
            if (child is PS1MeshGroup) continue;  // inner group, own scope
            if (child is MeshInstance3D mi && mi.Visible && mi.Mesh != null)
            {
                if (mi.Skin != null && !mi.Skeleton.IsEmpty)
                {
                    GD.PushWarning(
                        $"[PS1Godot] PS1MeshGroup: skinned mesh '{mi.Name}' skipped — " +
                        $"put skinned characters under PS1Player, not inside a mesh group.");
                    continue;
                }
                outList.Add(mi);
            }
            CollectMergableMeshes(child, outList);
        }
    }

    // Collect a canvas and its immediate PS1UIElement children. Nested
    // canvases / non-PS1UIElement children are ignored with a warning so
    // the scene tree stays honest.
    private static void EmitUICanvas(PS1UICanvas canvas, SceneData data)
    {
        string name = string.IsNullOrWhiteSpace(canvas.CanvasName)
            ? canvas.Name
            : canvas.CanvasName;

        var theme = canvas.Theme;
        var elements = new System.Collections.Generic.List<UIElementRecord>();
        foreach (var child in canvas.GetChildren())
        {
            if (child is not PS1UIElement el)
            {
                GD.PushWarning($"[PS1Godot] Canvas '{name}' has non-UI child '{child.Name}' — ignored.");
                continue;
            }
            string elName = string.IsNullOrWhiteSpace(el.ElementName) ? el.Name : el.ElementName;
            Color color = ResolveElementColor(el, theme);
            // Resolve anchor + inset/offset to an absolute top-left in
            // PSX coords. The runtime's UIElement still reads plain X/Y
            // (anchor bytes in the binary stay zero, see SplashpackWriter
            // WriteUISection); doing the math at export keeps the binary
            // layout unchanged.
            var (absX, absY) = PS1UIAnchoring.Resolve(el);
            // Resolve the font reference to a runtime index (0 = system
            // font, 1+ = one-based slot in data.UIFonts). Non-Text
            // elements get 0 regardless.
            byte fontIndex = 0;
            if (el.Type == PS1UIElementType.Text && el.Font != null)
            {
                fontIndex = ResolveFontIndex(el.Font, data, elementName: elName);
            }
            elements.Add(new UIElementRecord
            {
                Name = elName,
                Type = el.Type,
                VisibleOnLoad = el.VisibleOnLoad,
                X = (short)Mathf.Clamp(absX, short.MinValue, short.MaxValue),
                Y = (short)Mathf.Clamp(absY, short.MinValue, short.MaxValue),
                W = (short)Mathf.Clamp(el.Width, short.MinValue, short.MaxValue),
                H = (short)Mathf.Clamp(el.Height, short.MinValue, short.MaxValue),
                ColorR = (byte)Mathf.Clamp((int)(color.R * 255f), 0, 255),
                ColorG = (byte)Mathf.Clamp((int)(color.G * 255f), 0, 255),
                ColorB = (byte)Mathf.Clamp((int)(color.B * 255f), 0, 255),
                FontIndex = fontIndex,
                HAlign = (byte)el.TextAlign,
                VAlign = (byte)el.TextVAlign,
                Text = el.Text ?? "",
            });
        }

        data.UICanvases.Add(new UICanvasRecord
        {
            Name = name,
            Residency = canvas.Residency,
            VisibleOnLoad = canvas.VisibleOnLoad,
            SortOrder = (byte)Mathf.Clamp(canvas.SortOrder, 0, 255),
            Elements = elements,
        });
        GD.Print($"[PS1Godot] UICanvas '{name}': residency={canvas.Residency}, {elements.Count} elements");
    }

    // VRAM slot assignments for custom fonts. Slot 0 is the runtime's
    // built-in system font at (960, 464); we get two slots above it.
    // Matches splashedit-main/Runtime/PSXUIExporter.cs.
    private static readonly (ushort X, ushort Y, ushort MaxH)[] FontVramSlots =
    {
        (960,   0, 256),
        (960, 256, 208),
    };

    // Dedupe a PS1UIFontAsset against what's already in data.UIFonts.
    // Adds a new UIFontRecord on first sight (with VRAM slot + packed
    // pixel data) and returns the runtime-side font index (1-based,
    // since 0 is the system font). Max 2 custom fonts — a third logs
    // an error and falls back to system font (index 0).
    private static byte ResolveFontIndex(PS1UIFontAsset asset, SceneData data, string elementName)
    {
        if (!asset.IsGenerated || asset.Bitmap == null)
        {
            GD.PushError($"[PS1Godot] Element '{elementName}': font '{asset.ResourcePath}' " +
                         "has no generated bitmap. Run Tools → 'Generate bitmap for selected " +
                         "PS1UIFontAsset' first. Falling back to system font.");
            return 0;
        }

        // Already collected? Reuse the same slot.
        for (int i = 0; i < data.UIFonts.Count; i++)
        {
            if (data.UIFonts[i].Name == (string.IsNullOrWhiteSpace(asset.FontName) ? asset.ResourcePath : asset.FontName))
                return (byte)(i + 1);
        }

        if (data.UIFonts.Count >= FontVramSlots.Length)
        {
            GD.PushError($"[PS1Godot] Element '{elementName}': font '{asset.FontName}' is the " +
                         $"{data.UIFonts.Count + 1}rd custom font, but the runtime only has 2 slots. " +
                         "Consolidate fonts or drop the extra. Falling back to system font.");
            return 0;
        }

        var slot = FontVramSlots[data.UIFonts.Count];
        if (asset.Bitmap.GetHeight() > slot.MaxH)
        {
            GD.PushWarning($"[PS1Godot] Font '{asset.FontName}' atlas is {asset.Bitmap.GetHeight()} px tall, " +
                           $"slot {data.UIFonts.Count + 1} max is {slot.MaxH} px. Runtime will clip.");
        }

        byte[] advances = asset.AdvanceWidths ?? new byte[96];
        if (advances.Length != 96)
        {
            GD.PushError($"[PS1Godot] Font '{asset.FontName}': advance widths length {advances.Length} != 96.");
            return 0;
        }

        byte[] pixels;
        try
        {
            pixels = PS1FontPacker.Pack4bpp(asset.Bitmap);
        }
        catch (System.Exception e)
        {
            GD.PushError($"[PS1Godot] Font '{asset.FontName}': 4bpp pack failed — {e.Message}");
            return 0;
        }

        data.UIFonts.Add(new UIFontRecord
        {
            Name = string.IsNullOrWhiteSpace(asset.FontName) ? asset.ResourcePath : asset.FontName,
            GlyphW = (byte)asset.GlyphWidth,
            GlyphH = (byte)asset.GlyphHeight,
            VramX = slot.X,
            VramY = slot.Y,
            TextureH = (ushort)asset.Bitmap.GetHeight(),
            AdvanceWidths = advances,
            PixelData4bpp = pixels,
        });
        GD.Print($"[PS1Godot] Font slot {data.UIFonts.Count}: '{asset.FontName}' " +
                 $"{asset.GlyphWidth}×{asset.GlyphHeight} cells → VRAM ({slot.X},{slot.Y}), " +
                 $"{pixels.Length} B pixel data");
        return (byte)data.UIFonts.Count;
    }

    // Apply the canvas's theme to an element's color at export time.
    // ThemeSlot.Custom or no theme → the authored Color passes through;
    // any other slot pulls from the matching PS1Theme field. Resolution
    // is static: the splashpack's UI element bytes look identical to
    // what they'd be if the author typed the theme's colors by hand,
    // so no runtime format change.
    private static Color ResolveElementColor(PS1UIElement el, PS1Theme? theme)
    {
        if (theme == null || el.ThemeSlot == PS1UIThemeSlot.Custom) return el.Color;
        return el.ThemeSlot switch
        {
            PS1UIThemeSlot.Text      => theme.TextColor,
            PS1UIThemeSlot.Accent    => theme.AccentColor,
            PS1UIThemeSlot.Bg        => theme.BgColor,
            PS1UIThemeSlot.BgBorder  => theme.BgBorderColor,
            PS1UIThemeSlot.Highlight => theme.HighlightColor,
            PS1UIThemeSlot.Warning   => theme.WarningColor,
            PS1UIThemeSlot.Danger    => theme.DangerColor,
            PS1UIThemeSlot.Neutral   => theme.NeutralColor,
            _                        => el.Color,
        };
    }

    // Pull the albedo Texture2D out of either a StandardMaterial3D or a
    // ShaderMaterial carrying an `albedo_tex` shader parameter (our
    // ps1_default.tres uses that parameter name — see ps1.gdshader).
    // Returns null if the material type isn't recognized or has no
    // texture set.
    // The count we want is the number of distinct bones the MESH REFERENCES,
    // which is skin.GetBindCount() — the size of the index space for
    // SurfaceGetArrays(ArrayType.Bones). Returns the skeleton bone count as a
    // fallback for meshes without a Skin (test/placeholder assets) or 0 if
    // neither is configured.
    private static int DetectBoneCount(MeshInstance3D mesh)
    {
        if (mesh.Skin != null)
        {
            int binds = mesh.Skin.GetBindCount();
            if (binds > 0) return binds;
        }
        if (mesh.Skeleton != null && !mesh.Skeleton.IsEmpty)
        {
            var node = mesh.GetNodeOrNull<Skeleton3D>(mesh.Skeleton);
            return node?.GetBoneCount() ?? 0;
        }
        return 0;
    }

    // One bone index byte per triangle vertex (3 bytes per tri). Order
    // matches the post-winding-swap vertex order PSXMesh.FromGodotMesh
    // emits: for each indexed triangle (i0, i1, i2), the swap flips to
    // (i0, i2, i1). Stage 1 picks the dominant bone across all three
    // vertices (per-triangle rigid skinning) — simpler than per-vertex
    // and matches what most PS1 title skinning rigs actually did. Falls
    // back to bone 0 when the mesh has no weight data.
    private static byte[] ComputeBoneIndices(MeshInstance3D mesh, int boneCount)
    {
        var result = new System.Collections.Generic.List<byte>();
        if (mesh.Mesh == null) return result.ToArray();

        // Count unweighted triangles so we can flag them. Observed in the
        // psxsplash Discord (Kenji 195, 2026-04-20): unrigged vertices
        // silently default to bone 0, so a half-rigged crowd appears to
        // "jump in sync with one bone" because every unweighted vertex
        // moves together. Warning here lets authors catch the problem at
        // export time instead of debugging it on-PSX.
        int unweightedTris = 0;

        int surfaceCount = mesh.Mesh.GetSurfaceCount();
        for (int s = 0; s < surfaceCount; s++)
        {
            var arrays = mesh.Mesh.SurfaceGetArrays(s);
            var verts = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var indices = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
            var bonesArr   = arrays[(int)Mesh.ArrayType.Bones].AsInt32Array();
            var weightsArr = arrays[(int)Mesh.ArrayType.Weights].AsFloat32Array();

            int vertCount = verts.Length;
            int triCount = indices.Length > 0 ? indices.Length / 3 : vertCount / 3;
            bool hasSkin = bonesArr.Length > 0 && weightsArr.Length == bonesArr.Length;
            // Godot stores bones/weights as a flat array; the stride is
            // EITHER 4 per vertex (default) OR 8 per vertex if the mesh was
            // imported with ARRAY_FLAG_USE_8_BONE_WEIGHTS (common for Mixamo
            // rigs and other dense character skins). Hard-coding 4 here
            // reads the wrong offset for every vertex after the first in an
            // 8-weight mesh, which scrambles bone assignments across
            // triangles and produces the classic "exploded mesh" failure
            // mode — NOT a scale or bind-pose issue. Compute the stride
            // from the actual data to stay correct in both cases.
            int perVert = (hasSkin && vertCount > 0) ? (bonesArr.Length / vertCount) : 0;
            if (hasSkin && (perVert != 4 && perVert != 8))
            {
                GD.PushWarning($"[PS1Godot] '{mesh.Name}' surface {s}: unexpected bones-per-vertex stride {perVert} (bones={bonesArr.Length}, verts={vertCount}). Expected 4 or 8; skinning may be wrong.");
            }

            for (int t = 0; t < triCount; t++)
            {
                int i0 = indices.Length > 0 ? indices[t * 3 + 0] : t * 3 + 0;
                int i1 = indices.Length > 0 ? indices[t * 3 + 1] : t * 3 + 1;
                int i2 = indices.Length > 0 ? indices[t * 3 + 2] : t * 3 + 2;
                // PSXMesh swaps (i1, i2) for winding — mirror that here so
                // bone indices line up with the emitted triangle vertices.
                (i1, i2) = (i2, i1);

                // Per-VERTEX rigid skinning: each vertex gets its own
                // dominant bone (the one with the highest weight in its
                // 4-slot skin entry). The PSX runtime transforms each
                // vertex of a triangle by its own bone's matrix, so
                // neighbouring triangles that share a vertex agree on its
                // position (no seam). Per-triangle dominance would tear.
                byte b0 = PickDominantBone(i0, perVert, bonesArr, weightsArr, boneCount, out bool v0Weighted);
                byte b1 = PickDominantBone(i1, perVert, bonesArr, weightsArr, boneCount, out bool v1Weighted);
                byte b2 = PickDominantBone(i2, perVert, bonesArr, weightsArr, boneCount, out bool v2Weighted);

                if (!v0Weighted && !v1Weighted && !v2Weighted) unweightedTris++;

                result.Add(b0); result.Add(b1); result.Add(b2);
            }
        }

        if (unweightedTris > 0)
        {
            GD.PushWarning(
                $"[PS1Godot] '{mesh.Name}': {unweightedTris} triangle(s) have no bone weights " +
                $"and will default to bone 0. All unweighted geometry will move together with " +
                $"whatever animates bone 0 — a classic symptom (Discord, 2026-04-20) is a crowd " +
                $"of figures \"jumping in sync\" because half their bodies are unrigged. Paint " +
                $"weights on every vertex in your DCC tool, or split the mesh so unrigged parts " +
                $"live on a separate static MeshInstance3D.");
        }

        return result.ToArray();
    }

    // Pick the bone with the highest weight for a single vertex. Guards
    // against bones outside the skeleton range and the 64-bone runtime
    // cap, both falling through to bone 0. `anyWeight` is false only
    // when every slot's weight is ≤ 0 (truly unrigged vertex).
    private static byte PickDominantBone(int vi, int perVert, int[] bones, float[] weights,
                                         int boneCount, out bool anyWeight)
    {
        anyWeight = false;
        if (perVert == 0) return 0;
        int best = 0;
        float bestW = -1f;
        int baseIdx = vi * perVert;
        for (int k = 0; k < perVert; k++)
        {
            float w = weights[baseIdx + k];
            if (w <= 0f) continue;
            anyWeight = true;
            if (w > bestW)
            {
                bestW = w;
                best = bones[baseIdx + k];
            }
        }
        if (best >= boneCount) best = 0;
        if (best >= 64) best = 63;
        return (byte)best;
    }

    // Resolve the AnimationPlayer for a skinned mesh. Explicit path wins;
    // otherwise walk up the tree looking for an AnimationPlayer sibling,
    // which is where FBX imports put it. Returns null if none.
    private static AnimationPlayer? ResolveAnimationPlayer(MeshInstance3D mesh, NodePath animationPlayerPath)
    {
        if (animationPlayerPath != null && !animationPlayerPath.IsEmpty)
        {
            return mesh.GetNodeOrNull<AnimationPlayer>(animationPlayerPath);
        }
        return FindAnimationPlayerNearby(mesh);
    }

    // Bake all of the mesh's authored clips into BakedBoneMatrix blobs
    // the runtime can play back frame-by-frame. One call handles the
    // full pipeline: resolve AnimationPlayer + Skeleton3D, iterate clip
    // names, sample each at TargetFps, pack the per-frame bone matrices
    // into a single byte[] per clip. Returns an empty list if the skin
    // setup is incomplete — runtime renders rest-pose only in that case.
    private static System.Collections.Generic.List<SkinClipRecord> BakeSkinClips(
        MeshInstance3D mesh, string[] clipNames, NodePath animationPlayerPath, int targetFps,
        int boneCount, float gteScaling)
    {
        var result = new System.Collections.Generic.List<SkinClipRecord>();
        if (clipNames == null || clipNames.Length == 0) return result;

        var skeleton = (mesh.Skeleton != null && !mesh.Skeleton.IsEmpty)
            ? mesh.GetNodeOrNull<Skeleton3D>(mesh.Skeleton)
            : null;
        if (skeleton == null)
        {
            GD.PushWarning($"[PS1Godot] '{mesh.Name}': no skeleton resolved — skipping clip bake.");
            return result;
        }

        var skin = mesh.Skin;
        if (skin == null)
        {
            GD.PushWarning($"[PS1Godot] '{mesh.Name}': no Skin resource — bake math needs bind poses. Skipping clips.");
            return result;
        }

        var ap = ResolveAnimationPlayer(mesh, animationPlayerPath);
        if (ap == null)
        {
            GD.PushWarning($"[PS1Godot] '{mesh.Name}': no AnimationPlayer found. Skipping clip bake.");
            return result;
        }

        int fps = Mathf.Clamp(targetFps, 1, 30);

        // The mesh's bone indices (from SurfaceGetArrays / ArrayType.Bones) are
        // SKIN-LOCAL — they index into this Skin's bind list, not into the
        // Skeleton3D's bone list. So baked entry `bi` in the output must
        // correspond to skin-local bind index `bi`, combining:
        //   (a) the bind-inverse at slot bi (from the Skin), and
        //   (b) the current pose of whatever skeleton bone bind bi references
        //       (via Skin.GetBindBone(bi)).
        // For small rigs authored with skin bind order == skeleton bone order
        // (e.g. SkinnedTest cylinder), this reduces to the old
        // "skeleton.GetBoneGlobalPose(bi) * skin.GetBindPose(bi)" identity.
        // For Mixamo humanoids (skin binds often in a different order than
        // skeleton traversal), the mapping is essential — without it, every
        // vertex gets transformed by the wrong bone's pose and the mesh
        // explodes into Picasso.
        int skinBindCount = skin.GetBindCount();
        var bindInv = new Transform3D[boneCount];
        var skeletonBoneFor = new int[boneCount];
        int skeletonBoneTotal = skeleton.GetBoneCount();
        for (int bi = 0; bi < boneCount; bi++)
        {
            bindInv[bi] = (bi < skinBindCount) ? skin.GetBindPose(bi) : Transform3D.Identity;
            // Skin→skeleton bone mapping. Godot's FBX importer with
            // `skins/use_named_skins=true` stores bind *names* and returns
            // -1 from GetBindBone. Fall back to name lookup in that case;
            // only resort to identity mapping as a last ditch (which is
            // only correct for hand-authored rigs like the SkinnedTest
            // cylinder where bind indices == skeleton indices).
            int skBone = (bi < skinBindCount) ? skin.GetBindBone(bi) : -1;
            if (skBone < 0 && bi < skinBindCount)
            {
                StringName bindName = skin.GetBindName(bi);
                if (!string.IsNullOrEmpty(bindName.ToString()))
                {
                    skBone = skeleton.FindBone(bindName);
                }
            }
            if (skBone < 0) skBone = bi;
            skeletonBoneFor[bi] = skBone;
        }

        foreach (var rawName in clipNames)
        {
            string clipName = rawName ?? "";
            if (string.IsNullOrWhiteSpace(clipName)) continue;
            if (!ap.HasAnimation(clipName))
            {
                GD.PushWarning($"[PS1Godot] '{mesh.Name}': AnimationPlayer has no '{clipName}' — skipping.");
                continue;
            }

            var anim = ap.GetAnimation(clipName);
            if (anim == null) continue;

            int frameCount = Mathf.Max(1, Mathf.CeilToInt((float)anim.Length * fps));
            // Runtime loads frames as count × bones × 24 bytes contiguously.
            byte[] frameData = new byte[frameCount * boneCount * 24];

            // Stash the current play state so we restore it when done.
            // Guard the position read — GetCurrentAnimationPosition() errors
            // when no clip is assigned (common at export time on freshly-
            // imported FBXs whose AP hasn't been kicked off yet).
            var originalClip = ap.CurrentAnimation;
            double originalPos = string.IsNullOrEmpty(originalClip) ? 0.0 : ap.CurrentAnimationPosition;
            ap.CurrentAnimation = clipName;

            for (int f = 0; f < frameCount; f++)
            {
                double time = (double)f / fps;
                // Seek with update=true forces the AnimationMixer to apply
                // the sampled pose to target tracks immediately (bone poses
                // on the Skeleton3D), which is what we read below.
                ap.Seek(time, update: true);

                for (int bi = 0; bi < boneCount; bi++)
                {
                    int skBone = skeletonBoneFor[bi];
                    Transform3D currentPose = (skBone >= 0 && skBone < skeletonBoneTotal)
                        ? skeleton.GetBoneGlobalPose(skBone)
                        : Transform3D.Identity;
                    Transform3D combined = currentPose * bindInv[bi];
                    // Godot's inverse-bind for Mixamo-style rigs embeds a cm→m
                    // unit-scale factor (~0.01) inside the basis — the bind
                    // math is internally consistent on centimeter-scale mesh
                    // vertices. But we export vertices already scaled to
                    // meters (via Humanoid.Scale=0.01 baked into PSXMesh),
                    // so the same 0.01 applied on the PSX side would shrink
                    // every vertex 100× and collapse the mesh toward each
                    // bone's origin (the classic "exploded fan" symptom seen
                    // in PCSX-Redux with this pipeline). Orthonormalize strips
                    // the scale but preserves the rotation direction; the
                    // origin stays valid because it was composed BEFORE we
                    // touched the basis.
                    combined.Basis = combined.Basis.Orthonormalized();
                    WriteBakedBoneMatrix(frameData, (f * boneCount + bi) * 24, combined, gteScaling);
                }
            }

            // Restore AP state so the editor viewport isn't stuck on the
            // final-frame pose of whatever clip we baked last.
            if (!string.IsNullOrEmpty(originalClip))
            {
                ap.CurrentAnimation = originalClip;
                ap.Seek(originalPos, update: true);
            }
            else
            {
                ap.Stop();
            }

            byte flags = (anim.LoopMode != Animation.LoopModeEnum.None) ? (byte)1 : (byte)0;
            result.Add(new SkinClipRecord
            {
                Name = clipName,
                Flags = flags,
                Fps = (byte)fps,
                FrameCount = (ushort)frameCount,
                FrameData = frameData,
            });
        }
        return result;
    }

    // Write one BakedBoneMatrix into `buf` at `offset` (24 bytes total).
    // Layout: int16[9] row-major rotation (fp12) + int16[3] translation.
    // Using the ORIGINAL Y-only sign pattern — Y+Z reflection caused
    // skinned-mesh rendering artifacts (missing faces) that we couldn't
    // pin down analytically. Keeping the Y-only bone encoding preserves
    // the old skinned-mesh behavior while the rest of the pipeline uses
    // the new Y+Z mesh/camera encoding.
    // TODO: reconcile bone matrix sign pattern with Y+Z reflection.
    private static void WriteBakedBoneMatrix(byte[] buf, int offset, Transform3D t, float gteScaling)
    {
        Basis b = t.Basis;
        float[,] m =
        {
            {  b.Column0.X, -b.Column1.X,  b.Column2.X },
            { -b.Column0.Y,  b.Column1.Y, -b.Column2.Y },
            {  b.Column0.Z, -b.Column1.Z,  b.Column2.Z },
        };
        for (int i = 0; i < 3; i++)
        {
            for (int j = 0; j < 3; j++)
            {
                short fp = PSXTrig.ConvertToFixed12(m[i, j]);
                int off = offset + (i * 3 + j) * 2;
                buf[off + 0] = (byte)(fp & 0xFF);
                buf[off + 1] = (byte)((fp >> 8) & 0xFF);
            }
        }

        // Translation: Y-only negation for now (bone matrix kept in old
        // Y-only convention; see comment above).
        short tx = PSXTrig.ConvertCoordinateToPSX( t.Origin.X, gteScaling);
        short ty = PSXTrig.ConvertCoordinateToPSX(-t.Origin.Y, gteScaling);
        short tz = PSXTrig.ConvertCoordinateToPSX( t.Origin.Z, gteScaling);
        int tOff = offset + 18;
        buf[tOff + 0] = (byte)(tx & 0xFF);
        buf[tOff + 1] = (byte)((tx >> 8) & 0xFF);
        buf[tOff + 2] = (byte)(ty & 0xFF);
        buf[tOff + 3] = (byte)((ty >> 8) & 0xFF);
        buf[tOff + 4] = (byte)(tz & 0xFF);
        buf[tOff + 5] = (byte)((tz >> 8) & 0xFF);
    }

    private static Texture2D? ExtractAlbedoTexture(Material? mat)
    {
        if (mat == null) return null;
        if (mat is StandardMaterial3D std) return std.AlbedoTexture;
        if (mat is ShaderMaterial sm)
        {
            var val = sm.GetShaderParameter("albedo_tex");
            if (val.VariantType == Variant.Type.Object)
            {
                return val.As<Texture2D>();
            }
        }
        return null;
    }

    // Resolve a mesh's effective flat color for untextured export.
    // Precedence:
    //   1. ShaderMaterial "tint_color" parameter (our ps1_default / ps1_green
    //      materials expose this — most meshes in practice).
    //   2. StandardMaterial3D AlbedoColor.
    //   3. PS1MeshInstance.FlatColor node property (fallback / override).
    // If the author wants to override a material-provided color, they can
    // either swap to a StandardMaterial3D with AlbedoTexture (textured
    // path) or remove the material and set FlatColor on the node.
    private static Color ResolveEffectiveFlatColor(PS1MeshInstance pmi)
    {
        Material? mat = pmi.MaterialOverride;
        if (mat == null && pmi.Mesh != null)
        {
            for (int s = 0; s < pmi.Mesh.GetSurfaceCount(); s++)
            {
                mat = pmi.GetSurfaceOverrideMaterial(s) ?? pmi.Mesh.SurfaceGetMaterial(s);
                if (mat != null) break;
            }
        }

        if (mat is ShaderMaterial sm)
        {
            var tint = sm.GetShaderParameter("tint_color");
            if (tint.VariantType == Variant.Type.Color) return tint.AsColor();
        }
        if (mat is StandardMaterial3D std)
        {
            return std.AlbedoColor;
        }
        return pmi.FlatColor;
    }

    // Walk a PS1Animation node's children for keyframes, convert to PSX
    // fp12 (same scaling + Y-flip as static geometry), and append an
    // AnimationRecord. Target resolution (name → GameObject) happens at
    // runtime via the object name table, so the exporter just stamps the
    // target name as-is.
    private static void EmitAnimation(PS1Animation anim, SceneData data)
    {
        string name = string.IsNullOrWhiteSpace(anim.AnimationName)
            ? anim.Name
            : anim.AnimationName;
        string target = anim.TargetObjectName ?? "";
        if (string.IsNullOrEmpty(target))
        {
            GD.PushWarning($"[PS1Godot] Animation '{name}' has no TargetObjectName — runtime won't resolve a GameObject. Skipping.");
            return;
        }

        var kfs = new System.Collections.Generic.List<KeyframeRecord>();
        foreach (var child in anim.GetChildren())
        {
            if (child is not PS1AnimationKeyframe kf) continue;
            (short v0, short v1, short v2) = EncodeKeyframeValue(kf.Value, anim.TrackType, data.GteScaling);
            kfs.Add(new KeyframeRecord
            {
                Frame = (ushort)Mathf.Clamp(kf.Frame, 0, 8191),
                Interp = kf.Interp,
                V0 = v0, V1 = v1, V2 = v2,
            });
        }

        if (kfs.Count == 0)
        {
            GD.PushWarning($"[PS1Godot] Animation '{name}' has no PS1AnimationKeyframe children — skipping.");
            return;
        }

        // Keyframes must be monotonically increasing in frame number for
        // the runtime's linear walk. Sort defensively so authors can
        // rearrange the scene tree without worrying about order.
        kfs.Sort((a, b) => a.Frame.CompareTo(b.Frame));

        data.Animations.Add(new AnimationRecord
        {
            Name = name,
            TargetObjectName = target,
            TrackType = anim.TrackType,
            TotalFrames = (ushort)Mathf.Clamp(anim.TotalFrames, 1, 8191),
            Keyframes = kfs,
        });
        GD.Print($"[PS1Godot] Animation '{name}': track={anim.TrackType} target='{target}' frames={anim.TotalFrames} keyframes={kfs.Count}");
    }

    // Walk a PS1Cutscene's PS1AnimationTrack children, encode each track's
    // keyframes per its track type, and append a CutsceneRecord. Skips
    // tracks with no keyframes silently — they'd be no-ops at runtime.
    private static void EmitCutscene(PS1Cutscene cs, SceneData data)
    {
        string name = string.IsNullOrWhiteSpace(cs.CutsceneName) ? cs.Name : cs.CutsceneName;

        var tracks = new System.Collections.Generic.List<CutsceneTrackRecord>();
        foreach (var child in cs.GetChildren())
        {
            if (child is not PS1AnimationTrack tr) continue;

            var kfs = new System.Collections.Generic.List<KeyframeRecord>();
            foreach (var kfChild in tr.GetChildren())
            {
                if (kfChild is not PS1AnimationKeyframe kf) continue;
                (short v0, short v1, short v2) = EncodeKeyframeValue(kf.Value, tr.TrackType, data.GteScaling);
                kfs.Add(new KeyframeRecord
                {
                    Frame = (ushort)Mathf.Clamp(kf.Frame, 0, 8191),
                    Interp = kf.Interp,
                    V0 = v0, V1 = v1, V2 = v2,
                });
            }
            if (kfs.Count == 0) continue;
            kfs.Sort((a, b) => a.Frame.CompareTo(b.Frame));

            tracks.Add(new CutsceneTrackRecord
            {
                TargetObjectName = tr.TargetObjectName ?? "",
                TrackType = tr.TrackType,
                Keyframes = kfs,
            });
        }

        // Audio events — children of the cutscene typed PS1AudioEvent.
        // Resolve ClipName → audio clip index using the already-collected
        // SceneData.AudioClips list (the audio walk happens before the
        // cutscene walk because it's on PS1Scene).
        var audioEvents = new System.Collections.Generic.List<CutsceneAudioEventRecord>();
        foreach (var child in cs.GetChildren())
        {
            if (child is not PS1AudioEvent ae) continue;
            int clipIdx = -1;
            if (!string.IsNullOrEmpty(ae.ClipName))
            {
                for (int i = 0; i < data.AudioClips.Count; i++)
                {
                    if (data.AudioClips[i].Name == ae.ClipName) { clipIdx = i; break; }
                }
            }
            if (clipIdx < 0)
            {
                GD.PushWarning($"[PS1Godot] Cutscene '{name}' audio event at frame {ae.Frame}: clip '{ae.ClipName}' not found in PS1Scene.AudioClips — skipping.");
                continue;
            }
            audioEvents.Add(new CutsceneAudioEventRecord
            {
                Frame = (ushort)Mathf.Clamp(ae.Frame, 0, 8191),
                ClipIndex = (byte)Mathf.Clamp(clipIdx, 0, 255),
                Volume = (byte)Mathf.Clamp(ae.Volume, 0, 127),
                Pan = (byte)Mathf.Clamp(ae.Pan, 0, 127),
            });
        }
        // Runtime walks audio events linearly by frame; sort defensively.
        audioEvents.Sort((a, b) => a.Frame.CompareTo(b.Frame));

        if (tracks.Count == 0)
        {
            GD.PushWarning($"[PS1Godot] Cutscene '{name}' has no PS1AnimationTrack children with keyframes — skipping.");
            return;
        }

        data.Cutscenes.Add(new CutsceneRecord
        {
            Name = name,
            TotalFrames = (ushort)Mathf.Clamp(cs.TotalFrames, 1, 8191),
            Tracks = tracks,
            AudioEvents = audioEvents,
        });
        GD.Print($"[PS1Godot] Cutscene '{name}': frames={cs.TotalFrames} tracks={tracks.Count} audioEvents={audioEvents.Count}");
    }

    // Encode a Godot-space triple into the runtime's fp12 / fp10 values
    // per the given track type.
    //   Position / CameraPosition: (x, y, z) meters → PSX fp12 with Y and Z
    //                              flipped (same Y+Z reflection the mesh
    //                              vertex writer applies).
    //   Rotation / CameraRotation: Euler degrees per axis → psyqo::Angle
    //             fp10 (1024 = 180°). Under Y+Z world reflection with
    //             S=diag(1,-1,-1), the Euler decomposition transforms as
    //             X unchanged (R_X commutes with S here), Y flips sign
    //             (S·R_Y·S = R_Y(-y)), Z flips sign (S·R_Z·S = R_Z(-z)).
    //   Active:   value.X → 0 or 1.
    private static (short, short, short) EncodeKeyframeValue(Vector3 v, PS1AnimationTrackType type, float gteScaling)
    {
        switch (type)
        {
            case PS1AnimationTrackType.Position:
            case PS1AnimationTrackType.CameraPosition:
            {
                float s = Mathf.Max(gteScaling, 0.0001f);
                int vx = Mathf.RoundToInt( v.X / s * 4096f);
                int vy = Mathf.RoundToInt(-v.Y / s * 4096f);
                int vz = Mathf.RoundToInt(-v.Z / s * 4096f);
                return (ClampShort(vx), ClampShort(vy), ClampShort(vz));
            }
            case PS1AnimationTrackType.Rotation:
            case PS1AnimationTrackType.CameraRotation:
            {
                // psyqo::Angle = FixedPoint<10>, measured in fractions of
                // Pi (trigonometry.hh:45-48): 1.0 pi-unit = 180° = 1024
                // raw fp10. So 180° → 1024, 360° → 2048, 45° → 256.
                // Rotation encoding kept pre-Y+Z-flip for backward-compat
                // with scenes authored against the old convention; purely-
                // mathematical Y+Z-reflection encoding can't be expressed
                // in pure Euler + psyqo's SetRotation (needs a reflection
                // combined with rotation). Authors should re-balance 180°
                // yaw compensations in cutscenes that were added as a
                // workaround for the old runtime yaw-init hack.
                const float DegToFp10 = 1024f / 180f;
                int rx = Mathf.RoundToInt(-v.X * DegToFp10);
                int ry = Mathf.RoundToInt(-v.Y * DegToFp10);
                int rz = Mathf.RoundToInt( v.Z * DegToFp10);
                return (ClampShort(rx), ClampShort(ry), ClampShort(rz));
            }
            case PS1AnimationTrackType.Active:
                return ((short)(v.X != 0f ? 1 : 0), 0, 0);
        }
        return (0, 0, 0);
    }

    private static short ClampShort(int v) => (short)Mathf.Clamp(v, short.MinValue, short.MaxValue);

    // Compute world AABB for a PS1TriggerBox by baking its GlobalTransform
    // into the 8 corners of the local half-extent box, same approach the
    // collider path uses (runtime never calls updateCollider). Resolve the
    // attached Lua script to an index, or -1 if none.
    private static void EmitTriggerBox(PS1TriggerBox tb, SceneData data,
        Dictionary<string, int> luaCache)
    {
        Vector3 he = tb.HalfExtents;
        Vector3 lMin = -he;
        Vector3 lMax = he;
        Transform3D xform = tb.GlobalTransform;
        Vector3 wMin = new(float.MaxValue, float.MaxValue, float.MaxValue);
        Vector3 wMax = new(float.MinValue, float.MinValue, float.MinValue);
        for (int i = 0; i < 8; i++)
        {
            var corner = new Vector3(
                (i & 1) != 0 ? lMax.X : lMin.X,
                (i & 2) != 0 ? lMax.Y : lMin.Y,
                (i & 4) != 0 ? lMax.Z : lMin.Z);
            Vector3 world = xform * corner;
            wMin = new Vector3(Mathf.Min(wMin.X, world.X), Mathf.Min(wMin.Y, world.Y), Mathf.Min(wMin.Z, world.Z));
            wMax = new Vector3(Mathf.Max(wMax.X, world.X), Mathf.Max(wMax.Y, world.Y), Mathf.Max(wMax.Z, world.Z));
        }

        int luaIdx = ResolveLuaScript(tb.Name, tb.ScriptFile, data, luaCache);
        if (luaIdx < 0)
        {
            // Trigger boxes without a script do nothing at runtime —
            // nearly always an authoring oversight rather than intentional.
            GD.PushWarning($"[PS1Godot] Trigger '{tb.Name}' has no ScriptFile — onTriggerEnter/Exit will never fire. Set ScriptFile in the inspector.");
        }
        data.TriggerBoxes.Add(new TriggerBoxRecord
        {
            WorldMin = wMin,
            WorldMax = wMax,
            LuaFileIndex = (short)luaIdx,
        });
        GD.Print($"[PS1Godot] Trigger '{tb.Name}': AABB=[{wMin}..{wMax}] luaIdx={luaIdx}");
    }

    // Emit an Interactable record for a PS1MeshInstance marked interactable.
    // The runtime pairs this with the GameObject by objectIndex — the
    // object's attached script gets its onInteract called when the player
    // is in range and presses the configured button.
    private static void EmitInteractableFor(PS1MeshInstance pmi, ushort objectIndex, SceneData data)
    {
        if (!pmi.Interactable) return;

        string prompt = pmi.InteractionPromptCanvas ?? "";
        if (prompt.Length > 15)
        {
            GD.PushWarning($"[PS1Godot] {pmi.Name}: InteractionPromptCanvas '{prompt}' > 15 chars — truncating.");
            prompt = prompt[..15];
        }

        data.Interactables.Add(new InteractableRecord
        {
            GameObjectIndex = objectIndex,
            RadiusMeters = pmi.InteractionRadiusMeters,
            InteractButton = (byte)Mathf.Clamp(pmi.InteractButton, 0, 15),
            Repeatable = pmi.InteractionRepeatable,
            ShowPrompt = !string.IsNullOrEmpty(prompt),
            CooldownFrames = (ushort)Mathf.Clamp(pmi.InteractionCooldownFrames, 0, 65535),
            PromptCanvasName = prompt,
        });
    }

    private static void CollectAudioClips(PS1Scene scene, SceneData data)
    {
        GD.Print($"[PS1Godot] PS1Scene.AudioClips.Count = {scene.AudioClips?.Count ?? -1}");
        if (scene.AudioClips == null) return;
        var seen = new HashSet<string>();
        int slot = -1;
        foreach (var clip in scene.AudioClips)
        {
            slot++;
            GD.Print($"[PS1Godot]   slot[{slot}] = {(clip == null ? "null" : clip.GetType().Name)} Stream={(clip?.Stream == null ? "null" : clip.Stream.GetType().Name)} ClipName='{clip?.ClipName}'");
            if (clip == null || clip.Stream == null)
            {
                GD.PushWarning("[PS1Godot] PS1Scene.AudioClips has an empty slot or a clip with no Stream — skipping.");
                continue;
            }

            // Derive the clip name: authored ClipName wins; fall back to the
            // stream's resource basename so unnamed drops don't silently
            // collide on an empty string.
            string name = string.IsNullOrWhiteSpace(clip.ClipName)
                ? System.IO.Path.GetFileNameWithoutExtension(clip.Stream.ResourcePath ?? "clip")
                : clip.ClipName;

            if (!seen.Add(name))
            {
                GD.PushWarning($"[PS1Godot] Duplicate audio clip name '{name}' — Audio.Play will only find one of them.");
            }

            if (clip.Stream is not AudioStreamWav wav)
            {
                GD.PushError(
                    $"[PS1Godot] Audio clip '{name}': {clip.Stream.GetType().Name} is not a WAV. " +
                    $"PS1 export needs 16-bit PCM WAV — convert '{clip.Stream.ResourcePath}' " +
                    $"(Audacity: File → Export → WAV → 16-bit PCM, or ffmpeg -i in.mp3 -ac 1 -ar 22050 -sample_fmt s16 out.wav).");
                continue;
            }

            short[] pcm = ReadAudioStreamAsMono16(wav);
            if (pcm.Length == 0)
            {
                GD.PushWarning($"[PS1Godot] Audio clip '{name}' has no sample data — skipping.");
                continue;
            }

            byte[] adpcm = ADPCMEncoder.Encode(pcm, clip.Loop);
            ushort rate = (ushort)Mathf.Clamp(wav.MixRate, 1000, 44100);

            data.AudioClips.Add(new AudioClipRecord
            {
                AdpcmData = adpcm,
                SampleRate = rate,
                Loop = clip.Loop,
                Name = name,
            });
            GD.Print($"[PS1Godot] Audio clip '{name}': {pcm.Length} samples @ {rate}Hz → {adpcm.Length} bytes ADPCM (loop={clip.Loop}, index {data.AudioClips.Count - 1})");
        }
    }

    private static void CollectMusicSequences(PS1Scene scene, SceneData data)
    {
        var seqs = scene.MusicSequences;
        if (seqs == null || seqs.Count == 0) return;

        // Build a clip-name → index lookup once. AudioClips were collected
        // just before this in the parent walk.
        var clipIndexByName = new Dictionary<string, int>(data.AudioClips.Count);
        for (int i = 0; i < data.AudioClips.Count; i++)
            clipIndexByName[data.AudioClips[i].Name] = i;

        var seenNames = new HashSet<string>();

        foreach (var seq in seqs)
        {
            if (seq == null) continue;
            if (data.MusicSequences.Count >= 8)
            {
                GD.PushWarning("[PS1Godot] PS1Scene.MusicSequences: only the first 8 entries are exported; runtime cap.");
                break;
            }

            // Resolve a name. SequenceName wins; otherwise use the .mid
            // basename. Falls back to "music_<index>" if both are empty.
            string name = string.IsNullOrWhiteSpace(seq.SequenceName)
                ? System.IO.Path.GetFileNameWithoutExtension(seq.MidiFile ?? "")
                : seq.SequenceName;
            if (string.IsNullOrEmpty(name)) name = $"music_{data.MusicSequences.Count}";
            if (name.Length > 15) name = name[..15];   // matches MusicTableEntry.name[16]
            if (!seenNames.Add(name))
            {
                GD.PushWarning($"[PS1Godot] Duplicate music sequence name '{name}' — Music.Play will only find one.");
            }

            if (string.IsNullOrEmpty(seq.MidiFile))
            {
                GD.PushError($"[PS1Godot] PS1MusicSequence '{name}': MidiFile is empty — skipping.");
                continue;
            }

            byte[] midiBytes;
            try
            {
                string globalPath = ProjectSettings.GlobalizePath(seq.MidiFile);
                midiBytes = System.IO.File.ReadAllBytes(globalPath);
            }
            catch (System.Exception ex)
            {
                GD.PushError($"[PS1Godot] PS1MusicSequence '{name}': failed to read '{seq.MidiFile}' ({ex.Message}) — skipping.");
                continue;
            }

            MidiParser.ParsedMidi parsed;
            try { parsed = MidiParser.Parse(midiBytes); }
            catch (System.Exception ex)
            {
                GD.PushError($"[PS1Godot] PS1MusicSequence '{name}': MIDI parse failed ({ex.Message}) — skipping.");
                continue;
            }

            // Map authored channels to PS1MSerializer.ChannelBinding.
            // Skip channels whose AudioClipName doesn't resolve.
            var bindings = new List<PS1MSerializer.ChannelBinding>();
            if (seq.Channels != null)
            {
                foreach (var ch in seq.Channels)
                {
                    if (ch == null) continue;
                    if (string.IsNullOrEmpty(ch.AudioClipName))
                    {
                        GD.PushWarning($"[PS1Godot] PS1MusicSequence '{name}': MIDI channel {ch.MidiChannel} has no AudioClipName — skipped.");
                        continue;
                    }
                    if (!clipIndexByName.TryGetValue(ch.AudioClipName, out int clipIdx))
                    {
                        GD.PushError($"[PS1Godot] PS1MusicSequence '{name}': MIDI channel {ch.MidiChannel} references unknown audio clip '{ch.AudioClipName}'. Add it to PS1Scene.AudioClips.");
                        continue;
                    }
                    bindings.Add(new PS1MSerializer.ChannelBinding
                    {
                        MidiChannel = ch.MidiChannel,
                        MidiTrackIndex = ch.MidiTrackIndex,
                        MidiNoteMin = ch.MidiNoteMin,
                        MidiNoteMax = ch.MidiNoteMax,
                        AudioClipIndex = clipIdx,
                        BaseNoteMidi = ch.BaseNoteMidi,
                        Volume = ch.Volume,
                        Pan = ch.Pan,
                        LoopSample = ch.LoopSample,
                        Percussion = ch.Percussion,
                    });
                }
            }
            if (bindings.Count == 0)
            {
                GD.PushError($"[PS1Godot] PS1MusicSequence '{name}': no usable channel bindings — skipping. Add at least one PS1MusicChannel pointing at an audio clip.");
                continue;
            }

            byte[] ps1m;
            try
            {
                int? bpm = seq.BpmOverride > 0 ? seq.BpmOverride : null;
                ps1m = PS1MSerializer.Serialize(parsed, bindings, bpm, seq.LoopStartBeat);
            }
            catch (System.Exception ex)
            {
                GD.PushError($"[PS1Godot] PS1MusicSequence '{name}': PS1M serialize failed ({ex.Message}) — skipping.");
                continue;
            }

            data.MusicSequences.Add(new MusicSequenceRecord
            {
                Ps1mData = ps1m,
                Name = name,
            });
            GD.Print($"[PS1Godot] Music sequence '{name}': {parsed.Notes.Count} notes, {bindings.Count} channels, {ps1m.Length} bytes (index {data.MusicSequences.Count - 1}).");
        }
    }

    // AudioStreamWav.Data is raw PCM in the declared Format (8- or 16-bit,
    // mono or stereo, matching MixRate). We normalise to int16 mono at
    // whatever rate the stream was imported at; resampling is the user's
    // responsibility for now (44.1kHz → e.g. 22kHz should be done in Godot's
    // import settings).
    private static short[] ReadAudioStreamAsMono16(AudioStreamWav stream)
    {
        byte[] raw = stream.Data ?? System.Array.Empty<byte>();
        if (raw.Length == 0) return System.Array.Empty<short>();

        bool stereo = stream.Stereo;
        var fmt = stream.Format;

        // Decode to int16 mono. Stereo gets downmixed by averaging L+R since
        // PSX SPU voices are mono per channel.
        short[] samples;
        switch (fmt)
        {
            case AudioStreamWav.FormatEnum.Format8Bits:
            {
                int step = stereo ? 2 : 1;
                int count = raw.Length / step;
                samples = new short[count];
                for (int i = 0; i < count; i++)
                {
                    // AudioStreamWav 8-bit is signed per Godot source.
                    int l = (sbyte)raw[i * step] << 8;
                    int r = stereo ? (sbyte)raw[i * step + 1] << 8 : l;
                    samples[i] = (short)((l + r) / (stereo ? 2 : 1));
                }
                break;
            }
            case AudioStreamWav.FormatEnum.Format16Bits:
            {
                int bytesPerFrame = stereo ? 4 : 2;
                int count = raw.Length / bytesPerFrame;
                samples = new short[count];
                for (int i = 0; i < count; i++)
                {
                    int off = i * bytesPerFrame;
                    short l = (short)(raw[off] | (raw[off + 1] << 8));
                    short r = stereo
                        ? (short)(raw[off + 2] | (raw[off + 3] << 8))
                        : l;
                    samples[i] = (short)((l + r) / (stereo ? 2 : 1));
                }
                break;
            }
            default:
                GD.PushError(
                    $"[PS1Godot] Audio format {fmt} not supported. " +
                    $"Godot 4.4+ defaults WAV imports to QOA (lossy compressed). " +
                    $"Fix: select the .wav in FileSystem → Import tab → set " +
                    $"Compress Mode to 'Disabled' → Reimport. The exporter needs " +
                    $"raw 8- or 16-bit PCM bytes in AudioStreamWav.Data.");
                return System.Array.Empty<short>();
        }
        return samples;
    }

    // Look up the .lua file at `path`, dedup against `cache`, and return the
    // index into data.LuaFiles (-1 if path is empty or unreadable).
    //
    // `nodeLabel` is the node name used in log/warning messages only.
    //
    // Runtime v20 with the full parser linked accepts raw source text via
    // luaL_loadbuffer — bytecode compilation via luac_psx is an optimization
    // we'll layer on later without changing the splashpack shape.
    private static int ResolveLuaScript(string nodeLabel, string path, SceneData data, Dictionary<string, int> cache)
    {
        if (string.IsNullOrEmpty(path)) return -1;
        if (cache.TryGetValue(path, out int existing)) return existing;

        if (!Godot.FileAccess.FileExists(path))
        {
            GD.PushError($"[PS1Godot] {nodeLabel}: Lua script '{path}' not found.");
            return -1;
        }

        string source = Godot.FileAccess.GetFileAsString(path);
        if (string.IsNullOrEmpty(source))
        {
            GD.PushWarning($"[PS1Godot] {nodeLabel}: Lua script '{path}' is empty — skipping.");
            return -1;
        }

        var bytes = System.Text.Encoding.UTF8.GetBytes(source);
        int idx = data.LuaFiles.Count;
        data.LuaFiles.Add(new LuaFileRecord { Bytes = bytes, SourcePath = path });
        cache[path] = idx;
        GD.Print($"[PS1Godot] Lua on '{nodeLabel}': {path} ({bytes.Length} bytes, index {idx})");
        return idx;
    }

    // Emits whichever of (SPLASHPACKCollider AABB, NavRegion) apply for this
    // mesh's Collision setting. Runtime reads colliders for X/Z push-back and
    // nav regions for gravity/floor height — the two are independent.
    private static void EmitCollisionFor(PS1MeshInstance pmi, ushort objectIndex, SceneData data)
    {
        if (pmi.Collision == PS1MeshInstance.CollisionKind.None) return;
        if (pmi.Mesh == null) return;

        // psxsplash's CollisionSystem declares updateCollider() but the
        // runtime never calls it — registerCollider sets data.bounds =
        // localBounds as-is and that's the final value. So the bounds we
        // emit ARE the world-space bounds the grid will test against.
        //
        // Bake the full global transform (rotation + scale + translation)
        // into the 8 AABB corners and take their axis-aligned extent. For
        // rotated meshes this over-approximates the visual footprint a bit,
        // but matches what the collision tester expects.
        var localAabb = pmi.Mesh.GetAabb();
        Vector3 lMin = localAabb.Position;
        Vector3 lMax = localAabb.Position + localAabb.Size;
        Transform3D xform = pmi.GlobalTransform;
        Vector3 wMin = new(float.MaxValue, float.MaxValue, float.MaxValue);
        Vector3 wMax = new(float.MinValue, float.MinValue, float.MinValue);
        for (int i = 0; i < 8; i++)
        {
            var corner = new Vector3(
                (i & 1) != 0 ? lMax.X : lMin.X,
                (i & 2) != 0 ? lMax.Y : lMin.Y,
                (i & 4) != 0 ? lMax.Z : lMin.Z);
            Vector3 world = xform * corner;
            wMin = new Vector3(Mathf.Min(wMin.X, world.X), Mathf.Min(wMin.Y, world.Y), Mathf.Min(wMin.Z, world.Z));
            wMax = new Vector3(Mathf.Max(wMax.X, world.X), Mathf.Max(wMax.Y, world.Y), Mathf.Max(wMax.Z, world.Z));
        }

        data.Colliders.Add(new ColliderRecord
        {
            WorldMin = wMin,
            WorldMax = wMax,
            CollisionType = 1, // Solid
            LayerMask = (byte)pmi.LayerMask,
            GameObjectIndex = objectIndex,
        });

        // Emit a flat nav region for any Static mesh whose world AABB is
        // effectively flat on Y (PlaneMesh, subdivided plane → ArrayMesh,
        // or an intentionally flat slab). The 0.1 m threshold keeps walls
        // and props out of the nav system while letting floors in
        // regardless of mesh class.
        if (pmi.Collision == PS1MeshInstance.CollisionKind.Static)
        {
            float yExtent = Mathf.Abs(wMax.Y - wMin.Y);
            if (yExtent < 0.1f)
            {
                EmitFlatNavRegion(wMin, wMax, data);
            }
        }
    }

    // PS1 nav region from a PlaneMesh bounding rect, expressed in PSX fp12.
    // NavRegionSystem's point-in-poly requires CCW winding in PSX (X, Z). The
    // Godot→PSX Z-flip mirrors the polygon, so we emit Godot's CCW order in
    // reverse to land on PSX-CCW after the flip.
    private static void EmitFlatNavRegion(Vector3 wMin, Vector3 wMax, SceneData data)
    {
        int fp(float godotValue) => PSXTrig.ConvertWorldToFixed12(godotValue / data.GteScaling);
        float floorY = 0.5f * (wMin.Y + wMax.Y);

        // World-space verts in Godot units — consumed by the portal stitcher
        // downstream. Godot-CCW order.
        var worldVerts = new[]
        {
            new Vector3(wMin.X, floorY, wMin.Z),
            new Vector3(wMax.X, floorY, wMin.Z),
            new Vector3(wMax.X, floorY, wMax.Z),
            new Vector3(wMin.X, floorY, wMax.Z),
        };

        // Reversed + Z-negated → PSX-CCW.
        data.NavRegions.Add(new NavRegionRecord
        {
            VertsX = new[] { fp(wMin.X), fp(wMax.X), fp(wMax.X), fp(wMin.X) },
            VertsZ = new[] { fp(-wMax.Z), fp(-wMax.Z), fp(-wMin.Z), fp(-wMin.Z) },
            PlaneA = 0,
            PlaneB = 0,
            PlaneD = fp(-floorY),  // Y-down: Godot +Y → PSX -Y
            WorldVerts = worldVerts,
        });
    }

    // Author-drawn nav region. Transform local verts to world, CCW-check
    // them (auto-reverse if needed), fit a plane through (X, Y, Z), and
    // emit the PSX record.
    internal static void EmitAuthoredNavRegion(PS1NavRegion node, SceneData data)
    {
        var local = node.Verts;
        if (local == null || local.Length < 3)
        {
            GD.PushWarning($"[PS1Godot] PS1NavRegion '{node.Name}' has <3 verts — skipped.");
            return;
        }
        if (local.Length > 8)
        {
            GD.PushWarning($"[PS1Godot] PS1NavRegion '{node.Name}' has {local.Length} verts — runtime cap is 8; trimming.");
        }

        int count = System.Math.Min(local.Length, 8);
        var xform = node.GlobalTransform;
        var world = new Vector3[count];
        for (int i = 0; i < count; i++)
            world[i] = xform * local[i];

        // CCW check via signed area on XZ. Godot +Y is up; we treat XZ as
        // a standard right-handed plane where CCW = positive signed area.
        float signedArea = 0;
        for (int i = 0; i < count; i++)
        {
            var a = world[i];
            var b = world[(i + 1) % count];
            signedArea += a.X * b.Z - b.X * a.Z;
        }
        if (signedArea < 0)
        {
            System.Array.Reverse(world);
            GD.Print($"[PS1Godot] PS1NavRegion '{node.Name}': reversed vert order to CCW.");
        }

        FitPlane(world, out float planeA, out float planeB, out float planeD);

        // Auto-classify flat / ramp / stairs by slope of the fitted plane.
        byte surface = (byte)node.SurfaceType;
        if (node.SurfaceType == PS1NavSurfaceType.Flat)
        {
            float slopeDeg = Mathf.RadToDeg(Mathf.Atan(Mathf.Sqrt(planeA * planeA + planeB * planeB)));
            if (slopeDeg >= 25f) surface = (byte)PS1NavSurfaceType.Stairs;
            else if (slopeDeg >= 3f) surface = (byte)PS1NavSurfaceType.Ramp;
        }

        int fp(float godotValue) => PSXTrig.ConvertWorldToFixed12(godotValue / data.GteScaling);

        // Runtime plane equation: PSX_Y = A·PSX_X + B·PSX_Z + D.
        // Substituting PSX_Y = -Godot_Y and PSX_Z = -Godot_Z into the fit
        // Godot_Y = a·Godot_X + b·Godot_Z + d gives A = -a, B = +b, D = -d.
        // The pre-Y+Z-flip code had B = -b (matched SplashEdit); adding the
        // Z-flip flips that sign back.
        // Vert order reverses too — polygon chirality flips under a single-axis
        // reflection, and the runtime requires CCW in PSX (X, Z).
        var vertsX = new int[count];
        var vertsZ = new int[count];
        for (int i = 0; i < count; i++)
        {
            int src = count - 1 - i;
            vertsX[i] = fp( world[src].X);
            vertsZ[i] = fp(-world[src].Z);
        }

        byte flags = node.Platform ? (byte)0x01 : (byte)0;
        byte walkoff = 0; // platform walkoff handled by runtime via flags bit

        data.NavRegions.Add(new NavRegionRecord
        {
            VertsX = vertsX,
            VertsZ = vertsZ,
            PlaneA = PSXTrig.ConvertWorldToFixed12(-planeA),
            PlaneB = PSXTrig.ConvertWorldToFixed12( planeB),
            PlaneD = PSXTrig.ConvertWorldToFixed12(-planeD / data.GteScaling),
            SurfaceType = surface,
            RoomIndex = (byte)System.Math.Clamp(node.RoomIndex, 0, 255),
            Flags = flags,
            WalkoffEdgeMask = walkoff,
            WorldVerts = world,
        });
    }

    // Least-squares fit Y = A·X + B·Z + D against `pts`. Degenerates to a
    // flat plane at mean-Y when the system is singular (all verts collinear
    // on XZ).
    private static void FitPlane(Vector3[] pts, out float a, out float b, out float d)
    {
        int n = pts.Length;
        if (n == 3)
        {
            float x0 = pts[0].X, z0 = pts[0].Z, y0 = pts[0].Y;
            float x1 = pts[1].X, z1 = pts[1].Z, y1 = pts[1].Y;
            float x2 = pts[2].X, z2 = pts[2].Z, y2 = pts[2].Y;
            float det = (x0 - x2) * (z1 - z2) - (x1 - x2) * (z0 - z2);
            if (Mathf.Abs(det) < 1e-6f)
            {
                a = 0; b = 0; d = (y0 + y1 + y2) / 3f;
                return;
            }
            float inv = 1f / det;
            a = ((y0 - y2) * (z1 - z2) - (y1 - y2) * (z0 - z2)) * inv;
            b = ((x0 - x2) * (y1 - y2) - (x1 - x2) * (y0 - y2)) * inv;
            d = y0 - a * x0 - b * z0;
            return;
        }

        double sX = 0, sZ = 0, sY = 0, sXX = 0, sXZ = 0, sZZ = 0, sXY = 0, sZY = 0;
        foreach (var p in pts)
        {
            sX += p.X; sZ += p.Z; sY += p.Y;
            sXX += p.X * p.X; sXZ += p.X * p.Z; sZZ += p.Z * p.Z;
            sXY += p.X * p.Y; sZY += p.Z * p.Y;
        }
        double det2 = sXX * (sZZ * n - sZ * sZ) - sXZ * (sXZ * n - sZ * sX) + sX * (sXZ * sZ - sZZ * sX);
        if (System.Math.Abs(det2) < 1e-9)
        {
            a = 0; b = 0; d = (float)(sY / n);
            return;
        }
        double inv2 = 1.0 / det2;
        a = (float)((sXY * (sZZ * n - sZ * sZ) - sXZ * (sZY * n - sZ * sY) + sX * (sZY * sZ - sZZ * sY)) * inv2);
        b = (float)((sXX * (sZY * n - sZ * sY) - sXY * (sXZ * n - sZ * sX) + sX * (sXZ * sY - sZY * sX)) * inv2);
        d = (float)((sXX * (sZZ * sY - sZ * sZY) - sXZ * (sXZ * sY - sZY * sX) + sXY * (sXZ * sZ - sZZ * sX)) * inv2);
    }

    // Post-pass: scan every pair of regions for edges whose endpoints are
    // coincident in world XZ. For each match, emit a directed portal per
    // region (A→B and B→A), with heightDelta = (other region's Y at the
    // midpoint) - (this region's Y at the midpoint).
    internal static void StitchNavPortals(SceneData data)
    {
        if (data.NavRegions.Count < 2) return;

        // World-space epsilon in Godot units. 0.05 m is tighter than a
        // single grid cell and wider than FP12 quantization error.
        const float Eps = 0.05f;
        float eps2 = Eps * Eps;

        // Build a per-region directed-portal bucket so we can set PortalStart
        // contiguously once the scan finishes.
        var perRegion = new List<NavPortalRecord>[data.NavRegions.Count];
        for (int i = 0; i < perRegion.Length; i++) perRegion[i] = new();

        int fp(float godotValue) => PSXTrig.ConvertWorldToFixed12(godotValue / data.GteScaling);

        for (int i = 0; i < data.NavRegions.Count; i++)
        {
            var ri = data.NavRegions[i];
            var wi = ri.WorldVerts;
            if (wi == null || wi.Length < 2) continue;

            for (int j = i + 1; j < data.NavRegions.Count; j++)
            {
                var rj = data.NavRegions[j];
                var wj = rj.WorldVerts;
                if (wj == null || wj.Length < 2) continue;

                int niEdges = wi.Length, njEdges = wj.Length;
                for (int e = 0; e < niEdges; e++)
                {
                    Vector3 a0 = wi[e];
                    Vector3 a1 = wi[(e + 1) % niEdges];

                    for (int f = 0; f < njEdges; f++)
                    {
                        Vector3 b0 = wj[f];
                        Vector3 b1 = wj[(f + 1) % njEdges];

                        // Adjacent CCW polygons share an edge with reversed
                        // winding: A's (a0 → a1) ≈ B's (b1 → b0).
                        if (DistXZSq(a0, b1) < eps2 && DistXZSq(a1, b0) < eps2)
                        {
                            // Midpoint on the shared edge (world XZ).
                            float midX = 0.5f * (a0.X + a1.X);
                            float midZ = 0.5f * (a0.Z + a1.Z);
                            float yI = PlaneY(ri, midX, midZ);
                            float yJ = PlaneY(rj, midX, midZ);

                            // PSX Y is Godot's -Y, so flip sign when quantizing.
                            int hdIJ = fp(-(yJ - yI));
                            int hdJI = fp(-(yI - yJ));
                            perRegion[i].Add(new NavPortalRecord
                            {
                                Ax = fp( a0.X), Az = fp(-a0.Z),
                                Bx = fp( a1.X), Bz = fp(-a1.Z),
                                NeighborRegion = (ushort)j,
                                HeightDelta = (short)System.Math.Clamp(hdIJ, short.MinValue, short.MaxValue),
                            });
                            perRegion[j].Add(new NavPortalRecord
                            {
                                Ax = fp( b0.X), Az = fp(-b0.Z),
                                Bx = fp( b1.X), Bz = fp(-b1.Z),
                                NeighborRegion = (ushort)i,
                                HeightDelta = (short)System.Math.Clamp(hdJI, short.MinValue, short.MaxValue),
                            });
                        }
                    }
                }
            }
        }

        // Flatten portals into data.NavPortals in region order, stamping
        // PortalStart / PortalCount on each region as we go.
        int total = 0;
        for (int i = 0; i < data.NavRegions.Count; i++)
        {
            var ri = data.NavRegions[i];
            ri.PortalStart = (ushort)total;
            ri.PortalCount = (byte)System.Math.Min(perRegion[i].Count, 255);
            foreach (var p in perRegion[i]) data.NavPortals.Add(p);
            total += perRegion[i].Count;
        }
        if (total > 0)
        {
            GD.Print($"[PS1Godot] Nav: stitched {total} portals across {data.NavRegions.Count} regions.");
        }
    }

    private static float DistXZSq(Vector3 a, Vector3 b)
    {
        float dx = a.X - b.X, dz = a.Z - b.Z;
        return dx * dx + dz * dz;
    }

    // Evaluate the fitted plane at (x, z) in world space. The stored
    // PlaneA/B/D are negated + quantized for PSX; reconstruct an
    // approximate world-Y from WorldVerts centroid for now by solving the
    // same Y = A·x + B·z + D with un-negated coefficients.
    private static float PlaneY(NavRegionRecord r, float x, float z)
    {
        var w = r.WorldVerts;
        if (w == null || w.Length == 0) return 0f;
        // Re-fit on the spot — cheaper than tracking both quantized and raw
        // coefficients, and runs once per portal during export.
        FitPlane(w, out float a, out float b, out float d);
        return a * x + b * z + d;
    }

    // Walk the tree for PS1Room + PS1PortalLink nodes, compute per-room
    // AABBs, assign every exported triangle to a room (or the catch-all),
    // and capture portal connectivity. No-op when there are no PS1Room
    // nodes — exterior scenes keep relying on BVH culling.
    internal static void CollectRooms(Node root, SceneData data)
    {
        var rooms = new List<PS1Room>();
        var portals = new List<PS1PortalLink>();
        CollectRoomNodes(root, rooms, portals);

        if (rooms.Count == 0 && portals.Count == 0) return;

        if (portals.Count > 0 && rooms.Count == 0)
        {
            GD.PushWarning(
                "[PS1Godot] Scene has PS1PortalLink nodes but no PS1Room — portals skipped. " +
                "Add PS1Room nodes first so portals can point at them.");
            return;
        }

        // Room bounds in world space — same matrix-the-8-corners pattern
        // we use for colliders and trigger boxes so scaling / rotating the
        // PS1Room node reshapes the volume before we AABB-it.
        const float RoomMargin = 0.5f;  // matches SplashEdit's triangle-majority expand
        var roomBounds = new (Vector3 min, Vector3 max)[rooms.Count];
        for (int i = 0; i < rooms.Count; i++)
        {
            roomBounds[i] = ComputeRoomWorldBounds(rooms[i], RoomMargin);
        }

        // Bucket triangles per room + catch-all. Authored rooms track
        // per-tri AABB + centroid alongside the ref so the flatten pass
        // can subdivide each room into cells. Catch-all keeps the simple
        // shape — it never subdivides (loose ±1000 m bounds), and the
        // PS1MeshGroup path below doesn't have per-tri vertex access.
        var perRoomTris = new List<RoomTriAssignment>[rooms.Count];
        for (int i = 0; i < perRoomTris.Length; i++) perRoomTris[i] = new();
        var perRoomCatchAll = new List<RoomTriRefRecord>();
        int catchAllIdx = rooms.Count;

        for (int objIdx = 0; objIdx < data.Objects.Count; objIdx++)
        {
            var obj = data.Objects[objIdx];
            if (obj.Mesh == null) continue;

            // Triangles in data.Objects[*].Mesh are already baked to world
            // space and stored in PS1 fp12 — but we need world-space XYZ in
            // Godot units for the per-triangle room test. Walk the source
            // MeshInstance3D to get the positions at export-time precision.
            //
            // PS1MeshGroup objects have a merged PSXMesh but no single
            // source MeshInstance3D to rewalk — bucket them all into the
            // catch-all so interior room/portal culling stays correct
            // without requiring per-submesh traversal here. (Interior
            // scenes that want per-sub-room tri assignment should use
            // PS1MeshInstance per piece, not a group.)
            if (obj.Node is not MeshInstance3D mi || mi.Mesh == null)
            {
                int merged = obj.Mesh.Triangles.Count;
                for (int t = 0; t < merged; t++)
                {
                    perRoomCatchAll.Add(new RoomTriRefRecord
                    {
                        ObjectIndex = (ushort)objIdx,
                        TriangleIndex = (ushort)t,
                    });
                }
                continue;
            }

            var xform = mi.GlobalTransform;
            var nodeScale = mi.Scale;
            var godotMesh = mi.Mesh;
            int surfaceCount = godotMesh.GetSurfaceCount();

            int triGlobal = 0;
            for (int s = 0; s < surfaceCount; s++)
            {
                var arrays = godotMesh.SurfaceGetArrays(s);
                var verts = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                var idx = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();

                int triCount = idx.Length > 0 ? idx.Length / 3 : verts.Length / 3;

                for (int t = 0; t < triCount; t++)
                {
                    int i0 = idx.Length > 0 ? idx[t * 3] : t * 3;
                    int i1 = idx.Length > 0 ? idx[t * 3 + 1] : t * 3 + 1;
                    int i2 = idx.Length > 0 ? idx[t * 3 + 2] : t * 3 + 2;

                    Vector3 v0 = xform * (verts[i0] * nodeScale);
                    Vector3 v1 = xform * (verts[i1] * nodeScale);
                    Vector3 v2 = xform * (verts[i2] * nodeScale);

                    // Require ALL THREE verts inside a room before assigning
                    // the triangle to it. Partial-overlap triangles (e.g. a
                    // wide ground plane that crosses a room AABB) stay in
                    // catch-all so they remain visible from outside the
                    // room — otherwise authors get a "hole in the floor"
                    // wherever a room sits on top of a larger mesh.
                    // When multiple rooms wholly contain the triangle,
                    // pick the closest centroid.
                    int best = -1;
                    float bestDistSq = float.MaxValue;
                    Vector3 centroid = (v0 + v1 + v2) / 3f;

                    for (int r = 0; r < rooms.Count; r++)
                    {
                        var (rmin, rmax) = roomBounds[r];
                        if (!PointInAabb(v0, rmin, rmax)) continue;
                        if (!PointInAabb(v1, rmin, rmax)) continue;
                        if (!PointInAabb(v2, rmin, rmax)) continue;

                        Vector3 rc = (rmin + rmax) * 0.5f;
                        float d = (rc - centroid).LengthSquared();
                        if (d < bestDistSq)
                        {
                            bestDistSq = d;
                            best = r;
                        }
                    }

                    var triRef = new RoomTriRefRecord
                    {
                        ObjectIndex = (ushort)objIdx,
                        TriangleIndex = (ushort)triGlobal,
                    };
                    if (best >= 0)
                    {
                        perRoomTris[best].Add(new RoomTriAssignment
                        {
                            Ref = triRef,
                            Min = VecMin(VecMin(v0, v1), v2),
                            Max = VecMax(VecMax(v0, v1), v2),
                            Centroid = centroid,
                        });
                    }
                    else
                    {
                        perRoomCatchAll.Add(triRef);
                    }
                    triGlobal++;
                }
            }
        }

        // Flatten into data.RoomTriRefs / data.RoomCells, stamping
        // FirstTriRef / TriRefCount / FirstCell / CellCount on each
        // RoomRecord as we go. Authored rooms subdivide into a 3D grid
        // (~5 m per cell, max 4 per axis) when they have enough triangles
        // for the extra indirection to earn its keep; smaller or sparser
        // rooms skip subdivision and fall back to the full-room render
        // path at runtime. Catch-all never subdivides.
        int running = 0;
        ushort runningCell = 0;
        for (int r = 0; r < rooms.Count; r++)
        {
            var (rmin, rmax) = ComputeRoomWorldBounds(rooms[r], 0f);
            var tris = perRoomTris[r];
            int triCount = tris.Count;

            ushort firstTriRef = (ushort)running;
            ushort firstCell = runningCell;
            byte cellCount = 0;

            (int dx, int dy, int dz) = PickCellDivisions(rmax - rmin);
            int totalDivs = dx * dy * dz;

            if (totalDivs > 1 && triCount >= CellSubdivideMinTris)
            {
                // Bucket tris into grid cells by centroid.
                var perCell = new List<RoomTriAssignment>[totalDivs];
                for (int i = 0; i < totalDivs; i++) perCell[i] = new();
                Vector3 extent = rmax - rmin;
                float cwX = extent.X / dx, cwY = extent.Y / dy, cwZ = extent.Z / dz;
                foreach (var at in tris)
                {
                    int cx = Mathf.Clamp((int)((at.Centroid.X - rmin.X) / cwX), 0, dx - 1);
                    int cy = Mathf.Clamp((int)((at.Centroid.Y - rmin.Y) / cwY), 0, dy - 1);
                    int cz = Mathf.Clamp((int)((at.Centroid.Z - rmin.Z) / cwZ), 0, dz - 1);
                    perCell[cx + dx * (cy + dy * cz)].Add(at);
                }

                // Emit non-empty cells with tight AABB around their actual
                // triangles (tighter than the grid-cell box → better culling).
                int emitted = 0;
                int offsetInRoom = 0;
                for (int ci = 0; ci < totalDivs; ci++)
                {
                    var cellTris = perCell[ci];
                    if (cellTris.Count == 0) continue;
                    Vector3 cmin = cellTris[0].Min;
                    Vector3 cmax = cellTris[0].Max;
                    for (int j = 1; j < cellTris.Count; j++)
                    {
                        cmin = VecMin(cmin, cellTris[j].Min);
                        cmax = VecMax(cmax, cellTris[j].Max);
                    }
                    data.RoomCells.Add(new RoomCellRecord
                    {
                        WorldMin = cmin,
                        WorldMax = cmax,
                        FirstTriRef = (ushort)(running + offsetInRoom),
                        TriRefCount = (ushort)cellTris.Count,
                    });
                    foreach (var at in cellTris) data.RoomTriRefs.Add(at.Ref);
                    offsetInRoom += cellTris.Count;
                    emitted++;
                }
                cellCount = (byte)emitted;
                runningCell += (ushort)emitted;
            }
            else
            {
                // No cells — just append tris in assignment order.
                foreach (var at in tris) data.RoomTriRefs.Add(at.Ref);
            }

            data.Rooms.Add(new RoomRecord
            {
                WorldMin = rmin,
                WorldMax = rmax,
                Name = rooms[r].RoomName,
                FirstTriRef = firstTriRef,
                TriRefCount = (ushort)triCount,
                FirstCell = firstCell,
                CellCount = cellCount,
            });
            running += triCount;
        }
        // Catch-all room entry — loose ±1000-unit box in Godot space; the
        // writer quantizes per usual. Runtime always renders this room's
        // tri-refs regardless of camera position. No cell subdivision —
        // its bounds are deliberately loose and it sweeps up whatever
        // didn't fit an authored room.
        {
            var rec = new RoomRecord
            {
                WorldMin = new Vector3(-1000, -1000, -1000),
                WorldMax = new Vector3( 1000,  1000,  1000),
                Name = "_catchall",
                FirstTriRef = (ushort)running,
                TriRefCount = (ushort)perRoomCatchAll.Count,
            };
            data.Rooms.Add(rec);
            foreach (var tr in perRoomCatchAll) data.RoomTriRefs.Add(tr);
            running += perRoomCatchAll.Count;
        }

        // Resolve PS1PortalLink nodes: RoomA/RoomB NodePaths → room indices.
        // Auto-correct the normal so it points RoomA → RoomB.
        var roomToIndex = new Dictionary<PS1Room, int>();
        for (int i = 0; i < rooms.Count; i++) roomToIndex[rooms[i]] = i;

        int droppedPortals = 0;
        foreach (var link in portals)
        {
            var nodeA = link.GetNodeOrNull(link.RoomA) as PS1Room;
            var nodeB = link.GetNodeOrNull(link.RoomB) as PS1Room;
            if (nodeA == null || nodeB == null)
            {
                GD.PushWarning($"[PS1Godot] PS1PortalLink '{link.Name}' has unresolved / non-PS1Room RoomA/RoomB — skipped.");
                droppedPortals++;
                continue;
            }
            if (nodeA == nodeB)
            {
                GD.PushWarning($"[PS1Godot] PS1PortalLink '{link.Name}' points at the same room twice — skipped.");
                droppedPortals++;
                continue;
            }
            if (!roomToIndex.TryGetValue(nodeA, out int idxA) ||
                !roomToIndex.TryGetValue(nodeB, out int idxB))
            {
                GD.PushWarning($"[PS1Godot] PS1PortalLink '{link.Name}': one of its rooms is not in the collected room list — skipped.");
                droppedPortals++;
                continue;
            }

            var t = link.GlobalTransform;
            var centre = t.Origin;
            var normal = -t.Basis.Z.Normalized();   // Godot forward is -Z
            var right  =  t.Basis.X.Normalized();
            var up     =  t.Basis.Y.Normalized();

            var centreA = nodeA.GlobalTransform * nodeA.VolumeOffset;
            var centreB = nodeB.GlobalTransform * nodeB.VolumeOffset;
            var aToB = (centreB - centreA).Normalized();
            if (normal.Dot(aToB) < 0f)
            {
                normal = -normal;
                right = -right;
            }

            data.Portals.Add(new PortalRecord
            {
                RoomA = (ushort)idxA,
                RoomB = (ushort)idxB,
                WorldCenter = centre,
                PortalSize = link.PortalSize,
                Normal = normal,
                Right = right,
                Up = up,
            });
        }

        // Per-room portal-ref lists. For each portal, both RoomA and RoomB
        // get an entry pointing at the other — the renderer uses this to
        // iterate just a room's neighbors instead of scanning every portal
        // per frame. Catch-all has no portal links authored against it, so
        // its refCount lands at 0 (runtime falls back for that room).
        var perRoomPortalRefs = new List<RoomPortalRefRecord>[data.Rooms.Count];
        for (int i = 0; i < perRoomPortalRefs.Length; i++)
            perRoomPortalRefs[i] = new();
        for (int p = 0; p < data.Portals.Count; p++)
        {
            var portal = data.Portals[p];
            perRoomPortalRefs[portal.RoomA].Add(new RoomPortalRefRecord
            {
                PortalIndex = (ushort)p,
                OtherRoom = portal.RoomB,
            });
            perRoomPortalRefs[portal.RoomB].Add(new RoomPortalRefRecord
            {
                PortalIndex = (ushort)p,
                OtherRoom = portal.RoomA,
            });
        }
        ushort runningPortalRef = 0;
        for (int r = 0; r < data.Rooms.Count; r++)
        {
            var rec = data.Rooms[r];
            int count = perRoomPortalRefs[r].Count;
            if (count > 255)
            {
                GD.PushWarning($"[PS1Godot] Room '{rec.Name}' has {count} connecting portals — clamping to 255 (PortalRefCount is u8).");
                count = 255;
            }
            rec.FirstPortalRef = runningPortalRef;
            rec.PortalRefCount = (byte)count;
            for (int i = 0; i < count; i++)
                data.RoomPortalRefs.Add(perRoomPortalRefs[r][i]);
            runningPortalRef += (ushort)count;
        }

        GD.Print($"[PS1Godot] Rooms: {rooms.Count} authored + 1 catch-all, " +
                 $"{data.Portals.Count} portals ({droppedPortals} dropped), " +
                 $"{data.RoomTriRefs.Count} tri-refs " +
                 $"({perRoomCatchAll.Count} catch-all), " +
                 $"{data.RoomCells.Count} cells, " +
                 $"{data.RoomPortalRefs.Count} portal-refs.");
    }

    private static void CollectRoomNodes(Node n, List<PS1Room> rooms, List<PS1PortalLink> portals)
    {
        if (n is PS1Room r) rooms.Add(r);
        if (n is PS1PortalLink pl) portals.Add(pl);
        foreach (var child in n.GetChildren())
            CollectRoomNodes(child, rooms, portals);
    }

    // Per-triangle metadata captured while assigning to an authored room,
    // used by the cell-subdivision pass to bucket tris and compute tight
    // per-cell AABBs.
    private readonly struct RoomTriAssignment
    {
        public RoomTriRefRecord Ref { get; init; }
        public Vector3 Min { get; init; }
        public Vector3 Max { get; init; }
        public Vector3 Centroid { get; init; }
    }

    // Rooms with fewer tris than this skip cell subdivision — the indirect
    // cost of a cell header starts to lose against just iterating the room
    // below this threshold on PS1-scale tri counts.
    private const int CellSubdivideMinTris = 12;

    // Pick a cell grid for a room's world-space extent. Aims for ~5 m per
    // cell; caps at 4 per axis so worst-case per-room cells stay under
    // 4×4×4=64 (well within the u8 CellCount cap of 255). Axes with
    // <=1 m extent get 1 division (no benefit to splitting).
    private static (int dx, int dy, int dz) PickCellDivisions(Vector3 extent)
    {
        const float TargetCellSize = 5.0f;
        const int MaxDiv = 4;
        int Div(float e) => e <= 1.0f ? 1 : Mathf.Clamp(Mathf.CeilToInt(e / TargetCellSize), 1, MaxDiv);
        return (Div(extent.X), Div(extent.Y), Div(extent.Z));
    }

    // Component-wise Vector3 min/max (Godot 4.x exposes Min/Max as instance
    // methods but not componentwise — these wrap Mathf.Min per-axis).
    private static Vector3 VecMin(Vector3 a, Vector3 b) =>
        new(Mathf.Min(a.X, b.X), Mathf.Min(a.Y, b.Y), Mathf.Min(a.Z, b.Z));
    private static Vector3 VecMax(Vector3 a, Vector3 b) =>
        new(Mathf.Max(a.X, b.X), Mathf.Max(a.Y, b.Y), Mathf.Max(a.Z, b.Z));

    // Transform the 8 corners of (VolumeSize, VolumeOffset) by the room's
    // GlobalTransform, then AABB them. `margin` expands the box outward by
    // that many world units on every axis (SplashEdit uses this to catch
    // doorway geometry on the room boundary).
    private static (Vector3 min, Vector3 max) ComputeRoomWorldBounds(PS1Room room, float margin)
    {
        Vector3 half = room.VolumeSize * 0.5f;
        var xform = room.GlobalTransform;
        Vector3 min = new(float.MaxValue, float.MaxValue, float.MaxValue);
        Vector3 max = new(float.MinValue, float.MinValue, float.MinValue);
        for (int i = 0; i < 8; i++)
        {
            var corner = room.VolumeOffset + new Vector3(
                (i & 1) != 0 ? half.X : -half.X,
                (i & 2) != 0 ? half.Y : -half.Y,
                (i & 4) != 0 ? half.Z : -half.Z);
            var w = xform * corner;
            min = new Vector3(Mathf.Min(min.X, w.X), Mathf.Min(min.Y, w.Y), Mathf.Min(min.Z, w.Z));
            max = new Vector3(Mathf.Max(max.X, w.X), Mathf.Max(max.Y, w.Y), Mathf.Max(max.Z, w.Z));
        }
        if (margin > 0f)
        {
            var m = new Vector3(margin, margin, margin);
            min -= m;
            max += m;
        }
        return (min, max);
    }

    private static bool PointInAabb(Vector3 p, Vector3 min, Vector3 max)
        => p.X >= min.X && p.X <= max.X
        && p.Y >= min.Y && p.Y <= max.Y
        && p.Z >= min.Z && p.Z <= max.Z;

    // Returns an index into data.Textures, or -1 if this surface has no
    // export-time texture (untextured → FlatColor path in PSXMesh).
    private static int ResolveSurfaceTexture(PS1MeshInstance pmi, int surfaceIdx,
        SceneData data, Dictionary<(string, PSXBPP), int> cache)
        => ResolveSurfaceTextureCore(pmi, pmi.BitDepth, surfaceIdx, data, cache);

    // Same material-resolution pipeline for a raw MeshInstance3D (no PS1
    // settings). Used when auto-detecting FBX-imported character meshes
    // under PS1Player. Defaults to 8bpp — the exporter's safe middle
    // ground — since we have no author-configured bit depth to follow.
    private static int ResolveSurfaceTextureRaw(MeshInstance3D mi, int surfaceIdx,
        SceneData data, Dictionary<(string, PSXBPP), int> cache)
        => ResolveSurfaceTextureCore(mi, PSXBPP.TEX_8BIT, surfaceIdx, data, cache);

    private static int ResolveSurfaceTextureCore(MeshInstance3D mi, PSXBPP bitDepth, int surfaceIdx,
        SceneData data, Dictionary<(string, PSXBPP), int> cache)
    {
        // Material resolution order (matches Godot's rendering precedence):
        //   1. MaterialOverride — replaces every surface's material.
        //   2. Surface override material — per-surface override on the instance.
        //   3. Mesh's surface material — the material baked into the mesh itself.
        Material? mat = mi.MaterialOverride;
        if (mat == null) mat = mi.GetSurfaceOverrideMaterial(surfaceIdx);
        if (mat == null && mi.Mesh != null) mat = mi.Mesh.SurfaceGetMaterial(surfaceIdx);

        var tex = ExtractAlbedoTexture(mat);
        if (tex == null) return -1;

        string path = tex.ResourcePath ?? "";
        if (string.IsNullOrEmpty(path))
        {
            GD.PushWarning($"[PS1Godot] {mi.Name}: surface {surfaceIdx} has an in-memory texture (no resource path) — can't dedup; exporting as untextured.");
            return -1;
        }

        var key = (path, bitDepth);
        if (cache.TryGetValue(key, out int existing)) return existing;

        Image? img = tex.GetImage();
        if (img == null || img.IsEmpty())
        {
            GD.PushWarning($"[PS1Godot] {mi.Name}: surface {surfaceIdx} texture '{path}' has no image data — skipping.");
            return -1;
        }

        // Godot 4.4+ imports many textures in a VRAM-compressed format
        // (S3TC / BPTC / etc). GetPixel and Resize fail on those. Decompress
        // to RGBA so the rest of the export path can read pixels and
        // resample if needed.
        if (img.IsCompressed())
        {
            img.Decompress();
        }

        // PSX texture pages are 64/128/256 pixels wide depending on bpp and
        // 256 tall. Auto-downscale anything bigger so authors don't have
        // to re-export source assets at PSX-friendly sizes — but warn so
        // they know quality was lost.
        const int MaxDim = 256;
        if (img.GetWidth() > MaxDim || img.GetHeight() > MaxDim)
        {
            int srcW = img.GetWidth();
            int srcH = img.GetHeight();
            int newW = Mathf.Min(srcW, MaxDim);
            int newH = Mathf.Min(srcH, MaxDim);
            // Nearest filter preserves the chunky PS1 look; bilinear would
            // add blur the GPU can't afford on hardware anyway.
            img.Resize(newW, newH, Image.Interpolation.Nearest);
            GD.PushWarning($"[PS1Godot] {mi.Name}: texture '{path}' was {srcW}×{srcH}; auto-downscaled to {newW}×{newH} (PSX VRAM page max). Author at {MaxDim}×{MaxDim} or smaller for predictable results.");
        }

        // 4bpp requires width divisible by 4, 8bpp by 2.
        int requiredMultiple = bitDepth switch
        {
            PSXBPP.TEX_4BIT => 4,
            PSXBPP.TEX_8BIT => 2,
            _ => 1,
        };
        if (img.GetWidth() % requiredMultiple != 0)
        {
            GD.PushError($"[PS1Godot] {mi.Name}: texture '{path}' width {img.GetWidth()} not a multiple of {requiredMultiple} (required for {bitDepth}). Skipping.");
            return -1;
        }

        var psxTex = PSXTexture.FromGodotImage(img, bitDepth, path);
        int idx = data.Textures.Count;
        data.Textures.Add(psxTex);
        cache[key] = idx;
        return idx;
    }

    private static Camera3D? FindFirstCamera(Node n)
    {
        if (n is Camera3D cam) return cam;
        foreach (var child in n.GetChildren())
        {
            var found = FindFirstCamera(child);
            if (found != null) return found;
        }
        return null;
    }
}
