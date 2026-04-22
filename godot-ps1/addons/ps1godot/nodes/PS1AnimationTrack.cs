using Godot;

namespace PS1Godot;

// One track inside a PS1Cutscene. Each track drives one property
// (position / rotation / active state) of one target via a list of
// PS1AnimationKeyframe child nodes.
//
// PS1Animation (single-track) handles the "drop in a moving prop"
// case. PS1Cutscene + N PS1AnimationTrack children handle synchronized
// multi-property timelines (camera + door + NPC walking together).
[Tool]
[GlobalClass]
[Icon("res://addons/ps1godot/icons/ps1_animation_track.svg")]
public partial class PS1AnimationTrack : Node
{
    // Must match a PS1MeshInstance Name elsewhere in the scene. Camera-
    // type tracks (when supported in Phase 2) will allow this to be
    // empty since the camera is a singleton.
    [Export] public string TargetObjectName { get; set; } = "";

    [Export] public PS1AnimationTrackType TrackType { get; set; } = PS1AnimationTrackType.Position;
}
