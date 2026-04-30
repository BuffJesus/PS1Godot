using Godot;

namespace PS1Godot;

// Matches the runtime's InterpMode enum (cutscene.hh). Upper 3 bits of
// the on-disk `frameAndInterp` u16 encode the mode.
public enum PS1InterpMode
{
    Linear    = 0,
    Step      = 1,
    EaseIn    = 2,
    EaseOut   = 3,
    EaseInOut = 4,
}

// One keyframe inside a PS1Animation. Child node of a PS1Animation.
// Interpretation of Value depends on the parent animation's TrackType:
//   Position: Godot world-space XYZ in meters (converted to PSX fp12).
//   Rotation: Euler angles per axis in **degrees** (0..360). Exporter
//             converts to PSX fp10 angle units (4096 = full turn).
//   Active:   Value.X used as a boolean (non-zero = active, 0 = hidden).
[Tool]
[GlobalClass]
[Icon("res://addons/ps1godot/icons/ps1_animation_keyframe.svg")]
public partial class PS1AnimationKeyframe : Node
{
    /// <summary>
    /// Frame number within the parent animation's timeline. Must be in
    /// [0, TotalFrames). Multiple keyframes may share a frame if you want
    /// stepped behavior, but authors should generally pick distinct frames.
    /// </summary>
    [Export(PropertyHint.Range, "0,8191,1,suffix:frames")]
    public int Frame { get; set; } = 0;

    /// <summary>
    /// Track-type-dependent triple (see the class comment for units).
    /// </summary>
    [Export] public Vector3 Value { get; set; } = Vector3.Zero;

    /// <summary>
    /// How to interpolate between this keyframe and the next.
    /// </summary>
    [Export] public PS1InterpMode Interp { get; set; } = PS1InterpMode.Linear;
}
