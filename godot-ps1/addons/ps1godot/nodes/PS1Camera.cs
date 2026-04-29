using Godot;
using PS1Godot.Effects;

namespace PS1Godot;

// Camera3D tagged for PS1 export. Auto-attaches the PS1PixelizeEffect
// compositor when added to a scene so authors see the 320×240 PSX
// look in the editor viewport without manual setup. Authors who want
// a clean Godot view can clear pmi.Compositor.CompositorEffects in
// the inspector. (Re-add via Tools menu → Materials → Toggle PS1
// Preview on Selected Camera.)
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
