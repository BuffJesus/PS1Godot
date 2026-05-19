using Godot;

namespace PS1Godot;

// Per-entity stat block. Attach a PS1Stats .tres to a PS1MeshInstance's
// Stats slot; the runtime tracks current HP / Mana / Stamina for the
// entity, queryable + mutable from Lua via the Stats.* API.
//
// v1 surface: HP + Mana + Stamina (current + max for each). Poise,
// Defense, and element resistances are explicit non-features — they
// land as future v34+ bumps once the first encounter tells us which
// authors actually reach for. Mana/Stamina ship together with HP so
// HUD authoring (health bar + stamina bar + optional mana bar) is one
// coherent setup instead of two splashpack-format bumps + two passes
// through writer/loader/Lua.
//
// For games without a stamina system (puzzle, exploration), leave
// MaxStamina = 0 — every Stats.* query returns 0 and the bars aren't
// rendered. Same for mana in non-magic games.
//
// Authoring pattern:
//   1. Right-click a folder → New Resource → PS1Stats
//   2. Set MaxHP (and optionally StartingHP for "starts wounded")
//   3. Set MaxStamina/MaxMana if the encounter uses them
//   4. Drag the .tres onto the PS1MeshInstance's Stats slot
//
// Multiple PS1MeshInstance instances can reference the same .tres for
// "every gobbo has the same vitals" — that's the whole point of
// Resource over per-node fields.
[Tool]
[GlobalClass]
public partial class PS1Stats : Resource
{
    /// <summary>
    /// Maximum HP. Acts as the cap that runtime damage clamps against.
    /// 1..32767 — int16 range at export time.
    /// </summary>
    [Export] public int MaxHP { get; set; } = 100;

    /// <summary>
    /// HP at scene load. -1 (default) means "start at MaxHP". Override
    /// for enemies that spawn already wounded, training dummies, etc.
    /// </summary>
    [Export] public int StartingHP { get; set; } = -1;

    /// <summary>
    /// Maximum stamina. Set 0 for games without a stamina system —
    /// Stats.GetStamina returns 0 and the HUD authoring can branch on
    /// MaxStamina to render or skip the bar.
    /// </summary>
    [Export] public int MaxStamina { get; set; } = 0;

    /// <summary>
    /// Stamina at scene load. -1 (default) means "start at MaxStamina".
    /// </summary>
    [Export] public int StartingStamina { get; set; } = -1;

    /// <summary>
    /// Maximum mana. Set 0 for non-magic games — same pattern as
    /// MaxStamina.
    /// </summary>
    [Export] public int MaxMana { get; set; } = 0;

    /// <summary>
    /// Mana at scene load. -1 (default) means "start at MaxMana".
    /// </summary>
    [Export] public int StartingMana { get; set; } = -1;

    // Resolves StartingX (negative sentinel) to the effective initial
    // value for each stat. Used by the exporter when serializing.
    public int InitialHP      => StartingHP      < 0 ? MaxHP      : StartingHP;
    public int InitialStamina => StartingStamina < 0 ? MaxStamina : StartingStamina;
    public int InitialMana    => StartingMana    < 0 ? MaxMana    : StartingMana;
}
