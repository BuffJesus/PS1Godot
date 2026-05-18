#if TOOLS
using Godot;

namespace PS1Godot;

// UE editor port plan pick #6 — Project Settings → PS1Godot section.
//
// Registers PS1-specific knobs under Godot's Project Settings so the
// values that used to be hardcoded consts (VRAM budget, SPU budget)
// or env vars (GODOT_EXE, PCSX_REDUX_EXE) live in one author-
// discoverable place — Editor → Project → Project Settings →
// PS1Godot. Defaults match the prior hardcoded values, so existing
// projects without explicit overrides keep behaving identically.
//
// Readers (Get*) fall back to the original defaults when a setting
// isn't present — covers the case where the plugin is enabled in a
// project that pre-dates this slice. ProjectSettings persistence is
// handled by Godot; nothing to flush manually.
public static class PS1ProjectSettings
{
    public const int DefaultVramBudgetBytes = 512 * 1024;  // matches SceneStats const
    public const int DefaultSpuBudgetBytes  = 256 * 1024;
    public const int DefaultAudioSampleRateHz = 11025;     // common XA-favouring rate

    private const string KeyVramBudget    = "ps1godot/budgets/vram_bytes";
    private const string KeySpuBudget     = "ps1godot/budgets/spu_bytes";
    private const string KeyAudioRate     = "ps1godot/audio/default_sample_rate_hz";
    private const string KeyGodotExe      = "ps1godot/launcher/godot_exe_override";
    private const string KeyPcsxReduxExe  = "ps1godot/launcher/pcsx_redux_exe_override";
    private const string KeyAutoRunOnSave = "ps1godot/iteration/auto_run_on_save_default";

    // Called once from PS1GodotPlugin._EnterTree. Re-registering is
    // safe — AddPropertyInfo updates the hint on subsequent calls;
    // existing values stay put.
    public static void Register()
    {
        EnsureSetting(KeyVramBudget,    Variant.From(DefaultVramBudgetBytes),
                      PropertyHint.Range, "65536,524288,1024,suffix:B",
                      "VRAM budget per scene (bytes). Default 512 KiB. PSX hardware cap is 1 MiB but the framebuffer / palette areas reserve the upper half.");
        EnsureSetting(KeySpuBudget,     Variant.From(DefaultSpuBudgetBytes),
                      PropertyHint.Range, "65536,520192,1024,suffix:B",
                      "SPU RAM budget per scene (bytes). Default 256 KiB. Hardware cap is ~508 KiB but the BIOS reserves some at the bottom.");
        EnsureSetting(KeyAudioRate,     Variant.From(DefaultAudioSampleRateHz),
                      PropertyHint.Range, "4000,44100,1,suffix:Hz",
                      "Default sample rate for ADPCM conversion when an audio clip doesn't specify one.");
        EnsureSetting(KeyGodotExe,      "",
                      PropertyHint.GlobalFile, "*.exe",
                      "Override path to the Godot editor used for headless reimport (defaults to the GODOT_EXE env var).");
        EnsureSetting(KeyPcsxReduxExe,  "",
                      PropertyHint.GlobalFile, "*.exe",
                      "Override path to PCSX-Redux for the Run-on-PSX action (defaults to the PCSX_REDUX_EXE env var).");
        EnsureSetting(KeyAutoRunOnSave, Variant.From(false),
                      PropertyHint.None, "",
                      "Default state of the Auto Run-on-Save checkbox when a new editor session opens. False = manual F5 each time.");
    }

    public static int GetVramBudgetBytes()
        => GetIntWithFallback(KeyVramBudget, DefaultVramBudgetBytes);

    public static int GetSpuBudgetBytes()
        => GetIntWithFallback(KeySpuBudget, DefaultSpuBudgetBytes);

    public static int GetDefaultAudioSampleRateHz()
        => GetIntWithFallback(KeyAudioRate, DefaultAudioSampleRateHz);

    public static string GetGodotExeOverride()
        => GetStringWithFallback(KeyGodotExe, "");

    public static string GetPcsxReduxExeOverride()
        => GetStringWithFallback(KeyPcsxReduxExe, "");

    public static bool GetAutoRunOnSaveDefault()
        => GetBoolWithFallback(KeyAutoRunOnSave, false);

    // ── Internals ───────────────────────────────────────────────────

    private static void EnsureSetting(string key, Variant defaultValue,
                                       PropertyHint hint, string hintString,
                                       string description)
    {
        // Set the initial default only if the key doesn't exist yet —
        // otherwise we'd clobber author edits on every plugin reload.
        if (!ProjectSettings.HasSetting(key))
        {
            ProjectSettings.SetSetting(key, defaultValue);
        }
        ProjectSettings.SetInitialValue(key, defaultValue);
        var info = new Godot.Collections.Dictionary
        {
            { "name",        key },
            { "type",        (int)defaultValue.VariantType },
            { "hint",        (int)hint },
            { "hint_string", hintString },
        };
        ProjectSettings.AddPropertyInfo(info);
        // ProjectSettings doesn't surface tooltip strings in the UI for
        // custom keys; we keep the description embedded as a comment
        // sidecar via SetAsBasic so it lives in the "Basic" filter
        // group rather than the "Advanced" one.
        ProjectSettings.SetAsBasic(key, true);
        _ = description;  // referenced in source as documentation
    }

    private static int GetIntWithFallback(string key, int fallback)
    {
        if (!ProjectSettings.HasSetting(key)) return fallback;
        var v = ProjectSettings.GetSetting(key);
        if (v.VariantType == Variant.Type.Int) return v.AsInt32();
        if (v.VariantType == Variant.Type.Float) return (int)v.AsDouble();
        return fallback;
    }

    private static string GetStringWithFallback(string key, string fallback)
    {
        if (!ProjectSettings.HasSetting(key)) return fallback;
        var v = ProjectSettings.GetSetting(key);
        return v.VariantType == Variant.Type.String ? (v.AsString() ?? fallback) : fallback;
    }

    private static bool GetBoolWithFallback(string key, bool fallback)
    {
        if (!ProjectSettings.HasSetting(key)) return fallback;
        var v = ProjectSettings.GetSetting(key);
        return v.VariantType == Variant.Type.Bool ? v.AsBool() : fallback;
    }
}
#endif
