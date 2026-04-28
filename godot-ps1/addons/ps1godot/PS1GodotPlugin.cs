#if TOOLS
using Godot;
using PS1Godot.Effects;
using PS1Godot.Exporter;
using PS1Godot.Tools;
using PS1Godot.UI;

namespace PS1Godot;

[Tool]
public partial class PS1GodotPlugin : EditorPlugin
{
    // The dock panel is the canonical surface for these actions. The
    // Project > Tools menu entries are mirrors — kept for keyboard-driven
    // users and discoverability via Godot's command palette.
    private const string SubdivideMenuLabel = "PS1Godot: Subdivide Selected Mesh (×4 tris)";
    private const string AnalyzeTexturesMenuLabel = "PS1Godot: Analyze Texture Compliance";
    private const string ToggleCompositorMenuLabel = "PS1Godot: Toggle PS1 Preview on Selected Camera";
    private const string ExportEmptySplashpackMenuLabel = "PS1Godot: Export Splashpack";
    private const string BuildPsxsplashMenuLabel = "PS1Godot: Build psxsplash runtime";
    private const string LaunchEmulatorMenuLabel = "PS1Godot: Launch in PCSX-Redux";
    private const string RunOnPsxMenuLabel = "PS1Godot: Run on PSX (export + build + launch)";
    private const string ConvertMeshToPS1MenuLabel = "PS1Godot: Convert selected MeshInstance3D to PS1MeshInstance";
    private const string AddSkinnedTestMenuLabel = "PS1Godot: Add Skinned Test Mesh (bullet 11 test asset)";
    private const string GenerateFontBitmapMenuLabel = "PS1Godot: Generate bitmap for selected PS1UIFontAsset";
    private const string RunMidiTestsMenuLabel = "PS1Godot: Run MIDI Serializer Tests";
    private const string RunLuaRewriterTestsMenuLabel = "PS1Godot: Run Lua Decimal Rewriter Tests";
    private const string RegenLuaStubsMenuLabel = "PS1Godot: Regenerate Lua API stubs";
    private const string RunStubGenTestsMenuLabel = "PS1Godot: Run Lua API Stub Generator Tests";
    private const string FrameModelMenuLabel = "PS1Godot: Frame Selected Model in Viewport";
    private const string ApplyBlenderMetadataMenuLabel = "PS1Godot: Apply Blender Metadata Sidecars";
    private const string WriteBlenderMetadataMenuLabel = "PS1Godot: Write Blender Metadata Sidecars";
    private const string PopulateMaterialsMenuLabel    = "PS1Godot: Populate PS1MaterialMetadata for Selected";
    private const string InferDefaultsMenuLabel        = "PS1Godot: Infer PS1 Defaults for Selected";
    private const string SendToBlenderMenuLabel        = "PS1Godot: Send to Blender";
    private const string BakeVertexLightingMenuLabel   = "PS1Godot: Bake Vertex Lighting from Scene Lights";
    private const string EditMeshInBlenderMenuLabel    = "PS1Godot: Edit Mesh in Blender";
    private const string BakeVertexAOMenuLabel         = "PS1Godot: Bake Vertex AO into BakedColors";

    // Where extracted .glb files land. Matches the Blender add-on's
    // default `asset_subdir` so the round-trip back overwrites the
    // same path and Godot's import scanner picks up the change.
    private const string DefaultBlenderMeshDir = "res://ps1godot_assets/meshes/";

    // Default sidecar dir matches the Blender add-on default
    // (tools/blender-addon/.../properties.py: "ps1godot_assets/blender_sources").
    // Authors who relocate it on the Blender side should pass their
    // override via OnApplyBlenderMetadata's argument once we add UI.
    private const string DefaultBlenderSidecarDir = "res://ps1godot_assets/blender_sources/";

    private PS1TriggerBoxGizmo? _triggerBoxGizmo;
    private PS1GodotDock? _dock;
    private PS1UICanvasEditor? _uiCanvasEditor;
    private EditorSyntaxHighlighter? _luaHighlighter;
    private PS1TexturePreviewInspector? _texturePreviewInspector;
    private PS1VRAMViewerDock? _vramViewerDock;

    public override void _EnterTree()
    {
        AddToolMenuItem(SubdivideMenuLabel, Callable.From(OnSubdivide));
        AddToolMenuItem(AnalyzeTexturesMenuLabel, Callable.From(OnAnalyzeTextures));
        AddToolMenuItem(ToggleCompositorMenuLabel, Callable.From(OnToggleCompositor));
        AddToolMenuItem(ExportEmptySplashpackMenuLabel, Callable.From(OnExportEmptySplashpack));
        AddToolMenuItem(BuildPsxsplashMenuLabel, Callable.From(OnBuildPsxsplash));
        AddToolMenuItem(LaunchEmulatorMenuLabel, Callable.From(OnLaunchEmulator));
        AddToolMenuItem(RunOnPsxMenuLabel, Callable.From(OnRunOnPsx));
        AddToolMenuItem(ConvertMeshToPS1MenuLabel, Callable.From(OnConvertMeshToPS1));
        AddToolMenuItem(AddSkinnedTestMenuLabel, Callable.From(OnAddSkinnedTestMesh));
        AddToolMenuItem(GenerateFontBitmapMenuLabel, Callable.From(OnGenerateFontBitmap));
        AddToolMenuItem(RunMidiTestsMenuLabel, Callable.From(OnRunMidiTests));
        AddToolMenuItem(RunLuaRewriterTestsMenuLabel, Callable.From(OnRunLuaRewriterTests));
        AddToolMenuItem(RegenLuaStubsMenuLabel, Callable.From(OnRegenLuaStubs));
        AddToolMenuItem(RunStubGenTestsMenuLabel, Callable.From(OnRunStubGenTests));
        AddToolMenuItem(FrameModelMenuLabel, Callable.From(OnFrameSelectedModel));
        AddToolMenuItem(ApplyBlenderMetadataMenuLabel, Callable.From(OnApplyBlenderMetadata));
        AddToolMenuItem(WriteBlenderMetadataMenuLabel, Callable.From(OnWriteBlenderMetadata));
        AddToolMenuItem(PopulateMaterialsMenuLabel,    Callable.From(OnPopulateMaterials));
        AddToolMenuItem(InferDefaultsMenuLabel,        Callable.From(OnInferDefaults));
        AddToolMenuItem(SendToBlenderMenuLabel,        Callable.From(OnSendToBlender));
        AddToolMenuItem(BakeVertexLightingMenuLabel,   Callable.From(OnBakeVertexLighting));
        AddToolMenuItem(EditMeshInBlenderMenuLabel,    Callable.From(OnEditMeshInBlender));
        AddToolMenuItem(BakeVertexAOMenuLabel,         Callable.From(OnBakeVertexAO));

        _triggerBoxGizmo = new PS1TriggerBoxGizmo();
        AddNode3DGizmoPlugin(_triggerBoxGizmo);

        _dock = new PS1GodotDock();
        _dock.RunOnPsxRequested += OnRunOnPsx;
        _dock.BuildPsxsplashRequested += OnBuildPsxsplash;
        _dock.LaunchEmulatorRequested += OnLaunchEmulator;
        _dock.AnalyzeTexturesRequested += OnAnalyzeTextures;
        _dock.ExportOnlyRequested += OnExportEmptySplashpack;
        // AddControlToDock is marked [Obsolete] in Godot 4.7-dev in favor
        // of AddDock(EditorDock), which isn't stable yet. The old API still
        // works; suppressing the warning so warnings-as-errors builds
        // pass. Migrate once 4.7 stabilizes or we pin to 4.4 per ROADMAP.
#pragma warning disable CS0618 // Obsolete: AddControlToDock / RemoveControlFromDocks
        AddControlToDock(DockSlot.RightBr, _dock);
#pragma warning restore CS0618

        // Refresh dock stats whenever the edited scene changes. Also
        // push an initial read so the dock isn't blank on startup.
        SceneChanged += OnSceneChanged;
        OnSceneChanged(EditorInterface.Singleton.GetEditedSceneRoot());

        // PS1 UI canvas editor — bottom-panel tab showing a WYSIWYG
        // preview of the selected PS1UICanvas. Selecting any
        // PS1UIElement picks up its owning canvas too.
        _uiCanvasEditor = new PS1UICanvasEditor();
#pragma warning disable CS0618 // Obsolete: AddControlToBottomPanel / RemoveControlFromBottomPanel — migrate once 4.7 EditorDock API stabilizes (matches AddControlToDock site below).
        AddControlToBottomPanel(_uiCanvasEditor, "PS1 UI");
#pragma warning restore CS0618
        EditorInterface.Singleton.GetSelection().SelectionChanged += OnEditorSelectionChanged;
        OnEditorSelectionChanged();

        // Lua syntax highlighting. The PS1LuaSyntaxHighlighter CLASS is
        // defined in ps1lua.gdextension (C++) because C#-subclassed
        // EditorSyntaxHighlighters aren't picked up by Godot's ScriptEditor.
        // But we register the INSTANCE here because ScriptEditor isn't
        // constructed yet when the GDExtension fires its EDITOR-level
        // init — this _EnterTree is the earliest reliable moment.
        if (ClassDB.ClassExists("PS1LuaSyntaxHighlighter"))
        {
            _luaHighlighter = ClassDB.Instantiate("PS1LuaSyntaxHighlighter")
                .As<EditorSyntaxHighlighter>();
            if (_luaHighlighter != null)
            {
                EditorInterface.Singleton.GetScriptEditor()
                    .RegisterSyntaxHighlighter(_luaHighlighter);
            }
        }

        // Inspector plugin — adds a "PSX Preview (quantized)" panel to
        // any PS1Sky / PS1UIElement(Image) so authors see how the
        // texture quantizes at the chosen BitDepth without exporting.
        _texturePreviewInspector = new PS1TexturePreviewInspector();
        AddInspectorPlugin(_texturePreviewInspector);

        // VRAM viewer — bottom-panel tab that visualises the packed
        // 1024×512 layout after each export (atlases, textures, CLUTs,
        // reserved framebuffer + font regions).
        _vramViewerDock = new PS1VRAMViewerDock();
#pragma warning disable CS0618 // Obsolete: AddControlToBottomPanel — see AddControlToDock site above.
        AddControlToBottomPanel(_vramViewerDock, "PS1 VRAM");
#pragma warning restore CS0618

        GD.Print("[PS1Godot] Plugin enabled. F5 = Run on PSX (export + build + launch).");
    }

    // Hook Godot's Play button (F5 / F6 / Shift+F5) into the Run-on-PSX
    // pipeline. The PS1 game runs in PCSX-Redux, not in a Godot window,
    // so we intercept here and route to OnRunOnPsx instead of letting
    // Godot's scene runner spin up.
    //
    // Returning false is Godot's documented way to say "build step
    // failed, don't proceed with running" — which suppresses the scene
    // runner exactly as we want, but also pops a "Project run failed"
    // toast. The toast is harmless (PCSX-Redux is already launching by
    // the time it appears) and there's no cleaner override hook in
    // EditorPlugin's API today; the GD.Print below tells authors what's
    // actually happening so the toast doesn't read as a real error.
    public override bool _Build()
    {
        GD.Print("[PS1Godot] F5: routing Run to PSX (export + build + launch). " +
                 "Ignore any 'Project run failed' toast Godot pops — that's its " +
                 "stock response to a custom run handler. PCSX-Redux is launching " +
                 "with your game; check its window for the actual run.");
        OnRunOnPsx();
        return false;
    }

    private void OnEditorSelectionChanged()
    {
        if (_uiCanvasEditor == null) return;
        PS1UICanvas? canvas = null;
        Node? selectedUINode = null;
        foreach (var n in EditorInterface.Singleton.GetSelection().GetSelectedNodes())
        {
            if (n is PS1UICanvas c) { canvas = c; selectedUINode = c; break; }
            // Walk up the tree to find an owning PS1UICanvas — works
            // for any PS1UI* descendant (element, HBox, VBox, etc.)
            // now that containers can nest arbitrarily deep.
            Node? walker = n;
            while (walker != null)
            {
                if (walker is PS1UICanvas parent) { canvas = parent; break; }
                walker = walker.GetParent();
            }
            if (canvas != null) { selectedUINode = n; break; }
        }
        _uiCanvasEditor.SetSelection(canvas, selectedUINode);
    }

    private void OnSceneChanged(Node sceneRoot)
    {
        if (_dock == null) return;
        var stats = UI.SceneStats.Compute(sceneRoot);
        _dock.ApplySceneStats(stats);
    }

    public override void _ExitTree()
    {
        RemoveToolMenuItem(SubdivideMenuLabel);
        RemoveToolMenuItem(AnalyzeTexturesMenuLabel);
        RemoveToolMenuItem(ToggleCompositorMenuLabel);
        RemoveToolMenuItem(ExportEmptySplashpackMenuLabel);
        RemoveToolMenuItem(BuildPsxsplashMenuLabel);
        RemoveToolMenuItem(LaunchEmulatorMenuLabel);
        RemoveToolMenuItem(RunOnPsxMenuLabel);
        RemoveToolMenuItem(ConvertMeshToPS1MenuLabel);
        RemoveToolMenuItem(AddSkinnedTestMenuLabel);
        RemoveToolMenuItem(GenerateFontBitmapMenuLabel);
        RemoveToolMenuItem(RunMidiTestsMenuLabel);
        RemoveToolMenuItem(FrameModelMenuLabel);
        RemoveToolMenuItem(ApplyBlenderMetadataMenuLabel);
        RemoveToolMenuItem(WriteBlenderMetadataMenuLabel);
        RemoveToolMenuItem(PopulateMaterialsMenuLabel);
        RemoveToolMenuItem(InferDefaultsMenuLabel);
        RemoveToolMenuItem(SendToBlenderMenuLabel);
        RemoveToolMenuItem(BakeVertexLightingMenuLabel);
        RemoveToolMenuItem(EditMeshInBlenderMenuLabel);
        RemoveToolMenuItem(BakeVertexAOMenuLabel);

        SceneChanged -= OnSceneChanged;
        EditorInterface.Singleton.GetSelection().SelectionChanged -= OnEditorSelectionChanged;

        if (_luaHighlighter != null)
        {
            EditorInterface.Singleton.GetScriptEditor()
                .UnregisterSyntaxHighlighter(_luaHighlighter);
            _luaHighlighter = null;
        }

        if (_uiCanvasEditor != null)
        {
#pragma warning disable CS0618 // Obsolete — see AddControlToBottomPanel site above.
            RemoveControlFromBottomPanel(_uiCanvasEditor);
#pragma warning restore CS0618
            _uiCanvasEditor.QueueFree();
            _uiCanvasEditor = null;
        }

        if (_dock != null)
        {
#pragma warning disable CS0618 // Obsolete — see AddControlToDock site above.
            RemoveControlFromDocks(_dock);
#pragma warning restore CS0618
            _dock.QueueFree();
            _dock = null;
        }

        if (_triggerBoxGizmo != null)
        {
            RemoveNode3DGizmoPlugin(_triggerBoxGizmo);
            _triggerBoxGizmo = null;
        }

        if (_texturePreviewInspector != null)
        {
            RemoveInspectorPlugin(_texturePreviewInspector);
            _texturePreviewInspector = null;
        }

        if (_vramViewerDock != null)
        {
#pragma warning disable CS0618 // Obsolete — see AddControlToBottomPanel site above.
            RemoveControlFromBottomPanel(_vramViewerDock);
#pragma warning restore CS0618
            _vramViewerDock.QueueFree();
            _vramViewerDock = null;
        }

        GD.Print("[PS1Godot] Plugin disabled.");
    }

    // ─── In-editor wrappers around scripts/*.cmd ──────────────────────────
    //
    // The .cmd files in repo-root scripts/ stay as the source of truth so they
    // also work from a terminal. These menu items just shell out to them so the
    // user never has to leave Godot to test on PSX.

    private static string RepoRoot()
    {
        // ProjectSettings.GlobalizePath("res://") = .../godot-ps1/. Repo root is one up.
        var projectDir = ProjectSettings.GlobalizePath("res://").TrimEnd('/', '\\');
        return System.IO.Path.GetFullPath(System.IO.Path.Combine(projectDir, ".."));
    }

    private static int RunScript(string scriptRelative, string label)
    {
        string full = System.IO.Path.Combine(RepoRoot(), scriptRelative).Replace('/', '\\');
        if (!System.IO.File.Exists(full))
        {
            GD.PushError($"[PS1Godot] {label}: script not found at {full}");
            return -1;
        }
        var output = new Godot.Collections.Array();
        // /c → run and exit. Output is captured and printed to the editor log so
        // the user sees compile errors / missing-toolchain warnings without
        // opening a terminal.
        int code = OS.Execute("cmd.exe", new[] { "/c", full }, output, /* readStderr */ true);
        foreach (var line in output)
            GD.Print(line.AsString().TrimEnd('\r', '\n'));
        return code;
    }

    private void OnRunMidiTests()
    {
        MidiSerializerTests.RunAll();
    }

    private void OnRunLuaRewriterTests()
    {
        LuaDecimalRewriterTests.RunAll();
    }

    private void OnRegenLuaStubs()
    {
        LuaApiStubGenerator.Run();
    }

    private void OnRunStubGenTests()
    {
        LuaApiStubGeneratorTests.RunAll();
    }

    private void OnBuildPsxsplash()
    {
        GD.Print("[PS1Godot] Building psxsplash runtime…");
        int code = RunScript("scripts/build-psxsplash.cmd", "Build psxsplash");
        if (code == 0) GD.Print("[PS1Godot] Build OK.");
        else GD.PushError($"[PS1Godot] Build failed (exit {code}). See log above.");
    }

    private void OnLaunchEmulator()
    {
        // launch-emulator.cmd does NOT block — PCSX-Redux opens in its own
        // window and we get control back. CreateProcess is preferred over
        // Execute so the editor doesn't freeze waiting on the emulator.
        string script = System.IO.Path.Combine(RepoRoot(), "scripts", "launch-emulator.cmd").Replace('/', '\\');
        if (!System.IO.File.Exists(script))
        {
            GD.PushError($"[PS1Godot] launch-emulator.cmd not found at {script}");
            return;
        }
        GD.Print("[PS1Godot] Launching PCSX-Redux…");
        OS.CreateProcess("cmd.exe", new[] { "/c", script });
    }

    private void OnRunOnPsx()
    {
        // The full one-click loop. Stops at the first failure so the user
        // sees a clear "do this next" rather than a cascade of errors.
        //
        // Two run modes, picked by what the export produced:
        //   - PCdrv (default): no .xa sidecars in build/. Boot the
        //     PCdrv-loader runtime with the host filesystem mounted.
        //   - CD-ROM ISO: any scene_*.xa exists. XA-ADPCM streaming
        //     needs the disc bus, so we build a CDROM-loader runtime,
        //     run mkpsxiso, and boot via -iso. PCdrv won't see XA.
        OnExportEmptySplashpack();
        string buildDir = System.IO.Path.Combine(RepoRoot(), "godot-ps1", "build");
        if (HasAnyXaSidecar(buildDir))
        {
            RunIsoMode(buildDir);
        }
        else
        {
            RunPcdrvMode(buildDir);
        }
    }

    private static bool HasAnyXaSidecar(string buildDir)
    {
        if (!System.IO.Directory.Exists(buildDir)) return false;
        foreach (var path in System.IO.Directory.EnumerateFiles(buildDir, "scene_*.xa"))
        {
            // Treat a present file (even zero-byte) as "XA route is in
            // play"; the writer only emits this file when at least one
            // clip's psxavenc conversion succeeded.
            if (new System.IO.FileInfo(path).Length > 0) return true;
        }
        return false;
    }

    private void RunPcdrvMode(string buildDir)
    {
        bool hasPsExe = System.IO.File.Exists(System.IO.Path.Combine(buildDir, "psxsplash.ps-exe"));
        bool hasElf   = System.IO.File.Exists(System.IO.Path.Combine(buildDir, "psxsplash.elf"));
        if (!hasPsExe && !hasElf)
        {
            GD.Print("[PS1Godot] psxsplash PCdrv binary missing — building first…");
            OnBuildPsxsplash();
        }
        OnLaunchEmulator();
    }

    private void RunIsoMode(string buildDir)
    {
        GD.Print("[PS1Godot] XA-routed clips detected — switching to CD-ROM ISO run mode.");

        // 1. Ensure the CDROM-loader runtime exists AND is newer than
        //    every psxsplash-main source file. The build script does
        //    `make clean` so re-running it costs ~30 s; we only do it
        //    when sources actually changed.
        string cdromExe = System.IO.Path.Combine(buildDir, "psxsplash-cdrom.ps-exe");
        bool needsBuild = !System.IO.File.Exists(cdromExe) || IsCdromBuildStale(cdromExe);
        if (needsBuild)
        {
            string reason = !System.IO.File.Exists(cdromExe)
                ? "CDROM-loader runtime missing"
                : "CDROM-loader runtime older than psxsplash sources";
            GD.Print($"[PS1Godot] {reason} — rebuilding (this clears the PCdrv build cache)…");
            int code = RunScript("scripts/build-psxsplash-cdrom.cmd", "Build psxsplash CDROM");
            if (code != 0)
            {
                GD.PushError($"[PS1Godot] CDROM build failed (exit {code}). Falling back to PCdrv (XA clips will be silent).");
                RunPcdrvMode(buildDir);
                return;
            }
        }

        // 2. Build the ISO via tools/build_iso/build_iso.py. Pass the
        //    CDROM-loader runtime explicitly so the script doesn't pick
        //    up the (potentially stale) PCdrv .ps-exe.
        GD.Print("[PS1Godot] Building ISO via tools/build_iso/build_iso.py…");
        int isoCode = RunBuildIso(buildDir, cdromExe);
        if (isoCode != 0)
        {
            GD.PushError($"[PS1Godot] ISO build failed (exit {isoCode}). See log above.");
            return;
        }

        // 3. Launch PCSX-Redux with the ISO mounted.
        string isoLauncher = System.IO.Path.Combine(RepoRoot(), "scripts", "launch-emulator-iso.cmd").Replace('/', '\\');
        if (!System.IO.File.Exists(isoLauncher))
        {
            GD.PushError($"[PS1Godot] launch-emulator-iso.cmd missing at {isoLauncher}");
            return;
        }
        GD.Print("[PS1Godot] Launching PCSX-Redux (CD-ROM ISO mode)…");
        OS.CreateProcess("cmd.exe", new[] { "/c", isoLauncher });
    }

    // True if any psxsplash-main/src/*.{cpp,hh,h} or the Makefile is
    // newer than the cached CDROM binary. Walking src/ takes <50 ms
    // even on a cold filesystem; checking on every Run-on-PSX click is
    // cheap, and the false-negative cost (running with stale runtime)
    // is high — silent XA misbehavior, hard to debug.
    private static bool IsCdromBuildStale(string cdromExe)
    {
        var binStamp = System.IO.File.GetLastWriteTimeUtc(cdromExe);
        string srcDir = System.IO.Path.Combine(RepoRoot(), "psxsplash-main", "src");
        if (!System.IO.Directory.Exists(srcDir)) return false;
        try
        {
            foreach (var path in System.IO.Directory.EnumerateFiles(srcDir, "*.*", System.IO.SearchOption.AllDirectories))
            {
                string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
                if (ext != ".cpp" && ext != ".hh" && ext != ".h") continue;
                if (System.IO.File.GetLastWriteTimeUtc(path) > binStamp) return true;
            }
            // Makefile changes (LOADER guards, sources list) also invalidate.
            string makefile = System.IO.Path.Combine(RepoRoot(), "psxsplash-main", "Makefile");
            if (System.IO.File.Exists(makefile)
                && System.IO.File.GetLastWriteTimeUtc(makefile) > binStamp) return true;
        }
        catch { /* unreadable file — fail open, treat as not stale */ }
        return false;
    }

    // Run tools/build_iso/build_iso.py with explicit psxexec override so
    // the ISO uses the CDROM build, not the PCdrv build that lives next
    // to it as psxsplash.ps-exe.
    private static int RunBuildIso(string buildDir, string psxexecPath)
    {
        string script = System.IO.Path.Combine(RepoRoot(), "tools", "build_iso", "build_iso.py").Replace('/', '\\');
        if (!System.IO.File.Exists(script))
        {
            GD.PushError($"[PS1Godot] build_iso.py missing at {script}");
            return -1;
        }
        var output = new Godot.Collections.Array();
        int code = OS.Execute("python",
            new[] { script, "--build-dir", buildDir, "--psxexec", psxexecPath, "--out", System.IO.Path.Combine(buildDir, "game.bin") },
            output, /* readStderr */ true);
        foreach (var line in output)
            GD.Print(line.AsString().TrimEnd('\r', '\n'));
        return code;
    }

    // Last export's aggregate validation summary (mesh dedup + texture +
    // audio + UV linter warnings). Reset at the start of each
    // OnExportEmptySplashpack pass; fed to the dock once the multi-scene
    // export is done.
    private LastExportSummary? _lastExportSummary;

    private void OnExportEmptySplashpack()
    {
        // psxsplash's FileLoader expects "scene_<index>.splashpack" (and .vram, .spu)
        // siblings under PCdrv's root. The launch-emulator.cmd script points PCdrv at
        // godot-ps1/build/, so that's where these files have to land.
        string buildDir = ProjectSettings.GlobalizePath("res://build/");
        System.IO.Directory.CreateDirectory(buildDir);

        var sceneRoot = EditorInterface.Singleton.GetEditedSceneRoot();
        if (sceneRoot == null)
        {
            GD.PushError("[PS1Godot] No scene open — open a .tscn whose root is a PS1Scene before exporting.");
            return;
        }

        _lastExportSummary = new LastExportSummary();

        // Reset the VRAM viewer dock — clears stale snapshots from any
        // previous export run so the scene picker only lists scenes
        // produced by the current pass.
        _vramViewerDock?.BeginExportRun();

        // Export the open scene as scene_0, then iterate PS1Scene.SubScenes
        // (if any) to emit scene_1, scene_2, … in declared order. Each
        // sub-scene is instantiated, walked, exported, then disposed so its
        // PSX texture/buffer state doesn't bleed into the next.
        ExportOneScene(sceneRoot, 0);

        if (sceneRoot is PS1Scene rootPs1 && rootPs1.SubScenes != null)
        {
            for (int i = 0; i < rootPs1.SubScenes.Count; i++)
            {
                var packed = rootPs1.SubScenes[i];
                if (packed == null)
                {
                    GD.PushWarning($"[PS1Godot] SubScenes[{i}] is null — skipped (Scene.Load({i + 1}) won't have a target).");
                    continue;
                }
                Node? sub = packed.Instantiate();
                if (sub == null)
                {
                    GD.PushError($"[PS1Godot] SubScenes[{i}] failed to instantiate.");
                    continue;
                }

                // Sub-scenes need to live in the SceneTree before
                // GlobalTransform / GlobalPosition return anything but
                // identity (Godot gates them on is_inside_tree()). Park the
                // orphan under the SceneTree's root for the duration of
                // the export, then yank it back out — keeps the editor's
                // own scene unchanged.
                var tree = GetTree();
                var tempHost = new Node { Name = $"__ps1godot_export_subscene_{i + 1}" };
                tree.Root.AddChild(tempHost);
                tempHost.AddChild(sub);
                try
                {
                    ExportOneScene(sub, i + 1);
                }
                finally
                {
                    tempHost.QueueFree();
                }
            }
        }

        // Push the aggregated validation summary to the dock so the
        // author sees the headline issue count without scanning the
        // Output panel. Tooltip on the dock label expands to per-category
        // subtotals + the worst mesh-cleanup names.
        _dock?.ApplyLastExportSummary(_lastExportSummary);
    }

    private void ExportOneScene(Node sceneRoot, int sceneIndex)
    {
        string buildDir = ProjectSettings.GlobalizePath("res://build/");
        string absPath = System.IO.Path.Combine(buildDir, $"scene_{sceneIndex}.splashpack");

        Exporter.MeshLinter.ResetForScene();
        var sceneData = Exporter.SceneCollector.FromRoot(sceneRoot, sceneRoot.SceneFilePath ?? "");
        GD.Print($"[PS1Godot] Scene[{sceneIndex}]: {(string.IsNullOrEmpty(sceneData.ScenePath) ? "(unsaved)" : sceneData.ScenePath)}");
        GD.Print($"[PS1Godot]   PS1MeshInstance objects found: {sceneData.Objects.Count}");

        int totalTris = 0;
        foreach (var obj in sceneData.Objects)
        {
            totalTris += obj.Mesh.Triangles.Count;
            // Static-batch synthetic nodes are parentless. Godot's
            // GlobalPosition returns Vector3.Zero for not-in-tree nodes,
            // hiding the actual anchor in this diagnostic. Use the local
            // Position in that case so the printed coords match what gets
            // written to the splashpack.
            var p = obj.Node.IsInsideTree() ? obj.Node.GlobalPosition : obj.Node.Position;
            // PS1-specific fields only exist on PS1MeshInstance; auto-detected
            // raw MeshInstance3D avatars (FBX characters under PS1Player) get
            // 8bpp + no collision defaults, reported here as "(auto)".
            string bpp = obj.Node is PS1MeshInstance pmi1 ? pmi1.BitDepth.ToString() : "8bit(auto)";
            string coll = obj.Node is PS1MeshInstance pmi2 ? pmi2.Collision.ToString() : "None(auto)";
            GD.Print($"[PS1Godot]     - {obj.Node.Name}  pos=({p.X:F2},{p.Y:F2},{p.Z:F2})  bpp={bpp}  collide={coll}  → {obj.Mesh.Triangles.Count} tris");
        }
        GD.Print($"[PS1Godot]   Total triangles: {totalTris}");

        // v25 texture validation: print per-asset row + warn on oversized
        // sources, 16bpp gameplay textures, and small cutouts that should
        // be 4bpp. Print-only; no behavioral change.
        int textureWarnings = Exporter.TextureValidationReport.EmitForScene(sceneData, sceneIndex);

        // Audio validation: per-clip row + warn on big SPU clips that
        // should route XA, big resident loops, dangling XA payloads.
        int audioWarnings = Exporter.AudioValidationReport.EmitForScene(sceneData, sceneIndex);

        // Animation validation: per-track + per-skin-clip rows + warn
        // on dead tracks, oversized clips, high fps/bone counts.
        int animWarnings = Exporter.AnimationLinter.EmitForScene(sceneData, sceneIndex);

        // UV linter: warn on any vertex UV outside [0, 1]. PSX rasteriser
        // doesn't wrap or clamp — out-of-range UVs sample neighbouring
        // VRAM data as garbage. Editor's wrapping sampler hides this.
        int uvDirty = Exporter.MeshLinter.EmitForScene(sceneIndex);

        // Decal stack validator: WARN on UI canvases stacking more
        // translucent quads than the PSX GPU can blend at acceptable
        // fillrate cost (>6 overlapping). Folded into the texture
        // warning bucket since the dock's summary line groups
        // texture-tier validators together.
        textureWarnings += Exporter.DecalValidationReport.EmitForScene(sceneData, sceneIndex);

        // Aggregate this scene's results into the run-wide summary the
        // dock reads after OnExportEmptySplashpack returns. Mesh-dedup
        // counts come from sceneData.MeshDedup which the SceneCollector
        // populated during FromRoot. uvDirtyNames is captured from
        // MeshLinter on its way out so the headline can name a real
        // failing mesh instead of just counting.
        _lastExportSummary?.Add(
            sceneData, textureWarnings, audioWarnings, animWarnings, uvDirty,
            new System.Collections.Generic.List<string>(Exporter.MeshLinter.LastDirtyMeshNames));

        try
        {
            Exporter.SplashpackWriter.Write(absPath, sceneData);
            long packBytes = new System.IO.FileInfo(absPath).Length;
            long vramBytes = new System.IO.FileInfo(System.IO.Path.ChangeExtension(absPath, ".vram")).Length;
            long spuBytes = new System.IO.FileInfo(System.IO.Path.ChangeExtension(absPath, ".spu")).Length;
            GD.Print($"[PS1Godot] Splashpack written: {absPath}");
            GD.Print($"[PS1Godot]   .splashpack = {packBytes}B");
            GD.Print($"[PS1Godot]   .vram       = {vramBytes}B  (cap {UI.SceneStats.VramBudgetBytes}B)");
            GD.Print($"[PS1Godot]   .spu        = {spuBytes}B  (cap {UI.SceneStats.SpuBudgetBytes}B)");

            // Push the packed VRAM layout to the dock for visual review.
            // SceneData.Packer is non-null at this point (SplashpackWriter
            // ran the pack pass before serialising), so the snapshot
            // captures the same coords the .vram file holds.
            _vramViewerDock?.ApplySnapshot(UI.VramSnapshot.Capture(sceneData, sceneIndex));

            // Cap warnings: explicit PushWarning when a bus is over its
            // hardware-usable cap so authors don't have to do the math.
            // Cross-references the dock's red bars but works even when the
            // dock isn't open. Real cap values live in SceneStats so this
            // file and the dock can't drift.
            if (vramBytes > UI.SceneStats.VramBudgetBytes)
            {
                GD.PushWarning($"[PS1Godot] Scene[{sceneIndex}] VRAM OVER BUDGET: {vramBytes} B vs {UI.SceneStats.VramBudgetBytes} B cap (over by {vramBytes - UI.SceneStats.VramBudgetBytes} B). See texture report rows for the biggest offenders.");
            }
            if (spuBytes > UI.SceneStats.SpuBudgetBytes)
            {
                GD.PushWarning($"[PS1Godot] Scene[{sceneIndex}] SPU OVER BUDGET: {spuBytes} B vs {UI.SceneStats.SpuBudgetBytes} B cap (over by {spuBytes - UI.SceneStats.SpuBudgetBytes} B). Mark long ambient/dialog clips Route=XA (Phase 3) or trim duration/sample rate.");
            }
        }
        catch (System.Exception e)
        {
            GD.PushError($"[PS1Godot] Scene[{sceneIndex}] export failed: {e.Message}");
        }
    }

    // One-click "make this mesh exportable as a PS1 mesh." Use case: an
    // FBX import that contains a regular MeshInstance3D somewhere in its
    // sub-tree. After toggling Editable Children on the import, the user
    // selects the body MeshInstance3D and runs this — a sibling
    // PS1MeshInstance is created on the scene root with the same Mesh +
    // Transform + material override. Names it "Player" if no Player node
    // exists yet (lets the test_logger.lua Player.GetPosition tracker
    // pick it up automatically).
    //
    // Doesn't touch the source node — destructive cleanup is on the
    // author. Once they delete the imported FBX scene, only the new
    // PS1MeshInstance remains.
    private void OnConvertMeshToPS1()
    {
        var sceneRoot = EditorInterface.Singleton.GetEditedSceneRoot();
        if (sceneRoot == null)
        {
            GD.PushWarning("[PS1Godot] No scene open. Open the .tscn you want to add the PS1MeshInstance to.");
            return;
        }

        var selected = EditorInterface.Singleton.GetSelection().GetSelectedNodes();
        bool playerExists = sceneRoot.FindChild("Player", recursive: true, owned: false) != null;
        int converted = 0;
        Node? lastCreated = null;

        foreach (var n in selected)
        {
            if (n is not MeshInstance3D src) continue;
            if (src.Mesh == null)
            {
                GD.PushWarning($"[PS1Godot] '{src.Name}' has no Mesh — skipping.");
                continue;
            }

            var ps1 = new PS1MeshInstance
            {
                Mesh = src.Mesh,
                MaterialOverride = src.MaterialOverride,
                Collision = PS1MeshInstance.CollisionKind.None,
                BitDepth = PSXBPP.TEX_8BIT,
            };

            // Pick a name — first conversion claims "Player" if it's free,
            // subsequent ones use the source name.
            if (!playerExists && converted == 0)
            {
                ps1.Name = "Player";
                playerExists = true;
            }
            else
            {
                ps1.Name = src.Name + "_PS1";
            }

            // Add as a child of the scene root so it's saved with the
            // .tscn (not stuck inside an imported sub-scene).
            sceneRoot.AddChild(ps1);
            ps1.Owner = sceneRoot;
            // Match world-space placement of the source.
            ps1.GlobalTransform = src.GlobalTransform;

            // Carry over per-surface override materials too — useful when
            // the FBX has a couple of distinct materials per surface.
            int surfaceCount = src.Mesh.GetSurfaceCount();
            for (int s = 0; s < surfaceCount; s++)
            {
                var ovr = src.GetSurfaceOverrideMaterial(s);
                if (ovr != null) ps1.SetSurfaceOverrideMaterial(s, ovr);
            }

            converted++;
            lastCreated = ps1;
            GD.Print($"[PS1Godot] Converted '{src.Name}' → '{ps1.Name}' (PS1MeshInstance, {surfaceCount} surface(s))");
        }

        if (converted == 0)
        {
            GD.PushWarning("[PS1Godot] Nothing converted. Select one or more MeshInstance3D nodes in the Scene tree (toggle 'Editable Children' on imported FBX scenes first to expose internal meshes).");
            return;
        }

        // Make the inspector follow the created node so the author can
        // immediately tweak BitDepth / FlatColor / Collision, etc.
        if (lastCreated != null)
        {
            EditorInterface.Singleton.GetSelection().Clear();
            EditorInterface.Singleton.GetSelection().AddNode(lastCreated);
        }
        GD.Print($"[PS1Godot] {converted} mesh(es) converted. Original FBX nodes are unchanged — delete them once you're sure the PS1MeshInstance has what you need.");
    }

    private void OnAddSkinnedTestMesh()
    {
        var sceneRoot = EditorInterface.Singleton.GetEditedSceneRoot();
        if (sceneRoot == null)
        {
            GD.PushWarning("[PS1Godot] No scene open. Open the .tscn you want to drop the test mesh into.");
            return;
        }

        // Parent choice: selected Node3D if one is picked, else the scene root.
        Node3D parent = sceneRoot as Node3D ?? (Node3D)sceneRoot;
        var selected = EditorInterface.Singleton.GetSelection().GetSelectedNodes();
        if (selected.Count == 1 && selected[0] is Node3D sel)
        {
            parent = sel;
        }

        var test = Tools.SkinnedTestBuilder.Build();
        parent.AddChild(test);
        // Recursively set Owner so the nodes serialize into the .tscn.
        SetOwnerRecursive(test, sceneRoot);

        EditorInterface.Singleton.GetSelection().Clear();
        EditorInterface.Singleton.GetSelection().AddNode(test);

        EditorInterface.Singleton.MarkSceneAsUnsaved();
        GD.Print("[PS1Godot] Added 'SkinnedTest' — a 2-bone cylinder with a 'wave' animation. " +
                 "Export to see the stage-1 skin block emit; stage 2 will wire the animation to PSX.");
    }

    // Rasterize the selected PS1UIFontAsset via the C++ PS1FontRasterizer
    // GDExtension class. Picks the resource from inspector focus first,
    // falling back to the FileSystem selection. Result is saved back to
    // disk so the Generated fields persist.
    private void OnGenerateFontBitmap()
    {
        var inspected = EditorInterface.Singleton.GetInspector().GetEditedObject();
        if (inspected is PS1UIFontAsset asset)
        {
            if (Tools.PS1FontGenerator.Populate(asset))
            {
                ResourceSaver.Save(asset);
                EditorInterface.Singleton.GetResourceFilesystem().Scan();
            }
            return;
        }

        // Fallback — walk the FileSystem selection for the first .tres
        // that loads as a PS1UIFontAsset. Nicer than demanding the
        // inspector be focused.
        var fs = EditorInterface.Singleton.GetResourceFilesystem();
        var selectedPaths = EditorInterface.Singleton.GetSelectedPaths();
        foreach (var p in selectedPaths)
        {
            if (!p.EndsWith(".tres", System.StringComparison.OrdinalIgnoreCase)) continue;
            if (ResourceLoader.Load(p) is PS1UIFontAsset a)
            {
                if (Tools.PS1FontGenerator.Populate(a))
                {
                    ResourceSaver.Save(a, p);
                    fs.Scan();
                }
                return;
            }
        }

        GD.PushWarning("[PS1Godot] Generate font bitmap: select a PS1UIFontAsset resource " +
                       "(click into its inspector, or select the .tres in FileSystem).");
    }

    private static void SetOwnerRecursive(Node n, Node owner)
    {
        n.Owner = owner;
        foreach (var child in n.GetChildren())
        {
            SetOwnerRecursive(child, owner);
        }
    }

    private void OnFrameSelectedModel()
    {
        var selected = EditorInterface.Singleton.GetSelection().GetSelectedNodes();
        Node3D? target = null;
        foreach (var n in selected)
        {
            if (n is Node3D n3) { target = n3; break; }
        }
        if (target == null)
        {
            GD.PushWarning("[PS1Godot] Frame Model: select a Node3D (the model you want to frame) in the Scene dock first.");
            return;
        }

        // Default apparent width for now: 128 px (about 40% of the 320-wide
        // screen — a decent splash / inventory-preview size). No modal
        // dialog — author edits the value in code and re-runs, or copies
        // the Lua snippet and edits at runtime.
        const int apparentWidthPx = 128;

        var r = PS1ModelFramer.Compute(target, apparentWidthPx);
        if (r.Radius <= 0f)
        {
            GD.PushWarning($"[PS1Godot] Frame Model: '{target.Name}' has no MeshInstance3D descendants to measure.");
            return;
        }

        // If the scene has exactly one Camera3D, position it — gives the
        // author instant viewport feedback. Multiple cameras would be
        // ambiguous; zero means no viewport camera to move.
        var root = EditorInterface.Singleton.GetEditedSceneRoot();
        Camera3D? activeCam = root == null ? null : FindSingleCamera3D(root);

        GD.Print("");
        GD.Print($"[PS1Godot] Framed '{target.Name}': radius={r.Radius:0.##}m " +
                 $"→ camera distance={r.Distance:0.##}m, projection H={r.ProjectionH}.");
        GD.Print($"[PS1Godot] Godot-space camera position: {r.CameraPosition}");
        GD.Print("[PS1Godot] Lua snippet (paste into a scene script's onSceneCreationEnd):");
        GD.Print(PS1ModelFramer.BuildLuaSnippet(r));

        if (activeCam != null)
        {
            activeCam.GlobalPosition = r.CameraPosition;
            activeCam.GlobalRotation = r.CameraRotationRadians;
            GD.Print($"[PS1Godot] Moved Camera3D '{activeCam.Name}' to the computed transform.");
        }
        else
        {
            GD.Print("[PS1Godot] No single Camera3D in scene — left viewport alone.");
        }
    }

    private static Camera3D? FindSingleCamera3D(Node root)
    {
        Camera3D? found = null;
        int count = 0;
        void Walk(Node n)
        {
            if (n is Camera3D c) { found = c; count++; }
            foreach (var child in n.GetChildren()) Walk(child);
        }
        Walk(root);
        return count == 1 ? found : null;
    }

    private void OnSubdivide()
    {
        var selected = EditorInterface.Singleton.GetSelection().GetSelectedNodes();
        int touched = 0;
        int trisBefore = 0, trisAfter = 0;

        foreach (var n in selected)
        {
            if (n is MeshInstance3D mi && mi.Mesh != null)
            {
                int before = PS1MeshSubdivider.CountTriangles(mi.Mesh);
                mi.Mesh = PS1MeshSubdivider.Subdivide(mi.Mesh, 1);
                int after = PS1MeshSubdivider.CountTriangles(mi.Mesh);
                trisBefore += before;
                trisAfter += after;
                touched++;
            }
        }

        if (touched == 0)
            GD.PushWarning("[PS1Godot] No MeshInstance3D selected. Click a mesh node in the Scene dock first.");
        else
            GD.Print($"[PS1Godot] Subdivided {touched} mesh(es): {trisBefore} → {trisAfter} triangles.");
    }

    private void OnToggleCompositor()
    {
        var selected = EditorInterface.Singleton.GetSelection().GetSelectedNodes();
        int attached = 0, detached = 0;

        foreach (var n in selected)
        {
            if (n is not Camera3D cam) continue;

            if (cam.Compositor != null)
            {
                cam.Compositor = null;
                detached++;
            }
            else
            {
                var comp = new Compositor();
                var effects = new Godot.Collections.Array<CompositorEffect>
                {
                    new PS1PixelizeEffect(),
                };
                comp.CompositorEffects = effects;
                cam.Compositor = comp;
                attached++;
            }
        }

        if (attached + detached == 0)
            GD.PushWarning("[PS1Godot] No Camera3D selected.");
        else
            GD.Print($"[PS1Godot] PS1 preview attached to {attached}, detached from {detached} camera(s).");
    }

    private const int VramBudgetBytes = 1024 * 1024; // 1 MB PS1 VRAM

    private void OnAnalyzeTextures()
    {
        var paths = PS1TextureAnalyzer.FindProjectImages();
        if (paths.Count == 0)
        {
            GD.Print("[PS1Godot] No textures found in project.");
            return;
        }

        GD.Print($"[PS1Godot] Analyzing {paths.Count} texture(s):");
        int ok = 0, warn = 0, fail = 0;
        int totalVram = 0;

        foreach (var path in paths)
        {
            // Go through ResourceLoader so we analyze the *imported* texture
            // (what actually ships), not the raw source file. Also silences
            // the "use Image resource for export" warning that LoadFromFile
            // produces.
            Image? img = null;
            if (ResourceLoader.Load(path) is Texture2D tex)
                img = tex.GetImage();

            if (img == null || img.IsEmpty())
            {
                GD.PushWarning($"  [FAIL] {path} — could not load as image.");
                fail++;
                continue;
            }

            var r = PS1TextureAnalyzer.Analyze(img);
            totalVram += r.VramBytes;

            string tag = r.Verdict switch
            {
                PS1TextureAnalyzer.Verdict.Clut4bpp => "OK  ",
                PS1TextureAnalyzer.Verdict.Clut8bpp => "OK  ",
                PS1TextureAnalyzer.Verdict.Direct16bpp => "WARN",
                PS1TextureAnalyzer.Verdict.TooBig => "FAIL",
                _ => "?   "
            };
            switch (r.Verdict)
            {
                case PS1TextureAnalyzer.Verdict.Clut4bpp:
                case PS1TextureAnalyzer.Verdict.Clut8bpp: ok++; break;
                case PS1TextureAnalyzer.Verdict.Direct16bpp: warn++; break;
                case PS1TextureAnalyzer.Verdict.TooBig: fail++; break;
            }

            GD.Print($"  [{tag}] {path} — {r.Width}×{r.Height}, {r.UniqueColors} colors, {r.VramBytes}B. {r.Note}");
        }

        float pct = 100.0f * totalVram / VramBudgetBytes;
        GD.Print($"[PS1Godot] Summary: {ok} OK, {warn} WARN, {fail} FAIL. " +
                 $"Estimated VRAM {totalVram}B / {VramBudgetBytes}B ({pct:F1}% of budget).");
    }

    // ── Slot C / Phase 2: apply Blender JSON sidecars to the open scene ─
    //
    // Reads `<mesh_id>.ps1meshmeta.json` files from the conventional
    // sidecar directory and applies their metadata (MeshRole / DrawPhase /
    // ShadingMode / AlphaMode / AtlasGroup / Residency / stable IDs) to
    // matching PS1MeshInstance / PS1MeshGroup nodes in the active scene.
    //
    // Author trigger only — never silently mutates scene state. Author
    // saves the .tscn afterwards to persist; round-trip back to Blender
    // happens via the Phase 8 export-back operator (not yet shipped).
    private void OnApplyBlenderMetadata()
    {
        var sceneRoot = EditorInterface.Singleton.GetEditedSceneRoot();
        if (sceneRoot == null)
        {
            GD.PushError("[PS1Godot] No scene open — open a .tscn before applying Blender metadata.");
            return;
        }

        string sidecarDir = ProjectSettings.GlobalizePath(DefaultBlenderSidecarDir);
        GD.Print($"[PS1Godot] Applying Blender sidecars from {sidecarDir}…");

        var result = Exporter.BlenderMetadataReader.Apply(sidecarDir, sceneRoot);

        GD.Print(
            $"[PS1Godot] Sidecars: found={result.SidecarsFound}, " +
            $"applied={result.Applied}, unmatched={result.Unmatched}, " +
            $"version-skipped={result.VersionSkip}, parse-error={result.ParseError}.");
        foreach (var name in result.UnmatchedNames)
        {
            GD.PushWarning($"[PS1Godot]   unmatched sidecar: {name}");
        }
        if (result.Applied > 0)
        {
            GD.Print("[PS1Godot] Save the .tscn to persist the new metadata.");
        }
    }

    // ── Phase 8: write sidecars OUT to the Blender side ─────────────
    //
    // Symmetric counterpart to OnApplyBlenderMetadata. Walks every
    // PS1MeshInstance / PS1MeshGroup in the active scene and emits
    // one <mesh_id>.ps1meshmeta.json per node, matching the Blender
    // add-on's wire format byte-for-byte. Auto-generates asset_id +
    // mesh_id on first export and writes them back to the node — save
    // the .tscn afterwards to persist the new IDs.
    //
    // After this runs, the Blender side's import operator can pull the
    // values into Object PropertyGroups so the .blend reflects what
    // the Godot author did.
    private void OnWriteBlenderMetadata()
    {
        var sceneRoot = EditorInterface.Singleton.GetEditedSceneRoot();
        if (sceneRoot == null)
        {
            GD.PushError("[PS1Godot] No scene open — open a .tscn before writing Blender sidecars.");
            return;
        }

        string sidecarDir = ProjectSettings.GlobalizePath(DefaultBlenderSidecarDir);
        GD.Print($"[PS1Godot] Writing Blender sidecars to {sidecarDir}…");

        var result = Exporter.BlenderMetadataWriter.WriteScene(sceneRoot, sidecarDir);

        GD.Print(
            $"[PS1Godot] Sidecars written: {result.Written} (skipped {result.Skipped}, " +
            $"new IDs auto-generated for {result.IdsGenerated} node(s), io-errors {result.IoErrors}).");
        foreach (var p in result.Paths)
        {
            GD.Print($"[PS1Godot]   {p}");
        }
        if (result.IdsGenerated > 0)
        {
            GD.Print("[PS1Godot] Save the .tscn to persist the new asset_id / mesh_id values.");
        }
    }

    // ── UX2: one-click PS1MaterialMetadata population ────────────────
    //
    // Walk the selected node's Material slots (or the children's, if a
    // PS1MeshGroup is selected) and append a PS1MaterialMetadata for
    // each unique slot whose name doesn't already have one. Authors
    // run this once per mesh after import, then fill in
    // texture_page_id / clut_id etc. instead of right-clicking → New
    // Resource per surface.
    private void OnPopulateMaterials()
    {
        var selected = EditorInterface.Singleton.GetSelection().GetSelectedNodes();
        if (selected.Count == 0)
        {
            GD.PushError("[PS1Godot] Populate Materials: select a PS1MeshInstance / PS1MeshGroup first.");
            return;
        }

        int touchedNodes = 0;
        int addedTotal   = 0;

        foreach (var n in selected)
        {
            int added = 0;
            if (n is PS1MeshInstance pmi)
            {
                added = PopulateForInstance(pmi);
            }
            else if (n is PS1MeshGroup pmg)
            {
                added = PopulateForGroup(pmg);
            }
            else
            {
                continue;
            }
            if (added > 0)
            {
                touchedNodes++;
                addedTotal += added;
                GD.Print($"[PS1Godot]   {n.Name}: +{added} PS1MaterialMetadata entry(s).");
            }
            else
            {
                GD.Print($"[PS1Godot]   {n.Name}: already populated, no change.");
            }
        }

        if (addedTotal == 0)
        {
            GD.Print("[PS1Godot] Populate Materials: nothing to add — every slot already has a PS1MaterialMetadata entry.");
        }
        else
        {
            GD.Print($"[PS1Godot] Populate Materials: added {addedTotal} entry(s) across {touchedNodes} node(s). Save the .tscn to persist.");
        }
    }

    private static int PopulateForInstance(PS1MeshInstance pmi)
    {
        if (pmi.Mesh == null) return 0;
        var existing = ExistingMaterialNames(pmi.Materials);
        int added = 0;
        for (int s = 0; s < pmi.Mesh.GetSurfaceCount(); s++)
        {
            var mat = pmi.GetSurfaceOverrideMaterial(s) ?? pmi.Mesh.SurfaceGetMaterial(s);
            if (mat == null) continue;
            string name = string.IsNullOrEmpty(mat.ResourceName) ? $"Surface {s}" : mat.ResourceName;
            if (!existing.Add(name)) continue;
            pmi.Materials.Add(new PS1MaterialMetadata { MaterialName = name, MaterialId = name });
            added++;
        }
        return added;
    }

    private static int PopulateForGroup(PS1MeshGroup pmg)
    {
        var existing = ExistingMaterialNames(pmg.Materials);
        int added = 0;
        AddFromDescendants(pmg, existing, pmg.Materials, ref added);
        return added;
    }

    private static void AddFromDescendants(Node n, System.Collections.Generic.HashSet<string> existing,
                                           Godot.Collections.Array<PS1MaterialMetadata> dest, ref int added)
    {
        if (n is MeshInstance3D mi && mi.Mesh != null)
        {
            for (int s = 0; s < mi.Mesh.GetSurfaceCount(); s++)
            {
                var mat = mi.GetSurfaceOverrideMaterial(s) ?? mi.Mesh.SurfaceGetMaterial(s);
                if (mat == null) continue;
                string name = string.IsNullOrEmpty(mat.ResourceName) ? $"{mi.Name}_Surface{s}" : mat.ResourceName;
                if (!existing.Add(name)) continue;
                dest.Add(new PS1MaterialMetadata { MaterialName = name, MaterialId = name });
                added++;
            }
        }
        foreach (var child in n.GetChildren())
        {
            AddFromDescendants(child, existing, dest, ref added);
        }
    }

    private static System.Collections.Generic.HashSet<string> ExistingMaterialNames(
        Godot.Collections.Array<PS1MaterialMetadata> metas)
    {
        var set = new System.Collections.Generic.HashSet<string>(System.StringComparer.Ordinal);
        foreach (var m in metas)
        {
            if (m == null) continue;
            string key = !string.IsNullOrEmpty(m.MaterialName) ? m.MaterialName : m.MaterialId;
            if (!string.IsNullOrEmpty(key)) set.Add(key);
        }
        return set;
    }

    // ── UX3: smart default inference ─────────────────────────────────
    //
    // For each selected PS1MeshInstance / PS1MeshGroup, look at the
    // scene context (animation player ancestor? alpha-cutout texture?
    // sibling skinned mesh?) and overwrite the construction defaults
    // with values that match the scene shape. Authored values that
    // already differ from the default are PRESERVED — we only fill
    // gaps. Authors run this consciously; nothing here is automatic.
    private void OnInferDefaults()
    {
        var selected = EditorInterface.Singleton.GetSelection().GetSelectedNodes();
        if (selected.Count == 0)
        {
            GD.PushError("[PS1Godot] Infer Defaults: select a PS1MeshInstance / PS1MeshGroup first.");
            return;
        }

        int touched = 0;
        foreach (var n in selected)
        {
            var changes = new System.Collections.Generic.List<string>();
            if (n is PS1MeshInstance pmi) InferOnInstance(pmi, changes);
            else if (n is PS1MeshGroup pmg) InferOnGroup(pmg, changes);
            else continue;

            if (changes.Count > 0)
            {
                touched++;
                GD.Print($"[PS1Godot]   {n.Name}: {string.Join(", ", changes)}");
            }
            else
            {
                GD.Print($"[PS1Godot]   {n.Name}: no inferrable defaults to set.");
            }
        }

        if (touched > 0)
        {
            GD.Print($"[PS1Godot] Infer Defaults: updated {touched} node(s). Save the .tscn to persist.");
        }
    }

    private static void InferOnInstance(PS1MeshInstance pmi, System.Collections.Generic.List<string> changes)
    {
        // Animation context — has an AnimationPlayer in the scene that
        // targets this node by name? Treat as DynamicRigid.
        if (pmi.MeshRole == Exporter.MeshRole.StaticWorld && HasAnimationTargeting(pmi))
        {
            pmi.MeshRole  = Exporter.MeshRole.DynamicRigid;
            pmi.DrawPhase = Exporter.DrawPhase.OpaqueDynamic;
            pmi.ExportMode = Exporter.ExportMode.KeepSeparate;
            changes.Add("MeshRole→DynamicRigid + KeepSeparate (animation targets this node)");
        }

        // Material alpha — any cutout / transparent material on this
        // mesh implies CutoutDecals draw phase + AlphaMode=Cutout.
        if (pmi.AlphaMode == Exporter.AlphaMode.Opaque && HasAlphaMaterial(pmi))
        {
            pmi.AlphaMode = Exporter.AlphaMode.Cutout;
            if (pmi.DrawPhase == Exporter.DrawPhase.OpaqueStatic)
            {
                pmi.DrawPhase = Exporter.DrawPhase.CutoutDecals;
            }
            // Translucent already drives the runtime; mirror so writers
            // that consume AlphaMode see the same intent. Don't clobber
            // a user-set Translucent=true — the legacy field stays.
            if (!pmi.Translucent) pmi.Translucent = true;
            changes.Add("AlphaMode→Cutout + DrawPhase→CutoutDecals (alpha-keyed material)");
        }

        // Vertex color hint — if the mesh has a COLOR channel, ShadingMode
        // should be VertexColor instead of FlatColor.
        if (pmi.ShadingMode == Exporter.ShadingMode.FlatColor && HasVertexColors(pmi.Mesh))
        {
            pmi.ShadingMode = Exporter.ShadingMode.VertexColor;
            changes.Add("ShadingMode→VertexColor (mesh has COLOR channel)");
        }
    }

    private static void InferOnGroup(PS1MeshGroup pmg, System.Collections.Generic.List<string> changes)
    {
        if (pmg.MeshRole == Exporter.MeshRole.StaticWorld && HasAnimationTargeting(pmg))
        {
            pmg.MeshRole  = Exporter.MeshRole.DynamicRigid;
            pmg.DrawPhase = Exporter.DrawPhase.OpaqueDynamic;
            pmg.ExportMode = Exporter.ExportMode.KeepSeparate;
            changes.Add("MeshRole→DynamicRigid + KeepSeparate (animation targets this group)");
        }
    }

    // True when an ancestor's children include an AnimationPlayer with
    // a track that mentions `node`'s name. Rough match — Godot animation
    // tracks use NodePaths but for our authoring conventions a contains
    // check is good enough to flag intent.
    private static bool HasAnimationTargeting(Node node)
    {
        for (var p = node.GetParent(); p != null; p = p.GetParent())
        {
            foreach (var c in p.GetChildren())
            {
                if (c is not AnimationPlayer ap) continue;
                foreach (var libName in ap.GetAnimationLibraryList())
                {
                    var lib = ap.GetAnimationLibrary(libName);
                    if (lib == null) continue;
                    foreach (var animName in lib.GetAnimationList())
                    {
                        var anim = lib.GetAnimation(animName);
                        if (anim == null) continue;
                        for (int t = 0; t < anim.GetTrackCount(); t++)
                        {
                            string path = anim.TrackGetPath(t).ToString();
                            if (path.Contains((string)node.Name)) return true;
                        }
                    }
                }
            }
        }
        return false;
    }

    private static bool HasAlphaMaterial(PS1MeshInstance pmi)
    {
        if (pmi.Mesh == null) return false;
        for (int s = 0; s < pmi.Mesh.GetSurfaceCount(); s++)
        {
            var mat = pmi.GetSurfaceOverrideMaterial(s) ?? pmi.Mesh.SurfaceGetMaterial(s);
            if (mat is StandardMaterial3D std)
            {
                if (std.Transparency != BaseMaterial3D.TransparencyEnum.Disabled) return true;
                if (std.AlbedoTexture != null && std.AlbedoTexture.HasAlpha()) return true;
            }
        }
        return false;
    }

    // ── Phase L2: bake vertex AO into BakedColors ───────────────────
    //
    // Pure additive over Phase L1 — meant to run AFTER a directional
    // bake. Walks every visible mesh in the scene as occluder
    // geometry, fires N rays per vertex into the hemisphere above its
    // normal, multiplies the AO term into existing BakedColors.
    //
    // Doesn't require authored colliders — uses raw triangle data
    // straight from each mesh. Brute-force ray-tri intersection is
    // O(verts × rays × triangles); fine for typical PS1 scenes.
    //
    // Layered authoring loop:
    //   1. Bake Vertex Lighting from Scene Lights → fills BakedColors
    //      with Lambert directional contribution.
    //   2. Bake Vertex AO into BakedColors → multiplies AO term in.
    //   3. Save .tscn. Done.
    private void OnBakeVertexAO()
    {
        var sceneRoot = EditorInterface.Singleton.GetEditedSceneRoot();
        if (sceneRoot == null)
        {
            GD.PushError("[PS1Godot] No scene open — open a .tscn before baking AO.");
            return;
        }

        var selection = EditorInterface.Singleton.GetSelection().GetSelectedNodes();
        if (selection.Count == 0)
        {
            GD.PushError("[PS1Godot] Bake Vertex AO: select one or more PS1MeshInstance nodes first.");
            return;
        }

        // Default options — author tunes via inspector / hand-edit
        // post-bake. Intentionally not exposed to a popup since the
        // values are well-tuned for typical PS1-scale scenes.
        var opts = new Exporter.VertexAOBaker.Options
        {
            RayCount = 12,
            MaxRayDistance = 0.5f,
            Strength = 0.5f,
            Bias = 0.001f,
        };

        var startNs = System.Diagnostics.Stopwatch.GetTimestamp();
        var result = Exporter.VertexAOBaker.Bake(sceneRoot, selection, opts);
        double elapsedMs = (System.Diagnostics.Stopwatch.GetTimestamp() - startNs)
                         * 1000.0 / System.Diagnostics.Stopwatch.Frequency;

        GD.Print(
            $"[PS1Godot] Bake Vertex AO: {result.MeshesBaked} mesh(es), " +
            $"{result.VerticesPainted} vertices painted, " +
            $"{result.TrianglesInScene} occluder triangles, " +
            $"{result.RaysCast} rays cast in {elapsedMs:F0} ms. " +
            $"{result.Skipped} skipped.");
        foreach (var reason in result.SkippedReasons)
        {
            GD.PushWarning($"[PS1Godot]   skipped: {reason}");
        }
        if (result.MeshesBaked > 0)
        {
            GD.Print("[PS1Godot] Save the .tscn to persist the AO-multiplied BakedColors.");
        }
    }

    // ── UX-C: extract Mesh from Godot, open in Blender ──────────────
    //
    // Godot → Blender geometry round-trip. Author selects one or more
    // PS1MeshInstance(s), clicks once: their Mesh resources are
    // exported as <mesh_id>.glb files at res://ps1godot_assets/meshes/
    // (the same path Blender's "Export to Godot" writes back to), then
    // Blender launches pointing at the first extracted file. Author
    // edits → saves → uses Blender's "Export to Godot" → the same .glb
    // gets overwritten → Godot's import scanner picks up the change.
    //
    // Honest limitation surfaced in the post-extract message: the
    // PS1MeshInstance.Mesh field still references the OLD Mesh
    // resource. After re-export, the new .glb produces a fresh
    // PackedScene with a fresh Mesh resource — author drags the new
    // mesh onto the Mesh slot manually to pick up the changes. Auto-
    // rebind via .import config + meshes/save_to_file is a future
    // enhancement; we ship the minimum tier first.
    private void OnEditMeshInBlender()
    {
        var sceneRoot = EditorInterface.Singleton.GetEditedSceneRoot();
        if (sceneRoot == null)
        {
            GD.PushError("[PS1Godot] No scene open — open a .tscn before extracting meshes.");
            return;
        }

        var selection = EditorInterface.Singleton.GetSelection().GetSelectedNodes();
        var targets = new System.Collections.Generic.List<PS1MeshInstance>();
        foreach (var n in selection)
        {
            if (n is PS1MeshInstance pmi) targets.Add(pmi);
        }
        if (targets.Count == 0)
        {
            GD.PushError("[PS1Godot] Edit Mesh in Blender: select one or more PS1MeshInstance nodes first.");
            return;
        }

        string outputDir = ProjectSettings.GlobalizePath(DefaultBlenderMeshDir);
        var extracted = new System.Collections.Generic.List<string>();
        int idsGenerated = 0;
        int rebound = 0;
        int rebindFailed = 0;
        foreach (var pmi in targets)
        {
            bool wasEmpty = string.IsNullOrEmpty(pmi.MeshId);
            var result = Exporter.MeshExtractor.ExtractToGlb(pmi, outputDir);
            if (!result.Success)
            {
                GD.PushWarning($"[PS1Godot] Extract failed for '{pmi.Name}': {result.ErrorMessage}");
                continue;
            }
            if (wasEmpty) idsGenerated++;
            extracted.Add(result.OutputPath);
            if (result.Rebound)
            {
                rebound++;
                GD.Print($"[PS1Godot]   extracted + rebound {pmi.Name} → {result.OutputPath}");
            }
            else
            {
                rebindFailed++;
                string suffix = string.IsNullOrEmpty(result.ErrorMessage)
                    ? ""
                    : $" (rebind skipped: {result.ErrorMessage})";
                GD.Print($"[PS1Godot]   extracted {pmi.Name} → {result.OutputPath}{suffix}");
            }
        }

        if (extracted.Count == 0)
        {
            GD.PushError("[PS1Godot] No meshes extracted — see warnings above.");
            return;
        }

        if (idsGenerated > 0)
        {
            GD.Print($"[PS1Godot] {idsGenerated} mesh_id(s) auto-generated. Save the .tscn to persist.");
        }
        if (rebound > 0)
        {
            GD.Print($"[PS1Godot] {rebound} PS1MeshInstance.Mesh field(s) auto-rebound to the imported .glb. " +
                     "Future Blender edits flow through Godot's import scanner — no manual rebind needed.");
        }
        if (rebindFailed > 0)
        {
            GD.PushWarning($"[PS1Godot] {rebindFailed} extraction(s) succeeded but auto-rebind didn't run — " +
                           "drag the imported mesh onto the Mesh slot manually or re-run the operator.");
        }

        // Launch Blender on the first extracted .glb. If the user
        // selected multiple meshes, the others sit on disk waiting
        // for the author to open them in subsequent Blender sessions.
        string? blenderExe = UI.SetupDetector.ResolveBlenderExe();
        if (blenderExe == null)
        {
            GD.PushWarning(
                "[PS1Godot] Blender not found (set BLENDER_EXE or install to a conventional path). " +
                $"Extracted .glb(s) are ready at {outputDir}; open manually.");
            return;
        }

        try
        {
            int pid = (int)OS.CreateProcess(blenderExe, new[] { extracted[0] });
            if (pid > 0)
            {
                GD.Print($"[PS1Godot] Launched Blender on {extracted[0]} (pid {pid}).");
                GD.Print("[PS1Godot] Edit, then click 'Export to Godot' in the addon to send back. " +
                         "Drag the newly-imported Mesh onto this PS1MeshInstance's Mesh slot to pick up the changes.");
            }
            else
            {
                GD.PushError("[PS1Godot] Failed to launch Blender — OS.CreateProcess returned no PID.");
            }
        }
        catch (System.Exception ex)
        {
            GD.PushError($"[PS1Godot] Failed to launch Blender: {ex.Message}");
        }
    }

    // ── Phase L1: bake vertex lighting from scene lights ────────────
    //
    // Mirrors the Blender add-on's vc_bake_scene_lights — same
    // formula, same 0.8 PSX 2x semi-trans ceiling, same iteration
    // loop. Authors who already lit a Godot scene for editor preview
    // get a one-click bake that produces vertex colors matching the
    // viewport. Per-instance storage on PS1MeshInstance.BakedColors
    // means same mesh in two scenes can have two lighting setups
    // (something SplashEdit can't do — it bakes into the source mesh).
    private void OnBakeVertexLighting()
    {
        var sceneRoot = EditorInterface.Singleton.GetEditedSceneRoot();
        if (sceneRoot == null)
        {
            GD.PushError("[PS1Godot] No scene open — open a .tscn before baking lighting.");
            return;
        }

        var selection = EditorInterface.Singleton.GetSelection().GetSelectedNodes();
        if (selection.Count == 0)
        {
            GD.PushError("[PS1Godot] Bake Vertex Lighting: select one or more PS1MeshInstance nodes first.");
            return;
        }

        var result = Exporter.VertexLightingBaker.Bake(sceneRoot, selection);
        GD.Print(
            $"[PS1Godot] Bake Vertex Lighting: {result.MeshesBaked} mesh(es), " +
            $"{result.VerticesPainted} vertices painted, {result.LightsUsed} light(s) used. " +
            $"{result.Skipped} skipped.");
        foreach (var reason in result.SkippedReasons)
        {
            GD.PushWarning($"[PS1Godot]   skipped: {reason}");
        }
        if (result.MeshesBaked > 0)
        {
            GD.Print("[PS1Godot] Save the .tscn to persist the BakedColors override.");
        }
    }

    // ── UX-B: "Send to Blender" — write sidecars + launch Blender ────
    //
    // Symmetric counterpart to the Blender-side "Export to Godot"
    // button. One click does the round-trip-out half of the loop:
    //   1. Write JSON sidecars from the active scene's PS1MeshInstance /
    //      PS1MeshGroup state via BlenderMetadataWriter.
    //   2. Launch Blender, opening the configured PS1Scene.SourceBlendFile
    //      if any, with --python-expr running ps1godot.import_metadata so
    //      the Blender-side state refreshes immediately.
    //
    // No SourceBlendFile? Launch Blender empty; the author opens the
    // .blend manually and clicks "Import Metadata" in the addon. The
    // sidecars still wrote successfully so the manual path works.
    //
    // No Blender on the system? Stop after step 1 and surface a hint.
    // Same fall-through used by other Setup-detected deps.
    private void OnSendToBlender()
    {
        var sceneRoot = EditorInterface.Singleton.GetEditedSceneRoot();
        if (sceneRoot == null)
        {
            GD.PushError("[PS1Godot] No scene open — open a .tscn before Send to Blender.");
            return;
        }

        // 1. Write sidecars (same path as the existing Tools menu item).
        string sidecarDir = ProjectSettings.GlobalizePath(DefaultBlenderSidecarDir);
        var write = Exporter.BlenderMetadataWriter.WriteScene(sceneRoot, sidecarDir);
        GD.Print(
            $"[PS1Godot] Send to Blender: {write.Written} sidecar(s) → {sidecarDir} " +
            $"(skipped {write.Skipped}, new IDs {write.IdsGenerated}, errors {write.IoErrors}).");
        if (write.IdsGenerated > 0)
        {
            GD.Print("[PS1Godot] Save the .tscn to persist the new asset_id / mesh_id values.");
        }

        // 2. Launch Blender if available.
        string? blenderExe = UI.SetupDetector.ResolveBlenderExe();
        if (blenderExe == null)
        {
            GD.PushWarning(
                "[PS1Godot] Send to Blender: Blender executable not found. " +
                "Set the BLENDER_EXE environment variable or install Blender to a conventional location. " +
                "Sidecars were written; you can run 'Import Metadata' in the addon manually.");
            return;
        }

        // PS1Scene.SourceBlendFile — when set, open that .blend so the
        // import operator runs against the right scene. Otherwise launch
        // Blender empty and the author opens the file themselves.
        string? blendFile = null;
        if (sceneRoot is PS1Scene ps1 && !string.IsNullOrEmpty(ps1.SourceBlendFile))
        {
            string abs = ProjectSettings.GlobalizePath(ps1.SourceBlendFile);
            if (System.IO.File.Exists(abs))
            {
                blendFile = abs;
            }
            else
            {
                GD.PushWarning(
                    $"[PS1Godot] Send to Blender: PS1Scene.SourceBlendFile = '{ps1.SourceBlendFile}' " +
                    $"resolves to '{abs}' which does not exist. Launching Blender without an open file.");
            }
        }

        // The --python-expr trick: run the import operator after Blender
        // finishes loading. The addon must already be enabled in the
        // user's Blender prefs for the op to be registered. If the
        // expression fails (e.g. addon not installed), Blender still
        // opens the .blend — author does the import manually.
        var args = new System.Collections.Generic.List<string>();
        if (blendFile != null) args.Add(blendFile);
        args.Add("--python-expr");
        args.Add("import bpy; bpy.app.timers.register(lambda: bpy.ops.ps1godot.import_metadata() and None, first_interval=0.5)");

        try
        {
            int pid = (int)OS.CreateProcess(blenderExe, args.ToArray());
            if (pid > 0)
            {
                GD.Print($"[PS1Godot] Launched Blender (pid {pid})" +
                         (blendFile != null ? $" with '{blendFile}'" : "") +
                         "; import operator queued to run on load.");
            }
            else
            {
                GD.PushError("[PS1Godot] Failed to launch Blender — OS.CreateProcess returned no PID.");
            }
        }
        catch (System.Exception ex)
        {
            GD.PushError($"[PS1Godot] Failed to launch Blender: {ex.Message}");
        }
    }

    private static bool HasVertexColors(Mesh? mesh)
    {
        if (mesh == null) return false;
        // Mesh base class doesn't expose SurfaceGetFormat in C# — peek
        // the surface arrays directly. ArrayType.Color slot is empty
        // on meshes without a vertex-color channel.
        for (int s = 0; s < mesh.GetSurfaceCount(); s++)
        {
            var arrays = mesh.SurfaceGetArrays(s);
            if (arrays == null || arrays.Count <= (int)Mesh.ArrayType.Color) continue;
            var colorSlot = arrays[(int)Mesh.ArrayType.Color];
            if (colorSlot.VariantType != Variant.Type.Nil)
            {
                var colors = colorSlot.AsColorArray();
                if (colors != null && colors.Length > 0) return true;
            }
        }
        return false;
    }
}
#endif
