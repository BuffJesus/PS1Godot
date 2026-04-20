using Godot;

namespace PS1Godot;

// Root node of a PS1 scene. Maps to splashpack header fields at export time.
// Unity equivalent: PSXSceneExporter MonoBehaviour in SplashEdit.
[Tool]
[GlobalClass]
public partial class PS1Scene : Node3D
{
    public enum SceneTypeKind
    {
        Exterior = 0,  // BVH frustum culling
        Interior = 1,  // Room/portal culling
    }

    [ExportGroup("Scene")]
    [Export] public SceneTypeKind SceneType { get; set; } = SceneTypeKind.Exterior;

    [ExportGroup("Player")]
    [Export] public Vector3 PlayerStartPosition { get; set; } = Vector3.Zero;
    [Export] public Vector3 PlayerStartRotation { get; set; } = Vector3.Zero;
    [Export(PropertyHint.Range, "0.3,3.0,0.05")]
    public float PlayerHeight { get; set; } = 1.7f;
    [Export(PropertyHint.Range, "0.1,2.0,0.05")]
    public float PlayerRadius { get; set; } = 0.3f;
    [Export(PropertyHint.Range, "0.1,20.0,0.1")]
    public float MoveSpeed { get; set; } = 3.0f;
    [Export(PropertyHint.Range, "0.1,20.0,0.1")]
    public float SprintSpeed { get; set; } = 6.0f;
    [Export(PropertyHint.Range, "0.1,10.0,0.1")]
    public float JumpHeight { get; set; } = 1.2f;
    [Export(PropertyHint.Range, "0.1,40.0,0.1")]
    public float Gravity { get; set; } = 9.81f;

    [ExportGroup("Fog")]
    [Export] public bool FogEnabled { get; set; } = false;
    [Export] public Color FogColor { get; set; } = new Color(0.5f, 0.5f, 0.6f);
    [Export(PropertyHint.Range, "1,10,1")]
    public int FogDensity { get; set; } = 5;

    [ExportGroup("Scripting")]
    [Export(PropertyHint.File, "*.lua")]
    public string SceneLuaFile { get; set; } = "";

    [ExportGroup("Audio")]
    [Export]
    public Godot.Collections.Array<PS1AudioClip> AudioClips { get; set; } = new();

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
