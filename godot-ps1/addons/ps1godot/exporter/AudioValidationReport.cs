#if TOOLS
using System.Collections.Generic;
using Godot;

namespace PS1Godot.Exporter;

// Per-scene audio validation pass — runs at the end of export, walks
// the resolved AudioClipRecord list, and prints one row per clip plus
// WARN rows for risky shapes the SPU/XA budget bars can't spot on
// their own (long clips that should have routed to XA, loops at the
// SPU resident-ceiling, etc.).
//
// Mirrors TextureValidationReport — same per-row + warnings shape,
// same "print only, no behavioural change" contract. Designed to fill
// the gap between the scene-aggregate `.spu` cap warning and the
// per-clip "what's actually big" question the author has when they
// blow the budget.
//
// Cross-references:
//   docs/ps1_asset_pipeline_plan.md — animation / audio policy.
//   SceneStats.cs                  — same SPU cost model (gameplay-resident only).
public static class AudioValidationReport
{
    // SPU usable RAM after kernel + reverb reservations. Matches
    // SceneStats.SpuBudgetBytes — one source of truth would be nicer
    // but a literal here keeps the report standalone.
    private const long SpuBudgetBytes = 256 * 1024;

    // Threshold for "this SPU clip is big enough that XA streaming is
    // probably the right call". 32 KB ≈ 1.5s of mono ADPCM @ 11025 Hz.
    private const long SpuLongClipBytes = 32 * 1024;

    // Threshold for "this looping SPU clip eats permanent VRAM". Even
    // shorter loops add up if there are several. WARN at 12 KB.
    private const long SpuLoopBigBytes = 12 * 1024;

    // Routes match AudioClipRecord.Routing values + splashpack.hh
    // SplashpackSceneSetup::AudioRouting enum.
    private const byte ROUTE_SPU  = 0;
    private const byte ROUTE_XA   = 1;
    private const byte ROUTE_CDDA = 2;

    public sealed record Row(
        string Name,
        ushort SampleRate,
        long AdpcmBytes,
        long XaBytes,
        byte Route,
        bool Loop,
        byte CddaTrack,
        string? Warning);

    // Returns the number of WARN-level rows emitted (0 if every clip
    // is fine). Plugin sums this into the dock's "Last export" line.
    public static int EmitForScene(SceneData data, int sceneIndex)
    {
        if (data.AudioClips.Count == 0)
        {
            GD.Print($"[PS1Godot]   Audio report: scene[{sceneIndex}] has no audio clips.");
            return 0;
        }

        var rows = new List<Row>(data.AudioClips.Count);
        long totalSpu = 0;
        long totalXa  = 0;
        int spuCount = 0, xaCount = 0, cddaCount = 0;
        int warnCount = 0;

        foreach (var c in data.AudioClips)
        {
            long adpcm = c.AdpcmData?.Length ?? 0;
            long xa    = c.XaPayload?.Length ?? 0;

            switch (c.Routing)
            {
                case ROUTE_SPU:  spuCount++;  totalSpu += adpcm; break;
                case ROUTE_XA:   xaCount++;   totalXa  += xa;    break;
                case ROUTE_CDDA: cddaCount++; break;
            }

            string? warning = ClassifyRisk(c, adpcm, xa);
            if (warning != null) warnCount++;

            rows.Add(new Row(
                Name: c.Name,
                SampleRate: c.SampleRate,
                AdpcmBytes: adpcm,
                XaBytes: xa,
                Route: c.Routing,
                Loop: c.Loop,
                CddaTrack: c.CddaTrackNumber,
                Warning: warning));
        }

        // Sort SPU-resident first (the scarce bus), then by descending
        // size — over-budget scenes show offenders at the top.
        rows.Sort((a, b) =>
        {
            int aResident = a.Route == ROUTE_SPU ? 0 : 1;
            int bResident = b.Route == ROUTE_SPU ? 0 : 1;
            if (aResident != bResident) return aResident.CompareTo(bResident);
            return b.AdpcmBytes.CompareTo(a.AdpcmBytes);
        });

        double spuPct = SpuBudgetBytes > 0 ? (100.0 * totalSpu / SpuBudgetBytes) : 0;
        GD.Print($"[PS1Godot] Audio report scene[{sceneIndex}]: {rows.Count} clips ({spuCount} SPU / {xaCount} XA / {cddaCount} CDDA), SPU resident {totalSpu / 1024.0:F1} KB ({spuPct:F0}% of {SpuBudgetBytes / 1024} KB cap), XA streamed {totalXa / 1024.0:F1} KB, {warnCount} warning(s).");
        GD.Print("[PS1Godot]   name                                          rate    route  loop   ADPCM     XA       warn");
        foreach (var r in rows)
        {
            string route = r.Route switch
            {
                ROUTE_SPU  => "SPU",
                ROUTE_XA   => "XA",
                ROUTE_CDDA => $"CD#{r.CddaTrack}",
                _          => "?",
            };
            string loop = r.Loop ? "loop" : "once";
            string adpcm = r.AdpcmBytes > 0 ? $"{r.AdpcmBytes / 1024.0:F1} KB" : "—";
            string xa    = r.XaBytes    > 0 ? $"{r.XaBytes    / 1024.0:F1} KB" : "—";
            string warn = r.Warning ?? "";
            GD.Print($"[PS1Godot]   {Truncate(r.Name, 45),-45} {r.SampleRate,5} Hz  {route,-5}  {loop,-5}  {adpcm,8}  {xa,8}  {warn}");
        }

        return warnCount;
    }

    private static string? ClassifyRisk(AudioClipRecord c, long adpcm, long xa)
    {
        // Big SPU-routed clip — author probably meant XA streaming.
        // Looping clips legitimately need to be SPU-resident, so don't
        // nag those even when they're large; the next branch handles them.
        if (c.Routing == ROUTE_SPU && !c.Loop && adpcm >= SpuLongClipBytes)
        {
            return $"large SPU clip ({adpcm / 1024.0:F1} KB, {c.SampleRate} Hz, no loop) — route XA?";
        }

        // Looping clip that owns a non-trivial slice of SPU forever.
        if (c.Routing == ROUTE_SPU && c.Loop && adpcm >= SpuLoopBigBytes)
        {
            return $"big resident loop ({adpcm / 1024.0:F1} KB) — keep tight; XA can't loop on its own";
        }

        // XA route with no XA payload (psxavenc missing at export). The
        // SceneCollector falls back to the SPU encoding, so the runtime
        // can still play it, but the author asked for XA — flag it.
        if (c.Routing == ROUTE_XA && xa == 0)
        {
            return "XA-routed but no XA payload (psxavenc missing?) — falling back to SPU";
        }

        // CDDA route with no track number — the runtime won't play it.
        if (c.Routing == ROUTE_CDDA && c.CddaTrackNumber == 0)
        {
            return "CDDA-routed with track #0 — runtime treats as 'no mapping' and stays silent";
        }

        return null;
    }

    private static string Truncate(string s, int max)
        => s.Length <= max ? s : s[..(max - 1)] + "…";
}
#endif
