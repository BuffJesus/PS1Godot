#if TOOLS
using Godot;
using PS1Godot.Effects;
using PS1Godot.Exporter;
using PS1Godot.Tools;

namespace PS1Godot;

[Tool]
public partial class PS1GodotPlugin : EditorPlugin
{
    private const string SubdivideMenuLabel = "PS1Godot: Subdivide Selected Mesh (×4 tris)";
    private const string AnalyzeTexturesMenuLabel = "PS1Godot: Analyze Texture Compliance";
    private const string ToggleCompositorMenuLabel = "PS1Godot: Toggle PS1 Preview on Selected Camera";
    private const string ExportEmptySplashpackMenuLabel = "PS1Godot: Export Splashpack";
    private const string BuildPsxsplashMenuLabel = "PS1Godot: Build psxsplash runtime";
    private const string LaunchEmulatorMenuLabel = "PS1Godot: Launch in PCSX-Redux";
    private const string RunOnPsxMenuLabel = "PS1Godot: Run on PSX (export + build + launch)";

    private PS1TriggerBoxGizmo? _triggerBoxGizmo;

    public override void _EnterTree()
    {
        AddToolMenuItem(SubdivideMenuLabel, Callable.From(OnSubdivide));
        AddToolMenuItem(AnalyzeTexturesMenuLabel, Callable.From(OnAnalyzeTextures));
        AddToolMenuItem(ToggleCompositorMenuLabel, Callable.From(OnToggleCompositor));
        AddToolMenuItem(ExportEmptySplashpackMenuLabel, Callable.From(OnExportEmptySplashpack));
        AddToolMenuItem(BuildPsxsplashMenuLabel, Callable.From(OnBuildPsxsplash));
        AddToolMenuItem(LaunchEmulatorMenuLabel, Callable.From(OnLaunchEmulator));
        AddToolMenuItem(RunOnPsxMenuLabel, Callable.From(OnRunOnPsx));

        _triggerBoxGizmo = new PS1TriggerBoxGizmo();
        AddNode3DGizmoPlugin(_triggerBoxGizmo);

        GD.Print("[PS1Godot] Plugin enabled.");
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
        string relPath = "res://build/scene_0.splashpack";
        string absPath = ProjectSettings.GlobalizePath(relPath);
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(absPath)!);

        // Collect the currently-edited scene (walks tree, converts each
        // PS1MeshInstance's mesh to PSX format).
        var sceneRoot = EditorInterface.Singleton.GetEditedSceneRoot();
        var sceneData = Exporter.SceneCollector.FromRoot(sceneRoot, sceneRoot?.SceneFilePath ?? "");
        GD.Print($"[PS1Godot] Scene: {(string.IsNullOrEmpty(sceneData.ScenePath) ? "(unsaved)" : sceneData.ScenePath)}");
        GD.Print($"[PS1Godot]   PS1MeshInstance objects found: {sceneData.Objects.Count}");

        int totalTris = 0;
        foreach (var obj in sceneData.Objects)
        {
            totalTris += obj.Mesh.Triangles.Count;
            var p = obj.Node.GlobalPosition;
            GD.Print($"[PS1Godot]     - {obj.Node.Name}  pos=({p.X:F2},{p.Y:F2},{p.Z:F2})  bpp={obj.Node.BitDepth}  collide={obj.Node.Collision}  → {obj.Mesh.Triangles.Count} tris");
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
            GD.PushError($"[PS1Godot] Splashpack export failed: {e.Message}");
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
