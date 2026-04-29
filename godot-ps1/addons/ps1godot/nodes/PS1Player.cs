using Godot;

namespace PS1Godot;

// Camera rig style — how the runtime positions the camera relative to
// the player during gameplay. Authoring today; runtime support is a
// Phase 2.5 item (Camera.SetMode). The exporter stamps the chosen mode
// so the runtime picks it up once wired.
public enum PS1CameraMode
{
    ThirdPerson = 0,  // Camera trails behind + above the player (default).
    FirstPerson = 1,  // Camera locked at player head height, player mesh hidden.
    Orbit       = 2,  // Right stick orbits the camera around the player.
    // Author drives the camera via Lua Camera.SetPosition + SetRotation
    // and the runtime never updates it from player position. Used for
    // Resident Evil / FFVII pre-rendered background scenes (ROADMAP
    // Phase 4 stretch). Pair with a PS1UICanvas Image at sortOrder 9999
    // showing the baked backdrop and an invisible PS1MeshInstance traced
    // over the BG for collision.
    FixedPreRendered = 3,
}

// Spawn point for the PS1 player. Place one in each scene where you
// want the player to appear; the exporter reads this node's world
// transform into the splashpack's playerStart fields.
//
// If the PS1Player has a Camera3D child, its local transform defines
// the initial camera rig offset (behind/above for 3rd-person, at head
// for 1st-person). No Camera3D child → runtime uses a default offset.
//
// If no PS1Player is in the scene, the exporter falls back to the first
// Camera3D it finds — preserves older demo scenes that were authored
// before this node existed.
//
// Player physics (height, radius, speeds, gravity) still live on
// PS1Scene — they're scene-global, not per-player. Future: per-
// character stats move onto the Phase 2.6 RPG toolkit's AttributeSet.
[Tool]
[GlobalClass]
[Icon("res://addons/ps1godot/icons/ps1_player.svg")]
public partial class PS1Player : Node3D
{
    [ExportGroup("Camera")]
    /// <summary>
    /// Authoring hint for the camera rig. ThirdPerson = trails behind +
    /// above the player (uses the child Camera3D's offset). FirstPerson =
    /// camera at player head, avatar mesh hidden. Orbit = right-stick
    /// rotates around the player. FixedPreRendered = camera ignores the
    /// player; author drives via Lua Camera.SetMode("fixed") for Resident
    /// Evil / FFVII style scenes.
    /// </summary>
    [Export] public PS1CameraMode CameraMode { get; set; } = PS1CameraMode.ThirdPerson;

    [ExportGroup("Avatar")]
    /// <summary>
    /// Texture for the auto-wired avatar mesh. Lives here (not on the
    /// nested FBX mesh's material_override) because instanced-scene
    /// overrides break when Mixamo renames internal mesh nodes. Setting it
    /// here survives re-imports. Wired into the ps1_default shader's
    /// albedo_tex parameter by _EnterTree.
    /// </summary>
    private Texture2D? _avatarTexture;
    [Export] public Texture2D? AvatarTexture
    {
        get => _avatarTexture;
        set
        {
            _avatarTexture = value;
            // Re-apply when the inspector changes this mid-session so the
            // editor preview updates live. CallDeferred dedups with the
            // _EnterTree call during scene load.
            if (Engine.IsEditorHint() && IsInsideTree())
            {
                CallDeferred(MethodName.ApplyPS1DefaultsToAvatar);
            }
        }
    }

    public override void _EnterTree()
    {
        // Editor-only: apply the PS1 shader + a sensibly-sized cull margin to
        // any raw MeshInstance3D descendants (e.g. the inner mesh inside an
        // instanced Mixamo FBX character). Without this, FBX-imported avatars
        // render in the editor with whatever StandardMaterial3D Godot's
        // importer attached, and PS1MeshInstance._EnterTree never runs on
        // them — so the user sees a non-PS1-looking mesh + Godot's default
        // AABB gizmo.
        //
        // Runs deferred because children of instanced PackedScenes aren't
        // reliably in the tree yet when the parent's _EnterTree fires.
        if (Engine.IsEditorHint())
        {
            CallDeferred(MethodName.ApplyPS1DefaultsToAvatar);
        }
    }

    private void ApplyPS1DefaultsToAvatar()
    {
        var ps1 = ResourceLoader.Load<ShaderMaterial>("res://addons/ps1godot/shaders/ps1_default.tres");
        string avatarInfo = AvatarTexture == null
            ? "none"
            : $"'{AvatarTexture.ResourcePath ?? "(no path)"}'";
        GD.Print($"[PS1Godot] PS1Player._EnterTree → ApplyPS1DefaultsToAvatar: ps1_default={(ps1 != null ? "loaded" : "NULL")}, AvatarTexture={avatarInfo}");
        WalkAndApply(this, ps1, AvatarTexture);
    }

    private static void WalkAndApply(Node n, ShaderMaterial? ps1, Texture2D? avatarTexture)
    {
        foreach (var child in n.GetChildren())
        {
            if (child is MeshInstance3D mi && child is not PS1MeshInstance)
            {
                // Promote the override to a per-mesh ps1_default so the editor
                // preview matches what the PSX runtime will render. Prefer
                // PS1Player.AvatarTexture (discoverable, survives FBX
                // re-imports) over whatever the existing override carries.
                // Falls back to the StandardMaterial3D's AlbedoTexture for
                // backwards compat with scenes that set material_override
                // directly. Users who hand-authored a ShaderMaterial (or
                // already applied ps1_default) are left alone, except to
                // update the avatar texture if one is set.
                if (ps1 != null)
                {
                    Texture2D? albedo = avatarTexture;
                    Color tint = new Color(1, 1, 1, 1);
                    if (albedo == null && mi.MaterialOverride is StandardMaterial3D std)
                    {
                        albedo = std.AlbedoTexture;
                        tint = std.AlbedoColor;
                    }
                    if (mi.MaterialOverride is not ShaderMaterial)
                    {
                        var dup = (ShaderMaterial)ps1.Duplicate(true);
                        if (albedo != null)
                        {
                            dup.SetShaderParameter("albedo_tex", albedo);
                        }
                        dup.SetShaderParameter("tint_color", tint);
                        mi.MaterialOverride = dup;
                    }
                    else if (albedo != null)
                    {
                        // Already a ShaderMaterial — just sync the texture
                        // (e.g., user toggled AvatarTexture at design time).
                        ((ShaderMaterial)mi.MaterialOverride).SetShaderParameter("albedo_tex", albedo);
                    }
                }

                // Dynamic cull margin — always apply, even if the demo.tscn
                // has a stale 2.0 override carried over from the old constant
                // default. Mesh-proportional pad sized to 10 % of the largest
                // AABB edge (clamped [0.1, 2.0]) covers the PS1 vertex-snap
                // shader's pixel-scale jitter without the giant yellow cage
                // a constant 2 m wraps around a 0.1 m prop.
                if (mi.Mesh != null)
                {
                    var size = mi.Mesh.GetAabb().Size;
                    float maxEdge = Mathf.Max(size.X, Mathf.Max(size.Y, size.Z));
                    float dyn = Mathf.Clamp(maxEdge * 0.1f, 0.1f, 2.0f);
                    mi.ExtraCullMargin = dyn;
                }
            }
            WalkAndApply(child, ps1, avatarTexture);
        }
    }
}
