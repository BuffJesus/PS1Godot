using Godot;
using PS1Godot.Effects;

namespace PS1Godot;

/// <summary>
/// Camera3D tagged for PS1 export. Auto-attaches a PS1PixelizeEffect
/// compositor so the editor viewport shows the 320×240 PSX look without
/// manual setup. To disable the preview, clear Compositor → Effects in
/// the inspector (re-add via Tools → Materials → Toggle PS1 Preview).
/// At export time, the PS1Camera's transform becomes the scene's initial
/// camera position. Only one PS1Camera per scene is expected — the
/// exporter uses the first one it finds.
/// </summary>
[Tool]
[GlobalClass]
[Icon("res://addons/ps1godot/icons/ps1_camera.svg")]
public partial class PS1Camera : Camera3D
{
    public override void _EnterTree()
    {
        if (!Engine.IsEditorHint()) return;
        // Skip if author already attached an effect or any compositor.
        if (Compositor != null && Compositor.CompositorEffects.Count > 0) return;

        // Build a fresh Compositor + PS1PixelizeEffect. CallDeferred
        // because Godot's editor occasionally calls _EnterTree before
        // the Compositor resource is constructible cleanly on
        // duplicate / scene-reload paths.
        CallDeferred(MethodName.AttachPS1PreviewIfAbsent);
    }

    private void AttachPS1PreviewIfAbsent()
    {
        if (!Engine.IsEditorHint()) return;
        if (Compositor != null && Compositor.CompositorEffects.Count > 0) return;

        var compositor = Compositor ?? new Compositor();
        var effect = new PS1PixelizeEffect { Enabled = true };

        var effects = compositor.CompositorEffects ?? new Godot.Collections.Array<CompositorEffect>();
        effects.Add(effect);
        compositor.CompositorEffects = effects;
        Compositor = compositor;
    }
}
