#if TOOLS
using Godot;

namespace PS1Godot.UI;

// PS1 Lua REPL — UE editor port plan pick #4.
//
// Sends ad-hoc Lua snippets to the running psxsplash via PCdrv (the
// host filesystem the emulator mounts as its disc). Writes the
// snippet to `<build>/repl.lua` and bumps `<build>/repl.ver` with a
// monotonic byte stamp. The psxsplash runtime polls those files
// every ~0.5 s (same cadence as Lua hot-swap) and pcalls fresh
// versions in the global env — see psxsplash-main/src/lua.cpp's
// `TryRepl`.
//
// Slice 1 — fire-and-forget. The dock keeps a local scrollback of
// the commands the author sent + a placeholder line for each ("→
// sent (see PSX console for result)"). Slice 2 can layer a
// response file the runtime writes back to so results show in the
// dock.
//
// PCdrv-only by construction. Surface a warning when the build/
// directory doesn't exist or when the export pipeline is in
// CD-ROM ISO mode (XA-routed clips force ISO). The export
// pipeline's run-mode choice is independent of this dock, so we
// just warn and proceed — author can still queue commands; they
// just won't execute until the next PCdrv run.
public partial class PS1LuaReplDock : VBoxContainer
{
    private LineEdit?  _inputEdit;
    private TextEdit?  _history;
    private Label?     _statusLabel;

    public PS1LuaReplDock()
    {
        Name = "PS1 Lua REPL";
        SizeFlagsVertical   = SizeFlags.ExpandFill;
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        BuildUi();
    }

    private void BuildUi()
    {
        AddThemeConstantOverride("separation", 6);

        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 6);
        AddChild(header);

        _statusLabel = new Label
        {
            Text = "PCdrv-only — result prints to PCSX-Redux's debug console.",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _statusLabel.AddThemeColorOverride("font_color", new Color(0.70f, 0.70f, 0.70f));
        header.AddChild(_statusLabel);

        var clearBtn = new Button { Text = "Clear history" };
        clearBtn.Pressed += () => { if (_history != null) _history.Text = ""; };
        header.AddChild(clearBtn);

        _history = new TextEdit
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            Editable = false,
            ScrollFitContentHeight = false,
            PlaceholderText = "No commands sent yet.",
        };
        _history.AddThemeColorOverride("font_color", new Color(0.85f, 0.85f, 0.85f));
        AddChild(_history);

        var inputRow = new HBoxContainer();
        inputRow.AddThemeConstantOverride("separation", 6);
        AddChild(inputRow);

        inputRow.AddChild(new Label { Text = "Lua >", VerticalAlignment = VerticalAlignment.Center });

        _inputEdit = new LineEdit
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            PlaceholderText = "print(_G.dialogue_test ~= nil)",
        };
        _inputEdit.TextSubmitted += _ => Send();
        inputRow.AddChild(_inputEdit);

        var sendBtn = new Button { Text = "Send" };
        sendBtn.Pressed += Send;
        inputRow.AddChild(sendBtn);
    }

    private void Send()
    {
        if (_inputEdit == null || _history == null || _statusLabel == null) return;
        string snippet = _inputEdit.Text ?? "";
        if (string.IsNullOrWhiteSpace(snippet)) return;

        string buildDir = ResolveBuildDir();
        if (string.IsNullOrEmpty(buildDir) || !DirAccess.DirExistsAbsolute(buildDir))
        {
            _statusLabel.Text = "Build dir not found — run an export first so PCdrv has a working directory.";
            _statusLabel.AddThemeColorOverride("font_color", new Color(0.95f, 0.45f, 0.45f));
            return;
        }

        string luaPath = buildDir.TrimEnd('/', '\\') + "/repl.lua";
        string verPath = buildDir.TrimEnd('/', '\\') + "/repl.ver";

        // Write the snippet first, THEN bump the version stamp — the
        // runtime polls .ver and only reads .lua after it sees a new
        // stamp. If .lua hasn't landed yet, the runtime gets a stale
        // file.
        try
        {
            System.IO.File.WriteAllText(luaPath, snippet);
            // Monotonic stamp = unix ms timestamp. Plain bytes; runtime
            // compares without parsing.
            long stamp = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            System.IO.File.WriteAllText(verPath, stamp.ToString());
        }
        catch (System.Exception ex)
        {
            _statusLabel.Text = $"Write failed: {ex.Message}";
            _statusLabel.AddThemeColorOverride("font_color", new Color(0.95f, 0.45f, 0.45f));
            return;
        }

        _history.Text += $"> {snippet}\n  → sent (see PSX console for result)\n";
        _history.SetCaretLine(_history.GetLineCount() - 1);
        _inputEdit.Text = "";
        _statusLabel.Text = "PCdrv-only — result prints to PCSX-Redux's debug console.";
        _statusLabel.AddThemeColorOverride("font_color", new Color(0.70f, 0.70f, 0.70f));
    }

    // Mirror the PS1GodotPlugin.RepoRoot() logic with a fallback
    // chain — we don't want to take a hard dep on the plugin's
    // private RepoRoot just for this. ProjectSettings.GlobalizePath
    // returns the absolute path equivalent of res://.
    private static string ResolveBuildDir()
    {
        string global = ProjectSettings.GlobalizePath("res://build");
        if (DirAccess.DirExistsAbsolute(global)) return global;
        // Fallback: the export pipeline writes to godot-ps1/build/
        // alongside the project root. ProjectSettings.GlobalizePath
        // resolves res:// to that root.
        string root = ProjectSettings.GlobalizePath("res://");
        return System.IO.Path.Combine(root.TrimEnd('/', '\\'), "build");
    }
}
#endif
