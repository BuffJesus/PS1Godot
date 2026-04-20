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
    public const ushort SplashpackVersion = 20;
    public const int HeaderSize = 120;
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
        int luaFileCount = scene.LuaFiles.Count;
        int triggerBoxCount = scene.TriggerBoxes.Count;
        int interactableCount = scene.Interactables.Count;

        var headerOffsets = WriteHeader(w, scene, atlasCount, clutCount, colliderCount, navRegionCount, luaFileCount, triggerBoxCount, interactableCount);

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
            w.Write((ushort)0);           // portalCount (no multi-region yet)
            w.Write((ushort)0);           // startRegion (player spawns in region 0)
            w.Write((ushort)0);           // pad
            foreach (var nr in scene.NavRegions)
            {
                WriteNavRegion(w, nr);
            }
            // 0 portals — nothing to write.
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
        for (int i = 0; i < count; i++)
        {
            var c = scene.Cutscenes[i];
            AlignTo4(w);
            long blockStart = w.BaseStream.Position;
            BackfillUInt32(w, dataOffPos[i], (uint)blockStart);

            // SPLASHPACKCutscene (16 B for v19+ layout).
            w.Write(c.TotalFrames);                  // u16
            w.Write((byte)c.Tracks.Count);           // trackCount
            w.Write((byte)0);                        // audioEventCount (B.2 Phase 1: none)
            long tracksOffPos = w.BaseStream.Position;
            w.Write((uint)0);                        // tracksOff (backfilled)
            w.Write((uint)0);                        // audioOff (none)
            // v19 skin anim event fields — stubbed in B.2 Phase 1.
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

    // Emit UI table + element arrays + strings. Runtime's parser starts at
    // uiTableOffset, walks fontCount×112 B font descriptors (zero in MVP),
    // then canvasCount×12 B canvas descriptors. Element arrays and string
    // data live at arbitrary offsets referenced by those descriptors.
    private static void WriteUISection(BinaryWriter w, SceneData scene, long uiTableOffsetPos)
    {
        AlignTo4(w);
        long uiTableStart = w.BaseStream.Position;
        BackfillUInt32(w, uiTableOffsetPos, (uint)uiTableStart);

        // MVP: fontCount = 0, so no font descriptors here.

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

                // Type-specific (16 B). Text = fontIndex (0 = system) +
                // 15 pad; Box = all zeros.
                if (el.Type == PS1UIElementType.Text)
                {
                    w.Write((byte)0);                        // fontIndex (system)
                    for (int k = 0; k < 15; k++) w.Write((byte)0);
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
    }

    private static HeaderOffsets WriteHeader(BinaryWriter w, SceneData scene, int atlasCount, int clutCount, int colliderCount, int navRegionCount, int luaFileCount, int triggerBoxCount, int interactableCount)
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
        var pp = scene.PlayerPosition;
        w.Write(PSXTrig.ConvertCoordinateToPSX(pp.X, scene.GteScaling));
        w.Write(PSXTrig.ConvertCoordinateToPSX(-pp.Y, scene.GteScaling));
        w.Write(PSXTrig.ConvertCoordinateToPSX(pp.Z, scene.GteScaling));
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
        w.Write((ushort)0);            // navPortalCount

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
        w.Write((ushort)0);            // roomCount
        w.Write((ushort)0);            // portalCount
        w.Write((ushort)0);            // roomTriRefCount

        w.Write((ushort)scene.Cutscenes.Count); // cutsceneCount
        w.Write((ushort)0);            // roomCellCount
        offsets.CutsceneTableOffsetPos = w.BaseStream.Position;
        w.Write((uint)0);              // cutsceneTableOffset (backfilled)

        w.Write((ushort)scene.UICanvases.Count); // uiCanvasCount
        w.Write((byte)0);              // uiFontCount (MVP: system font only)
        w.Write((byte)0);              // uiPad5
        offsets.UiTableOffsetPos = w.BaseStream.Position;
        w.Write((uint)0);              // uiTableOffset (backfilled)

        offsets.PixelDataOffsetPos = w.BaseStream.Position;
        w.Write((uint)0);              // pixelDataOffset (0 = v20 split)

        w.Write((ushort)scene.Animations.Count); // animationCount
        w.Write((ushort)0);            // roomPortalRefCount
        offsets.AnimationTableOffsetPos = w.BaseStream.Position;
        w.Write((uint)0);              // animationTableOffset (backfilled)

        w.Write((ushort)0);            // skinnedMeshCount
        w.Write((ushort)0);            // pad_skin
        offsets.SkinTableOffsetPos = w.BaseStream.Position;
        w.Write((uint)0);              // skinTableOffset

        long written = w.BaseStream.Position - headerStart;
        if (written != HeaderSize)
            throw new InvalidOperationException(
                $"Splashpack header size mismatch: wrote {written} bytes, expected {HeaderSize}.");

        return offsets;
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
        // the final world coords, not locals. Godot→PSX negates Y; that
        // inverts min↔max on the Y axis so the PSX minY ends up equal to
        // -(Godot maxY).
        w.Write(PSXTrig.ConvertWorldToFixed12(c.WorldMin.X / gteScaling));
        w.Write(PSXTrig.ConvertWorldToFixed12(-c.WorldMax.Y / gteScaling));
        w.Write(PSXTrig.ConvertWorldToFixed12(c.WorldMin.Z / gteScaling));
        w.Write(PSXTrig.ConvertWorldToFixed12(c.WorldMax.X / gteScaling));
        w.Write(PSXTrig.ConvertWorldToFixed12(-c.WorldMin.Y / gteScaling));
        w.Write(PSXTrig.ConvertWorldToFixed12(c.WorldMax.Z / gteScaling));

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
    //   Same Godot→PSX convention as colliders: negate Y, swap min/max.Y.
    private static void WriteTriggerBox(BinaryWriter w, TriggerBoxRecord t, float gteScaling)
    {
        long entryStart = w.BaseStream.Position;
        w.Write(PSXTrig.ConvertWorldToFixed12(t.WorldMin.X / gteScaling));
        w.Write(PSXTrig.ConvertWorldToFixed12(-t.WorldMax.Y / gteScaling));
        w.Write(PSXTrig.ConvertWorldToFixed12(t.WorldMin.Z / gteScaling));
        w.Write(PSXTrig.ConvertWorldToFixed12(t.WorldMax.X / gteScaling));
        w.Write(PSXTrig.ConvertWorldToFixed12(-t.WorldMin.Y / gteScaling));
        w.Write(PSXTrig.ConvertWorldToFixed12(t.WorldMax.Z / gteScaling));
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

        w.Write((ushort)0);             // portalStart
        w.Write((byte)0);               // portalCount
        w.Write((byte)nr.VertsX.Length); // vertCount
        w.Write((byte)0);               // surfaceType 0 = FLAT
        w.Write((byte)0xFF);            // roomIndex 0xFF = exterior
        w.Write((byte)0);               // flags
        w.Write((byte)0);               // walkoffEdgeMask

        long written = w.BaseStream.Position - entryStart;
        if (written != 84)
            throw new InvalidOperationException($"NavRegion size mismatch: {written} vs 84.");
    }

    // ─── GameObject entry (92 bytes) ─────────────────────────────────────

    private static long WriteGameObjectEntry(BinaryWriter w, SceneObject obj, float gteScaling)
    {
        long entryStart = w.BaseStream.Position;
        long polygonsOffsetPos = w.BaseStream.Position;
        w.Write((uint)0); // polygonsOffset placeholder

        // Position: psyqo::Vec3 = 3 × int32. Godot → PSX: negate Y.
        var pos = obj.Node.GlobalPosition;
        w.Write(PSXTrig.ConvertWorldToFixed12(pos.X / gteScaling));
        w.Write(PSXTrig.ConvertWorldToFixed12(-pos.Y / gteScaling));
        w.Write(PSXTrig.ConvertWorldToFixed12(pos.Z / gteScaling));

        // Rotation: 3×3 int32 matrix
        Quaternion q = obj.Node.GlobalBasis.GetRotationQuaternion();
        int[,] rot = PSXTrig.ConvertRotationToPSXMatrix(q);
        for (int r = 0; r < 3; r++)
            for (int c = 0; c < 3; c++)
                w.Write(rot[r, c]);

        w.Write((ushort)obj.Mesh.Triangles.Count);
        w.Write((short)obj.LuaFileIndex); // -1 = no script attached

        w.Write((uint)1);               // flags — bit 0 = isActive

        w.Write(NoComponent);           // interactableIndex
        w.Write((ushort)0);             // _reserved0
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
        Aabb local = obj.Node.Mesh.GetAabb();
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

        // Godot → PSX: negate Y only. Swap min/max for Y since we negated it.
        w.Write(PSXTrig.ConvertWorldToFixed12(wmin.X / gteScaling));
        w.Write(PSXTrig.ConvertWorldToFixed12(-wmax.Y / gteScaling));
        w.Write(PSXTrig.ConvertWorldToFixed12(wmin.Z / gteScaling));
        w.Write(PSXTrig.ConvertWorldToFixed12(wmax.X / gteScaling));
        w.Write(PSXTrig.ConvertWorldToFixed12(-wmin.Y / gteScaling));
        w.Write(PSXTrig.ConvertWorldToFixed12(wmax.Z / gteScaling));
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

    private static void BackfillUInt32(BinaryWriter w, long position, uint value)
    {
        long curPos = w.BaseStream.Position;
        w.Seek((int)position, SeekOrigin.Begin);
        w.Write(value);
        w.Seek((int)curPos, SeekOrigin.Begin);
    }
}
