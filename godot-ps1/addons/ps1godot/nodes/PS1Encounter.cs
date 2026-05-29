using Godot;

namespace PS1Godot;

// Composite "boss-fight gate" node — RFC §L4 (combat-framework).
//
// Drops in one place what previously took a PS1TriggerBox + a
// hand-written `boss_smoke_fog_gate.lua` to express. At scene
// export this node lowers to:
//
//   1. A TriggerBoxRecord (same AABB-bake as PS1TriggerBox) with
//      a LuaFileIndex pointing at...
//   2. An auto-generated Lua sidecar that calls
//      `Encounter.new{...}` with the inspector-exposed properties
//      and binds `onTriggerEnter` / `onTriggerExit` to its
//      `:onEnter()` / `:onExit()` methods.
//
// The runtime sees this as a regular trigger pointing at a regular
// script — no new engine code. Hand-written `Encounter.new{}` and
// composite-node usage interoperate freely within one scene.
//
// Authoring expectation: pair with a `Combat.MeleeBoss{
// encounter_id = "<same id>" }` brain on the boss entity, so the
// shared id derives the `<id>_aggro` / `<id>_dead` Persist keys
// both sides agree on. The Doctor flags missing pairings.
//
// Conservative scope (Phase 4 first slice, 2026-05-29): dropping
// `FogWall` (visual hide-on-death) and `on_enter_extra` from the
// RFC table because boss_smoke doesn't use them and the RFC
// itself says composite nodes should extract from a *second* boss's
// actual needs, not speculation. Both can land as additive fields
// when a second boss demands them.
[Tool]
[GlobalClass]
[Icon("res://addons/ps1godot/icons/ps1_trigger_box.svg")]
public partial class PS1Encounter : Node3D
{
    /// <summary>
    /// Persist key prefix. Two keys derive from this:
    /// `id .. "_aggro"` (gate flag — boss reads to know when to
    /// wake) and `id .. "_dead"` (cleared flag — encounter reads
    /// to skip re-entry after the boss dies). Pair with
    /// `Combat.MeleeBoss{encounter_id = "same id"}` on the boss.
    /// </summary>
    [ExportGroup("Identity")]
    [Export] public string EncounterId { get; set; } = "";

    /// <summary>
    /// Half-extents in local space (so x=2 means 4-unit wide).
    /// World AABB is computed at export by baking the node's
    /// GlobalTransform into the 8 corners. Same convention as
    /// PS1TriggerBox.
    /// </summary>
    [ExportGroup("Volume")]
    [Export] public Vector3 HalfExtents { get; set; } = new Vector3(2, 1, 0.5f);

    /// <summary>
    /// Pointer to the boss entity (a PS1MeshInstance with a PS1Stats
    /// resource and a Lua script using `Combat.MeleeBoss{encounter_id
    /// = "matching id"}`). NOT read by the auto-generated encounter
    /// Lua — the runtime binding is purely via the shared Persist
    /// key prefix. The field exists so PS1Doctor can lint the pair
    /// (boss exists, has stats, has a script) at editor time.
    /// </summary>
    [ExportGroup("Boss")]
    [Export] public NodePath BossEntity { get; set; } = new NodePath("");

    /// <summary>
    /// PS1UICanvas to reveal on encounter entry and hide on boss
    /// death. NodePath resolves to the canvas's `CanvasName` at
    /// export time. Optional — encounters without a HUD bar can
    /// leave this empty.
    /// </summary>
    [ExportGroup("HUD")]
    [Export] public NodePath BossHPCanvas { get; set; } = new NodePath("");

    /// <summary>
    /// Music track id (matches a PS1AudioClip name) to start on
    /// entry. Volume defaults to 100 (matches the old hand-rolled
    /// boss_smoke fog gate). Empty string skips the music start.
    /// </summary>
    [ExportGroup("Audio")]
    [Export] public string MusicTrack { get; set; } = "";

    [Export(PropertyHint.Range, "0,127,1")]
    public int MusicVolume { get; set; } = 100;

    /// <summary>
    /// One-shot SFX clip id played on first cross (fog-gate
    /// whoosh, gate-clang, etc.). Empty string skips it.
    /// </summary>
    [Export] public string SfxOnEnter { get; set; } = "";

    /// <summary>
    /// "Fog wall is solid from inside" semantic. While the
    /// encounter is active and uncleared, retreating across the
    /// trigger AABB toward the spawn side snaps the player back
    /// to `ArenaAnchor`. After the boss dies the gate opens and
    /// the player can walk out freely.
    /// </summary>
    [ExportGroup("Retreat")]
    [Export] public bool BlockRetreat { get; set; } = true;

    /// <summary>
    /// Snap target in Godot world coords when retreat is
    /// blocked. Z is converted to fp12 PSX z at export
    /// (z * -1024). X is passed through; Y is currently
    /// preserved from the player's live position by the
    /// runtime (z-only snap matches the boss_smoke behavior).
    /// </summary>
    [Export] public Vector3 ArenaAnchor { get; set; } = Vector3.Zero;

    public override string[] _GetConfigurationWarnings()
    {
        var w = new System.Collections.Generic.List<string>();
        if (string.IsNullOrEmpty(EncounterId))
        {
            w.Add("EncounterId is empty. Persist keys derive from this — " +
                  "without it, the gate flag and dead flag collide with " +
                  "other unnamed encounters in the same save.");
        }
        if (BossHPCanvas != null && !BossHPCanvas.IsEmpty)
        {
            var node = GetNodeOrNull(BossHPCanvas);
            if (node == null)
            {
                w.Add($"BossHPCanvas NodePath '{BossHPCanvas}' doesn't resolve. " +
                      "Drag the PS1UICanvas node into the inspector field.");
            }
            else if (node is not PS1UICanvas)
            {
                w.Add($"BossHPCanvas '{node.Name}' is not a PS1UICanvas. " +
                      "Only PS1UICanvas nodes can be revealed by an encounter.");
            }
        }
        if (BossEntity == null || BossEntity.IsEmpty)
        {
            w.Add("BossEntity is empty. The encounter still works (binding is " +
                  "via Persist key, not a direct reference), but Doctor can't " +
                  "verify the boss has matching `encounter_id` set in its Lua.");
        }
        return w.ToArray();
    }
}
