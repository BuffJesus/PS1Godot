using Godot;

namespace PS1Godot;

// Matches the runtime's TrackType enum (cutscene.hh). Camera tracks
// don't need a target object — the runtime drives its singleton Camera
// directly. Object tracks need a TargetObjectName matching a
// PS1MeshInstance somewhere in the scene.
public enum PS1AnimationTrackType
{
    CameraPosition = 0,  // TrackType::CameraPosition (cutscenes only)
    CameraRotation = 1,  // TrackType::CameraRotation (cutscenes only)
    Position       = 2,  // TrackType::ObjectPosition
    Rotation       = 3,  // TrackType::ObjectRotation
    Active         = 4,  // TrackType::ObjectActive
}

// A named timeline that drives one target GameObject over a fixed
// number of frames. Keyframes are PS1AnimationKeyframe child nodes;
// authors reorder / add / delete them via the scene tree, not via an
// array editor. Keyframe value interpretation depends on TrackType —
// see PS1AnimationKeyframe.cs.
//
// MVP still ships one track per animation. Multi-track timelines live
// in cutscenes (follow-up). Play from Lua via Animation.Play("<name>").
[Tool]
[GlobalClass]
public partial class PS1Animation : Node
{
    // Unique name used by Animation.Play lookups. Falls back to the node's
    // name if empty.
    [Export] public string AnimationName { get; set; } = "";

    // Must match the Name of a PS1MeshInstance somewhere in the scene —
    // that's what the runtime's object name table resolves to a GameObject.
    [Export] public string TargetObjectName { get; set; } = "";

    // What this animation drives on the target.
    [Export] public PS1AnimationTrackType TrackType { get; set; } = PS1AnimationTrackType.Position;

    // Total length in 30-fps frames. 60 = 2 seconds. Max 8191 per the
    // runtime's 13-bit frame field in CutsceneKeyframe (~4.5 minutes).
    [Export(PropertyHint.Range, "1,8191,1")]
    public int TotalFrames { get; set; } = 60;
}
