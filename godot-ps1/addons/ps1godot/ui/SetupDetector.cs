#if TOOLS
using System;
using System.Collections.Generic;
using System.IO;
using Godot;

namespace PS1Godot.UI;

// Minimum-viable dependency detection for the dock's Setup section.
// Mirrors the Phase 0.5 plan in ROADMAP.md (dependency rows with ✓/✗)
// without yet offering the "Install" buttons those envision — those
// need careful HTTP download + extract logic per dep, deferred to the
// full Phase 0.5 work. For now, authors at least see what's missing.
public static class SetupDetector
{
    public enum Status
    {
        Ok,       // Detected and looks usable.
        Missing,  // Not found; blocks a workflow (e.g., no MIPS → can't Build).
        Optional, // Would be nice; not blocking (e.g., Godot-version hint).
    }

    public readonly struct Row
    {
        public readonly string Name;
        public readonly Status Status;
        public readonly string Detail;  // Short one-liner: version, path, or hint.

        public Row(string name, Status s, string detail)
        {
            Name = name;
            Status = s;
            Detail = detail;
        }
    }

    public static List<Row> Detect()
    {
        var rows = new List<Row>();

        // ── Godot version ───────────────────────────────────────────────
        // Always present (we're running inside it). Report as Optional so
        // it never blocks, but surface the version so authors can tell if
        // they're on an unsupported dev build.
        var v = Engine.GetVersionInfo();
        string godotVer = $"{v["major"]}.{v["minor"]}.{v["patch"]}.{v["status"]}";
        int major = v["major"].AsInt32();
        int minor = v["minor"].AsInt32();
        bool supported = major > 4 || (major == 4 && minor >= 4);
        rows.Add(new Row(
            "Godot",
            supported ? Status.Ok : Status.Optional,
            supported ? godotVer : $"{godotVer} (PS1Godot needs 4.4+)"));

        // ── MIPS toolchain ──────────────────────────────────────────────
        // build-psxsplash.cmd expects mipsel-none-elf-gcc on PATH. Probe
        // the same way: System.Diagnostics.Process would be heavyweight;
        // just check PATH for the executable name with Windows suffix.
        string? mipsPath = FindOnPath("mipsel-none-elf-gcc.exe")
                           ?? FindOnPath("mipsel-none-elf-gcc");
        if (mipsPath != null)
        {
            rows.Add(new Row("MIPS toolchain", Status.Ok, TrimHome(mipsPath)));
        }
        else
        {
            rows.Add(new Row("MIPS toolchain", Status.Missing,
                "Needed to Build psxsplash. See pcsx-redux-main/mips.ps1."));
        }

        // ── make ───────────────────────────────────────────────────────
        string? makePath = FindOnPath("make.exe") ?? FindOnPath("make");
        if (makePath != null)
        {
            rows.Add(new Row("make", Status.Ok, TrimHome(makePath)));
        }
        else
        {
            rows.Add(new Row("make", Status.Missing,
                "Needed to Build psxsplash. MSYS2 or Git Bash bundles it."));
        }

        // ── PCSX-Redux ─────────────────────────────────────────────────
        string? pcsx = System.Environment.GetEnvironmentVariable("PCSX_REDUX_EXE");
        if (!string.IsNullOrEmpty(pcsx) && File.Exists(pcsx))
        {
            rows.Add(new Row("PCSX-Redux", Status.Ok, TrimHome(pcsx)));
        }
        else if (!string.IsNullOrEmpty(pcsx))
        {
            rows.Add(new Row("PCSX-Redux", Status.Missing,
                $"PCSX_REDUX_EXE points at a non-existent file: {pcsx}"));
        }
        else
        {
            string? fallback = FindOnPath("pcsx-redux.exe");
            if (fallback != null)
            {
                rows.Add(new Row("PCSX-Redux", Status.Ok,
                    $"{TrimHome(fallback)} (on PATH; set PCSX_REDUX_EXE to pin)"));
            }
            else
            {
                rows.Add(new Row("PCSX-Redux", Status.Missing,
                    "Needed to Run on PSX. Set PCSX_REDUX_EXE to its path."));
            }
        }

        // ── Blender ───────────────────────────────────────────────────
        // Optional dependency — only the round-trip workflow ("Send to
        // Blender" / opening the .blend) needs it. Same env-var-or-PATH
        // shape as PCSX-Redux above.
        string? blender = ResolveBlenderExe();
        if (blender != null)
        {
            rows.Add(new Row("Blender", Status.Ok, TrimHome(blender)));
        }
        else
        {
            rows.Add(new Row("Blender", Status.Optional,
                "Optional — needed for 'Send to Blender'. Set BLENDER_EXE or add Blender to PATH."));
        }

        // ── psxsplash vendored submodule ───────────────────────────────
        // The vendor tree in psxsplash-main/ drags in a nugget submodule.
        // Zip-downloads of the repo arrive with the submodule empty,
        // which breaks Build silently. Flag it explicitly.
        string nuggetDir = GetProjectAbsolute("../psxsplash-main/third_party/nugget/psyqo");
        if (Directory.Exists(nuggetDir))
        {
            rows.Add(new Row("psxsplash submodules", Status.Ok, "nugget checked out"));
        }
        else
        {
            rows.Add(new Row("psxsplash submodules", Status.Missing,
                "third_party/nugget is empty. Run: git submodule update --init --recursive"));
        }

        return rows;
    }

    /// <summary>
    /// Locate Blender via BLENDER_EXE env var, then PATH, then a couple
    /// of conventional Windows install locations. Public so the
    /// "Send to Blender" handler can use the same resolution logic.
    /// </summary>
    public static string? ResolveBlenderExe()
    {
        string? envPath = System.Environment.GetEnvironmentVariable("BLENDER_EXE");
        if (!string.IsNullOrEmpty(envPath) && File.Exists(envPath)) return envPath;

        string? onPath = FindOnPath("blender.exe") ?? FindOnPath("blender");
        if (onPath != null) return onPath;

        // Conventional install locations as a last resort. Authors who
        // installed elsewhere should set BLENDER_EXE; we don't probe
        // every drive letter.
        var candidates = new[]
        {
            @"C:\Programs\Blender\blender.exe",
            @"C:\Program Files\Blender Foundation\Blender 5.0\blender.exe",
            @"C:\Program Files\Blender Foundation\Blender 4.5\blender.exe",
            @"C:\Program Files\Blender Foundation\Blender 4.4\blender.exe",
            @"C:\Program Files\Blender Foundation\Blender 4.3\blender.exe",
            @"C:\Program Files\Blender Foundation\Blender 4.2\blender.exe",
        };
        foreach (var c in candidates)
        {
            if (File.Exists(c)) return c;
        }
        return null;
    }

    // Walks PATH looking for an exact filename match. Returns the first
    // full path or null. Case-insensitive on Windows is fine because the
    // filesystem itself is.
    private static string? FindOnPath(string filename)
    {
        string? path = System.Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path)) return null;
        char sep = System.OperatingSystem.IsWindows() ? ';' : ':';
        foreach (string dir in path.Split(sep))
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            try
            {
                string candidate = Path.Combine(dir.Trim(), filename);
                if (File.Exists(candidate)) return candidate;
            }
            catch (ArgumentException) { /* malformed entry — skip */ }
        }
        return null;
    }

    private static string TrimHome(string p)
    {
        string? home = System.Environment.GetEnvironmentVariable("USERPROFILE")
                       ?? System.Environment.GetEnvironmentVariable("HOME");
        if (!string.IsNullOrEmpty(home) && p.StartsWith(home, StringComparison.OrdinalIgnoreCase))
        {
            return "~" + p[home.Length..];
        }
        return p;
    }

    // Resolve relative to the project's res:// root so we don't rely on
    // the CWD of whatever launched Godot.
    private static string GetProjectAbsolute(string rel)
    {
        string projectDir = ProjectSettings.GlobalizePath("res://");
        return Path.GetFullPath(Path.Combine(projectDir, rel));
    }
}
#endif
