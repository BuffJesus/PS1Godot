#if TOOLS
using Godot;

namespace PS1Godot.Tools;

// Thin C# shim over the PS1FontRasterizer GDExtension class. The
// heavy lifting (rasterize TTF glyphs at a given size, extract UV
// rects, blit into a 256-wide atlas, capture proportional advance
// widths) runs in C++ because Godot 4.7-dev.5's C# binding omits
// FontFile.GetGlyphUvRect — godot-cpp has it. See
// addons/ps1godot/scripting/src/PS1FontRasterizer.{hpp,cpp}.
//
// Call Populate(asset) from a tool-menu handler; the asset's
// Generated fields are written back on success.
public static class PS1FontGenerator
{
    public static bool Populate(PS1UIFontAsset asset)
    {
        if (asset == null)
        {
            GD.PushError("[PS1Godot] PS1FontGenerator: null asset.");
            return false;
        }
        if (asset.SourceFont == null)
        {
            GD.PushError("[PS1Godot] PS1FontGenerator: asset has no SourceFont. " +
                         "Assign a .ttf / .otf (or any FontFile) before generating.");
            return false;
        }
        if (asset.FontSize < 6 || asset.FontSize > 32)
        {
            GD.PushError($"[PS1Godot] PS1FontGenerator: FontSize {asset.FontSize} outside [6, 32].");
            return false;
        }

        // PS1FontRasterizer is registered by the GDExtension. Instantiate
        // dynamically so we don't need a C# stub generated — works even
        // on fresh clones before the editor has regenerated bindings.
        var obj = ClassDB.Instantiate("PS1FontRasterizer").As<GodotObject>();
        if (obj == null)
        {
            GD.PushError("[PS1Godot] PS1FontGenerator: PS1FontRasterizer class not found. " +
                         "Is the ps1lua.gdextension loaded? Rebuild the extension via " +
                         "'scons' in addons/ps1godot/scripting/ and reopen the project.");
            return false;
        }

        // PS1FontRasterizer inherits RefCounted — don't call Free() on
        // it, let the reference drop when obj goes out of scope.
        var resultVar = obj.Call("rasterize", asset.SourceFont, asset.FontSize, asset.AlphaThreshold);
        var result = resultVar.As<Godot.Collections.Dictionary>();
        if (result == null || result.Count == 0)
        {
            GD.PushError("[PS1Godot] PS1FontGenerator: rasterizer returned empty. " +
                         "See earlier ERR_PRINT for the root cause.");
            return false;
        }

        int glyphW = result["glyph_width"].AsInt32();
        int glyphH = result["glyph_height"].AsInt32();
        var bitmap = result["bitmap"].As<Image>();
        var advances = result["advance_widths"].AsByteArray();

        if (bitmap == null)
        {
            GD.PushError("[PS1Godot] PS1FontGenerator: rasterizer returned null bitmap.");
            return false;
        }
        if (advances == null || advances.Length != 96)
        {
            GD.PushError($"[PS1Godot] PS1FontGenerator: advance widths length {advances?.Length ?? 0} != 96.");
            return false;
        }

        asset.GlyphWidth = glyphW;
        asset.GlyphHeight = glyphH;
        asset.Bitmap = bitmap;
        asset.AdvanceWidths = advances;

        // Default the Lua-facing name to the file stem if unset.
        if (string.IsNullOrWhiteSpace(asset.FontName))
        {
            string stem = string.IsNullOrEmpty(asset.ResourcePath)
                ? "Custom"
                : System.IO.Path.GetFileNameWithoutExtension(asset.ResourcePath);
            asset.FontName = stem;
        }

        GD.Print($"[PS1Godot] Generated '{asset.FontName}' @ {asset.FontSize}px: " +
                 $"{glyphW}×{glyphH} cells, atlas {bitmap.GetWidth()}×{bitmap.GetHeight()} px.");
        return true;
    }
}
#endif
