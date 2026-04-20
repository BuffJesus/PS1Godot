using Godot;

namespace PS1Godot;

// A multi-track timeline played by the runtime's CutscenePlayer.
// Children of type PS1AnimationTrack become the cutscene's tracks at
// export time; each track has its own keyframes (PS1AnimationKeyframe
// children of the track).
//
// Compared to PS1Animation: PS1Cutscene supports multiple tracks
// (drive several objects in sync) and — once Phase 2/3 of bullet 10 B
// land — camera tracks and frame-triggered audio events.
//
// Play from Lua: Cutscene.Play("<CutsceneName>") — exposed via the
// runtime's existing CutscenePlayer.
[Tool]
[GlobalClass]
public partial class PS1Cutscene : Node
{
    [Export] public string CutsceneName { get; set; } = "";

    // Total length in 30-fps frames. Tracks longer than this are
    // truncated by the runtime's MAX_TRACKS / MAX_KEYFRAMES caps.
    [Export(PropertyHint.Range, "1,8191,1")]
    public int TotalFrames { get; set; } = 90;
}
