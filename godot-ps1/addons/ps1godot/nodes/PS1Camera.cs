using Godot;

namespace PS1Godot;

// Camera3D tagged for PS1 export. Kept as a thin wrapper for now so we can
// later enforce PS1-plausible FOV/near/far ranges and drive the GTE H register.
[Tool]
[GlobalClass]
public partial class PS1Camera : Camera3D
{
}
