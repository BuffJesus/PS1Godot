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
        }

        // Dedup cache keyed by (resourcePath, bitDepth). A texture used at two
        // different bit depths is intentionally two atlas entries.
        var textureCache = new Dictionary<(string path, PSXBPP bpp), int>();

        WalkAddMeshes(root, data, textureCache, luaCache);

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

            // MeshInstance3D child (typically a PS1MeshInstance) → player
            // avatar. Its local position is the offset from player origin;
            // runtime tracks and rotates it each frame (no Lua needed).
            // The mesh itself is picked up by WalkAddMeshes above, so we
            // just resolve its index here.
            var avatar = FindFirstOfType<MeshInstance3D>(player);
            if (avatar != null)
            {
                data.PlayerAvatarOffset = avatar.Position;
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

    private static void WalkAddMeshes(Node n, SceneData data,
        Dictionary<(string, PSXBPP), int> textureCache,
        Dictionary<string, int> luaCache)
    {
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
                SurfaceTextureIndices = surfaceTextureIndices,
                LuaFileIndex = ResolveLuaScript(pmi.Name, pmi.ScriptFile, data, luaCache),
            });

            EmitCollisionFor(pmi, objectIndex, data);
            EmitInteractableFor(pmi, objectIndex, data);

            // Stage 0 skinned-mesh detection: if this is a PS1SkinnedMesh,
            // log enough that the author can tell the exporter "saw" it.
            // Splashpack emission + bone matrix baking land in Phase 2
            // bullet 11 stages 1+. Until then the mesh still exports as a
            // static PS1MeshInstance (bind-pose only; animations ignored).
            if (pmi is PS1SkinnedMesh ps1Skin)
            {
                int boneCount = DetectBoneCount(ps1Skin);
                int animCount = ps1Skin.ClipNames != null ? ps1Skin.ClipNames.Length : 0;
                GD.Print(
                    $"[PS1Godot] PS1SkinnedMesh '{ps1Skin.Name}' detected: " +
                    $"{boneCount} bones, {animCount} authored clips, " +
                    $"sampling @ {ps1Skin.TargetFps} fps. " +
                    $"(stage 0 — splashpack skin-data emission pending; exporting as static mesh for now.)");
            }
        }
        else if (n is PS1TriggerBox tb && tb.Visible)
        {
            EmitTriggerBox(tb, data, luaCache);
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

    // Collect a canvas and its immediate PS1UIElement children. Nested
    // canvases / non-PS1UIElement children are ignored with a warning so
    // the scene tree stays honest.
    private static void EmitUICanvas(PS1UICanvas canvas, SceneData data)
    {
        string name = string.IsNullOrWhiteSpace(canvas.CanvasName)
            ? canvas.Name
            : canvas.CanvasName;

        var elements = new System.Collections.Generic.List<UIElementRecord>();
        foreach (var child in canvas.GetChildren())
        {
            if (child is not PS1UIElement el)
            {
                GD.PushWarning($"[PS1Godot] Canvas '{name}' has non-UI child '{child.Name}' — ignored.");
                continue;
            }
            string elName = string.IsNullOrWhiteSpace(el.ElementName) ? el.Name : el.ElementName;
            elements.Add(new UIElementRecord
            {
                Name = elName,
                Type = el.Type,
                VisibleOnLoad = el.VisibleOnLoad,
                X = (short)Mathf.Clamp(el.X, short.MinValue, short.MaxValue),
                Y = (short)Mathf.Clamp(el.Y, short.MinValue, short.MaxValue),
                W = (short)Mathf.Clamp(el.Width, short.MinValue, short.MaxValue),
                H = (short)Mathf.Clamp(el.Height, short.MinValue, short.MaxValue),
                ColorR = (byte)Mathf.Clamp((int)(el.Color.R * 255f), 0, 255),
                ColorG = (byte)Mathf.Clamp((int)(el.Color.G * 255f), 0, 255),
                ColorB = (byte)Mathf.Clamp((int)(el.Color.B * 255f), 0, 255),
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

    // Pull the albedo Texture2D out of either a StandardMaterial3D or a
    // ShaderMaterial carrying an `albedo_tex` shader parameter (our
    // ps1_default.tres uses that parameter name — see ps1.gdshader).
    // Returns null if the material type isn't recognized or has no
    // texture set.
    // Best-effort bone count for a PS1SkinnedMesh. Returns 0 if the
    // mesh isn't bound to a Skeleton3D or if the skeleton has no bones
    // yet (e.g., placeholder authoring). Used for logging in stage 0;
    // stage 1+ will walk the skeleton to emit real SkinData.
    private static int DetectBoneCount(PS1SkinnedMesh mesh)
    {
        if (mesh.Skeleton == null || mesh.Skeleton.IsEmpty) return 0;
        var node = mesh.GetNodeOrNull<Skeleton3D>(mesh.Skeleton);
        return node?.GetBoneCount() ?? 0;
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
    //   Position / CameraPosition: (x, y, z) meters → PSX fp12 with Y flipped.
    //   Rotation / CameraRotation: Euler degrees per axis → psyqo::Angle
    //             fp10 (4096 = 360°). PSX Y-down mirrors Godot X & Y
    //             rotation signs; Z rotation passes through.
    //   Active:   value.X → 0 or 1.
    private static (short, short, short) EncodeKeyframeValue(Vector3 v, PS1AnimationTrackType type, float gteScaling)
    {
        switch (type)
        {
            case PS1AnimationTrackType.Position:
            case PS1AnimationTrackType.CameraPosition:
            {
                float s = Mathf.Max(gteScaling, 0.0001f);
                int vx = Mathf.RoundToInt(v.X / s * 4096f);
                int vy = Mathf.RoundToInt(-v.Y / s * 4096f);
                int vz = Mathf.RoundToInt(v.Z / s * 4096f);
                return (ClampShort(vx), ClampShort(vy), ClampShort(vz));
            }
            case PS1AnimationTrackType.Rotation:
            case PS1AnimationTrackType.CameraRotation:
            {
                // 1 full turn (360°) = 4096 in fp10.
                const float DegToFp10 = 4096f / 360f;
                int rx = Mathf.RoundToInt(-v.X * DegToFp10);
                int ry = Mathf.RoundToInt(-v.Y * DegToFp10);
                int rz = Mathf.RoundToInt(v.Z * DegToFp10);
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
    // NavRegionSystem's point-in-poly requires CCW winding — we output corners
    // in (-X,-Z) → (+X,-Z) → (+X,+Z) → (-X,+Z) which is CCW in standard XZ.
    private static void EmitFlatNavRegion(Vector3 wMin, Vector3 wMax, SceneData data)
    {
        int fp(float godotValue) => PSXTrig.ConvertWorldToFixed12(godotValue / data.GteScaling);
        float floorY = 0.5f * (wMin.Y + wMax.Y);

        data.NavRegions.Add(new NavRegionRecord
        {
            VertsX = new[] { fp(wMin.X), fp(wMax.X), fp(wMax.X), fp(wMin.X) },
            VertsZ = new[] { fp(wMin.Z), fp(wMin.Z), fp(wMax.Z), fp(wMax.Z) },
            PlaneA = 0,
            PlaneB = 0,
            PlaneD = fp(-floorY),  // Y-down: Godot +Y → PSX -Y
        });
    }

    // Returns an index into data.Textures, or -1 if this surface has no
    // export-time texture (untextured → FlatColor path in PSXMesh).
    private static int ResolveSurfaceTexture(PS1MeshInstance pmi, int surfaceIdx,
        SceneData data, Dictionary<(string, PSXBPP), int> cache)
    {
        // Material resolution order (matches Godot's rendering precedence):
        //   1. MaterialOverride — replaces every surface's material.
        //   2. Surface override material — per-surface override on the instance.
        //   3. Mesh's surface material — the material baked into the mesh itself.
        Material? mat = pmi.MaterialOverride;
        if (mat == null) mat = pmi.GetSurfaceOverrideMaterial(surfaceIdx);
        if (mat == null && pmi.Mesh != null) mat = pmi.Mesh.SurfaceGetMaterial(surfaceIdx);

        var tex = ExtractAlbedoTexture(mat);
        if (tex == null) return -1;

        string path = tex.ResourcePath ?? "";
        if (string.IsNullOrEmpty(path))
        {
            GD.PushWarning($"[PS1Godot] {pmi.Name}: surface {surfaceIdx} has an in-memory texture (no resource path) — can't dedup; exporting as untextured.");
            return -1;
        }

        var key = (path, pmi.BitDepth);
        if (cache.TryGetValue(key, out int existing)) return existing;

        Image? img = tex.GetImage();
        if (img == null || img.IsEmpty())
        {
            GD.PushWarning($"[PS1Godot] {pmi.Name}: surface {surfaceIdx} texture '{path}' has no image data — skipping.");
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
            GD.PushWarning($"[PS1Godot] {pmi.Name}: texture '{path}' was {srcW}×{srcH}; auto-downscaled to {newW}×{newH} (PSX VRAM page max). Author at {MaxDim}×{MaxDim} or smaller for predictable results.");
        }

        // 4bpp requires width divisible by 4, 8bpp by 2.
        int requiredMultiple = pmi.BitDepth switch
        {
            PSXBPP.TEX_4BIT => 4,
            PSXBPP.TEX_8BIT => 2,
            _ => 1,
        };
        if (img.GetWidth() % requiredMultiple != 0)
        {
            GD.PushError($"[PS1Godot] {pmi.Name}: texture '{path}' width {img.GetWidth()} not a multiple of {requiredMultiple} (required for {pmi.BitDepth}). Skipping.");
            return -1;
        }

        var psxTex = PSXTexture.FromGodotImage(img, pmi.BitDepth, path);
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
