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

    private PS1TriggerBoxGizmo? _triggerBoxGizmo;
    private PS1GodotDock? _dock;
    private PS1UICanvasEditor? _uiCanvasEditor;

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

        GD.Print("[PS1Godot] Plugin enabled.");
    }

    private void OnEditorSelectionChanged()
    {
        if (_uiCanvasEditor == null) return;
        PS1UICanvas? canvas = null;
        foreach (var n in EditorInterface.Singleton.GetSelection().GetSelectedNodes())
        {
            if (n is PS1UICanvas c) { canvas = c; break; }
            if (n is PS1UIElement el && el.GetParent() is PS1UICanvas parent)
            {
                canvas = parent;
                break;
            }
        }
        _uiCanvasEditor.SetSelectedCanvas(canvas);
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

        SceneChanged -= OnSceneChanged;
        EditorInterface.Singleton.GetSelection().SelectionChanged -= OnEditorSelectionChanged;

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
        // The full one-click loop. Stops at the first failure so the user sees
        // a clear "do this next" rather than a cascade of errors.
        //
        // launch-emulator.cmd prefers psxsplash.ps-exe (raw PSX-EXE format) over
        // psxsplash.elf — PCSX-Redux's ELF loader left the CPU executing
        // garbage on this build. Check for either; absence of both means
        // we need to build.
        OnExportEmptySplashpack();
        string buildDir = System.IO.Path.Combine(RepoRoot(), "godot-ps1", "build");
        bool hasPsExe = System.IO.File.Exists(System.IO.Path.Combine(buildDir, "psxsplash.ps-exe"));
        bool hasElf = System.IO.File.Exists(System.IO.Path.Combine(buildDir, "psxsplash.elf"));
        if (!hasPsExe && !hasElf)
        {
            GD.Print("[PS1Godot] psxsplash binary missing — building first…");
            OnBuildPsxsplash();
        }
        OnLaunchEmulator();
    }

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
    }

    private void ExportOneScene(Node sceneRoot, int sceneIndex)
    {
        string buildDir = ProjectSettings.GlobalizePath("res://build/");
        string absPath = System.IO.Path.Combine(buildDir, $"scene_{sceneIndex}.splashpack");

        var sceneData = Exporter.SceneCollector.FromRoot(sceneRoot, sceneRoot.SceneFilePath ?? "");
        GD.Print($"[PS1Godot] Scene[{sceneIndex}]: {(string.IsNullOrEmpty(sceneData.ScenePath) ? "(unsaved)" : sceneData.ScenePath)}");
        GD.Print($"[PS1Godot]   PS1MeshInstance objects found: {sceneData.Objects.Count}");

        int totalTris = 0;
        foreach (var obj in sceneData.Objects)
        {
            totalTris += obj.Mesh.Triangles.Count;
            var p = obj.Node.GlobalPosition;
            // PS1-specific fields only exist on PS1MeshInstance; auto-detected
            // raw MeshInstance3D avatars (FBX characters under PS1Player) get
            // 8bpp + no collision defaults, reported here as "(auto)".
            string bpp = obj.Node is PS1MeshInstance pmi1 ? pmi1.BitDepth.ToString() : "8bit(auto)";
            string coll = obj.Node is PS1MeshInstance pmi2 ? pmi2.Collision.ToString() : "None(auto)";
            GD.Print($"[PS1Godot]     - {obj.Node.Name}  pos=({p.X:F2},{p.Y:F2},{p.Z:F2})  bpp={bpp}  collide={coll}  → {obj.Mesh.Triangles.Count} tris");
        }
        GD.Print($"[PS1Godot]   Total triangles: {totalTris}");

        try
        {
            Exporter.SplashpackWriter.Write(absPath, sceneData);
            long packBytes = new System.IO.FileInfo(absPath).Length;
            long vramBytes = new System.IO.FileInfo(System.IO.Path.ChangeExtension(absPath, ".vram")).Length;
            long spuBytes = new System.IO.FileInfo(System.IO.Path.ChangeExtension(absPath, ".spu")).Length;
            GD.Print($"[PS1Godot] Splashpack written: {absPath}");
            GD.Print($"[PS1Godot]   .splashpack = {packBytes}B");
            GD.Print($"[PS1Godot]   .vram       = {vramBytes}B");
            GD.Print($"[PS1Godot]   .spu        = {spuBytes}B");
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
}
#endif
