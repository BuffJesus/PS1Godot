using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Godot;
using FileAccess = System.IO.FileAccess; // Disambiguate from Godot.FileAccess

namespace PS1Godot.Exporter;

// Splashpack writer — emits the three-file v20 triplet (.splashpack, .vram, .spu).
//
// Source of truth for byte layout:
//   - psxsplash-main/src/splashpack.cpp     (SPLASHPACKFileHeader, 120 bytes)
//   - psxsplash-main/src/gameobject.hh      (GameObject, 92 bytes)
//   - psxsplash-main/src/mesh.hh            (Tri, 52 bytes)
//   - psxsplash-main/src/scenemanager.cpp   (uploadVramData — .vram format)
//   - splashedit-main/Runtime/PSXSceneWriter.cs (the reference Unity exporter)
//
// File layout (in offset order within the .splashpack file):
//   0..119         Header
//   120..          GameObject entries (92 B each)
//                  [colliders, trigger boxes, BVH, interactables, worldCollision,
//                   navRegions, rooms — all empty for now]
//                  Atlas metadata (12 B each, cursor-walked but content ignored in v20)
//                  CLUT metadata  (12 B each, same)
//                  Mesh triangle data — per object, aligned to 4. Referenced by
//                  each GameObject's polygonsOffset.
//                  Name table — referenced by header.nameTableOffset.
public static class SplashpackWriter
{
    public const ushort SplashpackVersion = 24;
    // Header layout grew by 16 bytes in v24 (sky struct: tpage + clut + UVs
    // + bitDepth + tint + enabled flag, mirroring the UI Image typeData
    // union slot). See WriteHeader and the runtime's SPLASHPACKFileHeader.
    public const int HeaderSize = 168;
    public const int GameObjectSize = 92;
    public const int TriSize = 52;
    public const int LuaFileSize = 8; // luaCodeOffset (u32) + length (u32)
    public const int AtlasMetaSize = 12;
    public const int ClutMetaSize = 12;
    public const ushort UntexturedTpage = 0xFFFF;
    public const ushort NoComponent = 0xFFFF;

    public static void WriteEmpty(string splashpackPath) =>
        Write(splashpackPath, new SceneData());

    public static void Write(string splashpackPath, SceneData scene)
    {
        // Run VRAM packing once before writing. Populates each texture's
        // PackingX/Y, TexpageX/Y, ClutPackingX/Y so the writer can stamp
        // per-triangle tpage + clut info.
        if (scene.Textures.Count > 0 && scene.Packer == null)
        {
            scene.Packer = new VRAMPacker();
            scene.Packer.Pack(scene.Textures);
        }

        WriteSplashpack(splashpackPath, scene);
        WriteVram(Path.ChangeExtension(splashpackPath, ".vram"), scene);
        WriteSpu(Path.ChangeExtension(splashpackPath, ".spu"), scene);
    }

    // ─── Splashpack file ─────────────────────────────────────────────────

    private static void WriteSplashpack(string path, SceneData scene)
    {
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
        using var w = new BinaryWriter(fs);

        int objCount = scene.Objects.Count;
        int atlasCount = scene.Packer?.AtlasCount ?? 0;
        int clutCount = 0;
        if (scene.Packer != null)
            foreach (var _ in scene.Packer.CLUTOwners()) clutCount++;

        int colliderCount = scene.Colliders.Count;
        int navRegionCount = scene.NavRegions.Count;
        int navPortalCount = scene.NavPortals.Count;
        int luaFileCount = scene.LuaFiles.Count;
        int triggerBoxCount = scene.TriggerBoxes.Count;
        int interactableCount = scene.Interactables.Count;
        int roomCount = scene.Rooms.Count;
        int portalCount = scene.Portals.Count;
        int roomTriRefCount = scene.RoomTriRefs.Count;

        var headerOffsets = WriteHeader(w, scene, atlasCount, clutCount, colliderCount,
            navRegionCount, navPortalCount, luaFileCount, triggerBoxCount, interactableCount,
            roomCount, portalCount, roomTriRefCount);

        // ── LuaFile entries (8 bytes each, cursor-walked first after header) ──
        // luaCodeOffset is backfilled later when we know where each source
        // blob landed (see "Lua blob section" below).
        var luaOffsetPositions = new List<long>(luaFileCount);
        foreach (var lua in scene.LuaFiles)
        {
            luaOffsetPositions.Add(w.BaseStream.Position);
            w.Write((uint)0);                  // luaCodeOffset (backfilled)
            w.Write((uint)lua.Bytes.Length);   // length
        }

        // ── GameObject section: 92 bytes per object ──
        var meshOffsetPositions = new List<long>(objCount);
        foreach (var obj in scene.Objects)
        {
            meshOffsetPositions.Add(WriteGameObjectEntry(w, obj, scene.GteScaling));
        }

        // ── Collider section: 32 bytes per entry ──
        // World-AABB in fp12 + type/mask/objIndex/pad. Loader uses these for
        // X/Z push-back against walls and props.
        foreach (var c in scene.Colliders)
        {
            WriteCollider(w, c, scene.GteScaling);
        }

        // ── Trigger box section: 32 bytes per entry (matches cursor order). ──
        // World-AABB + lua file index for onTriggerEnter/Exit dispatch.
        foreach (var t in scene.TriggerBoxes)
        {
            WriteTriggerBox(w, t, scene.GteScaling);
        }

        // ── [empty sections]: BVH (bvhNodeCount=0 so skipped). ──

        // ── Interactable section: 28 bytes per entry. ──
        // Runtime iterates these independent of GameObject iteration.
        foreach (var ia in scene.Interactables)
        {
            WriteInteractable(w, ia, scene.GteScaling);
        }

        // ── [empty sections]: legacy world collision (count=0). ──

        // ── NavRegion block: NavDataHeader(8) + NavRegion[N]*84 + NavPortal[M]*20.
        //    Aligned to 4 bytes per the loader's pre-align.
        if (navRegionCount > 0)
        {
            AlignTo4(w);
            // NavDataHeader
            w.Write((ushort)navRegionCount);
            w.Write((ushort)navPortalCount);
            w.Write((ushort)0);           // startRegion (player spawns in region 0)
            w.Write((ushort)0);           // pad
            foreach (var nr in scene.NavRegions)
            {
                WriteNavRegion(w, nr);
            }
            foreach (var p in scene.NavPortals)
            {
                WriteNavPortal(w, p);
            }
        }

        // ── Room block: RoomData[R]*36 + PortalData[P]*40 + TriangleRef[T]*4
        //    + RoomCell[C]*28 (when cells populated) + RoomPortalRef[P']*4
        //    (when portal-refs populated). Order matches the runtime
        //    loader in splashpack.cpp — if cells come after portal-refs
        //    the loader will read the wrong bytes. Loader aligns its
        //    cursor to 4 bytes before reading RoomData, so do the same.
        if (roomCount > 0)
        {
            AlignTo4(w);
            foreach (var r in scene.Rooms)
            {
                WriteRoom(w, r, scene.GteScaling);
            }
            foreach (var p in scene.Portals)
            {
                WritePortal(w, p, scene.GteScaling);
            }
            foreach (var tr in scene.RoomTriRefs)
            {
                w.Write(tr.ObjectIndex);
                w.Write(tr.TriangleIndex);
            }
            foreach (var c in scene.RoomCells)
            {
                WriteRoomCell(w, c, scene.GteScaling);
            }
            foreach (var pr in scene.RoomPortalRefs)
            {
                w.Write(pr.PortalIndex);
                w.Write(pr.OtherRoom);
            }
        }

        // ── Atlas metadata (12 B each) ──
        // The loader advances the cursor past these but doesn't read the
        // content in v20 (tpage/clut coords are baked into triangles). We
        // still write plausible values so it matches SplashEdit's file shape.
        if (scene.Packer != null)
        {
            foreach (var chunk in scene.Packer.EnumerateAtlasChunks())
            {
                w.Write((uint)0);                  // polygonsOffset (unused in v20)
                w.Write((ushort)chunk.width);
                w.Write((ushort)chunk.height);
                w.Write((ushort)chunk.vramX);
                w.Write((ushort)chunk.vramY);
            }
            // ── CLUT metadata (12 B each) ──
            foreach (var tex in scene.Packer.CLUTOwners())
            {
                w.Write((uint)0);                  // clutOffset (unused in v20)
                w.Write((ushort)tex.ClutPackingX); // pre-divided by 16
                w.Write((ushort)tex.ClutPackingY);
                w.Write((ushort)(tex.ColorPalette?.Count ?? 0));
                w.Write((ushort)0);                // pad
            }
        }

        // ── Mesh data: per object, align to 4, write Tri[] ──
        for (int i = 0; i < objCount; i++)
        {
            AlignTo4(w);
            long meshStart = w.BaseStream.Position;
            BackfillUInt32(w, meshOffsetPositions[i], (uint)meshStart);

            foreach (var tri in scene.Objects[i].Mesh.Triangles)
            {
                WriteTri(w, tri, scene);
            }
        }

        // ── Object name table (referenced by header.nameTableOffset) ──
        AlignTo4(w);
        long nameTableStart = w.BaseStream.Position;
        foreach (var obj in scene.Objects)
        {
            string name = obj.Node.Name;
            if (name.Length > 24) name = name[..24];
            byte[] bytes = Encoding.UTF8.GetBytes(name);
            w.Write((byte)bytes.Length);
            w.Write(bytes);
            w.Write((byte)0); // null terminator
        }
        BackfillUInt32(w, headerOffsets.NameTableOffsetPos, objCount > 0 ? (uint)nameTableStart : 0u);

        // ── Lua source blobs (referenced by each LuaFile.luaCodeOffset) ──
        // Placed last so every earlier section is cursor-walked normally.
        // Source text is passed to luaL_loadbuffer with an explicit length, so
        // no null terminator is required — we still align to 4 between blobs.
        for (int i = 0; i < scene.LuaFiles.Count; i++)
        {
            AlignTo4(w);
            long blobStart = w.BaseStream.Position;
            w.Write(scene.LuaFiles[i].Bytes);
            BackfillUInt32(w, luaOffsetPositions[i], (uint)blobStart);
        }

        // ── Audio clip table + name strings ──
        // Table entry (16 B): dataOff(u32) size(u32) rate(u16) loop(u8) nameLen(u8) nameOff(u32)
        // v20: dataOff is 0 (ADPCM is in the .spu sidecar); nameOff points
        // at a null-terminated C string elsewhere in this splashpack. Name
        // strings are written right after the table.
        if (scene.AudioClips.Count > 0)
        {
            AlignTo4(w);
            long audioTableStart = w.BaseStream.Position;
            BackfillUInt32(w, headerOffsets.AudioTableOffsetPos, (uint)audioTableStart);

            var nameOffsetPositions = new List<long>(scene.AudioClips.Count);
            foreach (var clip in scene.AudioClips)
            {
                w.Write((uint)0);                       // dataOff (0 in v20 split)
                w.Write((uint)clip.AdpcmData.Length);   // size
                w.Write((ushort)clip.SampleRate);       // rate
                w.Write((byte)(clip.Loop ? 1 : 0));     // loop
                w.Write((byte)clip.Name.Length);        // nameLen
                nameOffsetPositions.Add(w.BaseStream.Position);
                w.Write((uint)0);                       // nameOff (backfilled)
            }

            // Name strings (null-terminated). Runtime reads nameLen bytes then
            // null terminator via `reinterpret_cast<const char*>(data + nameOff)`.
            for (int i = 0; i < scene.AudioClips.Count; i++)
            {
                long nameStart = w.BaseStream.Position;
                byte[] nameBytes = Encoding.UTF8.GetBytes(scene.AudioClips[i].Name);
                w.Write(nameBytes);
                w.Write((byte)0); // null terminator
                BackfillUInt32(w, nameOffsetPositions[i], (uint)nameStart);
            }
        }

        // ── UI section ──
        // Layout (matching uisystem.cpp:loadFromSplashpack):
        //   [uiTableOffset]
        //   [Font descriptors — 112 B each × fontCount (MVP: 0)]
        //   [Canvas descriptors — 12 B each × canvasCount]
        //   [Element arrays (48 B/elem), canvas names, element names,
        //    text bodies — all elsewhere in the file, referenced by
        //    offset]
        if (scene.UICanvases.Count > 0)
        {
            WriteUISection(w, scene, headerOffsets.UiTableOffsetPos);
        }

        // ── Animation section ──
        // Per splashpack.cpp:378 the table at animationTableOffset is an
        // array of 12 B SPLASHPACKAnimationEntry. Each entry's dataOffset
        // points to a 16 B SPLASHPACKAnimation block which in turn points
        // to a track array (12 B / track, MVP: 1 track) and each track
        // points to a keyframe array (8 B / keyframe).
        if (scene.Animations.Count > 0)
        {
            WriteAnimationSection(w, scene, headerOffsets.AnimationTableOffsetPos);
        }

        // ── Cutscene section ──
        // Same shape as animations but multi-track + audio events. Audio
        // and skin-anim events are stubbed (count=0, off=0) for B.2
        // Phase 1 — Phase 2/3 wires camera tracks and audio cues.
        if (scene.Cutscenes.Count > 0)
        {
            WriteCutsceneSection(w, scene, headerOffsets.CutsceneTableOffsetPos);
        }

        // ── Skin section (Phase 2 bullet 11, stage 1) ──
        // Table of 12 B entries + per-mesh SkinData blocks + name strings.
        // Clip baking lands in stage 2 — today every block writes clipCount = 0.
        if (scene.SkinnedMeshes.Count > 0)
        {
            WriteSkinSection(w, scene, headerOffsets.SkinTableOffsetPos);
        }

        // ── Music sequence section (v22+) ──
        // Table of 24-byte MusicTableEntry { dataOff(u32), dataSize(u32),
        // name[16] } followed by the PS1M blobs in order. Reader code:
        // splashpack.cpp:295.
        if (scene.MusicSequences.Count > 0)
        {
            WriteMusicSection(w, scene, headerOffsets.MusicTableOffsetPos);
        }

        // ── UI 3D-model section (v23+) ──
        // 48-byte UIModelEntry × N, no data-block chase (entries are
        // self-contained). Entries aggregate across all canvases; each
        // carries its owning canvas index so the renderer can gate
        // visibility on canvas.visible.
        if (CountUIModels(scene) > 0)
        {
            WriteUIModelSection(w, scene, headerOffsets.UiModelTableOffsetPos);
        }
    }

    // ─── Music sequence section ─────────────────────────────────────────
    //
    // 24-byte MusicTableEntry × N, then PS1M blobs aligned to 4 bytes
    // each. Names are baked inline (16-byte fixed field, null-terminated
    // when shorter) so the runtime doesn't need a second offset chase.
    private static void WriteMusicSection(BinaryWriter w, SceneData scene, long musicTableOffsetPos)
    {
        AlignTo4(w);
        long tableStart = w.BaseStream.Position;
        BackfillUInt32(w, musicTableOffsetPos, (uint)tableStart);

        int count = Math.Min(scene.MusicSequences.Count, 8);
        var dataOffPositions = new long[count];

        for (int i = 0; i < count; i++)
        {
            var seq = scene.MusicSequences[i];
            dataOffPositions[i] = w.BaseStream.Position;
            w.Write((uint)0);                    // dataOffset (backfilled)
            w.Write((uint)seq.Ps1mData.Length);  // dataSize
            // Name: 16-byte fixed field, null-padded. Names already
            // truncated to 15 chars in SceneCollector so the trailing
            // byte stays 0 for null termination.
            byte[] nameBytes = Encoding.UTF8.GetBytes(seq.Name ?? string.Empty);
            int nameLen = Math.Min(nameBytes.Length, 15);
            w.Write(nameBytes, 0, nameLen);
            for (int p = nameLen; p < 16; p++) w.Write((byte)0);
        }

        for (int i = 0; i < count; i++)
        {
            AlignTo4(w);
            long blobStart = w.BaseStream.Position;
            w.Write(scene.MusicSequences[i].Ps1mData);
            BackfillUInt32(w, dataOffPositions[i], (uint)blobStart);
        }
    }

    // ─── UI 3D-model section (v23+) ─────────────────────────────────────
    //
    // Flat array of 48-byte UIModelEntry records aggregated across every
    // canvas, capped at MAX_UI_MODELS (16 — matching the runtime's static
    // array). Layout MUST match psxsplash's UIModel struct:
    //   char     name[16];       // 16 B null-padded
    //   uint16_t canvasIndex;    //  2 B — owning canvas
    //   uint16_t targetObjIndex; //  2 B — GameObject to re-render
    //   int16_t  screenX;        //  2 B
    //   int16_t  screenY;        //  2 B
    //   uint16_t screenW;        //  2 B
    //   uint16_t screenH;        //  2 B
    //   int16_t  orbitYawFp10;   //  2 B (psyqo::Angle raw, 1024=π)
    //   int16_t  orbitPitchFp10; //  2 B
    //   int32_t  orbitDistFp12;  //  4 B
    //   uint16_t projectionH;    //  2 B
    //   uint8_t  visibleOnLoad;  //  1 B
    //   uint8_t  _pad;           //  1 B
    //   uint32_t _reserved;      //  4 B  → reserved for runtime (e.g., current yaw)
    //                                  Total: 48 B.
    private const int UiModelEntrySize = 48;
    private const int MaxUiModels = 16;

    private static int CountUIModels(SceneData scene)
    {
        int total = 0;
        foreach (var canvas in scene.UICanvases)
        {
            total += canvas.Models.Count;
        }
        if (total > MaxUiModels) total = MaxUiModels;
        return total;
    }

    private static void WriteUIModelSection(BinaryWriter w, SceneData scene, long uiModelTableOffsetPos)
    {
        AlignTo4(w);
        long tableStart = w.BaseStream.Position;
        BackfillUInt32(w, uiModelTableOffsetPos, (uint)tableStart);

        int emitted = 0;
        for (int ci = 0; ci < scene.UICanvases.Count && emitted < MaxUiModels; ci++)
        {
            var canvas = scene.UICanvases[ci];
            foreach (var m in canvas.Models)
            {
                if (emitted >= MaxUiModels) break;
                if (m.TargetObjectIndex < 0)
                {
                    GD.PushWarning($"[PS1Godot] UIModel '{m.Name}' has no valid Target — skipped in splashpack.");
                    continue;
                }
                long entryStart = w.BaseStream.Position;

                // Name — 16 B fixed, null-padded.
                byte[] nameBytes = Encoding.UTF8.GetBytes(m.Name ?? "");
                int nameLen = Math.Min(nameBytes.Length, 15);
                w.Write(nameBytes, 0, nameLen);
                for (int p = nameLen; p < 16; p++) w.Write((byte)0);

                w.Write((ushort)ci);                                // canvasIndex
                w.Write((ushort)m.TargetObjectIndex);               // targetObjIndex
                w.Write(m.X);                                       // screenX
                w.Write(m.Y);                                       // screenY
                w.Write((ushort)Mathf.Max(0, (int)m.W));            // screenW
                w.Write((ushort)Mathf.Max(0, (int)m.H));            // screenH
                w.Write(m.OrbitYawFp10);                            // orbitYawFp10
                w.Write(m.OrbitPitchFp10);                          // orbitPitchFp10
                w.Write(m.OrbitDistanceFp12);                       // orbitDistFp12
                w.Write(m.ProjectionH);                             // projectionH
                w.Write((byte)(m.VisibleOnLoad ? 1 : 0));           // visibleOnLoad
                w.Write((byte)0);                                   // _pad
                w.Write((uint)0);                                   // _reserved1
                w.Write((uint)0);                                   // _reserved2

                long written = w.BaseStream.Position - entryStart;
                if (written != UiModelEntrySize)
                    throw new InvalidOperationException(
                        $"UIModelEntry size mismatch: wrote {written} bytes, expected {UiModelEntrySize}.");
                emitted++;
            }
        }
    }

    // ─── Cutscene section ───────────────────────────────────────────────
    //
    // Layout per splashpack.cpp:267:
    //   12 B SPLASHPACKCutsceneEntry × cutsceneCount (table)
    //   16 B SPLASHPACKCutscene per cutscene (data block)
    //   12 B SPLASHPACKCutsceneTrack × trackCount (per cutscene)
    //   8 B CutsceneKeyframe × keyframeCount (per track)
    //   Audio events + skin anim events (zero in B.2 Phase 1).
    //   Strings — cutscene names + track target object names.
    private static void WriteCutsceneSection(BinaryWriter w, SceneData scene, long cutsceneTableOffsetPos)
    {
        AlignTo4(w);
        long tableStart = w.BaseStream.Position;
        BackfillUInt32(w, cutsceneTableOffsetPos, (uint)tableStart);

        int count = scene.Cutscenes.Count;

        // Table entries (12 B each). Backfill positions for dataOffset + nameOffset.
        var dataOffPos = new long[count];
        var nameOffPos = new long[count];
        for (int i = 0; i < count; i++)
        {
            var c = scene.Cutscenes[i];
            dataOffPos[i] = w.BaseStream.Position;
            w.Write((uint)0);                                         // dataOffset (backfilled)
            byte nameLen = (byte)Math.Min(Encoding.UTF8.GetByteCount(c.Name ?? ""), 255);
            w.Write(nameLen);
            w.Write((byte)0); w.Write((byte)0); w.Write((byte)0);     // 3 B pad
            nameOffPos[i] = w.BaseStream.Position;
            w.Write((uint)0);                                         // nameOffset (backfilled)
        }

        // Per-cutscene data blocks + their track arrays.
        // Per-track backfill positions: objNameOff + kfOff.
        var trackObjNameOffPositions = new long[count][];
        var trackKfOffPositions      = new long[count][];
        var audioOffPositions        = new long[count];
        for (int i = 0; i < count; i++)
        {
            var c = scene.Cutscenes[i];
            AlignTo4(w);
            long blockStart = w.BaseStream.Position;
            BackfillUInt32(w, dataOffPos[i], (uint)blockStart);

            // SPLASHPACKCutscene (16 B for v19+ layout).
            w.Write(c.TotalFrames);                  // u16
            w.Write((byte)c.Tracks.Count);           // trackCount
            w.Write((byte)c.AudioEvents.Count);      // audioEventCount
            long tracksOffPos = w.BaseStream.Position;
            w.Write((uint)0);                        // tracksOff (backfilled)
            audioOffPositions[i] = w.BaseStream.Position;
            w.Write((uint)0);                        // audioOff (backfilled if events > 0)
            // v19 skin anim event fields — still stubbed (bullet 11 work).
            w.Write((byte)0);                        // skinAnimEventCount
            w.Write((byte)0); w.Write((byte)0); w.Write((byte)0); // 3 B pad
            w.Write((uint)0);                        // skinAnimOff

            // Track array — 12 B per track, immediately after the data block.
            AlignTo4(w);
            long tracksStart = w.BaseStream.Position;
            BackfillUInt32(w, tracksOffPos, (uint)tracksStart);

            trackObjNameOffPositions[i] = new long[c.Tracks.Count];
            trackKfOffPositions[i]      = new long[c.Tracks.Count];
            for (int ti = 0; ti < c.Tracks.Count; ti++)
            {
                var tr = c.Tracks[ti];
                w.Write((byte)tr.TrackType);
                w.Write((byte)tr.Keyframes.Count);
                byte objNameLen = (byte)Math.Min(Encoding.UTF8.GetByteCount(tr.TargetObjectName ?? ""), 255);
                w.Write(objNameLen);
                w.Write((byte)0);                    // pad
                trackObjNameOffPositions[i][ti] = w.BaseStream.Position;
                w.Write((uint)0);                    // objNameOff (backfilled)
                trackKfOffPositions[i][ti] = w.BaseStream.Position;
                w.Write((uint)0);                    // kfOff (backfilled)
            }
        }

        // Keyframe arrays per cutscene per track.
        for (int i = 0; i < count; i++)
        {
            var c = scene.Cutscenes[i];
            for (int ti = 0; ti < c.Tracks.Count; ti++)
            {
                var tr = c.Tracks[ti];
                AlignTo4(w);
                long kfStart = w.BaseStream.Position;
                BackfillUInt32(w, trackKfOffPositions[i][ti], (uint)kfStart);
                foreach (var kf in tr.Keyframes)
                {
                    ushort fai = (ushort)(((uint)kf.Frame & 0x1FFFu) | (((uint)kf.Interp & 0x7u) << 13));
                    w.Write(fai);
                    w.Write(kf.V0);
                    w.Write(kf.V1);
                    w.Write(kf.V2);
                }
            }
        }

        // Audio event arrays per cutscene (8 B per event). Skip cutscenes
        // with no events — leave audioOff at 0 (runtime treats as null).
        for (int i = 0; i < count; i++)
        {
            var c = scene.Cutscenes[i];
            if (c.AudioEvents.Count == 0) continue;
            AlignTo4(w);
            long aeStart = w.BaseStream.Position;
            BackfillUInt32(w, audioOffPositions[i], (uint)aeStart);
            foreach (var ae in c.AudioEvents)
            {
                w.Write(ae.Frame);              // u16
                w.Write(ae.ClipIndex);          // u8
                w.Write(ae.Volume);             // u8
                w.Write(ae.Pan);                // u8
                w.Write((byte)0);               // 3 B pad
                w.Write((byte)0);
                w.Write((byte)0);
            }
        }

        // Strings — cutscene names + per-track object names.
        for (int i = 0; i < count; i++)
        {
            var c = scene.Cutscenes[i];
            if (!string.IsNullOrEmpty(c.Name))
            {
                long off = w.BaseStream.Position;
                w.Write(Encoding.UTF8.GetBytes(c.Name));
                w.Write((byte)0);
                BackfillUInt32(w, nameOffPos[i], (uint)off);
            }
            for (int ti = 0; ti < c.Tracks.Count; ti++)
            {
                var tr = c.Tracks[ti];
                if (!string.IsNullOrEmpty(tr.TargetObjectName))
                {
                    long off = w.BaseStream.Position;
                    w.Write(Encoding.UTF8.GetBytes(tr.TargetObjectName));
                    w.Write((byte)0);
                    BackfillUInt32(w, trackObjNameOffPositions[i][ti], (uint)off);
                }
            }
        }
    }

    // ─── Animation section ──────────────────────────────────────────────
    //
    // Three levels of backfill:
    //   1. Table entries point at animation data blocks.
    //   2. Animation data blocks point at track arrays.
    //   3. Track entries point at keyframe arrays + object-name strings.
    // All offsets are absolute into the splashpack; align to 4 between
    // each block since the runtime reads 32-bit fields directly.
    private static void WriteAnimationSection(BinaryWriter w, SceneData scene, long animTableOffsetPos)
    {
        AlignTo4(w);
        long tableStart = w.BaseStream.Position;
        BackfillUInt32(w, animTableOffsetPos, (uint)tableStart);

        int count = scene.Animations.Count;

        // Reserve 12 B × count for SPLASHPACKAnimationEntry. Backfill
        // positions for dataOffset and nameOffset.
        var dataOffPos  = new long[count];
        var nameOffPos  = new long[count];
        for (int i = 0; i < count; i++)
        {
            var a = scene.Animations[i];
            dataOffPos[i] = w.BaseStream.Position;
            w.Write((uint)0);                                        // dataOffset (backfilled)
            byte nameLen = (byte)Math.Min(Encoding.UTF8.GetByteCount(a.Name ?? ""), 255);
            w.Write(nameLen);
            w.Write((byte)0); w.Write((byte)0); w.Write((byte)0);    // 3 B pad
            nameOffPos[i]  = w.BaseStream.Position;
            w.Write((uint)0);                                        // nameOffset (backfilled)
        }

        // Per-animation data blocks (16 B each). Plus its track array.
        // MVP: one ObjectPosition track per animation.
        var trackObjNameOffPositions = new long[count]; // track.objNameOff
        var trackKfOffPositions      = new long[count]; // track.kfOff
        for (int i = 0; i < count; i++)
        {
            var a = scene.Animations[i];
            AlignTo4(w);
            long blockStart = w.BaseStream.Position;
            BackfillUInt32(w, dataOffPos[i], (uint)blockStart);

            // SPLASHPACKAnimation: 16 B.
            w.Write(a.TotalFrames);            // u16
            w.Write((byte)1);                  // trackCount = 1 (MVP)
            w.Write((byte)0);                  // pad
            long trackArrOffPos = w.BaseStream.Position;
            w.Write((uint)0);                  // tracksOff (backfilled below)
            // v19 skin anim fields — none in MVP.
            w.Write((byte)0);                  // skinAnimEventCount
            w.Write((byte)0); w.Write((byte)0); w.Write((byte)0); // 3 B pad
            w.Write((uint)0);                  // skinAnimOff

            // Write the single track (12 B) immediately after. Backfill
            // the tracksOff in the animation block above.
            AlignTo4(w);
            long trackStart = w.BaseStream.Position;
            BackfillUInt32(w, trackArrOffPos, (uint)trackStart);

            w.Write((byte)a.TrackType);        // trackType (Position/Rotation/Active)
            w.Write((byte)a.Keyframes.Count);  // keyframeCount
            byte objNameLen = (byte)Math.Min(Encoding.UTF8.GetByteCount(a.TargetObjectName ?? ""), 255);
            w.Write(objNameLen);               // objNameLen
            w.Write((byte)0);                  // pad
            trackObjNameOffPositions[i] = w.BaseStream.Position;
            w.Write((uint)0);                  // objNameOff (backfilled)
            trackKfOffPositions[i]      = w.BaseStream.Position;
            w.Write((uint)0);                  // kfOff (backfilled)
        }

        // Keyframe arrays per animation (8 B each).
        for (int i = 0; i < count; i++)
        {
            var a = scene.Animations[i];
            AlignTo4(w);
            long kfStart = w.BaseStream.Position;
            BackfillUInt32(w, trackKfOffPositions[i], (uint)kfStart);
            foreach (var kf in a.Keyframes)
            {
                // frameAndInterp: upper 3 bits interp, lower 13 bits frame.
                ushort fai = (ushort)(((uint)kf.Frame & 0x1FFFu) | (((uint)kf.Interp & 0x7u) << 13));
                w.Write(fai);
                w.Write(kf.V0);
                w.Write(kf.V1);
                w.Write(kf.V2);
            }
        }

        // Strings — animation names + target object names.
        for (int i = 0; i < count; i++)
        {
            var a = scene.Animations[i];
            if (!string.IsNullOrEmpty(a.Name))
            {
                long off = w.BaseStream.Position;
                w.Write(Encoding.UTF8.GetBytes(a.Name));
                w.Write((byte)0);
                BackfillUInt32(w, nameOffPos[i], (uint)off);
            }
            if (!string.IsNullOrEmpty(a.TargetObjectName))
            {
                long off = w.BaseStream.Position;
                w.Write(Encoding.UTF8.GetBytes(a.TargetObjectName));
                w.Write((byte)0);
                BackfillUInt32(w, trackObjNameOffPositions[i], (uint)off);
            }
        }
    }

    // ─── Skin section (Phase 2 bullet 11, stage 1) ─────────────────────
    //
    // Layout per splashpack.cpp:470 (SkinPackLoader skinned-mesh parser):
    //   12 B SPLASHPACKSkinEntry × skinnedMeshCount (table)
    //   SkinData block per entry at its dataOffset:
    //       u16 gameObjectIndex
    //       u8  boneCount
    //       u8  clipCount
    //       u8[polyCount × 3] boneIndices
    //       (4-byte align)
    //       SkinAnimClip × clipCount  (empty in stage 1)
    //   Name strings (null-terminated C strings) at each nameOffset.
    //
    // Stage 1 writes clipCount = 0 so runtime skips the clip-parse loop.
    // Even without clips the runtime's skinned render path treats the
    // mesh as present and renders it at bind-pose (bone-0 identity).
    private static void WriteSkinSection(BinaryWriter w, SceneData scene, long skinTableOffsetPos)
    {
        AlignTo4(w);
        long tableStart = w.BaseStream.Position;
        BackfillUInt32(w, skinTableOffsetPos, (uint)tableStart);

        int count = scene.SkinnedMeshes.Count;

        var dataOffPos = new long[count];
        var nameOffPos = new long[count];
        for (int i = 0; i < count; i++)
        {
            var sm = scene.SkinnedMeshes[i];
            dataOffPos[i] = w.BaseStream.Position;
            w.Write((uint)0);                                        // dataOffset (backfilled)
            byte nameLen = (byte)Math.Min(Encoding.UTF8.GetByteCount(sm.Name ?? ""), 255);
            w.Write(nameLen);
            w.Write((byte)0); w.Write((byte)0); w.Write((byte)0);    // 3 B pad
            nameOffPos[i] = w.BaseStream.Position;
            w.Write((uint)0);                                        // nameOffset (backfilled)
        }

        for (int i = 0; i < count; i++)
        {
            var sm = scene.SkinnedMeshes[i];
            AlignTo4(w);
            long blockStart = w.BaseStream.Position;
            BackfillUInt32(w, dataOffPos[i], (uint)blockStart);

            byte clipCount = (byte)Math.Min(sm.Clips.Count, 16);  // SKINMESH_MAX_CLIPS
            w.Write(sm.GameObjectIndex);     // u16
            w.Write(sm.BoneCount);            // u8
            w.Write(clipCount);               // u8
            w.Write(sm.BoneIndices);          // polyCount × 3 bytes
            AlignTo4(w);

            // Clips: loader expects length-prefixed name (in-place
            // null-terminated), flags, fps, u16 frameCount (2-byte
            // aligned), then frameCount × boneCount × 24 bytes of
            // BakedBoneMatrix. See splashpack.cpp:503 for the parser.
            for (int c = 0; c < clipCount; c++)
            {
                var clip = sm.Clips[c];
                byte nameLen = (byte)Math.Min(Encoding.UTF8.GetByteCount(clip.Name ?? ""), 255);
                w.Write(nameLen);
                if (nameLen > 0) w.Write(Encoding.UTF8.GetBytes(clip.Name!));
                w.Write((byte)0);             // the loader writes a \0 here; we pre-emit so the name byte count is deterministic
                w.Write(clip.Flags);
                w.Write(clip.Fps);
                AlignTo2(w);
                w.Write(clip.FrameCount);
                w.Write(clip.FrameData);
            }

            AlignTo4(w);
        }

        // Strings — mesh names.
        for (int i = 0; i < count; i++)
        {
            var sm = scene.SkinnedMeshes[i];
            if (!string.IsNullOrEmpty(sm.Name))
            {
                long off = w.BaseStream.Position;
                w.Write(Encoding.UTF8.GetBytes(sm.Name));
                w.Write((byte)0);
                BackfillUInt32(w, nameOffPos[i], (uint)off);
            }
        }
    }

    // Emit UI table + element arrays + strings. Runtime's parser starts at
    // uiTableOffset, walks fontCount×112 B font descriptors (zero in MVP),
    // then canvasCount×12 B canvas descriptors. Element arrays and string
    // data live at arbitrary offsets referenced by those descriptors.
    private static void WriteUISection(BinaryWriter w, SceneData scene, long uiTableOffsetPos)
    {
        AlignTo4(w);
        long uiTableStart = w.BaseStream.Position;
        BackfillUInt32(w, uiTableOffsetPos, (uint)uiTableStart);

        // Font descriptors (112 B each). Pixel-data offsets are
        // placeholders until we write the actual 4bpp blobs at the
        // end of this section, then backfill.
        int fontCount = scene.UIFonts.Count;
        var fontDataOffPositions = new long[fontCount];
        for (int fi = 0; fi < fontCount; fi++)
        {
            var f = scene.UIFonts[fi];
            long fontStart = w.BaseStream.Position;
            w.Write(f.GlyphW);
            w.Write(f.GlyphH);
            w.Write(f.VramX);
            w.Write(f.VramY);
            w.Write(f.TextureH);
            fontDataOffPositions[fi] = w.BaseStream.Position;
            w.Write((uint)0);              // dataOffset (backfilled)
            w.Write((uint)f.PixelData4bpp.Length);
            // advanceWidths[96] — byte-for-byte copy.
            w.Write(f.AdvanceWidths);
            long written = w.BaseStream.Position - fontStart;
            if (written != 112)
                throw new InvalidOperationException($"UIFontDesc size mismatch: {written} vs 112.");
        }

        int canvasCount = scene.UICanvases.Count;

        // Canvas descriptor table (12 B each). Offsets backfilled after the
        // element arrays / strings are placed.
        var canvasDataOffPositions = new long[canvasCount];
        var canvasNameOffPositions = new long[canvasCount];
        for (int ci = 0; ci < canvasCount; ci++)
        {
            var c = scene.UICanvases[ci];
            canvasDataOffPositions[ci] = w.BaseStream.Position;
            w.Write((uint)0);                             // dataOffset (backfilled)
            byte nameLen = (byte)Math.Min(Encoding.UTF8.GetByteCount(c.Name ?? ""), 255);
            w.Write(nameLen);
            w.Write(c.SortOrder);
            w.Write((byte)c.Elements.Count);
            // flags: bit 0 visible; bits 1–2 reserved for residency
            // (runtime ignores today; writer stamps for future upgrade).
            byte flags = (byte)((c.VisibleOnLoad ? 1 : 0) | (((byte)c.Residency & 0x3) << 1));
            w.Write(flags);
            canvasNameOffPositions[ci] = w.BaseStream.Position;
            w.Write((uint)0);                             // nameOffset (backfilled)
        }

        // Element arrays per canvas. Each element is 48 B. Record
        // per-element backfill positions for name + text offsets.
        var elementNameOffPositions = new long[canvasCount][];
        var elementTextOffPositions = new long[canvasCount][];
        for (int ci = 0; ci < canvasCount; ci++)
        {
            var c = scene.UICanvases[ci];
            AlignTo4(w);
            long arrStart = w.BaseStream.Position;
            BackfillUInt32(w, canvasDataOffPositions[ci], (uint)arrStart);

            elementNameOffPositions[ci] = new long[c.Elements.Count];
            elementTextOffPositions[ci] = new long[c.Elements.Count];
            for (int ei = 0; ei < c.Elements.Count; ei++)
            {
                var el = c.Elements[ei];
                long elStart = w.BaseStream.Position;

                // Identity (8 B)
                w.Write((byte)el.Type);
                w.Write((byte)(el.VisibleOnLoad ? 1 : 0));   // flags
                byte elNameLen = (byte)Math.Min(Encoding.UTF8.GetByteCount(el.Name ?? ""), 255);
                w.Write(elNameLen);
                w.Write((byte)0);                            // pad0
                elementNameOffPositions[ci][ei] = w.BaseStream.Position;
                w.Write((uint)0);                            // nameOffset (backfilled)

                // Layout (8 B)
                w.Write(el.X); w.Write(el.Y); w.Write(el.W); w.Write(el.H);

                // Anchors (4 B) — MVP: top-left absolute positioning.
                w.Write((byte)0); w.Write((byte)0);
                w.Write((byte)0); w.Write((byte)0);

                // Color (4 B)
                w.Write(el.ColorR); w.Write(el.ColorG); w.Write(el.ColorB);
                w.Write((byte)0);                            // pad1

                // Type-specific (16 B). Layout matches the runtime's
                // UIImageData / UITextData / UIProgressData unions in
                // uisystem.hh / uisystem.cpp:loadFromSplashpack.
                //   Text:  fontIndex(1) hAlign(1) vAlign(1) + 13 pad
                //   Image: texpageX(1) texpageY(1) clutX(2) clutY(2)
                //          u0(1) v0(1) u1(1) v1(1) bitDepth(1) + 5 pad
                //   Box / unmapped: 16 zero bytes
                if (el.Type == PS1UIElementType.Text)
                {
                    w.Write(el.FontIndex);
                    w.Write(el.HAlign);
                    w.Write(el.VAlign);
                    for (int k = 0; k < 13; k++) w.Write((byte)0);
                }
                else if (el.Type == PS1UIElementType.Image)
                {
                    WriteUIImageTypeData(w, el, scene);
                }
                else
                {
                    for (int k = 0; k < 16; k++) w.Write((byte)0);
                }

                // Text body pointer (4 B) + pad (4 B)
                elementTextOffPositions[ci][ei] = w.BaseStream.Position;
                w.Write((uint)0);                            // textOffset (backfilled)
                w.Write((uint)0);                            // pad2

                long written = w.BaseStream.Position - elStart;
                if (written != 48)
                    throw new InvalidOperationException($"UI element size mismatch: {written} vs 48.");
            }
        }

        // Strings — canvas names, element names, text bodies. Placed after
        // element arrays, backfilled into the descriptors above.
        for (int ci = 0; ci < canvasCount; ci++)
        {
            var c = scene.UICanvases[ci];
            if (!string.IsNullOrEmpty(c.Name))
            {
                long off = w.BaseStream.Position;
                w.Write(Encoding.UTF8.GetBytes(c.Name));
                w.Write((byte)0);
                BackfillUInt32(w, canvasNameOffPositions[ci], (uint)off);
            }
            for (int ei = 0; ei < c.Elements.Count; ei++)
            {
                var el = c.Elements[ei];
                if (!string.IsNullOrEmpty(el.Name))
                {
                    long off = w.BaseStream.Position;
                    w.Write(Encoding.UTF8.GetBytes(el.Name));
                    w.Write((byte)0);
                    BackfillUInt32(w, elementNameOffPositions[ci][ei], (uint)off);
                }
                if (el.Type == PS1UIElementType.Text && !string.IsNullOrEmpty(el.Text))
                {
                    long off = w.BaseStream.Position;
                    w.Write(Encoding.UTF8.GetBytes(el.Text));
                    w.Write((byte)0);
                    BackfillUInt32(w, elementTextOffPositions[ci][ei], (uint)off);
                }
            }
        }

        // Font pixel data — 4bpp packed glyph atlas per custom font.
        // Placed after strings so nothing else references the offsets
        // we're about to backfill. Runtime's UISystem::uploadFonts
        // uploads each blob to (vramX, vramY) then overlays a 2-entry
        // white-on-transparent CLUT in the first 2 hwords — we don't
        // write the CLUT ourselves.
        for (int fi = 0; fi < fontCount; fi++)
        {
            AlignTo4(w);
            long dataOff = w.BaseStream.Position;
            w.Write(scene.UIFonts[fi].PixelData4bpp);
            BackfillUInt32(w, fontDataOffPositions[fi], (uint)dataOff);
        }
    }

    // Stamp the 16-byte type-specific block for a UI Image element.
    // Reads PSXTexture placement (TexpageX/Y, ClutPackingX/Y, PackingX/Y)
    // populated by VRAMPacker.Pack earlier in this writer's pipeline,
    // converts the element's normalized UVRect into PSX-byte UV coords
    // inside the tpage, and writes the layout the runtime parses in
    // uisystem.cpp:153-163. Falls back to all-zeros when the texture
    // is missing — runtime then renders a tinted untextured triangle
    // pair (a flat-colored rect, basically).
    private static void WriteUIImageTypeData(BinaryWriter w, UIElementRecord el, SceneData scene)
    {
        if (el.TextureIndex < 0 || el.TextureIndex >= scene.Textures.Count)
        {
            for (int k = 0; k < 16; k++) w.Write((byte)0);
            return;
        }

        var tex = scene.Textures[el.TextureIndex];

        // expander: source-pixels per VRAM word horizontally, matching
        // the mesh UV pipeline (WriteUvByte). For 4bpp 4 source pixels
        // pack into one VRAM word, so PackingX (in VRAM words) → byte
        // U coord = PackingX * 4. 8bpp = ×2; 16bpp = ×1.
        int expander = tex.BitDepth switch
        {
            PSXBPP.TEX_4BIT => 4,
            PSXBPP.TEX_8BIT => 2,
            _ => 1,
        };
        int baseU = tex.PackingX * expander;
        int baseV = tex.PackingY;

        // UV rect in source-texture-pixel space, then offset by the
        // texture's tpage-local origin. Clamp to byte range — the GPU
        // wraps modulo 256 within a tpage, so an out-of-range UV would
        // silently sample garbage.
        var uv = el.UVRect;
        int u0 = baseU + Mathf.RoundToInt(uv.Position.X * tex.Width);
        int v0 = baseV + Mathf.RoundToInt(uv.Position.Y * tex.Height);
        int u1 = baseU + Mathf.RoundToInt((uv.Position.X + uv.Size.X) * tex.Width);
        int v1 = baseV + Mathf.RoundToInt((uv.Position.Y + uv.Size.Y) * tex.Height);

        u0 = Mathf.Clamp(u0, 0, 255);
        v0 = Mathf.Clamp(v0, 0, 255);
        u1 = Mathf.Clamp(u1, 0, 255);
        v1 = Mathf.Clamp(v1, 0, 255);

        w.Write(tex.TexpageX);              // typeData[0]
        w.Write(tex.TexpageY);              // typeData[1]
        w.Write((ushort)tex.ClutPackingX);  // typeData[2..3]
        w.Write((ushort)tex.ClutPackingY);  // typeData[4..5]
        w.Write((byte)u0);                  // typeData[6]
        w.Write((byte)v0);                  // typeData[7]
        w.Write((byte)u1);                  // typeData[8]
        w.Write((byte)v1);                  // typeData[9]
        w.Write(el.BitDepthByte);           // typeData[10]
        for (int k = 0; k < 5; k++) w.Write((byte)0);  // typeData[11..15] pad
    }

    // ─── Header (120 bytes) ──────────────────────────────────────────────

    private struct HeaderOffsets
    {
        public long NameTableOffsetPos;
        public long AudioTableOffsetPos;
        public long CutsceneTableOffsetPos;
        public long UiTableOffsetPos;
        public long PixelDataOffsetPos;
        public long AnimationTableOffsetPos;
        public long SkinTableOffsetPos;
        public long MusicTableOffsetPos;
        public long UiModelTableOffsetPos;
    }

    private static HeaderOffsets WriteHeader(BinaryWriter w, SceneData scene, int atlasCount, int clutCount, int colliderCount,
        int navRegionCount, int navPortalCount, int luaFileCount, int triggerBoxCount, int interactableCount,
        int roomCount, int portalCount, int roomTriRefCount)
    {
        long headerStart = w.BaseStream.Position;
        var offsets = new HeaderOffsets();
        int objCount = scene.Objects.Count;

        // Magic + version
        w.Write((byte)'S');
        w.Write((byte)'P');
        w.Write(SplashpackVersion);

        // Counts
        w.Write((ushort)luaFileCount);       // luaFileCount
        w.Write((ushort)objCount);           // gameObjectCount
        w.Write((ushort)atlasCount);         // atlasCount
        w.Write((ushort)clutCount);          // clutCount
        w.Write((ushort)colliderCount);      // colliderCount
        w.Write((ushort)interactableCount);  // interactableCount

        // Player start position + rotation (PackedVec3 each = 3 × int16 = 6 bytes).
        // Y+Z flipped to PSX convention; rotation Euler left as-is pending
        // the fp12/fp10 unit fix in the runtime (TODO in scenemanager.cpp).
        var pp = scene.PlayerPosition;
        w.Write(PSXTrig.ConvertCoordinateToPSX( pp.X, scene.GteScaling));
        w.Write(PSXTrig.ConvertCoordinateToPSX(-pp.Y, scene.GteScaling));
        w.Write(PSXTrig.ConvertCoordinateToPSX(-pp.Z, scene.GteScaling));
        var pr = scene.PlayerRotation;
        w.Write(PSXTrig.ConvertToFixed12(pr.X));
        w.Write(PSXTrig.ConvertToFixed12(pr.Y));
        w.Write(PSXTrig.ConvertToFixed12(pr.Z));

        // Player physics in fp12 ushort. World-unit values (height, radius,
        // jump velocity) divide by gteScaling to land in PSX space. Gravity
        // uses the same scale (accel units match position units / s^2). The
        // runtime divides gravity by 30 internally to get per-frame delta.
        ushort playerHeightFp = MetersToPsxFp12Ushort(scene.PlayerHeightMeters, scene.GteScaling);
        w.Write(playerHeightFp);
        // sceneLuaFileIndex: stored as u16, 0xFFFF means "no scene script".
        w.Write((ushort)(scene.SceneLuaFileIndex < 0 ? 0xFFFF : scene.SceneLuaFileIndex));

        w.Write((ushort)0);            // bvhNodeCount
        w.Write((ushort)0);            // bvhTriRefCount
        // Map the authored 7-way SceneType to the runtime's 2-way render
        // path: 0 = BVH exterior, 1 = room/portal interior.
        ushort runtimeSceneType = scene.SceneType switch
        {
            PS1Scene.SceneTypeKind.Interior => 1,
            PS1Scene.SceneTypeKind.DungeonCorridor => 1,
            _ => 0,
        };
        w.Write(runtimeSceneType);      // sceneType
        w.Write((ushort)triggerBoxCount); // triggerBoxCount
        w.Write((ushort)0);            // collisionMeshCount (legacy)
        w.Write((ushort)0);            // collisionTriCount (legacy)
        w.Write((ushort)navRegionCount); // navRegionCount
        w.Write((ushort)navPortalCount); // navPortalCount

        // Movement parameters (12 bytes).
        //   moveSpeed/sprintSpeed are PER-FRAME fp12 PSX-units: runtime does
        //     (stick * speed * dt12) >> 19, with dt12=4096 at 30fps. The
        //     stored value IS the 30fps step — divide the m/s input by 30.
        //   jumpVelocity is a per-FRAME vertical velocity: runtime assigns
        //     m_velocityY = -header.jumpVelocity directly and integrates it
        //     against gravityPerFrame each frame. For a jump to reach height
        //     h_fp12 against per-frame gravity a_fp12, initial velocity must
        //     be sqrt(2 * a_fp12 * h_fp12). Because runtime does a=gravity/30
        //     internally, we match its arithmetic here.
        //   gravity is kept as per-second fp12 — runtime divides by 30 itself.
        //   playerRadius is a raw fp12 distance (no time factor).
        const float fps = 30f;
        const float fp12 = 4096f;
        float gravityPerFrameFp = (scene.GravityMps2 / scene.GteScaling * fp12) / fps;
        float jumpHeightFp = scene.JumpHeightMeters / scene.GteScaling * fp12;
        float jumpVelFp = Mathf.Sqrt(2f * gravityPerFrameFp * jumpHeightFp);

        w.Write(MetersToPsxFp12Ushort(scene.MoveSpeedMps / fps, scene.GteScaling));
        w.Write(MetersToPsxFp12Ushort(scene.SprintSpeedMps / fps, scene.GteScaling));
        w.Write((ushort)Mathf.Clamp(Mathf.RoundToInt(jumpVelFp), 0, 65535));
        w.Write(MetersToPsxFp12Ushort(scene.GravityMps2, scene.GteScaling));
        w.Write(MetersToPsxFp12Ushort(scene.PlayerRadiusMeters, scene.GteScaling));
        w.Write((ushort)0);            // pad1

        offsets.NameTableOffsetPos = w.BaseStream.Position;
        w.Write((uint)0);              // nameTableOffset (backfilled)

        w.Write((ushort)scene.AudioClips.Count); // audioClipCount
        w.Write((ushort)0);            // pad2
        offsets.AudioTableOffsetPos = w.BaseStream.Position;
        w.Write((uint)0);              // audioTableOffset (backfilled)

        // Fog block (6 bytes) — RGB888 + density byte. Reads
        // scene.FogEnabled / FogColor / FogDensity authored on PS1Scene.
        w.Write((byte)(scene.FogEnabled ? 1 : 0));
        w.Write((byte)Mathf.Clamp((int)(scene.FogColor.R * 255f), 0, 255));
        w.Write((byte)Mathf.Clamp((int)(scene.FogColor.G * 255f), 0, 255));
        w.Write((byte)Mathf.Clamp((int)(scene.FogColor.B * 255f), 0, 255));
        w.Write(scene.FogDensity);
        w.Write((byte)0);              // pad3

        // Room system counts
        w.Write((ushort)roomCount);       // roomCount
        w.Write((ushort)portalCount);     // portalCount
        w.Write((ushort)roomTriRefCount); // roomTriRefCount

        w.Write((ushort)scene.Cutscenes.Count); // cutsceneCount
        w.Write((ushort)scene.RoomCells.Count); // roomCellCount
        offsets.CutsceneTableOffsetPos = w.BaseStream.Position;
        w.Write((uint)0);              // cutsceneTableOffset (backfilled)

        w.Write((ushort)scene.UICanvases.Count); // uiCanvasCount
        w.Write((byte)scene.UIFonts.Count); // uiFontCount — custom fonts only; system font is built into the runtime
        w.Write((byte)0);              // uiPad5
        offsets.UiTableOffsetPos = w.BaseStream.Position;
        w.Write((uint)0);              // uiTableOffset (backfilled)

        offsets.PixelDataOffsetPos = w.BaseStream.Position;
        w.Write((uint)0);              // pixelDataOffset (0 = v20 split)

        w.Write((ushort)scene.Animations.Count); // animationCount
        w.Write((ushort)scene.RoomPortalRefs.Count); // roomPortalRefCount
        offsets.AnimationTableOffsetPos = w.BaseStream.Position;
        w.Write((uint)0);              // animationTableOffset (backfilled)

        w.Write((ushort)scene.SkinnedMeshes.Count); // skinnedMeshCount
        w.Write((ushort)0);            // pad_skin
        offsets.SkinTableOffsetPos = w.BaseStream.Position;
        w.Write((uint)0);              // skinTableOffset (backfilled by WriteSkinSection)

        // v21: editor-configured rig data. Offsets are PackedVec3 = 3 × int16.
        // Y+Z flipped: Godot +Y (up) → PSX -Y, Godot +Z (toward viewer) →
        // PSX -Z (toward viewer in the Y-down / Z-forward convention).
        // Player-local space; runtime rotates by player yaw each frame.
        var camOff = scene.CameraRigOffset;
        w.Write(PSXTrig.ConvertCoordinateToPSX( camOff.X, scene.GteScaling));
        w.Write(PSXTrig.ConvertCoordinateToPSX(-camOff.Y, scene.GteScaling));
        w.Write(PSXTrig.ConvertCoordinateToPSX(-camOff.Z, scene.GteScaling));
        var avOff = scene.PlayerAvatarOffset;
        w.Write(PSXTrig.ConvertCoordinateToPSX( avOff.X, scene.GteScaling));
        w.Write(PSXTrig.ConvertCoordinateToPSX(-avOff.Y, scene.GteScaling));
        w.Write(PSXTrig.ConvertCoordinateToPSX(-avOff.Z, scene.GteScaling));
        w.Write((ushort)(scene.PlayerAvatarObjectIndex < 0 ? 0xFFFF : scene.PlayerAvatarObjectIndex));
        w.Write((ushort)0);            // pad_rig

        // v22: sequenced-music table (count + 16-bit pad + u32 offset).
        // Capped at 8 entries by the runtime; any extras were already
        // dropped by the collector.
        int musicCount = Math.Min(scene.MusicSequences.Count, 8);
        w.Write((ushort)musicCount);   // musicSequenceCount
        w.Write((ushort)0);            // pad_music
        offsets.MusicTableOffsetPos = w.BaseStream.Position;
        w.Write((uint)0);              // musicTableOffset (backfilled)

        // v23: UI 3D-model table. Parallel to the UI canvas section — each
        // entry references a GameObject by index + carries a screen rect +
        // per-model orbit camera params. Rendered in a post-main-scene
        // HUD pass. Capped at 16 entries.
        int uiModelCount = CountUIModels(scene);
        w.Write((ushort)uiModelCount); // uiModelCount
        w.Write((ushort)0);            // pad_uimodel
        offsets.UiModelTableOffsetPos = w.BaseStream.Position;
        w.Write((uint)0);              // uiModelTableOffset (backfilled)

        // v24: scene-level skybox (16 bytes). Layout matches the UI
        // Image typeData union slot so the runtime can reuse the same
        // tpage/clut/UV decode path. skyEnabled = 0 means the runtime
        // skips the sky pass entirely; texture/clut/UV bytes are then
        // ignored. Resolved here from VRAMPacker (already run before
        // header write) so we can stamp final tpage/clut/UVs.
        WriteSkyBytes(w, scene);

        long written = w.BaseStream.Position - headerStart;
        if (written != HeaderSize)
            throw new InvalidOperationException(
                $"Splashpack header size mismatch: wrote {written} bytes, expected {HeaderSize}.");

        return offsets;
    }

    // Write the 16-byte sky struct at the end of the header. When the
    // scene has no PS1Sky (or the sky's texture failed to register),
    // emits 16 zero bytes — the runtime checks skyEnabled and skips.
    private static void WriteSkyBytes(BinaryWriter w, SceneData scene)
    {
        if (scene.Sky == null || scene.Sky.TextureIndex < 0
                              || scene.Sky.TextureIndex >= scene.Textures.Count)
        {
            for (int k = 0; k < 16; k++) w.Write((byte)0);
            return;
        }

        var sky = scene.Sky;
        var tex = scene.Textures[sky.TextureIndex];

        // Same expander math as mesh + UI Image: 4bpp packs 4 source
        // pixels per VRAM word horizontally, so byte-U coord =
        // PackingX (in VRAM words) × expander. UV rect is the full
        // texture (sky has no sub-region authoring; use entire image).
        int expander = tex.BitDepth switch
        {
            PSXBPP.TEX_4BIT => 4,
            PSXBPP.TEX_8BIT => 2,
            _ => 1,
        };
        int u0 = tex.PackingX * expander;
        int v0 = tex.PackingY;
        int u1 = u0 + tex.Width;
        int v1 = v0 + tex.Height;
        u0 = Mathf.Clamp(u0, 0, 255);
        v0 = Mathf.Clamp(v0, 0, 255);
        u1 = Mathf.Clamp(u1, 0, 255);
        v1 = Mathf.Clamp(v1, 0, 255);

        w.Write(tex.TexpageX);              // skyTexpageX
        w.Write(tex.TexpageY);              // skyTexpageY
        w.Write((ushort)tex.ClutPackingX);  // skyClutX
        w.Write((ushort)tex.ClutPackingY);  // skyClutY
        w.Write((byte)u0);                  // skyU0
        w.Write((byte)v0);                  // skyV0
        w.Write((byte)u1);                  // skyU1
        w.Write((byte)v1);                  // skyV1
        w.Write(sky.BitDepthByte);          // skyBitDepth
        w.Write(sky.TintR);                 // skyTintR
        w.Write(sky.TintG);                 // skyTintG
        w.Write(sky.TintB);                 // skyTintB
        w.Write((byte)1);                   // skyEnabled
        w.Write((byte)0);                   // pad_sky
    }

    // Meters/m-per-s → PSX fp12 ushort, clamped.
    private static ushort MetersToPsxFp12Ushort(float metersOrMps, float gteScaling)
    {
        float psxUnits = metersOrMps / Mathf.Max(gteScaling, 0.0001f);
        int fp = Mathf.RoundToInt(psxUnits * 4096f);
        return (ushort)Mathf.Clamp(fp, 0, 65535);
    }

    // ─── SPLASHPACKCollider (32 bytes) ───────────────────────────────────

    private static void WriteCollider(BinaryWriter w, ColliderRecord c, float gteScaling)
    {
        long entryStart = w.BaseStream.Position;

        // World-space AABB in fp12. The runtime copies this straight into
        // its grid (updateCollider is declared but never called), so we need
        // the final world coords, not locals. Godot→PSX reflects Y and Z —
        // that swaps min↔max on both axes, so PSX minY = -(Godot maxY),
        // PSX minZ = -(Godot maxZ), etc.
        w.Write(PSXTrig.ConvertWorldToFixed12( c.WorldMin.X / gteScaling));
        w.Write(PSXTrig.ConvertWorldToFixed12(-c.WorldMax.Y / gteScaling));
        w.Write(PSXTrig.ConvertWorldToFixed12(-c.WorldMax.Z / gteScaling));
        w.Write(PSXTrig.ConvertWorldToFixed12( c.WorldMax.X / gteScaling));
        w.Write(PSXTrig.ConvertWorldToFixed12(-c.WorldMin.Y / gteScaling));
        w.Write(PSXTrig.ConvertWorldToFixed12(-c.WorldMin.Z / gteScaling));

        w.Write(c.CollisionType);       // 1 byte
        w.Write(c.LayerMask);           // 1 byte
        w.Write(c.GameObjectIndex);     // 2 bytes
        w.Write((uint)0);               // 4-byte padding

        long written = w.BaseStream.Position - entryStart;
        if (written != 32)
            throw new InvalidOperationException($"Collider size mismatch: {written} vs 32.");
    }

    // ─── SPLASHPACKTriggerBox (32 bytes) ────────────────────────────────
    //   int32[6] min/max XYZ + int16 luaFileIndex + u16 pad + u32 pad2.
    //   Same Godot→PSX convention as colliders: reflect Y and Z, swapping
    //   min↔max on both axes.
    private static void WriteTriggerBox(BinaryWriter w, TriggerBoxRecord t, float gteScaling)
    {
        long entryStart = w.BaseStream.Position;
        w.Write(PSXTrig.ConvertWorldToFixed12( t.WorldMin.X / gteScaling));
        w.Write(PSXTrig.ConvertWorldToFixed12(-t.WorldMax.Y / gteScaling));
        w.Write(PSXTrig.ConvertWorldToFixed12(-t.WorldMax.Z / gteScaling));
        w.Write(PSXTrig.ConvertWorldToFixed12( t.WorldMax.X / gteScaling));
        w.Write(PSXTrig.ConvertWorldToFixed12(-t.WorldMin.Y / gteScaling));
        w.Write(PSXTrig.ConvertWorldToFixed12(-t.WorldMin.Z / gteScaling));
        w.Write(t.LuaFileIndex);        // int16
        w.Write((ushort)0);             // padding
        w.Write((uint)0);               // padding2
        long written = w.BaseStream.Position - entryStart;
        if (written != 32)
            throw new InvalidOperationException($"TriggerBox size mismatch: {written} vs 32.");
    }

    // ─── Interactable (28 bytes) ─────────────────────────────────────────
    //   fp12 radiusSquared + u8 button + u8 flags + u16 cooldown +
    //   u16 currentCooldown + u16 gameObjectIndex + char[16] promptCanvasName.
    //   Runtime computes radiusSquared at bake-time here so the hot path
    //   can dist-squared compare without a sqrt.
    private static void WriteInteractable(BinaryWriter w, InteractableRecord ia, float gteScaling)
    {
        long entryStart = w.BaseStream.Position;
        float radiusPsx = ia.RadiusMeters / Mathf.Max(gteScaling, 0.0001f);
        float radiusSqPsx = radiusPsx * radiusPsx;
        int radiusSqFp12 = Mathf.RoundToInt(radiusSqPsx * 4096f);
        w.Write(radiusSqFp12);                      // int32 fp12 radius squared
        w.Write(ia.InteractButton);                 // u8
        byte flags = 0;
        if (ia.Repeatable) flags |= 0x01;
        if (ia.ShowPrompt) flags |= 0x02;
        // bit 2 (requireLineOfSight) + bit 3 (disabled) — defaults off.
        w.Write(flags);                              // u8
        w.Write(ia.CooldownFrames);                 // u16
        w.Write((ushort)0);                         // currentCooldown (runtime state)
        w.Write(ia.GameObjectIndex);                // u16

        // promptCanvasName is a 16-byte inline char array. Pad with zeros.
        byte[] nameBytes = Encoding.UTF8.GetBytes(ia.PromptCanvasName);
        int nameLen = Mathf.Min(nameBytes.Length, 15); // reserve last byte for null
        for (int i = 0; i < nameLen; i++) w.Write(nameBytes[i]);
        for (int i = nameLen; i < 16; i++) w.Write((byte)0);

        long written = w.BaseStream.Position - entryStart;
        if (written != 28)
            throw new InvalidOperationException($"Interactable size mismatch: {written} vs 28.");
    }

    // ─── NavRegion (84 bytes) ────────────────────────────────────────────

    private static void WriteNavRegion(BinaryWriter w, NavRegionRecord nr)
    {
        long entryStart = w.BaseStream.Position;
        const int MaxVerts = 8;

        // VertsX: int32[8] (32 bytes)
        for (int i = 0; i < MaxVerts; i++)
            w.Write(i < nr.VertsX.Length ? nr.VertsX[i] : 0);
        // VertsZ: int32[8] (32 bytes)
        for (int i = 0; i < MaxVerts; i++)
            w.Write(i < nr.VertsZ.Length ? nr.VertsZ[i] : 0);

        w.Write(nr.PlaneA);             // int32
        w.Write(nr.PlaneB);             // int32
        w.Write(nr.PlaneD);             // int32

        w.Write(nr.PortalStart);         // ushort
        w.Write(nr.PortalCount);         // byte
        w.Write((byte)nr.VertsX.Length); // vertCount
        w.Write(nr.SurfaceType);         // byte
        w.Write(nr.RoomIndex);           // byte
        w.Write(nr.Flags);               // byte
        w.Write(nr.WalkoffEdgeMask);     // byte

        long written = w.BaseStream.Position - entryStart;
        if (written != 84)
            throw new InvalidOperationException($"NavRegion size mismatch: {written} vs 84.");
    }

    // ─── RoomData (36 bytes) ─────────────────────────────────────────────
    // World-space AABB in PS1 coords (Y+Z negated, swap min/max on both axes),
    // then the tri-ref / cell / portal-ref slice indices.
    private static void WriteRoom(BinaryWriter w, RoomRecord r, float gteScaling)
    {
        long entryStart = w.BaseStream.Position;
        w.Write(PSXTrig.ConvertWorldToFixed12( r.WorldMin.X / gteScaling));
        w.Write(PSXTrig.ConvertWorldToFixed12(-r.WorldMax.Y / gteScaling));
        w.Write(PSXTrig.ConvertWorldToFixed12(-r.WorldMax.Z / gteScaling));
        w.Write(PSXTrig.ConvertWorldToFixed12( r.WorldMax.X / gteScaling));
        w.Write(PSXTrig.ConvertWorldToFixed12(-r.WorldMin.Y / gteScaling));
        w.Write(PSXTrig.ConvertWorldToFixed12(-r.WorldMin.Z / gteScaling));

        w.Write(r.FirstTriRef);
        w.Write(r.TriRefCount);
        w.Write(r.FirstCell);
        w.Write(r.CellCount);
        w.Write(r.PortalRefCount);
        w.Write(r.FirstPortalRef);
        w.Write((ushort)0);            // pad

        long written = w.BaseStream.Position - entryStart;
        if (written != 36)
            throw new InvalidOperationException($"RoomData size mismatch: {written} vs 36.");
    }

    // ─── RoomCell (28 bytes) ─────────────────────────────────────────────
    // Tight world-space AABB around the triangles bucketed into this cell,
    // followed by the cell's slice of the room's tri-ref array. AABB
    // encoding matches RoomData (Y+Z reflected, min↔max swapped on both).
    private static void WriteRoomCell(BinaryWriter w, RoomCellRecord c, float gteScaling)
    {
        long entryStart = w.BaseStream.Position;
        w.Write(PSXTrig.ConvertWorldToFixed12( c.WorldMin.X / gteScaling));
        w.Write(PSXTrig.ConvertWorldToFixed12(-c.WorldMax.Y / gteScaling));
        w.Write(PSXTrig.ConvertWorldToFixed12(-c.WorldMax.Z / gteScaling));
        w.Write(PSXTrig.ConvertWorldToFixed12( c.WorldMax.X / gteScaling));
        w.Write(PSXTrig.ConvertWorldToFixed12(-c.WorldMin.Y / gteScaling));
        w.Write(PSXTrig.ConvertWorldToFixed12(-c.WorldMin.Z / gteScaling));

        w.Write(c.FirstTriRef);
        w.Write(c.TriRefCount);

        long written = w.BaseStream.Position - entryStart;
        if (written != 28)
            throw new InvalidOperationException($"RoomCell size mismatch: {written} vs 28.");
    }

    // ─── PortalData (40 bytes) ───────────────────────────────────────────
    // Centre is in world fp12 (Y and Z negated). half-W/H + axis vectors
    // are in 4.12 fp (multiply by 4096 then int16-clamp). Y and Z
    // components of the axis vectors flip sign for the PSX Y-down +
    // Z-forward convention.
    private static void WritePortal(BinaryWriter w, PortalRecord p, float gteScaling)
    {
        long entryStart = w.BaseStream.Position;
        w.Write(p.RoomA);
        w.Write(p.RoomB);

        w.Write(PSXTrig.ConvertWorldToFixed12( p.WorldCenter.X / gteScaling));
        w.Write(PSXTrig.ConvertWorldToFixed12(-p.WorldCenter.Y / gteScaling));
        w.Write(PSXTrig.ConvertWorldToFixed12(-p.WorldCenter.Z / gteScaling));

        short halfW = (short)Mathf.Clamp(Mathf.RoundToInt(p.PortalSize.X * 0.5f / gteScaling * 4096f), 1, short.MaxValue);
        short halfH = (short)Mathf.Clamp(Mathf.RoundToInt(p.PortalSize.Y * 0.5f / gteScaling * 4096f), 1, short.MaxValue);
        w.Write(halfW);
        w.Write(halfH);

        short Pack(float v) => (short)Mathf.Clamp(Mathf.RoundToInt(v * 4096f), short.MinValue, short.MaxValue);
        w.Write(Pack( p.Normal.X));
        w.Write(Pack(-p.Normal.Y));
        w.Write(Pack(-p.Normal.Z));
        w.Write((short)0);            // pad

        w.Write(Pack( p.Right.X));
        w.Write(Pack(-p.Right.Y));
        w.Write(Pack(-p.Right.Z));

        w.Write(Pack( p.Up.X));
        w.Write(Pack(-p.Up.Y));
        w.Write(Pack(-p.Up.Z));

        long written = w.BaseStream.Position - entryStart;
        if (written != 40)
            throw new InvalidOperationException($"PortalData size mismatch: {written} vs 40.");
    }

    // ─── NavPortal (20 bytes) ────────────────────────────────────────────
    private static void WriteNavPortal(BinaryWriter w, NavPortalRecord p)
    {
        long entryStart = w.BaseStream.Position;
        w.Write(p.Ax);                  // int32
        w.Write(p.Az);                  // int32
        w.Write(p.Bx);                  // int32
        w.Write(p.Bz);                  // int32
        w.Write(p.NeighborRegion);      // ushort
        w.Write(p.HeightDelta);         // short

        long written = w.BaseStream.Position - entryStart;
        if (written != 20)
            throw new InvalidOperationException($"NavPortal size mismatch: {written} vs 20.");
    }

    // ─── GameObject entry (92 bytes) ─────────────────────────────────────

    private static long WriteGameObjectEntry(BinaryWriter w, SceneObject obj, float gteScaling)
    {
        long entryStart = w.BaseStream.Position;
        long polygonsOffsetPos = w.BaseStream.Position;
        w.Write((uint)0); // polygonsOffset placeholder

        // Position: psyqo::Vec3 = 3 × int32. Godot → PSX: negate Y and Z.
        var pos = obj.Node.GlobalPosition;
        w.Write(PSXTrig.ConvertWorldToFixed12( pos.X / gteScaling));
        w.Write(PSXTrig.ConvertWorldToFixed12(-pos.Y / gteScaling));
        w.Write(PSXTrig.ConvertWorldToFixed12(-pos.Z / gteScaling));

        // Rotation: 3×3 int32 matrix
        Quaternion q = obj.Node.GlobalBasis.GetRotationQuaternion();
        int[,] rot = PSXTrig.ConvertRotationToPSXMatrix(q);
        for (int r = 0; r < 3; r++)
            for (int c = 0; c < 3; c++)
                w.Write(rot[r, c]);

        w.Write((ushort)obj.Mesh.Triangles.Count);
        w.Write((short)obj.LuaFileIndex); // -1 = no script attached

        // flags — bit 0 = isActive. StartsInactive=true for pool templates
        // that Lua's GameObject.Spawn will activate at runtime.
        w.Write(obj.StartsInactive ? (uint)0 : (uint)1);

        w.Write(NoComponent);           // interactableIndex
        w.Write(obj.Tag);               // gameplay tag (0 = untagged)
        w.Write((uint)0);               // eventMask (runtime)

        WriteWorldAabb(w, obj, gteScaling);

        long written = w.BaseStream.Position - entryStart;
        if (written != GameObjectSize)
            throw new InvalidOperationException(
                $"GameObject entry size mismatch: wrote {written} bytes, expected {GameObjectSize}.");

        return polygonsOffsetPos;
    }

    private static void WriteWorldAabb(BinaryWriter w, SceneObject obj, float gteScaling)
    {
        Aabb local = obj.LocalAabb;
        Vector3 min = local.Position;
        Vector3 max = local.Position + local.Size;

        Vector3 wmin = new(float.MaxValue, float.MaxValue, float.MaxValue);
        Vector3 wmax = new(float.MinValue, float.MinValue, float.MinValue);

        var xform = obj.Node.GlobalTransform;
        for (int i = 0; i < 8; i++)
        {
            var corner = new Vector3(
                (i & 1) != 0 ? max.X : min.X,
                (i & 2) != 0 ? max.Y : min.Y,
                (i & 4) != 0 ? max.Z : min.Z);
            Vector3 world = xform * corner;
            wmin = new Vector3(Mathf.Min(wmin.X, world.X), Mathf.Min(wmin.Y, world.Y), Mathf.Min(wmin.Z, world.Z));
            wmax = new Vector3(Mathf.Max(wmax.X, world.X), Mathf.Max(wmax.Y, world.Y), Mathf.Max(wmax.Z, world.Z));
        }

        // Godot → PSX: reflect Y and Z, so min↔max swaps on both axes.
        w.Write(PSXTrig.ConvertWorldToFixed12( wmin.X / gteScaling));
        w.Write(PSXTrig.ConvertWorldToFixed12(-wmax.Y / gteScaling));
        w.Write(PSXTrig.ConvertWorldToFixed12(-wmax.Z / gteScaling));
        w.Write(PSXTrig.ConvertWorldToFixed12( wmax.X / gteScaling));
        w.Write(PSXTrig.ConvertWorldToFixed12(-wmin.Y / gteScaling));
        w.Write(PSXTrig.ConvertWorldToFixed12(-wmin.Z / gteScaling));
    }

    // ─── Tri (52 bytes) ─────────────────────────────────────────────────

    private static void WriteTri(BinaryWriter w, Tri tri, SceneData scene)
    {
        long triStart = w.BaseStream.Position;

        // 3 × PackedVec3 vertex positions = 18 bytes
        WriteShort3(w, tri.v0.vx, tri.v0.vy, tri.v0.vz);
        WriteShort3(w, tri.v1.vx, tri.v1.vy, tri.v1.vz);
        WriteShort3(w, tri.v2.vx, tri.v2.vy, tri.v2.vz);

        // 1 × PackedVec3 normal (v0's only) = 6 bytes
        WriteShort3(w, tri.v0.nx, tri.v0.ny, tri.v0.nz);

        // 3 × Color = 12 bytes (rgb + 1 code byte)
        WriteColor4(w, tri.v0.r, tri.v0.g, tri.v0.b);
        WriteColor4(w, tri.v1.r, tri.v1.g, tri.v1.b);
        WriteColor4(w, tri.v2.r, tri.v2.g, tri.v2.b);

        // UVs + tpage + clut = 16 bytes
        PSXTexture? tex = (tri.TextureIndex >= 0 && tri.TextureIndex < scene.Textures.Count)
            ? scene.Textures[tri.TextureIndex]
            : null;

        if (tex == null)
        {
            // Untextured: zero UVs, sentinel tpage.
            w.Write((byte)0); w.Write((byte)0);  // uvA
            w.Write((byte)0); w.Write((byte)0);  // uvB
            w.Write((byte)0); w.Write((byte)0);  // uvC
            w.Write((ushort)0);                  // uvC padding
            w.Write(UntexturedTpage);
            w.Write((ushort)0);                  // clutX
            w.Write((ushort)0);                  // clutY
            w.Write((ushort)0);                  // padding
        }
        else
        {
            // PSX UVs are byte-addressed in VRAM. For <16bpp textures, multiple
            // texture pixels share one 16-bit VRAM word, so PackingX (in VRAM
            // words) is multiplied by bits-per-word / bpp to get the UV offset.
            int expander = tex.BitDepth switch
            {
                PSXBPP.TEX_4BIT => 4,
                PSXBPP.TEX_8BIT => 2,
                _ => 1,
            };
            WriteUvByte(w, tri.v0.u, tri.v0.v, tex, expander);
            WriteUvByte(w, tri.v1.u, tri.v1.v, tex, expander);
            WriteUvByte(w, tri.v2.u, tri.v2.v, tex, expander);
            w.Write((ushort)0);                  // uvC padding

            w.Write(VRAMPacker.BuildTpageAttr(tex.TexpageX, tex.TexpageY, tex.BitDepth));
            w.Write(tex.ClutPackingX);
            w.Write(tex.ClutPackingY);
            w.Write((ushort)0);                  // padding
        }

        long written = w.BaseStream.Position - triStart;
        if (written != TriSize)
            throw new InvalidOperationException(
                $"Tri size mismatch: wrote {written} bytes, expected {TriSize}.");
    }

    private static void WriteUvByte(BinaryWriter w, byte u, byte v, PSXTexture tex, int expander)
    {
        int ub = u + tex.PackingX * expander;
        int vb = v + tex.PackingY;
        w.Write((byte)(ub & 0xFF));
        w.Write((byte)(vb & 0xFF));
    }

    private static void WriteShort3(BinaryWriter w, short a, short b, short c)
    {
        w.Write(a); w.Write(b); w.Write(c);
    }

    private static void WriteColor4(BinaryWriter w, byte r, byte g, byte b)
    {
        w.Write(r); w.Write(g); w.Write(b);
        w.Write((byte)0); // code — unused for G3 prims
    }

    // ─── .vram file ──────────────────────────────────────────────────────
    //
    // Format parsed by scenemanager.cpp:uploadVramData():
    //   'V' 'R' atlasCount(u16) clutCount(u16) fontCount(u8) pad(u8)
    //   Per atlas: vramX(u16) vramY(u16) width(u16) height(u16) + pixel data(w*h*2) + align4
    //   Per CLUT:  clutPackingX(u16) clutPackingY(u16) length(u16) pad(u16) + data(length*2) + align4
    //   Per font:  [glyphW, glyphH, ...] — we never emit fonts here.

    private static void WriteVram(string path, SceneData scene)
    {
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
        using var w = new BinaryWriter(fs);

        int atlasCount = scene.Packer?.AtlasCount ?? 0;
        int clutCount = 0;
        if (scene.Packer != null)
            foreach (var _ in scene.Packer.CLUTOwners()) clutCount++;

        // Header
        w.Write((byte)'V');
        w.Write((byte)'R');
        w.Write((ushort)atlasCount);
        w.Write((ushort)clutCount);
        w.Write((byte)0);   // fontCount
        w.Write((byte)0);   // pad

        if (scene.Packer == null) return;

        // Atlas chunks
        foreach (var chunk in scene.Packer.EnumerateAtlasChunks())
        {
            w.Write((ushort)chunk.vramX);
            w.Write((ushort)chunk.vramY);
            w.Write((ushort)chunk.width);
            w.Write((ushort)chunk.height);
            for (int y = 0; y < chunk.height; y++)
                for (int x = 0; x < chunk.width; x++)
                    w.Write(chunk.pixels[x, y].Pack());
            AlignTo4(w);
        }

        // CLUT chunks
        foreach (var tex in scene.Packer.CLUTOwners())
        {
            int len = tex.ColorPalette!.Count;
            w.Write((ushort)tex.ClutPackingX);
            w.Write((ushort)tex.ClutPackingY);
            w.Write((ushort)len);
            w.Write((ushort)0); // pad
            for (int i = 0; i < len; i++)
                w.Write(tex.ColorPalette[i].Pack());
            AlignTo4(w);
        }
    }

    // .spu format (parsed by scenemanager.cpp:uploadSpuData):
    //   'S' 'A' clipCount(u16)
    //   Per clip: sizeBytes(u32) sampleRate(u16) loop(u8) pad(u8)
    //             + sizeBytes of raw ADPCM
    //             + align to 4
    private static void WriteSpu(string path, SceneData scene)
    {
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
        using var w = new BinaryWriter(fs);

        w.Write((byte)'S');
        w.Write((byte)'A');
        w.Write((ushort)scene.AudioClips.Count);

        foreach (var clip in scene.AudioClips)
        {
            w.Write((uint)clip.AdpcmData.Length);
            w.Write((ushort)clip.SampleRate);
            w.Write((byte)(clip.Loop ? 1 : 0));
            w.Write((byte)0); // pad
            w.Write(clip.AdpcmData);
            AlignTo4(w);
        }
    }

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static void AlignTo4(BinaryWriter w)
    {
        long pos = w.BaseStream.Position;
        int padding = (int)(4 - (pos % 4)) % 4;
        for (int i = 0; i < padding; i++) w.Write((byte)0);
    }

    private static void AlignTo2(BinaryWriter w)
    {
        if ((w.BaseStream.Position & 1) != 0) w.Write((byte)0);
    }

    private static void BackfillUInt32(BinaryWriter w, long position, uint value)
    {
        long curPos = w.BaseStream.Position;
        w.Seek((int)position, SeekOrigin.Begin);
        w.Write(value);
        w.Seek((int)curPos, SeekOrigin.Begin);
    }
}
