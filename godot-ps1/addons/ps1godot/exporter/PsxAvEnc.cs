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
        // Used to spawn `psxavenc -h` to capture the version banner, but
        // System.Diagnostics.Process running synchronously in Godot's
        // [Tool] context plus psxavenc's >4 KB help output deadlocked
        // ConvertWavToXa on every export. We don't actually need the
        // version string — File.Exists already proved the binary is
        // there. Treat any reachable file as available.
        return new Probe(true, path, "<banner skipped>", source, null);
    }

    // One-shot reporter — called from SceneCollector when any clip in
    // the scene resolves to XA. Idempotent: caches the probe so multiple
    // scenes per export trigger only one log line.
    private static Probe? _cached;
    public static Probe GetCached()
    {
        _cached ??= Detect();
        return _cached;
    }
    public static void ReportIfXaPresent(int xaClipCount)
    {
        if (xaClipCount <= 0) return;
        var p = GetCached();
        if (p.Available)
        {
            GD.Print($"[PS1Godot] psxavenc detected ({p.Source}: {p.Path}) — {p.Version}");
            GD.Print($"[PS1Godot] {xaClipCount} XA-routed clip(s) found; converting to .xa sidecar.");
        }
        else
        {
            GD.PushWarning($"[PS1Godot] {xaClipCount} XA-routed clip(s) found, but psxavenc is unavailable — XA sidecar will be empty and runtime will silence those clips. {p.Error}");
        }
    }

    // Convert a PCM int16 mono buffer to PSX XA-ADPCM bytes by shelling
    // out to psxavenc. Writes a temp WAV (psxavenc's preferred input)
    // and reads the resulting .xa back into memory. Caller is
    // responsible for routing decisions; this is pure conversion.
    //
    // Returns null on any failure (not-available, bad sample rate,
    // psxavenc nonzero exit). Errors are logged via GD.PushWarning so
    // the export doesn't break — the affected clip falls through to
    // SPU silence at runtime.
    //
    // psxavenc XA mode CLI:
    //   psxavenc -t xa -f <hz> -b 4 -c <chan> -F 0 -C 0 in.wav out.xa
    // -t xa     : XA-ADPCM Form 2 output
    // -f hz     : output sample rate (37800 or 18900 are the two PSX hardware-supported XA rates)
    // -b 4      : 4-bit ADPCM (PSX hardware native; -b 8 is supported by the encoder but not the SPU XA voice)
    // -c chan   : channel count (1 mono, 2 stereo)
    // -F file#  : XA file number (0 default, ranges 0..255)
    // -C chan#  : XA channel number (0 default, ranges 0..31)
    public static byte[]? ConvertWavToXa(short[] pcm, int sourceRate, int channels, string clipName)
    {
        var probe = GetCached();
        if (!probe.Available)
        {
            return null;
        }
        if (channels < 1 || channels > 2)
        {
            GD.PushWarning($"[PS1Godot] XA convert '{clipName}': unsupported channel count {channels}; XA needs mono or stereo.");
            return null;
        }

        // PSX XA hardware decodes at exactly 37800 Hz or 18900 Hz. Pick
        // the closer one to the source rate; psxavenc resamples on its
        // side so an arbitrary input rate is fine.
        int xaRate = (sourceRate >= 28350) ? 37800 : 18900;

        string tempDir = Path.Combine(Path.GetTempPath(), "ps1godot_xa");
        Directory.CreateDirectory(tempDir);
        string safeName = MakeFilesystemSafe(clipName);
        string wavPath = Path.Combine(tempDir, safeName + ".wav");
        string xaPath  = Path.Combine(tempDir, safeName + ".xa");

        try
        {
            GD.Print($"[PsxAvEnc] '{clipName}': writing WAV ({pcm.Length} samples @ {sourceRate}Hz) -> {wavPath}");
            WriteMonoWav(wavPath, pcm, sourceRate);
            long wavSize = new FileInfo(wavPath).Length;
            GD.Print($"[PsxAvEnc] '{clipName}': WAV written ({wavSize} B). Spawning psxavenc -> {xaPath}");

            // Switched from System.Diagnostics.Process to Godot's
            // OS.Execute — same code path used by the rest of the
            // plugin's shell-outs (build-psxsplash.cmd etc.) and avoids
            // a hang we saw with Process.Start in [Tool] context where
            // the export froze on the first XA-routed clip.
            var output = new Godot.Collections.Array();
            string args = $"-q -t xa -f {xaRate} -b 4 -c {channels} -F 0 -C 0 \"{wavPath}\" \"{xaPath}\"";
            GD.Print($"[PsxAvEnc] cmd: {probe.Path} {args}");
            int exit = OS.Execute(probe.Path!,
                new[] { "-q", "-t", "xa", "-f", xaRate.ToString(), "-b", "4",
                        "-c", channels.ToString(), "-F", "0", "-C", "0",
                        wavPath, xaPath },
                output, /* readStderr */ true);
            foreach (var line in output)
            {
                string text = line.AsString().TrimEnd('\r', '\n');
                if (text.Length > 0) GD.Print($"[psxavenc] {text}");
            }
            if (exit != 0 || !File.Exists(xaPath))
            {
                GD.PushWarning($"[PS1Godot] XA convert '{clipName}': psxavenc exit {exit}.");
                return null;
            }
            byte[] result = File.ReadAllBytes(xaPath);
            GD.Print($"[PsxAvEnc] '{clipName}': psxavenc OK ({result.Length} B XA).");
            return result;
        }
        catch (Exception ex)
        {
            GD.PushWarning($"[PS1Godot] XA convert '{clipName}' failed: {ex.Message}");
            return null;
        }
        finally
        {
            try { if (File.Exists(wavPath)) File.Delete(wavPath); } catch { }
            try { if (File.Exists(xaPath))  File.Delete(xaPath);  } catch { }
        }
    }

    private static string MakeFilesystemSafe(string name)
    {
        var sb = new System.Text.StringBuilder(name.Length);
        foreach (char c in name)
        {
            sb.Append(char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_');
        }
        return sb.Length == 0 ? "clip" : sb.ToString();
    }

    // Minimal 16-bit PCM mono WAV writer — enough for psxavenc to ingest.
    // We could use Godot's AudioStreamWav.SaveToWav but that takes a
    // resource path inside res:// and we want a temp file outside the
    // project tree.
    private static void WriteMonoWav(string path, short[] pcm, int rate)
    {
        using var fs = new FileStream(path, FileMode.Create, System.IO.FileAccess.Write);
        using var w = new BinaryWriter(fs);
        int dataSize = pcm.Length * 2;
        // RIFF header
        w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
        w.Write((uint)(36 + dataSize));
        w.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
        // fmt chunk
        w.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
        w.Write((uint)16);              // PCM fmt size
        w.Write((ushort)1);             // PCM format
        w.Write((ushort)1);             // mono
        w.Write((uint)rate);            // sample rate
        w.Write((uint)(rate * 2));      // byte rate
        w.Write((ushort)2);             // block align
        w.Write((ushort)16);            // bits per sample
        // data chunk
        w.Write(System.Text.Encoding.ASCII.GetBytes("data"));
        w.Write((uint)dataSize);
        foreach (short s in pcm) w.Write(s);
    }
}
#endif
