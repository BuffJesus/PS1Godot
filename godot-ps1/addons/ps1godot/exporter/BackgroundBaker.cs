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
        if (image == null || image.IsEmpty())
        {
            GD.PushError($"[PS1Godot] BackgroundBaker: render produced an empty image (camera: {sourceCam.Name}).");
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
