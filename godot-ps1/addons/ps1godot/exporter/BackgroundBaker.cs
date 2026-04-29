#if TOOLS
using System.IO;
using System.Threading.Tasks;
using Godot;

namespace PS1Godot.Exporter;

// Offscreen-renders the active scene from a Camera3D's POV and saves
// the result as a PNG suitable for use as a pre-rendered background
// (Resident Evil / FFVII style). See ROADMAP Phase 4 stretch
// "Pre-rendered backgrounds".
//
// Flow:
//   1. Build a SubViewport at the target resolution, share World3D so
//      the SubViewport's camera sees the same 3D content as the editor.
//   2. Clone the source camera's transform + projection into a render
//      Camera3D inside the SubViewport.
//   3. Wait for a single render frame.
//   4. Read the SubViewport's texture as an Image, save as PNG.
//   5. Dispose.
//
// The render is "what you see in the editor's free 3D view from this
// camera's exact POV" — not what the PSX will eventually render. The
// PSX runtime applies vertex snap, 5-bit quantization, ordered dither,
// and affine texture warping that the editor doesn't simulate (the
// preview shader gets close but isn't a render-target operation).
// That's the point: pre-rendered BGs *should* look better than the
// runtime — the whole technique compensates for PSX render limits by
// shipping a high-quality 2D image and only rendering 3D characters
// over it.
public static class BackgroundBaker
{
    // Native PSX-friendly resolution. 256×240 fits one VRAM page (256×256)
    // with 16 vertical pixels of headroom for the loader UI to overlay
    // a font/scrollbar without colliding with the BG. Scenes that ship
    // at 320×240 (e.g. monitor.tscn) can pass that explicitly.
    public const int DefaultWidth  = 256;
    public const int DefaultHeight = 240;

    public static string DefaultBackgroundsDir => "res://assets/backgrounds/";

    // Bake the camera's POV to a PNG. Returns the absolute path on disk
    // (or null on failure with the error already pushed via GD.PushError).
    // `host` is the editor-side Node used to drive the await — typically
    // the EditorPlugin itself.
    public static async Task<string?> BakeAsync(
        Node host,
        Camera3D sourceCam,
        int width  = DefaultWidth,
        int height = DefaultHeight,
        string? outAbsPath = null)
    {
        if (!GodotObject.IsInstanceValid(sourceCam))
        {
            GD.PushError("[PS1Godot] BackgroundBaker: source camera is not valid.");
            return null;
        }

        // Resolve output path. Default: res://assets/backgrounds/<scene>_<cam>.png
        if (string.IsNullOrEmpty(outAbsPath))
        {
            outAbsPath = ResolveDefaultPath(sourceCam);
        }
        string outDir = Path.GetDirectoryName(outAbsPath)!;
        Directory.CreateDirectory(outDir);

        // Mute the PSX preview shader before rendering. The bake should
        // capture *Godot's* native shading — high-quality colors that
        // ship to PSX as a texture, NOT the runtime's PSX-shaded
        // appearance. Two reasons:
        //   1. modulate_scale=2.0 is the PSYQo vertex-color convention
        //      where 0.5 means "neutral" and 1.0 means "double". Vertex
        //      colors at 0.5 get drawn correctly on PSX hardware, but if
        //      the bake doubles them, the saved PNG ends up over-bright.
        //      Then PSX samples that already-bright texture and the BG
        //      visibly washes out (or saturates to white where vertex
        //      colors hit 0.8 from a Vertex Lighting bake).
        //   2. The 5-bit quantize + Bayer dither shipped in Phase L3 are
        //      meant for the *editor preview* of how runtime geometry
        //      will look — they shouldn't bake into a BG texture, since
        //      the runtime samples that texture as-is (no shader pass).
        // Saved values get restored on the way out so the editor's main
        // viewport returns to the PSX preview look once the bake completes.
        var matDefault = ResourceLoader.Load<ShaderMaterial>("res://addons/ps1godot/shaders/ps1_default.tres");
        var matSkinned = ResourceLoader.Load<ShaderMaterial>("res://addons/ps1godot/shaders/ps1_skinned.tres");
        var saved = SaveBakeShaderState(matDefault, matSkinned);
        ApplyBakeShaderState(matDefault, matSkinned);

        // Auto-bake Vertex Lighting + AO before the render if the scene
        // has any Light3D and any PS1MeshInstance. Lets a one-click
        // "Bake Background" replace the three-step
        //   1) Bake Vertex Lighting
        //   2) Bake Vertex AO
        //   3) Bake Background
        // workflow that's easy to forget (the failure mode is silent —
        // BG renders white because BakedColors is empty). Overwrites
        // any existing BakedColors on those meshes — author edits are
        // preserved by removing the lights from the scene.
        AutoBakeLightingAndAo();

        // BakedColors live on the PS1MeshInstance NODE, not on the
        // Mesh resource — VertexLightingBaker / VertexAOBaker stamp
        // them as a per-instance override that the splashpack writer
        // reads at export. Godot's own renderer doesn't see them, so
        // the SubViewport draws every vertex at COLOR=1,1,1 (white)
        // and the bake captures blown-out silhouettes regardless of
        // shader modulate.
        //
        // Fix: for each visible PS1MeshInstance with non-empty
        // BakedColors, build an ArrayMesh copy with the bake stamped
        // into the COLOR array, swap it in for the duration of the
        // bake, restore afterward. Lets Godot's vertex pipeline
        // route BakedColors through the shader's COLOR attribute
        // exactly the way the PSX runtime will at draw time.
        var meshSwap = ApplyBakedColorMeshes(host);
        if (meshSwap.Count > 0)
        {
            GD.Print($"[PS1Godot] BG baker: applied BakedColors to {meshSwap.Count} mesh(es) for the bake render.");
        }

        // Confirmation print — if you don't see this when running the
        // baker, Godot is still on the old C# DLL (memory pin
        // project_godot_dll_hot_reload). Close + reopen the editor.
        GD.Print($"[PS1Godot] BG baker: muting PSX shader during bake (modulate {saved.DefaultModulate} → 1, quantize bits {saved.DefaultBits} → 0, dither {saved.DefaultDither} → false).");

        var subviewport = new SubViewport
        {
            Name = "PS1GodotBgBaker_Viewport",
            Size = new Vector2I(width, height),
            // OwnWorld3D = false → SubViewport inherits its parent's
            // World3D once we add it to the editor tree. We then
            // explicitly set World3D to the source camera's so the
            // render sees the same scene the author placed the camera in.
            OwnWorld3D = false,
            Disable3D = false,
            TransparentBg = false,
            RenderTargetClearMode = SubViewport.ClearMode.Always,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Once,
            // Anti-alias off: we want the PSX-like crispness; if authors
            // want smoother BGs they can run the PNG through a downscale
            // step in their image editor of choice.
            Msaa3D = Viewport.Msaa.Disabled,
        };
        host.AddChild(subviewport);
        subviewport.World3D = sourceCam.GetWorld3D();

        var renderCam = new Camera3D
        {
            Name = "PS1GodotBgBaker_Camera",
            // GlobalTransform copied wholesale so the bake matches the
            // editor camera placement exactly.
            Current = true,
            Fov = sourceCam.Fov,
            Near = sourceCam.Near,
            Far = sourceCam.Far,
            Projection = sourceCam.Projection,
            HOffset = sourceCam.HOffset,
            VOffset = sourceCam.VOffset,
            KeepAspect = sourceCam.KeepAspect,
        };
        subviewport.AddChild(renderCam);
        renderCam.GlobalTransform = sourceCam.GlobalTransform;

        // Two ProcessFrame waits: one for the SubViewport to set up its
        // render target, one for the actual render to land. A single
        // wait sometimes captures a black frame on cold starts because
        // RenderTargetUpdateMode.Once latches before the first draw.
        var tree = host.GetTree();
        await host.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        // Re-arm Once because the previous frame consumed the trigger.
        subviewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
        await host.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        // RenderingServer.FrameAfterDrawn signals the GPU has actually
        // produced the frame; without it the get_image() readback can
        // see the prior frame's stale contents.
        await host.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

        Image? image = subviewport.GetTexture()?.GetImage();

        // Restore mesh swaps + PSX preview shader state regardless of
        // whether the capture succeeded. Done before any early return so
        // the editor viewport never gets stuck without modulate or with
        // its meshes swapped to the bake-temporary ArrayMesh copies.
        RestoreBakedColorMeshes(meshSwap);
        RestoreBakeShaderState(matDefault, matSkinned, saved);

        if (image == null || image.IsEmpty())
        {
            GD.PushError(
                $"[PS1Godot] Background bake produced an empty image (camera '{sourceCam.Name}').\n" +
                "  Why: SubViewport rendered for 2 frames but the readback came back blank.\n" +
                "  Fix: usually means the camera doesn't see any geometry in the scene. " +
                "Frame the camera so the scene's meshes are in view. If you're certain meshes " +
                "are visible, try Tools → Build / Launch → Build psxsplash runtime first to " +
                "rule out a stale plugin DLL, then re-run the bake.");
            subviewport.QueueFree();
            return null;
        }

        // Strip alpha — PSX BGs are opaque. Saves a CLUT slot too once
        // the runtime quantizes 4bpp.
        if (image.GetFormat() != Image.Format.Rgb8)
        {
            image.Convert(Image.Format.Rgb8);
        }

        var err = image.SavePng(outAbsPath);
        subviewport.QueueFree();

        if (err != Error.Ok)
        {
            GD.PushError($"[PS1Godot] BackgroundBaker: SavePng failed ({err}) for '{outAbsPath}'.");
            return null;
        }

        GD.Print($"[PS1Godot] Baked background: {width}×{height} → {outAbsPath}");
        // Tell Godot's import system the new file exists so the next
        // .tscn save can reference it without a manual reimport.
        EditorInterface.Singleton?.GetResourceFilesystem()?.Scan();
        return outAbsPath;
    }

    // Saved shader-uniform snapshot used by the bake mute/restore pair.
    // Each tuple holds (modulate_scale, preview_quantize_bits, preview_dither_enabled).
    private record struct BakeShaderState(
        Variant DefaultModulate, Variant DefaultBits, Variant DefaultDither,
        Variant SkinnedModulate, Variant SkinnedBits, Variant SkinnedDither);

    private static BakeShaderState SaveBakeShaderState(ShaderMaterial? def, ShaderMaterial? skin)
    {
        Variant Get(ShaderMaterial? m, string name) =>
            m == null ? new Variant() : m.GetShaderParameter(name);
        return new BakeShaderState(
            DefaultModulate: Get(def, "modulate_scale"),
            DefaultBits:     Get(def, "preview_quantize_bits"),
            DefaultDither:   Get(def, "preview_dither_enabled"),
            SkinnedModulate: Get(skin, "modulate_scale"),
            SkinnedBits:     Get(skin, "preview_quantize_bits"),
            SkinnedDither:   Get(skin, "preview_dither_enabled"));
    }

    private static void ApplyBakeShaderState(ShaderMaterial? def, ShaderMaterial? skin)
    {
        // modulate_scale=1 → no PSYQo 2× doubling on vertex colors.
        // preview_quantize_bits=0 → disable the 5-bit channel quantize.
        // preview_dither_enabled=false → disable Bayer dither.
        // Together these give us a clean Godot-native render to capture.
        foreach (var m in new[] { def, skin })
        {
            if (m == null) continue;
            m.SetShaderParameter("modulate_scale", 1.0f);
            m.SetShaderParameter("preview_quantize_bits", 0);
            m.SetShaderParameter("preview_dither_enabled", false);
        }
    }

    private static void RestoreBakeShaderState(ShaderMaterial? def, ShaderMaterial? skin, BakeShaderState saved)
    {
        if (def != null)
        {
            def.SetShaderParameter("modulate_scale", saved.DefaultModulate);
            def.SetShaderParameter("preview_quantize_bits", saved.DefaultBits);
            def.SetShaderParameter("preview_dither_enabled", saved.DefaultDither);
        }
        if (skin != null)
        {
            skin.SetShaderParameter("modulate_scale", saved.SkinnedModulate);
            skin.SetShaderParameter("preview_quantize_bits", saved.SkinnedBits);
            skin.SetShaderParameter("preview_dither_enabled", saved.SkinnedDither);
        }
    }

    // ── Auto-bake Vertex Lighting + AO ────────────────────────────────
    //
    // Walk the edited scene root, collect every Light3D + every
    // PS1MeshInstance, and run VertexLightingBaker.Bake +
    // VertexAOBaker.Bake on the meshes. Skip silently if either list
    // is empty — author may have manually painted BakedColors, or the
    // scene may not need lighting (e.g. an Unlit signpost).
    private static void AutoBakeLightingAndAo()
    {
        var sceneRoot = EditorInterface.Singleton?.GetEditedSceneRoot();
        if (sceneRoot == null) return;

        var lights = new System.Collections.Generic.List<Node>();
        var meshes = new System.Collections.Generic.List<Node>();
        CollectLightsAndMeshes(sceneRoot, lights, meshes);
        if (lights.Count == 0 || meshes.Count == 0) return;

        var lit = VertexLightingBaker.Bake(sceneRoot, meshes);
        // AO settings match the menu-item defaults — see PS1GodotPlugin.OnBakeVertexAO.
        var ao = VertexAOBaker.Bake(sceneRoot, meshes, new VertexAOBaker.Options
        {
            RayCount = 12,
            MaxRayDistance = 0.5f,
            Strength = 0.5f,
            Bias = 0.001f,
        });
        GD.Print(
            $"[PS1Godot] BG baker: auto-baked Vertex Lighting ({lit.MeshesBaked} mesh(es), " +
            $"{lights.Count} light(s)) + AO ({ao.MeshesBaked} mesh(es), {ao.RaysCast} rays). " +
            $"Authors who want to preserve hand-tuned BakedColors should remove scene lights " +
            $"or skip this menu item and use the standalone Bake Vertex Lighting + Background actions separately.");
    }

    private static void CollectLightsAndMeshes(Node n,
        System.Collections.Generic.List<Node> lights,
        System.Collections.Generic.List<Node> meshes)
    {
        if (n is Light3D l && l.Visible) lights.Add(n);
        if (n is PS1MeshInstance pmi && pmi.Visible) meshes.Add(n);
        foreach (var child in n.GetChildren()) CollectLightsAndMeshes(child, lights, meshes);
    }

    // ── BakedColors → mesh.COLOR swap (transient, bake-only) ──────────
    //
    // Godot renders meshes using the Mesh resource's vertex arrays. The
    // PS1MeshInstance.BakedColors property is a per-instance override
    // the splashpack writer reads — it's NOT applied to the Mesh's
    // COLOR array, so Godot's pipeline can't see it.
    //
    // For the bake to capture the lighting/AO the author saw in the
    // editor, we duplicate each PS1MeshInstance's Mesh into an
    // ArrayMesh, write BakedColors into the COLOR slot of every
    // surface, and swap pmi.Mesh to that copy. Restore after the
    // capture so editor state is untouched.
    private record struct MeshSwapEntry(MeshInstance3D Node, Mesh Original);

    private static System.Collections.Generic.List<MeshSwapEntry> ApplyBakedColorMeshes(Node host)
    {
        var swapped = new System.Collections.Generic.List<MeshSwapEntry>();
        var sceneRoot = EditorInterface.Singleton?.GetEditedSceneRoot();
        if (sceneRoot == null) return swapped;

        WalkAndSwap(sceneRoot, swapped);
        return swapped;
    }

    private static void WalkAndSwap(Node n, System.Collections.Generic.List<MeshSwapEntry> swapped)
    {
        if (n is PS1MeshInstance pmi && pmi.Visible && pmi.Mesh != null
            && pmi.BakedColors != null && pmi.BakedColors.Length > 0)
        {
            var rebuilt = BakedColorMeshHelper.BuildMeshWithColors(pmi.Mesh, pmi.BakedColors);
            if (rebuilt != null)
            {
                swapped.Add(new MeshSwapEntry(pmi, pmi.Mesh));
                pmi.Mesh = rebuilt;
            }
        }
        foreach (var child in n.GetChildren()) WalkAndSwap(child, swapped);
    }

    private static void RestoreBakedColorMeshes(System.Collections.Generic.List<MeshSwapEntry> swapped)
    {
        foreach (var entry in swapped)
        {
            if (GodotObject.IsInstanceValid(entry.Node))
            {
                entry.Node.Mesh = entry.Original;
            }
        }
        swapped.Clear();
    }

    private static string ResolveDefaultPath(Camera3D cam)
    {
        string sceneName = "scene";
        var root = EditorInterface.Singleton?.GetEditedSceneRoot();
        if (root != null && !string.IsNullOrEmpty(root.SceneFilePath))
        {
            sceneName = Path.GetFileNameWithoutExtension(root.SceneFilePath);
        }
        string camName = string.IsNullOrEmpty(cam.Name) ? "cam" : cam.Name.ToString();
        // Sanitise — strip path-unfriendly chars.
        camName = camName.Replace('/', '_').Replace('\\', '_').Replace(':', '_');
        sceneName = sceneName.Replace('/', '_').Replace('\\', '_').Replace(':', '_');

        string fileName = $"{sceneName}_{camName}.png";
        string resPath = DefaultBackgroundsDir + fileName;
        return ProjectSettings.GlobalizePath(resPath);
    }
}
#endif
