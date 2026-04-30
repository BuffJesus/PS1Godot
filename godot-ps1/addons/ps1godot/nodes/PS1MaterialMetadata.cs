using Godot;
using PS1Godot.Exporter;

namespace PS1Godot;

// Per-material PS1 authoring metadata — the Godot side host for the
// `materials[]` array Phase 2 sidecars carry. Authors right-click in
// the inspector → New PS1MaterialMetadata, fill in the texture page /
// CLUT / atlas / alpha-mode / format-override fields, and reference
// the resource from a PS1MeshInstance's Materials array.
//
// Decoupled from Godot's Material so authors can keep using
// StandardMaterial3D (or any preview material) for the editor look
// without forking the asset. The PS1 metadata travels alongside,
// matched to the underlying material slot by MaterialName which
// mirrors the Blender side's `blender_name` / `material_id` string.
//
// Cross-references:
//   tools/blender-addon/.../properties.py   PS1GodotMaterialProps
//   exporter/PS1Metadata.cs                 wire-identifier contract
//   exporter/BlenderMetadataReader.cs       reader populates these
//   exporter/BlenderMetadataWriter.cs       writer serializes these
[Tool]
[GlobalClass]
public partial class PS1MaterialMetadata : Resource
{
    /// <summary>
    /// Wire identifier matched against the material slot's
    /// Material.ResourceName at round-trip time. When the Blender side
    /// emits `"material_id": "town_wood_dark"` the reader looks up the
    /// matching slot by this name. Empty = fallback to the underlying
    /// Material's ResourceName.
    /// </summary>
    [Export] public string MaterialName { get; set; } = "";

    /// <summary>
    /// Stable IDs — auto-populated by the sidecar reader. MaterialId
    /// is the cross-tool foreign key (think: "town_wood_dark"); the
    /// others bind it into the texture/atlas pipeline.
    /// </summary>
    [Export] public string MaterialId    { get; set; } = "";
    [Export] public string TexturePageId { get; set; } = "";
    [Export] public string ClutId        { get; set; } = "";
    [Export] public string PaletteGroup  { get; set; } = "";

    /// <summary>
    /// Soft hint for the atlas packer + Slot D batching (per-material
    /// here, vs. the per-mesh fallback on PS1MeshInstance). Empty
    /// string treated as "inherit from mesh".
    /// </summary>
    [Export] public AtlasGroup AtlasGroup { get; set; } = AtlasGroup.World;

    /// <summary>
    /// Override the per-mesh BitDepth pick. Auto = let the exporter's
    /// existing logic decide. 4bpp/8bpp/16bpp are explicit overrides.
    /// </summary>
    [Export] public TextureFormat TextureFormat { get; set; } = TextureFormat.Auto;

    /// <summary>
    /// Material's alpha behaviour. Independent of the per-mesh
    /// PS1MeshInstance.AlphaMode hint — a single mesh can host
    /// materials with different alpha policies.
    /// </summary>
    [Export] public AlphaMode AlphaMode { get; set; } = AlphaMode.Opaque;

    /// <summary>
    /// Authoring overrides. ForceNoFilter mostly affects Blender preview
    /// (PSX hardware never filters); Approved16bpp silences the
    /// 16bpp-without-approval validator warning.
    /// </summary>
    [Export] public bool ForceNoFilter { get; set; } = false;
    [Export] public bool Approved16bpp { get; set; } = false;
}
