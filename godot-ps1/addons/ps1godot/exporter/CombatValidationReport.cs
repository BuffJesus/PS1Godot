#if TOOLS
using System.Collections.Generic;
using Godot;

namespace PS1Godot.Exporter;

// Combat-side lint pass. Catches authoring oversights that the boss-
// smoke debug arc (see docs/internal/handoff-2026-05-19-* +
// 2026-05-20-* and the docs/internal/rfc/combat-framework.md RFC)
// found at runtime — each check below maps to a real bug the user
// hit while building a souls encounter on the existing primitives.
//
// All checks emit Warning-tier offenders into the shared validator
// sink. Plugin pipeline calls EmitForScene per scene; offenders
// surface in the PS1 Doctor dock alongside texture/audio/animation
// reports and in the small dock's headline.
//
// New check ideas live in docs/internal/rfc/combat-framework.md §
// "L5 — PS1Doctor lint checks". Add them here as static helper
// methods, sum the counts in EmitForScene.
public static class CombatValidationReport
{
    public static int EmitForScene(SceneData data, int sceneIndex,
                                    List<(string Name, string Reason)>? offenderSink = null)
    {
        int warnings = 0;
        warnings += CheckStatsWithoutHurtBox(data, sceneIndex, offenderSink);
        return warnings;
    }

    // Bug #8 from the boss_smoke debug arc: the player avatar had a
    // PS1Stats resource (so it could carry HP/Stamina) but no
    // PS1HurtBox children — combat looked broken because the boss's
    // Physics.OverlapBoxDetailed never returned the player in its
    // swing's hits list, so Stats.DealDamage never ran and no HP bar
    // moved. The boss's tell shake still fired (independent of hit
    // connection), making the symptom particularly confusing: screen
    // shakes but nothing happens.
    //
    // Check: any entity with PS1Stats.MaxHP > 0 should have at least
    // one PS1HurtBox child. Warning, not error — there are legitimate
    // niche cases ("invulnerable scripted boss," "stats track a non-
    // gameplay number") but they should be rare enough that surfacing
    // the bare PS1Stats author oversight is worth a warning even when
    // intentional.
    private static int CheckStatsWithoutHurtBox(
        SceneData data, int sceneIndex,
        List<(string Name, string Reason)>? offenderSink)
    {
        // Build a set of entity indices that have at least one
        // hurtbox. One pass over the hurtbox list is cheaper than
        // a nested scan for every stats record.
        var hasHurtBox = new HashSet<int>();
        foreach (var hb in data.HurtBoxes)
            hasHurtBox.Add(hb.EntityIndex);

        int warnings = 0;
        foreach (var stats in data.Stats)
        {
            if (stats.MaxHP <= 0) continue;
            if (hasHurtBox.Contains(stats.EntityIndex)) continue;

            // Entity name for the offender row. Stats records carry
            // an EntityIndex into data.Objects, populated by the
            // collector when it walks the scene tree.
            string entName = (stats.EntityIndex >= 0 &&
                              stats.EntityIndex < data.Objects.Count)
                ? (string)data.Objects[stats.EntityIndex].Node.Name
                : $"entity[{stats.EntityIndex}]";

            string msg = $"has PS1Stats (MaxHP={stats.MaxHP}) but no PS1HurtBox children — Physics.OverlapBoxDetailed won't return this entity, combat hits will whiff silently";
            GD.PushWarning($"[CombatLint] scene_{sceneIndex} {entName}: {msg}");
            offenderSink?.Add((entName, "PS1Stats without PS1HurtBox — invulnerable by accident"));
            warnings++;
        }

        if (warnings > 0)
        {
            GD.Print($"[PS1Godot]   Combat lint scene[{sceneIndex}]: {warnings} entity(ies) have PS1Stats but no PS1HurtBox.");
        }
        else
        {
            GD.Print($"[PS1Godot]   Combat lint scene[{sceneIndex}]: clean.");
        }
        return warnings;
    }
}
#endif
