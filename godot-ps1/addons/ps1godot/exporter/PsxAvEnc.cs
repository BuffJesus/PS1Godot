#if TOOLS
using System;
using System.Diagnostics;
using System.IO;
using Godot;

namespace PS1Godot.Exporter;

// Phase 3 audio routing scaffolding. psxavenc is the canonical PS1
// XA-ADPCM encoder (and CDDA WAV→raw helper) — we don't bundle it; it
// has to be installed on the host. Resolution order:
//   1. env var PSXAVENC=<absolute path>     (highest precedence)
//   2. `psxavenc` on PATH
// The actual XA conversion step is not wired yet — when an export
// includes XA-routed clips, this class logs whether the binary is
// reachable and where; conversion lands in Phase 3 once the runtime
// has an XA streaming backend to consume the output.
public static class PsxAvEnc
{
    public sealed record Probe(bool Available, string? Path, string? Version, string? Source, string? Error);

    public static Probe Detect()
    {
        // 1. env override
        string? envPath = System.Environment.GetEnvironmentVariable("PSXAVENC");
        if (!string.IsNullOrWhiteSpace(envPath))
        {
            if (!File.Exists(envPath))
            {
                return new Probe(false, envPath, null, "env:PSXAVENC",
                    $"PSXAVENC env var points at '{envPath}' but no file exists there.");
            }
            return ProbeBinary(envPath, "env:PSXAVENC");
        }

        // 2. PATH
        string exe = OS.GetName() == "Windows" ? "psxavenc.exe" : "psxavenc";
        string? pathDirs = System.Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(pathDirs))
        {
            char sep = OS.GetName() == "Windows" ? ';' : ':';
            foreach (string dir in pathDirs.Split(sep, StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    string candidate = Path.Combine(dir, exe);
                    if (File.Exists(candidate))
                    {
                        return ProbeBinary(candidate, "PATH");
                    }
                }
                catch { /* ignore unreadable PATH entries */ }
            }
        }

        return new Probe(false, null, null, null,
            "psxavenc not found. Install from https://github.com/WonderfulToolchain/psxavenc " +
            "and either put it on PATH or set the PSXAVENC env var to the full binary path. " +
            "XA-routed clips will be skipped (conversion is scaffolded, not yet implemented).");
    }

    private static Probe ProbeBinary(string path, string source)
    {
        try
        {
            using var p = new Process();
            p.StartInfo.FileName = path;
            p.StartInfo.Arguments = "-h";  // psxavenc prints usage banner with version
            p.StartInfo.UseShellExecute = false;
            p.StartInfo.RedirectStandardError = true;
            p.StartInfo.RedirectStandardOutput = true;
            p.StartInfo.CreateNoWindow = true;
            p.Start();
            string banner = p.StandardError.ReadToEnd() + p.StandardOutput.ReadToEnd();
            p.WaitForExit(2000);
            // banner first line is usually "psxavenc <version>"; just keep first 80 chars
            string version = banner.Split('\n', 2)[0].Trim();
            if (version.Length > 80) version = version[..80];
            return new Probe(true, path, version, source, null);
        }
        catch (Exception ex)
        {
            return new Probe(false, path, null, source, $"psxavenc launch failed: {ex.Message}");
        }
    }

    // One-shot reporter — called from SceneCollector when any clip in
    // the scene resolves to XA. Idempotent: caches the probe so multiple
    // scenes per export trigger only one log line.
    private static Probe? _cached;
    public static void ReportIfXaPresent(int xaClipCount)
    {
        if (xaClipCount <= 0) return;
        _cached ??= Detect();
        var p = _cached;
        if (p.Available)
        {
            GD.Print($"[PS1Godot] psxavenc detected ({p.Source}: {p.Path}) — {p.Version}");
            GD.Print($"[PS1Godot] {xaClipCount} XA-routed clip(s) found. Conversion is scaffolded (Phase 3); runtime will log 'not implemented' for these.");
        }
        else
        {
            GD.PushWarning($"[PS1Godot] {xaClipCount} XA-routed clip(s) found, but psxavenc is unavailable. {p.Error}");
        }
    }
}
#endif
