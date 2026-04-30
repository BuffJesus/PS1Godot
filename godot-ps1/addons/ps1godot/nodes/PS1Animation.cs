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
[Icon("res://addons/ps1godot/icons/ps1_animation.svg")]
public partial class PS1Animation : Node
{
    /// <summary>
    /// Unique name used by Animation.Play lookups. Falls back to the node's
    /// name if empty (so Animation.Play("MyAnim") resolves a child Node
    /// renamed "MyAnim" even with no AnimationName set).
    /// </summary>
    [ExportGroup("Identity")]
    [Export] public string AnimationName { get; set; } = "";

    /// <summary>
    /// Must match the Name of a PS1MeshInstance somewhere in the scene —
    /// that's what the runtime's object name table resolves to a GameObject.
    /// Mismatched / empty name = animation never plays (silent at runtime).
    /// </summary>
    [Export] public string TargetObjectName { get; set; } = "";

    /// <summary>
    /// What this animation drives on the target. Position / Rotation move
    /// the GameObject; Active toggles its visibility on/off via keyframe.
    /// Camera tracks are cutscene-only — use PS1Cutscene for those.
    /// </summary>
    [ExportGroup("Timing")]
    [Export] public PS1AnimationTrackType TrackType { get; set; } = PS1AnimationTrackType.Position;

    /// <summary>
    /// Total length in 30-fps frames. 60 = 2 seconds. Max 8191 per the
    /// runtime's 13-bit frame field in CutsceneKeyframe (~4.5 minutes).
    /// </summary>
    [Export(PropertyHint.Range, "1,8191,1,suffix:frames")]
    public int TotalFrames { get; set; } = 60;

    public override string[] _GetConfigurationWarnings()
    {
        var w = new System.Collections.Generic.List<string>();

        // Object tracks need a target. Camera tracks (cutscene-only) don't.
        bool isObjectTrack = TrackType == PS1AnimationTrackType.Position
                          || TrackType == PS1AnimationTrackType.Rotation
                          || TrackType == PS1AnimationTrackType.Active;
        if (isObjectTrack && string.IsNullOrEmpty(TargetObjectName))
        {
            w.Add("TargetObjectName is empty. Object-typed animations need to name " +
                  "a PS1MeshInstance in the scene; without it, the runtime has no " +
                  "GameObject to drive and the animation will never play.");
        }
        else if (isObjectTrack)
        {
            // Verify the target name resolves to a sibling/descendant under
            // the scene root. Walk up to find the scene root, then search.
            Node root = this;
            while (root.GetParent() is Node p) root = p;
            if (FindMeshByName(root, TargetObjectName) is null)
            {
                w.Add($"TargetObjectName '{TargetObjectName}' doesn't match the Name of " +
                      "any PS1MeshInstance in this scene. The runtime resolves the target " +
                      "by node name at export — fix the spelling or rename the mesh.");
            }
        }
        return w.ToArray();
    }

    private static PS1MeshInstance? FindMeshByName(Node n, string name)
    {
        if (n is PS1MeshInstance pmi && pmi.Name == name) return pmi;
        foreach (var c in n.GetChildren())
        {
            if (c is Node child)
            {
                var hit = FindMeshByName(child, name);
                if (hit != null) return hit;
            }
        }
        return null;
    }
}
