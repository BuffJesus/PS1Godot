#if TOOLS
using System.Collections.Generic;
using System.Linq;
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
        warnings += CheckHurtBoxWithoutStats(data, sceneIndex, offenderSink);
        warnings += CheckBarFillExceedsBg(data, sceneIndex, offenderSink);
        warnings += CheckPairedBarsNearBlack(data, sceneIndex, offenderSink);
        return warnings;
    }

    // Suffix convention for stat-bar element pairs. See
    // godot-ps1/demo/boss_smoke/boss_smoke.tscn (hp_bg + hp_fill,
    // stamina_bg + stamina_fill) and the authoring recipe at
    // docs/authoring/boss-encounters.md. PS1StatBar composite node
    // (RFC §L4, deferred to Phase 4) will emit these same suffixes
    // automatically, so the heuristic stays load-bearing across the
    // hand-authored and composite-node code paths.
    private const string BgSuffix   = "_bg";
    private const string FillSuffix = "_fill";

    // Returns the shared prefix for a paired BG/fill element name,
    // or null if `name` doesn't end in one of the two suffixes.
    private static string? PairPrefix(string name)
    {
        if (name.EndsWith(BgSuffix, System.StringComparison.Ordinal))
            return name.Substring(0, name.Length - BgSuffix.Length);
        if (name.EndsWith(FillSuffix, System.StringComparison.Ordinal))
            return name.Substring(0, name.Length - FillSuffix.Length);
        return null;
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
        return warnings;
    }

    // Symmetric to CheckStatsWithoutHurtBox. A PS1HurtBox attached
    // to an entity that has no PS1Stats is dead weight — the box
    // shows up in Physics.OverlapBoxDetailed hits, but
    // Stats.DealDamage on a no-stats entity returns 0 applied damage
    // (StatsResolveIndex returns 0xFFFF; runtime skips the debit).
    // Author either (a) forgot to add Stats, or (b) the hurtbox is
    // there for a non-combat purpose (region detection?) and should
    // be a PS1TriggerBox instead.
    //
    // Multiple hurtboxes per entity (head/body/legs) are common, so
    // emit ONE warning per entity-without-stats, not per hurtbox.
    private static int CheckHurtBoxWithoutStats(
        SceneData data, int sceneIndex,
        List<(string Name, string Reason)>? offenderSink)
    {
        var hasStats = new HashSet<int>();
        foreach (var s in data.Stats)
            hasStats.Add(s.EntityIndex);

        var alreadyWarned = new HashSet<int>();
        int warnings = 0;
        foreach (var hb in data.HurtBoxes)
        {
            if (hasStats.Contains(hb.EntityIndex)) continue;
            if (!alreadyWarned.Add(hb.EntityIndex)) continue;

            string entName = (hb.EntityIndex >= 0 &&
                              hb.EntityIndex < data.Objects.Count)
                ? (string)data.Objects[hb.EntityIndex].Node.Name
                : $"entity[{hb.EntityIndex}]";

            string msg = "has PS1HurtBox children but no PS1Stats — Stats.DealDamage will no-op on hits, hurtbox is dead weight";
            GD.PushWarning($"[CombatLint] scene_{sceneIndex} {entName}: {msg}");
            offenderSink?.Add((entName, "PS1HurtBox without PS1Stats — hits won't register damage"));
            warnings++;
        }

        if (warnings > 0)
        {
            GD.Print($"[PS1Godot]   Combat lint scene[{sceneIndex}]: {warnings} entity(ies) have PS1HurtBox but no PS1Stats.");
        }
        return warnings;
    }

    // RFC §L5 row 3 — "Bar fill exceeds BG." Stat bars are authored as
    // two sibling Box elements per stat: `<prefix>_bg` for the dark
    // backing panel, `<prefix>_fill` for the live-driven gauge. The
    // fill is normally inset by `Padding` pixels on each side of the
    // bg so the bg reads as a frame. If the fill ever extends past
    // the bg AABB the design intent is broken — either the author
    // typed mismatched W/H values, or someone scaled the fill while
    // forgetting to scale the bg. Symptom on PSX: the bg looks fine
    // but the fill paints over neighboring HUD or off-canvas.
    //
    // Pairing is by exact name prefix within the same canvas. A
    // lone `*_fill` with no matching `*_bg` is allowed (some HUDs
    // are unbacked); we just skip those.
    private static int CheckBarFillExceedsBg(
        SceneData data, int sceneIndex,
        List<(string Name, string Reason)>? offenderSink)
    {
        int warnings = 0;
        foreach (var canvas in data.UICanvases)
        {
            // Index by prefix inside this canvas — bg/fill pairs do
            // not span canvases (the runtime looks up per-canvas).
            var bgByPrefix = new Dictionary<string, UIElementRecord>();
            var fillByPrefix = new Dictionary<string, UIElementRecord>();
            foreach (var el in canvas.Elements)
            {
                if (el.Type != PS1UIElementType.Box) continue;
                var prefix = PairPrefix(el.Name);
                if (prefix == null) continue;
                if (el.Name.EndsWith(BgSuffix, System.StringComparison.Ordinal))
                    bgByPrefix[prefix] = el;
                else
                    fillByPrefix[prefix] = el;
            }

            foreach (var (prefix, fill) in fillByPrefix)
            {
                if (!bgByPrefix.TryGetValue(prefix, out var bg)) continue;

                int bgL = bg.X,        bgT = bg.Y;
                int bgR = bg.X + bg.W, bgB = bg.Y + bg.H;
                int fL  = fill.X,         fT = fill.Y;
                int fR  = fill.X + fill.W, fB = fill.Y + fill.H;

                bool contained = (fL >= bgL && fT >= bgT && fR <= bgR && fB <= bgB);
                if (contained) continue;

                string offender = $"{canvas.Name}/{prefix}";
                string detail = $"fill rect ({fL},{fT})-({fR},{fB}) escapes bg rect ({bgL},{bgT})-({bgR},{bgB})";
                GD.PushWarning($"[CombatLint] scene_{sceneIndex} {offender}: {detail}");
                offenderSink?.Add((offender, "UI bar fill exceeds bg — frame design will break, fill paints outside backing"));
                warnings++;
            }
        }

        if (warnings > 0)
        {
            GD.Print($"[PS1Godot]   Combat lint scene[{sceneIndex}]: {warnings} stat-bar pair(s) have fill exceeding bg.");
        }
        return warnings;
    }

    // RFC §L5 row 9 — "Boxed colors near-black on BG." When a
    // bg + fill pair both have RGB sums below ~32, the fill is
    // invisible against the bg and the bar reads as a dead black
    // strip on PSX. (PSX 24bpp framebuffer + no AA means contrast
    // < 1 step per channel is genuinely lost, not just hard to see.)
    // Info-tier — designers sometimes deliberately ship a black-on-
    // black "ghost" gauge, but it's worth surfacing.
    //
    // Reuses the bg/fill pairing from the previous check. Threshold
    // 32 matches the RFC; tweak via const if false positives surface.
    private const int NearBlackRgbSumThreshold = 32;
    private static int CheckPairedBarsNearBlack(
        SceneData data, int sceneIndex,
        List<(string Name, string Reason)>? offenderSink)
    {
        int warnings = 0;
        foreach (var canvas in data.UICanvases)
        {
            var bgByPrefix = new Dictionary<string, UIElementRecord>();
            var fillByPrefix = new Dictionary<string, UIElementRecord>();
            foreach (var el in canvas.Elements)
            {
                if (el.Type != PS1UIElementType.Box) continue;
                var prefix = PairPrefix(el.Name);
                if (prefix == null) continue;
                if (el.Name.EndsWith(BgSuffix, System.StringComparison.Ordinal))
                    bgByPrefix[prefix] = el;
                else
                    fillByPrefix[prefix] = el;
            }

            foreach (var (prefix, fill) in fillByPrefix)
            {
                if (!bgByPrefix.TryGetValue(prefix, out var bg)) continue;

                int bgSum   = bg.ColorR   + bg.ColorG   + bg.ColorB;
                int fillSum = fill.ColorR + fill.ColorG + fill.ColorB;
                if (bgSum >= NearBlackRgbSumThreshold) continue;
                if (fillSum >= NearBlackRgbSumThreshold) continue;

                string offender = $"{canvas.Name}/{prefix}";
                string detail = $"both bg (rgb sum {bgSum}) and fill (rgb sum {fillSum}) are near-black — fill will be invisible against bg";
                GD.PushWarning($"[CombatLint] scene_{sceneIndex} {offender}: {detail}");
                offenderSink?.Add((offender, "UI bar fill + bg both near-black — gauge will be unreadable"));
                warnings++;
            }
        }

        if (warnings > 0)
        {
            GD.Print($"[PS1Godot]   Combat lint scene[{sceneIndex}]: {warnings} stat-bar pair(s) have near-black fill against near-black bg.");
        }
        return warnings;
    }
}
#endif
