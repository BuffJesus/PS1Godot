using System.Collections.Generic;
using System.IO;
using Godot;
using FileAccess = System.IO.FileAccess;

namespace PS1Godot.Exporter;

// LoaderPack writer — emits the optional `scene_N.loading` sidecar that
// the runtime renders BEFORE the main splashpack is loaded. Layout
// parsed by psxsplash-main/src/loadingscreen.cpp; structures defined in
// loadingscreen.hh (LoaderPackHeader / LoaderPackAtlas / LoaderPackClut).
//
// File contents:
//   0..15      LoaderPackHeader (magic "LP", version 2, fontCount,
//              canvasCount=1, resW/H, atlasCount, clutCount, tableOffset)
//   16..       LoaderPackAtlas[]   (12 B × atlasCount, pixelDataOffset
//              backfilled after pixel blobs are written)
//   ..         LoaderPackClut[]    (12 B × clutCount,  clutDataOffset
//              backfilled after CLUT blobs are written)
//   ..align4   Atlas pixel blobs   (each: width × height × 2 bytes)
//   ..align4   CLUT pixel blobs    (each: length × 2 bytes)
//   ..align4   UI table block      (font descs + canvas desc + elements
//              + strings + font pixels — same layout as the splashpack
//              UI section, written via SplashpackWriter.WriteUITableBlock)
//
// Atlas/CLUT positions match the absolute VRAM coordinates the
// splashpack will later upload the same textures to. The runtime frees
// the loader pack's data before the splashpack uploads its own atlases,
// so the splashpack's textures cleanly overwrite whatever the loading
// screen put down — and the canvas's UV coordinates stay valid across
// both phases because they reference those same absolute positions.
//
// Skipped entirely when the scene has no LoadingScreen-residency
// canvas (SceneData.LoadingScreenCanvasIndex < 0). Stale .loading files
// from a previous export are deleted in that case so authors don't
// ship an unwanted loading screen.
public static class LoaderPackWriter
{
    public const int HeaderSize = 16;
    public const int AtlasMetaSize = 12;
    public const int ClutMetaSize = 12;
    public const ushort Version = 2;
    public const int ResW = 320;
    public const int ResH = 240;

    public static void Write(string loadingPath, SceneData scene)
    {
        if (scene.LoadingScreenCanvasIndex < 0 ||
            scene.LoadingScreenCanvasIndex >= scene.UICanvases.Count)
        {
            // No loading screen authored — make sure a stale file from a
            // previous export doesn't get bundled into the next ISO build.
            if (File.Exists(loadingPath)) File.Delete(loadingPath);
            return;
        }

        var canvas = scene.UICanvases[scene.LoadingScreenCanvasIndex];

        // Collect every PSXTexture this canvas references through Image
        // elements. Dedup by TextureIndex — multiple Image elements may
        // sample the same packed texture (sub-region UVs).
        var atlasTexIndices = new List<int>();
        var seenAtlas = new HashSet<int>();
        var clutTexIndices = new List<int>();
        var seenClut = new HashSet<int>();
        foreach (var el in canvas.Elements)
        {
            if (el.Type != PS1UIElementType.Image) continue;
            if (el.TextureIndex < 0 || el.TextureIndex >= scene.Textures.Count) continue;
            if (seenAtlas.Add(el.TextureIndex))
                atlasTexIndices.Add(el.TextureIndex);
            var tex = scene.Textures[el.TextureIndex];
            if (tex.ColorPalette != null && seenClut.Add(el.TextureIndex))
                clutTexIndices.Add(el.TextureIndex);
        }

        using var fs = new FileStream(loadingPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
        using var w = new BinaryWriter(fs);

        // ── Header (16 B) ─────────────────────────────────────────────
        // tableOffset is backfilled once we know where the UI table starts.
        w.Write((byte)'L');
        w.Write((byte)'P');
        w.Write(Version);
        w.Write((byte)scene.UIFonts.Count);
        w.Write((byte)1);                           // canvasCount — always 1
        w.Write((ushort)ResW);
        w.Write((ushort)ResH);
        w.Write((byte)atlasTexIndices.Count);
        w.Write((byte)clutTexIndices.Count);
        long tableOffsetPos = w.BaseStream.Position;
        w.Write((uint)0);                           // tableOffset (backfilled)

        // ── Atlas metadata (12 B each) ────────────────────────────────
        // Each entry's pixelDataOffset stays zero until the pixel blob
        // is written below. VRAM (x, y) is computed from the texture's
        // packed position so the runtime's uploadToVRAM lands the
        // pixels in the same slot the splashpack will overwrite later.
        var atlasDataOffPositions = new long[atlasTexIndices.Count];
        for (int i = 0; i < atlasTexIndices.Count; i++)
        {
            var tex = scene.Textures[atlasTexIndices[i]];
            atlasDataOffPositions[i] = w.BaseStream.Position;
            w.Write((uint)0);                            // pixelDataOffset (backfilled)
            // Atlas region in VRAM-word units along X, pixel rows along Y.
            // Same encoding scenemanager.cpp:uploadVramData consumes for
            // the main splashpack VRAM blobs.
            w.Write((ushort)tex.QuantizedWidth);          // width
            w.Write((ushort)tex.Height);                  // height
            int vramX = tex.TexpageX * 64 + tex.PackingX;
            int vramY = tex.TexpageY * 256 + tex.PackingY;
            w.Write((ushort)vramX);
            w.Write((ushort)vramY);
        }

        // ── CLUT metadata (12 B each) ────────────────────────────────
        var clutDataOffPositions = new long[clutTexIndices.Count];
        for (int i = 0; i < clutTexIndices.Count; i++)
        {
            var tex = scene.Textures[clutTexIndices[i]];
            int len = tex.ColorPalette!.Count;
            clutDataOffPositions[i] = w.BaseStream.Position;
            w.Write((uint)0);                            // clutDataOffset (backfilled)
            w.Write(tex.ClutPackingX);                   // pre-divided by 16
            w.Write(tex.ClutPackingY);
            w.Write((ushort)len);
            w.Write((ushort)0);                          // pad
        }

        // ── Atlas pixel blobs ─────────────────────────────────────────
        for (int i = 0; i < atlasTexIndices.Count; i++)
        {
            SplashpackWriter.AlignTo4(w);
            long dataOff = w.BaseStream.Position;
            var tex = scene.Textures[atlasTexIndices[i]];
            for (int y = 0; y < tex.Height; y++)
                for (int x = 0; x < tex.QuantizedWidth; x++)
                    w.Write(tex.ImageData[x, y].Pack());
            SplashpackWriter.BackfillUInt32(w, atlasDataOffPositions[i], (uint)dataOff);
        }

        // ── CLUT pixel blobs ──────────────────────────────────────────
        for (int i = 0; i < clutTexIndices.Count; i++)
        {
            SplashpackWriter.AlignTo4(w);
            long dataOff = w.BaseStream.Position;
            var tex = scene.Textures[clutTexIndices[i]];
            int len = tex.ColorPalette!.Count;
            for (int p = 0; p < len; p++)
                w.Write(tex.ColorPalette[p].Pack());
            SplashpackWriter.BackfillUInt32(w, clutDataOffPositions[i], (uint)dataOff);
        }

        // ── UI table block — same layout as splashpack UI section ────
        SplashpackWriter.AlignTo4(w);
        long tableStart = w.BaseStream.Position;
        SplashpackWriter.BackfillUInt32(w, tableOffsetPos, (uint)tableStart);
        // Emit ALL scene fonts (no FontIndex remap needed — element
        // FontIndex stays valid) and just the LoadingScreen canvas.
        var canvasList = new List<UICanvasRecord> { canvas };
        SplashpackWriter.WriteUITableBlock(w, scene, scene.UIFonts, canvasList);

        GD.Print($"[PS1Godot] LoaderPack written: '{Path.GetFileName(loadingPath)}' " +
                 $"({canvas.Elements.Count} elements, {atlasTexIndices.Count} atlases, " +
                 $"{clutTexIndices.Count} CLUTs, {scene.UIFonts.Count} fonts, " +
                 $"{w.BaseStream.Position} bytes)");
    }
}
