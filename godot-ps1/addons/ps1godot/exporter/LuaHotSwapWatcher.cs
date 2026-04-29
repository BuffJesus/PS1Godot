#if TOOLS
using System.Collections.Generic;
using System.IO;
using Godot;

namespace PS1Godot.Exporter;

// Watches the .lua files exported in the most recent Run-on-PSX export
// and writes res://build/hotswap.luac whenever one changes on disk.
//
// Runtime polls that file once a second via PCdrv. New version + matching
// file index → bytecode replaced + objects re-registered without firing
// onCreate. See psxsplash-main/src/lua.cpp Lua::TryHotSwap.
//
// Scope: object scripts only. Scene-level scripts (PS1Scene.SceneLuaFile,
// SceneCollector index 0 + SceneData.SceneLuaFileIndex) need a full
// restart because the runtime caches resolved per-scene event refs at
// scene-load time. Logged + skipped here so authors aren't confused.
//
// File format (little-endian, total = 12 + len):
//   [4] magic 'PHSW'
//   [4] u32 version (monotonic; bumped per write)
//   [2] u16 fileIndex (matches splashpack lua-table position)
//   [2] u16 codeLen
//   [N] u8  code  (post-LuaDecimalRewriter source text)
//
// Polling interval (1 s) is editor-side and intentionally generous —
// we don't want every keystroke triggering a write. The runtime side
// polls at the same cadence so end-to-end latency is ~2 s worst-case.
public partial class LuaHotSwapWatcher : Node
{
    public const string HotswapFilename = "hotswap.luac";
    public const string VersionFilename = "hotswap.version";
    private const uint Magic = 0x57534850u; // 'PHSW' little-endian

    // Time between filesystem polls. Half a second is fast enough that
    // the iteration feels snappy, slow enough that we're not pegging the
    // editor's I/O on a project full of .lua files.
    private const double PollIntervalSec = 0.5;

    private double _accum;

    // Map captured at the end of the most recent scene_0 export. Keys are
    // res:// paths; values are (fileIndex, lastWriteUtc).
    // Re-populated by SetActiveSceneMap on every export. Empty until
    // the first export — hot-swap is a no-op before then.
    private readonly Dictionary<string, ScriptEntry> _watched = new();
    private int _sceneScriptIndex = -1;
    private string _buildDir = "";
    private uint _lastWrittenVersion;

    private record struct ScriptEntry(int FileIndex, System.DateTime LastWriteUtc);

    // Called from PS1GodotPlugin.ExportOneScene after scene_0 exports.
    // Hot-swap targets the active boot scene only — sub-scenes loaded via
    // Scene.Load require a re-export anyway because their bytecode lives
    // in a different splashpack.
    public void SetActiveSceneMap(string buildDir, IEnumerable<(string SourcePath, int FileIndex)> entries, int sceneScriptIndex)
    {
        _buildDir = buildDir;
        _sceneScriptIndex = sceneScriptIndex;

        var fresh = new Dictionary<string, ScriptEntry>();
        foreach (var (path, idx) in entries)
        {
            if (string.IsNullOrEmpty(path)) continue;
            string abs = ProjectSettings.GlobalizePath(path);
            var stamp = File.Exists(abs) ? File.GetLastWriteTimeUtc(abs) : System.DateTime.MinValue;
            fresh[abs] = new ScriptEntry(idx, stamp);
        }

        _watched.Clear();
        foreach (var kv in fresh) _watched[kv.Key] = kv.Value;

        _lastWrittenVersion = ReadPersistedVersion(buildDir);
        GD.Print($"[PS1Godot] Hot-swap: tracking {_watched.Count} script(s) in {buildDir}");
    }

    public override void _Process(double delta)
    {
        if (_watched.Count == 0 || string.IsNullOrEmpty(_buildDir)) return;
        _accum += delta;
        if (_accum < PollIntervalSec) return;
        _accum = 0;

        // Snapshot keys before mutating dictionary values inside the loop.
        var keys = new List<string>(_watched.Keys);
        foreach (var abs in keys)
        {
            if (!File.Exists(abs)) continue;
            var current = File.GetLastWriteTimeUtc(abs);
            var entry = _watched[abs];
            if (current <= entry.LastWriteUtc) continue;

            _watched[abs] = entry with { LastWriteUtc = current };

            if (entry.FileIndex == _sceneScriptIndex)
            {
                GD.Print($"[PS1Godot] Hot-swap: '{abs}' is the scene script (index {entry.FileIndex}) — needs full restart, skipping.");
                continue;
            }
            WriteHotswap(abs, entry.FileIndex);
        }
    }

    private void WriteHotswap(string absLuaPath, int fileIndex)
    {
        string source;
        try { source = File.ReadAllText(absLuaPath); }
        catch (System.Exception ex)
        {
            GD.PushWarning($"[PS1Godot] Hot-swap: could not read '{absLuaPath}': {ex.Message}");
            return;
        }

        // Same rewrite the exporter applies. Without it the runtime parser
        // rejects any decimal literal the author edited in.
        string rewritten = LuaDecimalRewriter.Rewrite(source);
        var codeBytes = System.Text.Encoding.UTF8.GetBytes(rewritten);
        if (codeBytes.Length > ushort.MaxValue)
        {
            GD.PushError($"[PS1Godot] Hot-swap: '{absLuaPath}' is {codeBytes.Length} bytes, exceeds 64 KiB hot-swap limit. " +
                         "Split the script into multiple .lua files attached to separate nodes, or trim large string literals and comments.");
            return;
        }

        uint nextVersion = _lastWrittenVersion + 1;
        string finalPath = Path.Combine(_buildDir, HotswapFilename);
        string tmpPath = finalPath + ".tmp";

        try
        {
            using (var fs = File.Create(tmpPath))
            using (var bw = new BinaryWriter(fs))
            {
                bw.Write(Magic);
                bw.Write(nextVersion);
                bw.Write((ushort)fileIndex);
                bw.Write((ushort)codeBytes.Length);
                bw.Write(codeBytes);
            }
            // Atomic on NTFS for same-volume rename.
            if (File.Exists(finalPath)) File.Delete(finalPath);
            File.Move(tmpPath, finalPath);

            File.WriteAllText(Path.Combine(_buildDir, VersionFilename), nextVersion.ToString());
            _lastWrittenVersion = nextVersion;

            GD.Print($"[PS1Godot] Hot-swap v{nextVersion}: '{Path.GetFileName(absLuaPath)}' (idx {fileIndex}, {codeBytes.Length} B)");
        }
        catch (System.Exception ex)
        {
            GD.PushWarning($"[PS1Godot] Hot-swap write failed for '{absLuaPath}': {ex.Message}");
        }
    }

    private static uint ReadPersistedVersion(string buildDir)
    {
        try
        {
            string p = Path.Combine(buildDir, VersionFilename);
            if (!File.Exists(p)) return 0;
            return uint.TryParse(File.ReadAllText(p).Trim(), out var v) ? v : 0;
        }
        catch { return 0; }
    }
}
#endif
