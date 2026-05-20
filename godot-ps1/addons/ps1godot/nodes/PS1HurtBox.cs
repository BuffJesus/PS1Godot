using Godot;

namespace PS1Godot;

// v34+: a weak-point AABB attached to a PS1MeshInstance. At hit-detection
// time, Physics.OverlapBoxDetailed returns the hurtbox's DamageMultiplier
// along with the entity handle so callers can scale base damage before
// calling Stats.DealDamage. The classic use case is "boss head = 2x crit
// zone" without authoring a separate boss-head entity.
//
// An entity can have any number of PS1HurtBox children — each child is
// one weak point. If a query AABB overlaps multiple hurtboxes on the
// same entity, OverlapBoxDetailed returns the HIGHEST multiplier so
// authoring "head + body + legs" with descending crit values gives the
// expected "best hit wins" feel without per-call Lua dedup.
//
// World-space AABB at query time = entity world position + local Offset
// ± Size half-extents. The query is AXIS-ALIGNED — rotated boxes are
// future work. For boss encounters where the boss faces the player most
// of the fight, axis-aligned is close enough.
//
// Authoring pattern:
//   - Drop PS1HurtBox child nodes under a PS1MeshInstance (the boss).
//   - Set Size to the local half-extents (Godot inspector sliders).
//   - Set DamageMultiplier (default 100 = 1× baseline; 200 = 2× crit).
//   - The node's local Transform is the offset from the parent entity.
//     Position it visually in the 3D viewport; ignore rotation (axis-
//     aligned at query time).
[Tool]
[GlobalClass]
[Icon("res://addons/ps1godot/icons/ps1_player.svg")]
public partial class PS1HurtBox : Node3D
{
    /// <summary>
    /// Local half-extents. The world-space AABB is the entity's
    /// position + this node's local position ± Size, so set this to
    /// half the box's visible dimensions.
    /// </summary>
    [Export] public Vector3 Size { get; set; } = new Vector3(0.5f, 0.5f, 0.5f);

    /// <summary>
    /// Damage multiplier as a percentage. 100 = 1× baseline damage,
    /// 200 = 2× crit, 50 = half damage (armored zone). Stored as
    /// int16 at export time; clamp to [0, 32767].
    /// </summary>
    [Export(PropertyHint.Range, "0,400,5")]
    public int DamageMultiplier { get; set; } = 100;
}
