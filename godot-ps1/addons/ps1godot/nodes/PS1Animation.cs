using Godot;

namespace PS1Godot;

// A named timeline that drives one target GameObject's position over a
// fixed number of frames. Keyframes are PS1AnimationKeyframe child nodes;
// authors reorder / add / delete them via the scene tree, not via an
// array editor.
//
// MVP supports one track per animation (ObjectPosition). Extending to
// rotation / scale / UI visibility adds new track types without breaking
// the schema.
//
// Play from Lua: Animation.Play("<AnimationName>") — exposed via the
// runtime's existing AnimationPlayer.
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

    // Total length in 30-fps frames. 60 = 2 seconds. Max 8191 per the
    // runtime's 13-bit frame field in CutsceneKeyframe (~4.5 minutes).
    [Export(PropertyHint.Range, "1,8191,1")]
    public int TotalFrames { get; set; } = 60;
}
