using Godot;

namespace PS1Godot;

// Root node of a PS1 scene. Maps to splashpack header fields at export time.
// Unity equivalent: PSXSceneExporter MonoBehaviour in SplashEdit.
//
// `///` doc-comments below surface as field tooltips in Godot 4's
// Inspector — hover any [Export] field to see plain-language guidance.
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
    /// <summary>
    /// Authoring label that drives default budgets + editor overlays.
    /// Exporter collapses to one of two render paths: BVH (outdoor) or
    /// Room/portal (interior). Pick the closest match.
    /// </summary>
    [Export] public SceneTypeKind SceneType { get; set; } = SceneTypeKind.ExplorationOutdoor;

    [ExportGroup("Budgets (authoring only, no runtime effect)")]
    /// <summary>
    /// Soft cap shown in the dock as a budget bar. Greens at &lt;80%, ambers
    /// 80–95%, reds 95+%. No runtime effect — the export still runs over
    /// budget; the bar nudges you toward optimization.
    /// </summary>
    [Export(PropertyHint.Range, "0,20000,100")]
    public int TargetTriangles { get; set; } = 2000;
    /// <summary>
    /// Maximum live GameObject count (player + dynamic actors). Runtime
    /// has hard caps; this is your soft authoring target.
    /// </summary>
    [Export(PropertyHint.Range, "0,64,1")]
    public int MaxActors { get; set; } = 8;
    /// <summary>
    /// Soft cap on transient effects (particles, decals, animated UI).
    /// Authoring-only — surface a warning when exceeded.
    /// </summary>
    [Export(PropertyHint.Range, "0,128,1")]
    public int MaxEffects { get; set; } = 16;
    /// <summary>
    /// Target unique tpages this scene's textures occupy. PSX VRAM has 16
    /// tpages total; staying under 8–10 leaves room for UI + skinned
    /// characters.
    /// </summary>
    [Export(PropertyHint.Range, "0,32,1")]
    public int MaxTexturePages { get; set; } = 8;

    [ExportGroup("Player")]
    /// <summary>
    /// Player's eye height in meters. Camera sits at this height above the
    /// nav-region floor in first-person mode; third-person uses the
    /// PS1Player's child Camera3D offset instead.
    /// </summary>
    [Export(PropertyHint.Range, "0.3,3.0,0.05,suffix:m")]
    public float PlayerHeight { get; set; } = 1.7f;
    /// <summary>
    /// Collision radius for player vs. world push-back. Smaller values =
    /// player squeezes through narrow gaps. Doesn't affect navregion
    /// boundaries (those clamp by polygon edge).
    /// </summary>
    [Export(PropertyHint.Range, "0.1,2.0,0.05,suffix:m")]
    public float PlayerRadius { get; set; } = 0.3f;
    /// <summary> Walk speed in meters per second. </summary>
    [Export(PropertyHint.Range, "0.1,20.0,0.1,suffix:m/s")]
    public float MoveSpeed { get; set; } = 3.0f;
    /// <summary> Sprint speed in meters per second (when sprint button held). </summary>
    [Export(PropertyHint.Range, "0.1,20.0,0.1,suffix:m/s")]
    public float SprintSpeed { get; set; } = 6.0f;
    /// <summary>
    /// Apex height of a single jump in meters. Runtime back-solves jump
    /// velocity from this + Gravity.
    /// </summary>
    [Export(PropertyHint.Range, "0.1,10.0,0.1,suffix:m")]
    public float JumpHeight { get; set; } = 1.2f;
    /// <summary> Downward acceleration in meters per second squared. </summary>
    [Export(PropertyHint.Range, "0.1,40.0,0.1,suffix:m/s²")]
    public float Gravity { get; set; } = 9.81f;

    [ExportGroup("Fog")]
    /// <summary>
    /// Global fog on/off. When off, FogColor / Density / Near / Far are
    /// ignored and the scene clears to BackgroundColor (or black).
    /// </summary>
    [Export] public bool FogEnabled { get; set; } = false;
    /// <summary>
    /// Color geometry fades toward at distance. Picks the GPU clear color
    /// too unless BackgroundColorEnabled overrides.
    /// </summary>
    [Export] public Color FogColor { get; set; } = new Color(0.5f, 0.5f, 0.6f);
    /// <summary>
    /// Legacy density. Used only when FogNear and FogFar are both 0 (their
    /// defaults). Higher density = closer fog wall. Set FogNear/FogFar
    /// instead for explicit control.
    /// </summary>
    [Export(PropertyHint.Range, "1,100,1")]
    public int FogDensity { get; set; } = 1;
    /// <summary>
    /// Distance where fog starts (PSX GTE-Z units; 0 = legacy density-
    /// derived fogFar/8). Typical values: 2000 (close fog wall) – 8000
    /// (mid-distance haze). 0 keeps the legacy density-based behavior.
    /// </summary>
    [Export(PropertyHint.Range, "0,65535,1")]
    public int FogNear { get; set; } = 0;
    /// <summary>
    /// Distance where fog reaches full color (PSX GTE-Z units; 0 = legacy
    /// density-derived 20000/density). Typical values: 8000–30000. Runtime
    /// clamps Near to Far-1 so an inverted setup stays well-defined.
    /// </summary>
    [Export(PropertyHint.Range, "0,65535,1")]
    public int FogFar { get; set; } = 0;

    [ExportGroup("Background")]
    /// <summary>
    /// When ON, the GPU clear color comes from BackgroundColor instead of
    /// FogColor. Lets interiors keep pitch-black void behind dim mood-lit
    /// geometry while a separate fog tint still ramps mid-distance haze.
    /// (v32+)
    /// </summary>
    [Export] public bool BackgroundColorEnabled { get; set; } = false;
    /// <summary>
    /// Scene-level backdrop tone (only applies when BackgroundColorEnabled
    /// is ON). Independent of fog. Default black for interiors; brighter
    /// for stylized exteriors.
    /// </summary>
    [Export] public Color BackgroundColor { get; set; } = new Color(0f, 0f, 0f);

    [ExportGroup("Scripting")]
    /// <summary>
    /// Lua script attached to this scene's onSceneCreationStart /
    /// onSceneCreationEnd hooks. Use for scene-level setup (camera lock,
    /// initial Music.Play, controls toggling). Per-object scripts go on
    /// PS1MeshInstance.ScriptFile instead.
    /// </summary>
    [Export(PropertyHint.File, "*.lua")]
    public string SceneLuaFile { get; set; } = "";

    [ExportGroup("Audio")]
    /// <summary>
    /// Audio clips this scene loads. SPU-routed clips ship in the .spu
    /// sidecar; XA-routed stream from .xa. Each entry is a PS1AudioClip
    /// resource — set route, loop, and source .wav there.
    /// </summary>
    [Export]
    public Godot.Collections.Array<PS1AudioClip> AudioClips { get; set; } = new();

    /// <summary>
    /// Sequenced background music (.mid → PS1M). Up to 8 sequences per
    /// scene. Lua plays by name via Music.Play("track_name"). Each entry
    /// references a PS1MusicSequence resource pointing at a .mid file.
    /// </summary>
    [Export]
    public Godot.Collections.Array<PS1MusicSequence> MusicSequences { get; set; } = new();

    /// <summary>
    /// Scene-wide instrument bank. Sequences reference instruments by
    /// ProgramId; sharing instruments across sequences avoids duplicating
    /// samples in SPU RAM. Exporter dedups against PS1MusicChannel
    /// references.
    /// </summary>
    [Export]
    public Godot.Collections.Array<PS1Instrument> Instruments { get; set; } = new();

    /// <summary>
    /// Drum kits shared across sequences (note → sample mappings).
    /// Exporter dedups same as Instruments.
    /// </summary>
    [Export]
    public Godot.Collections.Array<PS1DrumKit> DrumKits { get; set; } = new();

    [ExportGroup("Sound (Phase 5)")]
    /// <summary>
    /// Composite SFX — frame-keyed event lists callable from Lua via
    /// Sound.PlayMacro("name"). Pulls from the SFX voice pool (won't fight
    /// reserved music voices). See PS1SoundMacro for event syntax.
    /// </summary>
    [Export]
    public Godot.Collections.Array<PS1SoundMacro> SoundMacros { get; set; } = new();

    /// <summary>
    /// Variation pools — Sound.PlayFamily("name") picks one of the family's
    /// clips with author-set pitch/volume/pan jitter. Replaces hand-baked
    /// footstep_01..08 with two clean clips + jitter params.
    /// </summary>
    [Export]
    public Godot.Collections.Array<PS1SoundFamily> SoundFamilies { get; set; } = new();

    [ExportGroup("Scene loading")]
    /// <summary>
    /// Additional scenes packaged with the open scene. Open scene exports
    /// as scene_0; SubScenes[0] = scene_1, [1] = scene_2, etc. Lua calls
    /// Scene.Load(N) at runtime to swap. Each PackedScene must have a
    /// PS1Scene root.
    /// </summary>
    [Export]
    public Godot.Collections.Array<PackedScene> SubScenes { get; set; } = new();

    [ExportGroup("Export")]
    /// <summary>
    /// World-to-PSX scale divisor. Godot units / GteScaling = PSX
    /// fixed-point units. Default 4.0 fits room-scale Godot demos. Higher
    /// values shrink the scene (large worlds stay inside int16 fp12);
    /// lower amplifies (1 Godot = 1 PSX unit).
    /// </summary>
    [Export(PropertyHint.Range, "0.1,1000.0,0.1")]
    public float GteScaling { get; set; } = 4.0f;

    [ExportGroup("Round-trip")]
    /// <summary>
    /// Source .blend file this scene was authored in. "Send to Blender"
    /// opens this file and auto-runs the import-metadata operator. Empty
    /// = launch Blender with no file open. res:// or absolute paths both
    /// work.
    /// </summary>
    [Export(PropertyHint.File, "*.blend")]
    public string SourceBlendFile { get; set; } = "";
}
