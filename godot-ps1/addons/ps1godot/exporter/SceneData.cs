using System.Collections.Generic;
using Godot;

namespace PS1Godot.Exporter;

// Snapshot of a Godot scene reduced to the things the splashpack writer cares
// about. Populated by SceneCollector; consumed by SplashpackWriter.

public sealed class SceneObject
{
    // Base Node3D — covers PS1MeshInstance, raw MeshInstance3D (FBX auto-
    // detect under PS1Player), and PS1MeshGroup (multi-mesh aggregates
    // that don't have a single Mesh on the node itself). PS1-specific
    // properties are read through a local typed cast in the collector.
    public required Node3D Node { get; init; }
    public required PSXMesh Mesh { get; init; }

    // Local-space AABB for the writer's WriteWorldAabb pass. Cached here
    // because PS1MeshGroup has no `Mesh` property to query at write time;
    // for single-mesh objects the collector fills this with
    // Node.Mesh.GetAabb(), for groups it aggregates descendant AABBs.
    public required Aabb LocalAabb { get; init; }

    // Indices into SceneData.Textures — one per mesh surface (parallel to
    // Mesh.GetSurfaceCount). -1 means "this surface is untextured; use
    // FlatColor". Populated by SceneCollector; consumed by PSXMesh.
    public required int[] SurfaceTextureIndices { get; init; }

    // Index into SceneData.LuaFiles, or -1 for no script. Maps to the
    // GameObject.luaFileIndex field the runtime reads to dispatch events.
    public int LuaFileIndex { get; set; } = -1;
}

// One entry in the splashpack's lua-file table. Runtime v20 (full-parser
// build) can accept raw source text via luaL_loadbuffer; once we wire
// luac_psx we'll swap Bytes for precompiled bytecode without changing
// this struct.
public sealed class LuaFileRecord
{
    public required byte[] Bytes { get; init; }
    // res:// path of the source file, used only as a dedup key.
    public required string SourcePath { get; init; }
}

// One pre-encoded ADPCM audio clip. AdpcmData is the bytes that land in
// the .spu sidecar file; the rest goes in the main splashpack's audio
// clip table.
public sealed class AudioClipRecord
{
    public required byte[] AdpcmData { get; init; }
    public required ushort SampleRate { get; init; }
    public required bool Loop { get; init; }
    public required string Name { get; init; }
}

// One pre-serialized PS1M sequenced-music blob. Lua picks it by name
// via Music.Play("..."). The runtime caps sequences per scene at 8
// (matches MusicSequencer::MAX_SEQUENCES).
public sealed class MusicSequenceRecord
{
    public required byte[] Ps1mData { get; init; }   // PS1M binary blob
    public required string Name { get; init; }       // null-truncated to 15 chars in writer
}

// World-space AABB that fires a Lua script when the player's AABB enters
// or leaves it. Serialized as SPLASHPACKTriggerBox (32 bytes).
public sealed class TriggerBoxRecord
{
    public required Vector3 WorldMin { get; init; }  // Godot-space, pre-scale/flip
    public required Vector3 WorldMax { get; init; }
    public required short LuaFileIndex { get; init; } // -1 for none
}

// GameObject-attached component that lets the player interact with an
// object (press button within radius → onInteract fires on the object's
// script). Serialized as Interactable (28 bytes).
public sealed class InteractableRecord
{
    public required ushort GameObjectIndex { get; init; }
    public required float RadiusMeters { get; init; }
    public required byte InteractButton { get; init; }
    public required bool Repeatable { get; init; }
    public required bool ShowPrompt { get; init; }
    public required ushort CooldownFrames { get; init; }
    public required string PromptCanvasName { get; init; } // max 15 chars + null
}

// A single keyframe on an animation track. Linear-interpolated by default;
// the runtime honors all InterpMode values set here.
public sealed class KeyframeRecord
{
    public required ushort Frame { get; init; }          // 0..8191
    public required PS1InterpMode Interp { get; init; }
    public required short V0 { get; init; }              // e.g. pos.X in fp12
    public required short V1 { get; init; }
    public required short V2 { get; init; }
}

// One animation: a named timeline targeting a single GameObject by name,
// carrying a single ObjectPosition track in MVP. Serialized as a 12-byte
// table entry pointing at a 16-byte data block + track + keyframe arrays.
public sealed class AnimationRecord
{
    public required string Name { get; init; }
    public required string TargetObjectName { get; init; }
    public required PS1AnimationTrackType TrackType { get; init; }
    public required ushort TotalFrames { get; init; }
    public required System.Collections.Generic.List<KeyframeRecord> Keyframes { get; init; }
}

// One track inside a CutsceneRecord. Same wire format (12 B
// SPLASHPACKCutsceneTrack) as the single track an AnimationRecord carries.
public sealed class CutsceneTrackRecord
{
    public required string TargetObjectName { get; init; } // empty = no target (e.g. camera tracks)
    public required PS1AnimationTrackType TrackType { get; init; }
    public required System.Collections.Generic.List<KeyframeRecord> Keyframes { get; init; }
}

// One audio cue inside a CutsceneRecord. Serialized as 8 B
// CutsceneAudioEvent (cutscene.hh).
public sealed class CutsceneAudioEventRecord
{
    public required ushort Frame { get; init; }
    public required byte ClipIndex { get; init; } // resolved at collect time
    public required byte Volume { get; init; }    // 0–127
    public required byte Pan { get; init; }       // 0–127, 64 = centered
}

// One cutscene: a named multi-track timeline. Serialized as a 12-byte
// SPLASHPACKCutsceneEntry → 16-byte SPLASHPACKCutscene block → tracks
// → keyframes + (B.2 Phase 3) audio events.
public sealed class CutsceneRecord
{
    public required string Name { get; init; }
    public required ushort TotalFrames { get; init; }
    public required System.Collections.Generic.List<CutsceneTrackRecord> Tracks { get; init; }
    public required System.Collections.Generic.List<CutsceneAudioEventRecord> AudioEvents { get; init; }
}

// Skinned mesh metadata. The mesh itself is an entry in SceneData.Objects
// (exported as a normal mesh at its bind pose); this record describes the
// skin-side data the runtime needs: which GameObject to skin, the bone
// count, and one bone index per triangle vertex (3 bytes per triangle).
// Stage 1 ships with Clips empty; stage 2 populates it with baked
// BakedBoneMatrix[] per clip.
public sealed class SkinnedMeshRecord
{
    public required string Name { get; init; }
    public required ushort GameObjectIndex { get; init; }
    public required byte BoneCount { get; init; }
    // BoneIndices has length = triangleCount * 3, one byte per triangle
    // vertex. Order matches the post-winding-swap vertex order the
    // PSXMesh writer emits so the runtime's skinned render path can
    // walk them in lockstep with the Tri[] array.
    public required byte[] BoneIndices { get; init; }
    // Baked animation clips. Empty means rest-pose-only rendering.
    public System.Collections.Generic.List<SkinClipRecord> Clips { get; init; } = new();
}

// One baked animation clip. Frames is frameCount × boneCount × 24 bytes
// laid out as [frame0_bone0, frame0_bone1, … frame1_bone0, …]. Each
// BakedBoneMatrix = 9 int16 rotation (row-major) + 3 int16 translation
// (fp12, PSX Y-down). Loader reads this blob raw, no further parsing.
public sealed class SkinClipRecord
{
    public required string Name { get; init; }
    public required byte Flags { get; init; }     // bit 0 = loop
    public required byte Fps { get; init; }       // sample rate (1–30)
    public required ushort FrameCount { get; init; }
    public required byte[] FrameData { get; init; }
}

// UI canvas + its widgets. Serialized as a 12-byte descriptor in the UI
// table plus a per-canvas element array elsewhere in the splashpack.
public sealed class UICanvasRecord
{
    public required string Name { get; init; }
    public required PS1UIResidency Residency { get; init; }
    public required bool VisibleOnLoad { get; init; }
    public required byte SortOrder { get; init; }
    public required System.Collections.Generic.List<UIElementRecord> Elements { get; init; }
}

// A single widget inside a UICanvasRecord. Serialized as 48 bytes per
// the runtime's UIElement parse layout in uisystem.cpp:loadFromSplashpack.
public sealed class UIElementRecord
{
    public required string Name { get; init; }
    public required PS1UIElementType Type { get; init; }
    public required bool VisibleOnLoad { get; init; }
    public required short X { get; init; }
    public required short Y { get; init; }
    public required short W { get; init; }
    public required short H { get; init; }
    public required byte ColorR { get; init; }
    public required byte ColorG { get; init; }
    public required byte ColorB { get; init; }
    // Font index for Text elements: 0 = built-in system font,
    // 1+ = index into SceneData.UIFonts. Zero for non-Text.
    public required byte FontIndex { get; init; }
    public required string Text { get; init; }  // empty for non-Text types
}

// A custom UI font ready for splashpack emission. Built by
// SceneCollector from PS1UIFontAsset resources referenced by
// elements. Serialized as a 112-byte UIFontDesc (see
// psxsplash-main/src/uisystem.hh) + pixel data placed in the
// splashpack "dead zone" and referenced by dataOffset.
public sealed class UIFontRecord
{
    public required string Name { get; init; }       // debug / Lua lookup
    public required byte GlyphW { get; init; }
    public required byte GlyphH { get; init; }
    public required ushort VramX { get; init; }      // PSX-VRAM hword coords
    public required ushort VramY { get; init; }
    public required ushort TextureH { get; init; }   // atlas height in pixels
    public required byte[] AdvanceWidths { get; init; }  // length 96 (ASCII 0x20-0x7F)
    public required byte[] PixelData4bpp { get; init; }  // 128 × TextureH bytes
}

// Per-object AABB collider written as SPLASHPACKCollider (32 bytes).
// Runtime uses these for X/Z push-back against walls and props — floor/ground
// goes through NavRegion instead.
public sealed class ColliderRecord
{
    public required Vector3 WorldMin { get; init; }   // Godot-space, pre-scale/flip
    public required Vector3 WorldMax { get; init; }
    public required byte CollisionType { get; init; } // 0 = None, 1 = Solid
    public required byte LayerMask { get; init; }
    public required ushort GameObjectIndex { get; init; } // index into SceneData.Objects
}

// Single convex nav region. For flat-slab auto-emit this is a 4-vertex
// rectangle with a zero-slope plane; for authored PS1NavRegion nodes the
// verts / plane are fit to whatever convex polygon the author drew.
// Portal connectivity is populated by a post-pass in SceneCollector.
public sealed class NavRegionRecord
{
    // Verts in XZ plane, PSX-space fp12 (int32). Stored CCW per the runtime's
    // cross-product test in navregion.cpp.
    public required int[] VertsX { get; init; }
    public required int[] VertsZ { get; init; }
    public required int PlaneA { get; init; }   // floor plane Y = A·x + B·z + D (fp12)
    public required int PlaneB { get; init; }
    public required int PlaneD { get; init; }

    public byte SurfaceType { get; set; } = 0;      // NAV_SURFACE_FLAT
    public byte RoomIndex { get; set; } = 0xFF;     // exterior / unknown
    public byte Flags { get; set; } = 0;            // bit 0 = platform
    public byte WalkoffEdgeMask { get; set; } = 0;

    // Populated by the portal-stitch pass in SceneCollector. Writer picks
    // these up when emitting the NavDataHeader + NavPortal block.
    public ushort PortalStart { get; set; } = 0;
    public byte PortalCount { get; set; } = 0;

    // World-space verts in Godot units — kept alongside the fp12 copies so
    // the portal stitcher can compare coincident endpoints in world space
    // before quantization. Empty on records produced before the collector
    // stitch pass is relevant (legacy paths).
    public Vector3[]? WorldVerts { get; set; }
}

// Runtime NavPortal entry (20 bytes). One per directed portal — if region A
// neighbours region B, we emit two entries (A→B and B→A).
public sealed class NavPortalRecord
{
    public required int Ax { get; init; }   // portal edge start (fp12, world/gteScaling)
    public required int Az { get; init; }
    public required int Bx { get; init; }   // portal edge end
    public required int Bz { get; init; }
    public required ushort NeighborRegion { get; init; }
    public required short HeightDelta { get; init; }  // fp12 (at portal midpoint)
}

// One RoomData entry (36 bytes). Authored via a PS1Room node; the
// collector fills in the AABB in world space and the tri-ref slice.
// CellCount / PortalRefCount stay 0 for the MVP — the runtime falls back
// cleanly to "render all of the room's tri-refs" when cells are absent.
public sealed class RoomRecord
{
    public required Vector3 WorldMin { get; init; }
    public required Vector3 WorldMax { get; init; }
    public string Name { get; init; } = "";
    public ushort FirstTriRef { get; set; } = 0;
    public ushort TriRefCount { get; set; } = 0;
    public ushort FirstCell { get; set; } = 0;
    public byte CellCount { get; set; } = 0;
    public byte PortalRefCount { get; set; } = 0;
    public ushort FirstPortalRef { get; set; } = 0;
}

// One PortalData entry (40 bytes). Authored via a PS1PortalLink node; the
// collector resolves RoomA / RoomB NodePath → room index, captures the
// node's transform as centre/right/up, and auto-corrects the normal so
// it points from RoomA → RoomB (matching SplashEdit's convention).
public sealed class PortalRecord
{
    public required ushort RoomA { get; init; }
    public required ushort RoomB { get; init; }
    public required Vector3 WorldCenter { get; init; }
    public required Vector2 PortalSize { get; init; }  // (width, height) in world units
    public required Vector3 Normal { get; init; }
    public required Vector3 Right { get; init; }
    public required Vector3 Up { get; init; }
}

// Flat triangle-ref entry (4 bytes). Room block writes these in room
// order, one slice per room.
public readonly struct RoomTriRefRecord
{
    public ushort ObjectIndex { get; init; }
    public ushort TriangleIndex { get; init; }
}

public sealed class SceneData
{
    public List<SceneObject> Objects { get; } = new();

    // Deduplicated textures referenced by this scene. Each entry is a
    // quantized PSX texture sitting at a specific spot in VRAM (populated
    // by VRAMPacker in SplashpackWriter).
    public List<PSXTexture> Textures { get; } = new();

    // VRAMPacker run over `Textures` — null until the writer populates it.
    public VRAMPacker? Packer { get; set; }

    // Collision + nav — per-object AABBs for walls/props, one flat region
    // per walkable floor. Floor/ground uses NavRegion; wall push-back uses
    // Colliders. Both can be empty in which case psxsplash boots with no
    // physics engagement.
    public List<ColliderRecord> Colliders { get; } = new();
    public List<NavRegionRecord> NavRegions { get; } = new();
    public List<NavPortalRecord> NavPortals { get; } = new();
    public List<TriggerBoxRecord> TriggerBoxes { get; } = new();
    public List<InteractableRecord> Interactables { get; } = new();

    // Interior room + portal data (empty in exterior scenes). Rooms includes
    // a trailing "catch-all" entry for triangles that don't land in any
    // authored volume; the room block writer appends that entry unconditionally.
    public List<RoomRecord> Rooms { get; } = new();
    public List<PortalRecord> Portals { get; } = new();
    public List<RoomTriRefRecord> RoomTriRefs { get; } = new();

    // Lua scripts discovered on scene nodes. Deduplicated by resource path.
    public List<LuaFileRecord> LuaFiles { get; } = new();

    // Audio clips authored on PS1Scene.AudioClips, already ADPCM-encoded.
    // Parallel name table lets Lua resolve `Audio.Play("name")` at runtime.
    public List<AudioClipRecord> AudioClips { get; } = new();

    // Sequenced music tracks (.mid → PS1M). Parallel name lookup via
    // Music.Play("..."). Capped at 8 entries by the runtime.
    public List<MusicSequenceRecord> MusicSequences { get; } = new();

    // UI canvases gathered from PS1UICanvas nodes + their PS1UIElement
    // children. Lua resolves by name via UI.FindCanvas.
    public List<UICanvasRecord> UICanvases { get; } = new();

    // Custom UI fonts referenced by elements. Deduped by resource
    // identity during collection. Max 2 custom fonts per splashpack
    // (runtime cap UI_MAX_FONTS - 1 reserved for the built-in system
    // font at slot 0). Index in this list + 1 = the element's
    // fontIndex byte (slot 0 is the system font).
    public List<UIFontRecord> UIFonts { get; } = new();

    // Animations gathered from PS1Animation nodes + their PS1AnimationKeyframe
    // children. Lua plays by name via Animation.Play.
    public List<AnimationRecord> Animations { get; } = new();

    // Cutscenes gathered from PS1Cutscene nodes + their PS1AnimationTrack
    // children. Lua plays by name via Cutscene.Play.
    public List<CutsceneRecord> Cutscenes { get; } = new();

    // Skinned meshes — PS1SkinnedMesh nodes in the scene. The mesh itself
    // lives in Objects (exported as a normal mesh in bind pose); this
    // entry carries the skin-side data: per-triangle bone assignments
    // and animation clips. Stage 1 lands with ClipCount = 0 (rest pose
    // only); stage 2 adds baked clips.
    public List<SkinnedMeshRecord> SkinnedMeshes { get; } = new();

    // Index into LuaFiles for a script attached to the PS1Scene root; -1 if
    // no root script. Runtime dispatches scene-level events (onSceneCreationStart
    // / onSceneCreationEnd) against this entry.
    public int SceneLuaFileIndex { get; set; } = -1;

    /// <summary>Path the scene was collected from (used in logs/errors).</summary>
    public string ScenePath { get; set; } = "";

    // Authored scene category (matches the PS1 optimization reference's
    // seven scene types). The writer maps this to the runtime's binary
    // render path (BVH vs room/portal). Budgets are authoring-only and
    // don't round-trip to the splashpack yet.
    public PS1Scene.SceneTypeKind SceneType { get; set; } = PS1Scene.SceneTypeKind.ExplorationOutdoor;

    /// <summary>
    /// World-units per GTE unit. Scene-level scaling factor — keeps vertex
    /// positions in fp12 short range (±32767, i.e. ±~8 GTE units).
    /// Default 4 lets a ~32-world-unit scene fit cleanly. Bump higher for
    /// larger scenes; lower for tiny ones (more precision per axis).
    /// </summary>
    public float GteScaling { get; set; } = 4.0f;

    // ─── Player start + physics (pulled from PS1Scene in SceneCollector) ──
    public Vector3 PlayerPosition { get; set; } = Vector3.Zero;
    public Vector3 PlayerRotation { get; set; } = Vector3.Zero;
    public float PlayerHeightMeters { get; set; } = 1.7f;
    public float PlayerRadiusMeters { get; set; } = 0.3f;

    // v21: editor-configured rig offsets captured from PS1Player children.
    // Values are in Godot local coords (relative to PS1Player). Exporter
    // converts to PSX units + Y-flip at write time. Defaults: camera 3 m
    // behind + 1 m above the player's eye; avatar offset zero (assumes
    // child mesh authored with its own feet at origin — scene overrides).
    public Vector3 CameraRigOffset { get; set; } = new Vector3(0, 1, 3);
    public Vector3 PlayerAvatarOffset { get; set; } = Vector3.Zero;
    public int PlayerAvatarObjectIndex { get; set; } = -1;  // -1 → 0xFFFF (none)
    public float MoveSpeedMps { get; set; } = 3.0f;
    public float SprintSpeedMps { get; set; } = 6.0f;
    public float JumpHeightMeters { get; set; } = 1.2f;
    public float GravityMps2 { get; set; } = 9.81f;

    // ─── Fog (pulled from PS1Scene in SceneCollector) ──
    public bool FogEnabled { get; set; } = false;
    public Color FogColor { get; set; } = new Color(0.5f, 0.5f, 0.6f);
    public byte FogDensity { get; set; } = 5;
}
