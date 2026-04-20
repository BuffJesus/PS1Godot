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
// The animation's TargetObjectName is the object whose position this
// keyframe drives.
[Tool]
[GlobalClass]
public partial class PS1AnimationKeyframe : Node
{
    // Frame number within the parent animation's timeline. Must be in
    // [0, TotalFrames). Multiple keyframes may share a frame if you want
    // stepped behavior, but authors should generally pick distinct frames.
    [Export(PropertyHint.Range, "0,8191,1")]
    public int Frame { get; set; } = 0;

    // Target position at this frame, in Godot world coordinates. At
    // export we convert to PSX fp12 (divided by the scene's GteScaling)
    // with Y negated to match PSX's Y-down convention — same as the
    // static mesh / collider pipeline.
    [Export] public Vector3 Position { get; set; } = Vector3.Zero;

    // How to interpolate between this keyframe and the next.
    [Export] public PS1InterpMode Interp { get; set; } = PS1InterpMode.Linear;
}
