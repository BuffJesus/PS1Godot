using System.Collections.Generic;
using Godot;

namespace PS1Godot.Exporter;

// Snapshot of a Godot scene reduced to the things the splashpack writer cares
// about. Populated by SceneCollector; consumed by SplashpackWriter.

public sealed class SceneObject
{
    public required PS1MeshInstance Node { get; init; }
    public required PSXMesh Mesh { get; init; }

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
    public required string Text { get; init; }  // empty for non-Text types
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

// Single convex nav region (the v20 walkable-surface primitive). For our
// current "flat floor" path this is always a 4-vertex rectangle with a flat
// plane equation. More complex decompositions wait for a real nav builder.
public sealed class NavRegionRecord
{
    // Verts in XZ plane, PSX-space fp12 (int32). Stored CCW per the runtime's
    // cross-product test in navregion.cpp.
    public required int[] VertsX { get; init; }
    public required int[] VertsZ { get; init; }
    public required int PlaneA { get; init; }   // floor plane Y = A·x + B·z + D (fp12)
    public required int PlaneB { get; init; }
    public required int PlaneD { get; init; }
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
    public List<TriggerBoxRecord> TriggerBoxes { get; } = new();
    public List<InteractableRecord> Interactables { get; } = new();

    // Lua scripts discovered on scene nodes. Deduplicated by resource path.
    public List<LuaFileRecord> LuaFiles { get; } = new();

    // Audio clips authored on PS1Scene.AudioClips, already ADPCM-encoded.
    // Parallel name table lets Lua resolve `Audio.Play("name")` at runtime.
    public List<AudioClipRecord> AudioClips { get; } = new();

    // UI canvases gathered from PS1UICanvas nodes + their PS1UIElement
    // children. Lua resolves by name via UI.FindCanvas.
    public List<UICanvasRecord> UICanvases { get; } = new();

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
    public float MoveSpeedMps { get; set; } = 3.0f;
    public float SprintSpeedMps { get; set; } = 6.0f;
    public float JumpHeightMeters { get; set; } = 1.2f;
    public float GravityMps2 { get; set; } = 9.81f;
}
