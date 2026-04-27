using Godot;

namespace PS1Godot;

// Root node of a PS1 scene. Maps to splashpack header fields at export time.
// Unity equivalent: PSXSceneExporter MonoBehaviour in SplashEdit.
[Tool]
[GlobalClass]
[Icon("res://addons/ps1godot/icons/ps1_scene.svg")]
public partial class PS1Scene : Node3D
{
    // Scene categories matching the PS1 optimization reference
    // (docs/ps1_large_rpg_optimization_reference.md REF-GAP-4). These are
    // authoring-time labels that drive budgets + editor overlays. The
    // exporter collapses them to the runtime's two render paths:
    //   BVH (0)       → ExplorationOutdoor, TownSquare, Combat, CutsceneCloseup, Menu
    //   Room/portal(1) → Interior, DungeonCorridor
    public enum SceneTypeKind
    {
        ExplorationOutdoor = 0,
        TownSquare = 1,
        Interior = 2,
        DungeonCorridor = 3,
        Combat = 4,
        Menu = 5,
        CutsceneCloseup = 6,
    }

    [ExportGroup("Scene")]
    [Export] public SceneTypeKind SceneType { get; set; } = SceneTypeKind.ExplorationOutdoor;

    // Budget fields are authoring metadata — no runtime impact yet. Editor
    // overlays (Phase 3 dock) will compare these caps against the actual
    // scene contents and warn when exceeded. Rough defaults come from the
    // reference's "exploration outdoor" category; tune per scene.
    [ExportGroup("Budgets (authoring only, no runtime effect)")]
    [Export(PropertyHint.Range, "0,20000,100")]
    public int TargetTriangles { get; set; } = 2000;
    [Export(PropertyHint.Range, "0,64,1")]
    public int MaxActors { get; set; } = 8;
    [Export(PropertyHint.Range, "0,128,1")]
    public int MaxEffects { get; set; } = 16;
    [Export(PropertyHint.Range, "0,32,1")]
    public int MaxTexturePages { get; set; } = 8;

    [ExportGroup("Player")]
    // Player spawn now lives on the PS1Player node — drop one in the
    // scene and place it where you want the player to appear. The old
    // PlayerStartPosition / PlayerStartRotation fields here were never
    // wired through the exporter.
    [Export(PropertyHint.Range, "0.3,3.0,0.05,suffix:m")]
    public float PlayerHeight { get; set; } = 1.7f;
    [Export(PropertyHint.Range, "0.1,2.0,0.05,suffix:m")]
    public float PlayerRadius { get; set; } = 0.3f;
    [Export(PropertyHint.Range, "0.1,20.0,0.1,suffix:m/s")]
    public float MoveSpeed { get; set; } = 3.0f;
    [Export(PropertyHint.Range, "0.1,20.0,0.1,suffix:m/s")]
    public float SprintSpeed { get; set; } = 6.0f;
    [Export(PropertyHint.Range, "0.1,10.0,0.1,suffix:m")]
    public float JumpHeight { get; set; } = 1.2f;
    [Export(PropertyHint.Range, "0.1,40.0,0.1,suffix:m/s²")]
    public float Gravity { get; set; } = 9.81f;

    [ExportGroup("Fog")]
    [Export] public bool FogEnabled { get; set; } = false;
    [Export] public Color FogColor { get; set; } = new Color(0.5f, 0.5f, 0.6f);
    // Higher = thicker / closer fog wall. Lower = fog starts farther
    // away and is fainter. Runtime maps this to fogFarSZ = 20000 /
    // density (so density 1 → fogFar ≈ 20000 GTE-Z, density 100 →
    // ≈200). Fog start is hardcoded by the runtime to fogFar/8 — true
    // independent near/far range is tracked in docs/psxsplash-
    // improvements.md (entry N+3) as a runtime feature request.
    [Export(PropertyHint.Range, "1,100,1")]
    public int FogDensity { get; set; } = 1;

    [ExportGroup("Scripting")]
    [Export(PropertyHint.File, "*.lua")]
    public string SceneLuaFile { get; set; } = "";

    [ExportGroup("Audio")]
    [Export]
    public Godot.Collections.Array<PS1AudioClip> AudioClips { get; set; } = new();

    // Sequenced background music. Each entry parses one .mid file and
    // ships as a PS1M blob in the splashpack. Lua plays by name via
    // Music.Play("..."). Up to 8 sequences per scene (runtime cap).
    [Export]
    public Godot.Collections.Array<PS1MusicSequence> MusicSequences { get; set; } = new();

    // Phase 2.5: scene-wide instrument bank. Sequences in this scene
    // can reference any instrument here by ProgramId. Multiple
    // sequences sharing instruments avoid duplicating samples in SPU
    // RAM — useful for variations on a town theme that all use the
    // same string + bell + bass kit.
    //
    // The bank is exporter-deduplicated: same PS1Instrument resource
    // referenced from multiple PS1MusicChannel.Instrument fields
    // becomes one record in the splashpack. Drop instruments here
    // explicitly when you want a sequence to ProgramChange-swap
    // between them at runtime; one-off instruments referenced only
    // from a channel field can also live here for clarity.
    [Export]
    public Godot.Collections.Array<PS1Instrument> Instruments { get; set; } = new();

    // Drum kits (note → sample mappings) shared across sequences.
    // Same dedup behaviour as Instruments — referenced from
    // PS1MusicSequence.DrumKit fields, exporter collapses repeats.
    [Export]
    public Godot.Collections.Array<PS1DrumKit> DrumKits { get; set; } = new();

    [ExportGroup("Sound (Phase 5)")]
    // Composite sound effects (frame-keyed event lists). Each macro
    // is one Lua-callable entry: Sound.PlayMacro("MacroName"). Macros
    // pull from the SFX voice pool — they don't touch reserved music
    // voices. See PS1SoundMacro for per-event semantics and the
    // strategy doc §15 for the design rationale.
    [Export]
    public Godot.Collections.Array<PS1SoundMacro> SoundMacros { get; set; } = new();

    // Variation pools: Sound.PlayFamily("FamilyName") picks one of
    // the family's clips with author-set pitch/volume/pan jitter.
    // Replaces hand-baked variations (footstep_01..08) with two
    // clean clips + jitter parameters. See PS1SoundFamily.
    [Export]
    public Godot.Collections.Array<PS1SoundFamily> SoundFamilies { get; set; } = new();

    [ExportGroup("Scene loading")]
    // Additional scenes that ship in the same splashpack drop. The currently-
    // open scene exports as scene_0; entries here export as scene_1, scene_2,
    // … in order. Lua calls Scene.Load(N) to swap to scene_N at runtime.
    // Each PackedScene must have a PS1Scene root; the exporter walks each one
    // the same way it walks the main scene. Use this for boss arenas,
    // dream-realm transitions, separate map regions — anywhere a hard scene
    // swap is preferable to streaming or putting everything in one file.
    [Export]
    public Godot.Collections.Array<PackedScene> SubScenes { get; set; } = new();

    [ExportGroup("Export")]
    // World-to-PSX scale: Godot units divided by this land in PSX fixed-point
    // space. At 4.0, a 30-unit floor ends up at ±3.75 PSX units — camera sits
    // 1-2 PSX units out, near-plane math works. Higher values shrink the scene
    // (useful for large worlds to stay in int16 fp12 range); lower values
    // amplify (1 Godot = 1 PSX unit). SplashEdit used 100 for its Unity scenes
    // where 1 unit = a meter in a large world; a 4.0 default is closer to
    // what a room-scale Godot demo expects.
    [Export(PropertyHint.Range, "0.1,1000.0,0.1")]
    public float GteScaling { get; set; } = 4.0f;
}
