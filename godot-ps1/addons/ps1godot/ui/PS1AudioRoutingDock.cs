#if TOOLS
using Godot;
using System.Collections.Generic;

namespace PS1Godot.UI;

// Bottom-panel tab — pick a clip from the active scene, see what route
// the export pipeline will resolve it to, and audition the SPU path
// directly through Godot's AudioStreamPlayer. The other three routes
// (PS2M, CDDA, XA) need the running PS1 runtime; the buttons are
// present for parity but disabled with tooltips that explain why.
//
// Mirrors the SceneCollector.ResolveAudioRoute heuristic so the
// "(resolves to)" annotation matches what the exporter will actually
// pick — catches the "I marked it Auto and got XA but expected SPU"
// surprise at edit time.
[Tool]
public partial class PS1AudioRoutingDock : VBoxContainer
{
    private OptionButton _clipPicker = null!;
    private Label        _metaLabel  = null!;
    private Button       _spuBtn     = null!;
    private Button       _ps2mBtn    = null!;
    private Button       _cddaBtn    = null!;
    private Button       _xaBtn      = null!;
    private Label        _logLabel   = null!;

    private AudioStreamPlayer? _activePlayer;

    public PS1AudioRoutingDock()
    {
        Name = "PS1 Audio";
        SizeFlagsVertical = SizeFlags.ExpandFill;
        AddThemeConstantOverride("separation", 8);
        BuildUI();
    }

    private void BuildUI()
    {
        var margin = new MarginContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        margin.AddThemeConstantOverride("margin_top",    8);
        margin.AddThemeConstantOverride("margin_bottom", 8);
        margin.AddThemeConstantOverride("margin_left",   8);
        margin.AddThemeConstantOverride("margin_right",  8);
        AddChild(margin);

        var inner = new VBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        inner.AddThemeConstantOverride("separation", 8);
        margin.AddChild(inner);

        var header = new Label
        {
            Text = "Audio Routing Test",
        };
        header.AddThemeColorOverride("font_color", new Color(0.7f, 0.85f, 1f));
        inner.AddChild(header);

        var hint = new Label
        {
            Text = "Pick a clip from PS1Scene.AudioClips. SPU plays via Godot's " +
                   "AudioStreamPlayer; the other routes require the running PS1 runtime.",
            AutowrapMode = TextServer.AutowrapMode.Word,
        };
        hint.AddThemeColorOverride("font_color", new Color(0.65f, 0.65f, 0.65f));
        inner.AddChild(hint);

        // ── Clip picker + refresh ──────────────────────────────────────────
        var pickerRow = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        inner.AddChild(pickerRow);

        _clipPicker = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _clipPicker.GetPopup().AboutToPopup += RebuildClipMenu;
        _clipPicker.ItemSelected += _ => RefreshMeta();
        pickerRow.AddChild(_clipPicker);

        var refresh = new Button { Text = "↻", TooltipText = "Re-scan PS1Scene.AudioClips" };
        refresh.Pressed += RebuildClipMenu;
        pickerRow.AddChild(refresh);

        // ── Metadata line ──────────────────────────────────────────────────
        _metaLabel = new Label
        {
            Text = "(no clip selected)",
            AutowrapMode = TextServer.AutowrapMode.Word,
        };
        inner.AddChild(_metaLabel);

        // ── Audition buttons ───────────────────────────────────────────────
        var btnRow = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        btnRow.AddThemeConstantOverride("separation", 6);
        inner.AddChild(btnRow);

        _spuBtn = new Button { Text = "▶ SPU", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _spuBtn.TooltipText = "Plays the source .wav through Godot's AudioStreamPlayer. " +
                               "Approximates the SPU path well enough for relative comparison " +
                               "(volume/pitch in macros, repetition in families) — exact PSX " +
                               "ADPCM coloration only audible after F5.";
        _spuBtn.Pressed += () => AuditionSpu();
        btnRow.AddChild(_spuBtn);

        _ps2mBtn = new Button { Text = "▶ PS2M", SizeFlagsHorizontal = SizeFlags.ExpandFill, Disabled = true };
        _ps2mBtn.TooltipText = "Sequenced playback through a PS1MusicSequence channel. " +
                                "Editor preview not implemented — the sequencer is runtime only. " +
                                "Hear it after F5 by triggering Music.Play(\"<sequence_name>\") in Lua.";
        btnRow.AddChild(_ps2mBtn);

        _cddaBtn = new Button { Text = "▶ CDDA", SizeFlagsHorizontal = SizeFlags.ExpandFill, Disabled = true };
        _cddaBtn.TooltipText = "Red-book CD audio. Only available from a built ISO; PCSX-Redux's " +
                                "loadexe path skips the disc, so audition needs a real ISO build " +
                                "(scripts/build-iso). The CddaTrackNumber on the clip picks the track.";
        btnRow.AddChild(_cddaBtn);

        _xaBtn = new Button { Text = "▶ XA", SizeFlagsHorizontal = SizeFlags.ExpandFill, Disabled = true };
        _xaBtn.TooltipText = "XA-ADPCM disc streaming. Played from the .splashpack.spu sidecar " +
                              "by the PS1 runtime; editor preview would need a separate XA decoder. " +
                              "Hear it after F5 — the SPU button above is the closest editor-side " +
                              "approximation.";
        btnRow.AddChild(_xaBtn);

        // ── Log ────────────────────────────────────────────────────────────
        _logLabel = new Label
        {
            Text = "",
            AutowrapMode = TextServer.AutowrapMode.Word,
        };
        _logLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.7f));
        inner.AddChild(_logLabel);

        // Initial population.
        RebuildClipMenu();
    }

    private void RebuildClipMenu()
    {
        // Preserve current selection across the rebuild so the user
        // doesn't lose context when they hit Refresh.
        string previous = "";
        if (_clipPicker.Selected >= 0 && _clipPicker.Selected < _clipPicker.ItemCount)
            previous = _clipPicker.GetItemMetadata(_clipPicker.Selected).AsString() ?? "";

        _clipPicker.Clear();

        var clips = ScanScene();
        if (clips.Count == 0)
        {
            _clipPicker.AddItem("(scene has no PS1Scene.AudioClips)");
            _clipPicker.SetItemDisabled(0, true);
            _metaLabel.Text = "Open a scene with a PS1Scene root and add at least one " +
                              "PS1AudioClip to its AudioClips array.";
            SetButtonsEnabled(false);
            return;
        }

        int restoreIdx = 0;
        foreach (var (name, _) in clips)
        {
            int idx = _clipPicker.ItemCount;
            _clipPicker.AddItem(name);
            _clipPicker.SetItemMetadata(idx, name);
            if (name == previous) restoreIdx = idx;
        }
        _clipPicker.Selected = restoreIdx;
        RefreshMeta();
    }

    private void RefreshMeta()
    {
        var clip = SelectedClip();
        if (clip == null)
        {
            _metaLabel.Text = "(no clip selected)";
            SetButtonsEnabled(false);
            return;
        }

        int adpcmBytes = EstimateAdpcmBytes(clip.Stream);
        byte resolved = ResolveAudioRoute(clip.Route, adpcmBytes, clip.Loop);
        string resolvedLabel = RouteLabel(resolved);
        string authoredLabel = clip.Route.ToString();
        string sizeKb = $"{adpcmBytes / 1024.0:0.0} KB";

        // For Auto, surface why the heuristic landed where it did.
        string autoNote = clip.Route == PS1AudioRoute.Auto
            ? $" (Auto → {resolvedLabel}; threshold = {(clip.Loop ? "32 KB loop" : "24 KB one-shot")})"
            : "";

        _metaLabel.Text =
            $"Route: authored {authoredLabel}{autoNote}\n" +
            $"Loop: {clip.Loop}  •  Estimated ADPCM size: {sizeKb}  •  " +
            $"Residency: {clip.Residency}";

        SetButtonsEnabled(true);
        // SPU button stays enabled when the resolved route is SPU; the
        // other three buttons are always disabled (editor preview not
        // implemented). Re-enable SPU even when resolved=XA so the
        // author can still hear the source.
        _spuBtn.Disabled = false;
    }

    private void SetButtonsEnabled(bool enabled)
    {
        _spuBtn.Disabled  = !enabled;
        // The other three remain disabled regardless — their tooltips
        // explain why. Setting Disabled=true again is harmless.
        _ps2mBtn.Disabled = true;
        _cddaBtn.Disabled = true;
        _xaBtn.Disabled   = true;
    }

    private void AuditionSpu()
    {
        var clip = SelectedClip();
        if (clip?.Stream == null) return;

        // Stop any in-flight player so a quick re-press doesn't stack.
        if (_activePlayer != null && IsInstanceValid(_activePlayer))
            _activePlayer.QueueFree();

        _activePlayer = new AudioStreamPlayer { Stream = clip.Stream };
        AddChild(_activePlayer);
        _activePlayer.Play();

        string name = ResolveClipName(clip);
        _logLabel.Text = $"▶ SPU: '{name}' playing via Godot AudioStreamPlayer.";
    }

    // ─── ResolveAudioRoute mirror (keep in sync with SceneCollector.cs) ──

    private static byte ResolveAudioRoute(PS1AudioRoute authored, int adpcmLen, bool loop)
    {
        switch (authored)
        {
            case PS1AudioRoute.SPU:  return 0;
            case PS1AudioRoute.XA:   return 1;
            case PS1AudioRoute.CDDA: return 2;
            case PS1AudioRoute.Auto:
            default:
                if (loop && adpcmLen > 32 * 1024) return 1;
                if (!loop && adpcmLen > 24 * 1024) return 1;
                return 0;
        }
    }

    private static string RouteLabel(byte resolved) => resolved switch
    {
        0 => "SPU",
        1 => "XA",
        2 => "CDDA",
        _ => $"unknown ({resolved})",
    };

    // Ballpark ADPCM size used only for the resolved-route preview.
    // PSX ADPCM packs 28 mono PCM samples into 16 bytes per block.
    // Stereo wavs get downmixed to mono in the exporter, so divide by
    // channel count first. Falls back to 0 (= SPU side of the
    // threshold) when the stream isn't a parseable WAV.
    private static int EstimateAdpcmBytes(AudioStream? stream)
    {
        if (stream is not AudioStreamWav wav) return 0;
        if (wav.Data == null || wav.Data.Length == 0) return 0;

        int bytesPerSample = wav.Format switch
        {
            AudioStreamWav.FormatEnum.Format8Bits  => 1,
            AudioStreamWav.FormatEnum.Format16Bits => 2,
            AudioStreamWav.FormatEnum.ImaAdpcm     => 1, // already-compressed; rough upper bound
            _ => 2,
        };
        int channels = wav.Stereo ? 2 : 1;
        long monoSamples = wav.Data.Length / (bytesPerSample * channels);
        long blocks = (monoSamples + 27) / 28;
        long adpcm = blocks * 16;
        return adpcm > int.MaxValue ? int.MaxValue : (int)adpcm;
    }

    // ─── Scene helpers ───────────────────────────────────────────────────

    private PS1AudioClip? SelectedClip()
    {
        if (_clipPicker.Selected < 0) return null;
        string name = _clipPicker.GetItemMetadata(_clipPicker.Selected).AsString() ?? "";
        if (string.IsNullOrEmpty(name)) return null;
        foreach (var (n, c) in ScanScene())
            if (n == name) return c;
        return null;
    }

    private static List<(string name, PS1AudioClip clip)> ScanScene()
    {
        var result = new List<(string, PS1AudioClip)>();
        var root = EditorInterface.Singleton?.GetEditedSceneRoot();
        if (root == null) return result;
        var ps1Scene = FindPS1Scene(root);
        if (ps1Scene?.AudioClips == null) return result;
        foreach (var clip in ps1Scene.AudioClips)
        {
            if (clip == null) continue;
            string n = ResolveClipName(clip);
            if (!string.IsNullOrEmpty(n)) result.Add((n, clip));
        }
        result.Sort((a, b) => System.StringComparer.OrdinalIgnoreCase.Compare(a.Item1, b.Item1));
        return result;
    }

    private static string ResolveClipName(PS1AudioClip? clip)
    {
        if (clip == null) return "";
        if (!string.IsNullOrWhiteSpace(clip.ClipName)) return clip.ClipName;
        if (!string.IsNullOrEmpty(clip.Stream?.ResourcePath))
            return System.IO.Path.GetFileNameWithoutExtension(clip.Stream.ResourcePath);
        return "";
    }

    private static PS1Scene? FindPS1Scene(Node n)
    {
        if (n is PS1Scene s) return s;
        foreach (var c in n.GetChildren())
            if (c is Node child)
            {
                var found = FindPS1Scene(child);
                if (found != null) return found;
            }
        return null;
    }
}
#endif
