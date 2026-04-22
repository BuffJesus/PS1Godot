#pragma once

#include <godot_cpp/classes/font_file.hpp>
#include <godot_cpp/classes/image.hpp>
#include <godot_cpp/classes/ref_counted.hpp>
#include <godot_cpp/variant/dictionary.hpp>
#include <godot_cpp/variant/packed_byte_array.hpp>

namespace godot {

// Rasterize a FontFile's glyph atlas into a PS1-shaped bitmap +
// proportional advance widths. Lives in the GDExtension because the
// Godot 4.7-dev.5 C# binding omits FontFile::get_glyph_uv_rect,
// which we need to locate glyph pixels inside the font's internal
// cache image. godot-cpp exposes it cleanly.
//
// Output: a Dictionary (so C# can read it as a plain map) with:
//   "glyph_width"    — int, one of {4, 8, 16, 32}
//   "glyph_height"   — int, cell height in px
//   "bitmap"         — Ref<Image>, 256 px wide × (rows * glyph_height),
//                      RGBA8, white-on-transparent
//   "advance_widths" — PackedByteArray of length 96 covering ASCII
//                      0x20..0x7F (index = char - 0x20), clamped [1, 255]
//
// Empty Dictionary on error (with ERR_PRINT describing the cause).
class PS1FontRasterizer : public RefCounted {
	GDCLASS(PS1FontRasterizer, RefCounted);

protected:
	static void _bind_methods();

public:
	// Rasterize `p_font` at `p_font_size` into the output format above.
	// `p_alpha_threshold` is the minimum source-pixel alpha that counts
	// as "opaque glyph ink" — TTF anti-alias below this becomes
	// transparent in the output (PS1 renderer can't reproduce alpha
	// gradients). 0.3 matches SplashEdit's PSXFontAsset.
	Dictionary rasterize(const Ref<FontFile> &p_font, int p_font_size, float p_alpha_threshold = 0.3f);
};

} // namespace godot
