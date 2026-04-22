#include "PS1FontRasterizer.hpp"

#include <godot_cpp/classes/image.hpp>
#include <godot_cpp/core/class_db.hpp>
#include <godot_cpp/core/math.hpp>
#include <godot_cpp/variant/color.hpp>
#include <godot_cpp/variant/rect2.hpp>
#include <godot_cpp/variant/utility_functions.hpp>
#include <godot_cpp/variant/vector2.hpp>
#include <godot_cpp/variant/vector2i.hpp>

using namespace godot;

namespace {
// PSX UV-wrapping constraint: cell widths must divide 256 evenly so
// UV coords don't land mid-texel. Matches SplashEdit's
// PSXFontAsset.cs.
const int VALID_GLYPH_WIDTHS[] = { 4, 8, 16, 32 };
const int VALID_GLYPH_WIDTH_COUNT = 4;

// VRAM budget. Font slot 1 gets 256 px tall (page 15 row 0); font
// slot 2 gets 208 px (page 15 row 1, shared with system font tail).
// Pick the smaller so either slot will accept the generated bitmap.
const int MAX_ATLAS_HEIGHT = 208;

int clamp_int(int v, int lo, int hi) {
	if (v < lo) return lo;
	if (v > hi) return hi;
	return v;
}
} // namespace

void PS1FontRasterizer::_bind_methods() {
	ClassDB::bind_method(
			D_METHOD("rasterize", "font", "font_size", "alpha_threshold"),
			&PS1FontRasterizer::rasterize, DEFVAL(0.3f));
}

Dictionary PS1FontRasterizer::rasterize(const Ref<FontFile> &p_font, int p_font_size, float p_alpha_threshold) {
	Dictionary result;

	if (p_font.is_null()) {
		ERR_PRINT("PS1FontRasterizer::rasterize: null font.");
		return result;
	}
	if (p_font_size < 6 || p_font_size > 32) {
		ERR_PRINT(String("PS1FontRasterizer::rasterize: font_size out of [6, 32] — got ") + String::num_int64(p_font_size));
		return result;
	}

	// Force rasterization of the printable ASCII range so subsequent
	// cache getters return populated data.
	Vector2i size_vec(p_font_size, 0);
	Ref<FontFile> font = p_font;
	font->render_range(0, size_vec, char32_t(0x20), char32_t(0x7E));

	// ── Pass 1: collect metrics + advance widths ────────────────
	PackedByteArray advances;
	advances.resize(96);
	for (int i = 0; i < 96; i++) advances[i] = 0;

	int max_glyph_w = 1;
	int max_ascent = 0;
	int max_descent = 0;

	for (int c = 0x20; c <= 0x7E; c++) {
		int glyph_idx = font->get_glyph_index(p_font_size, char32_t(c), char32_t(0));
		if (glyph_idx == 0) {
			// Missing glyph — default to half-em width so wrap logic
			// has something to measure against, render nothing.
			advances[c - 0x20] = uint8_t(clamp_int(p_font_size / 2, 1, 255));
			continue;
		}

		Vector2 adv = font->get_glyph_advance(0, p_font_size, glyph_idx);
		int adv_px = int(Math::ceil(adv.x));
		advances[c - 0x20] = uint8_t(clamp_int(adv_px, 1, 255));

		Vector2 gsz = font->get_glyph_size(0, size_vec, glyph_idx);
		Vector2 goff = font->get_glyph_offset(0, size_vec, glyph_idx);

		int glyph_w = int(Math::ceil(gsz.x));
		if (glyph_w > max_glyph_w) max_glyph_w = glyph_w;

		// Godot glyph offset Y is baseline→top-left (negative means
		// ascending above baseline). So ascender = -offset.y;
		// descender = size.y + offset.y.
		int ascent = int(Math::ceil(-goff.y));
		int descent = int(Math::ceil(gsz.y + goff.y));
		if (ascent > max_ascent) max_ascent = ascent;
		if (descent > max_descent) max_descent = descent;
	}

	// ── Pick cell size ──────────────────────────────────────────
	int cell_w = 32;
	for (int i = 0; i < VALID_GLYPH_WIDTH_COUNT; i++) {
		if (VALID_GLYPH_WIDTHS[i] >= max_glyph_w) {
			cell_w = VALID_GLYPH_WIDTHS[i];
			break;
		}
	}

	int ideal_cell_h = max_ascent + max_descent;
	if (ideal_cell_h < p_font_size) ideal_cell_h = p_font_size;

	int cols_per_row = 256 / cell_w;
	int row_count = (95 + cols_per_row - 1) / cols_per_row;

	// If the total atlas would overflow the VRAM slot, clamp cell
	// height. Glyphs get vertically compressed — imperfect but
	// matches SplashEdit's behavior and keeps the font on the PSX.
	int cell_h = ideal_cell_h;
	if (row_count * cell_h > MAX_ATLAS_HEIGHT) {
		cell_h = MAX_ATLAS_HEIGHT / row_count;
		WARN_PRINT(String("PS1FontRasterizer: font at size ") + String::num_int64(p_font_size) +
				" px: atlas would be " + String::num_int64(row_count * ideal_cell_h) +
				" px, clamped to " + String::num_int64(row_count * cell_h) +
				" px. Glyphs will be compressed. Reduce font_size to avoid.");
	}
	if (cell_h < 4) cell_h = 4;

	int atlas_h = row_count * cell_h;

	// ── Pass 2: blit glyphs into the target bitmap ──────────────
	Ref<Image> img = Image::create_empty(256, atlas_h, false, Image::FORMAT_RGBA8);
	img->fill(Color(0, 0, 0, 0));

	int baseline_y = clamp_int(cell_h - max_descent, 1, cell_h - 1);
	Color ink(1.0f, 1.0f, 1.0f, 1.0f);

	for (int c = 0x20; c <= 0x7E; c++) {
		int idx = c - 0x20;
		int col = idx % cols_per_row;
		int row = idx / cols_per_row;
		int cell_x = col * cell_w;
		int cell_y = row * cell_h;

		int glyph_idx = font->get_glyph_index(p_font_size, char32_t(c), char32_t(0));
		if (glyph_idx == 0) continue;

		int tex_idx = font->get_glyph_texture_idx(0, size_vec, glyph_idx);
		if (tex_idx < 0) continue;

		Ref<Image> atlas = font->get_texture_image(0, size_vec, tex_idx);
		if (atlas.is_null()) continue;

		Rect2 uv = font->get_glyph_uv_rect(0, size_vec, glyph_idx);
		Vector2 goff = font->get_glyph_offset(0, size_vec, glyph_idx);

		int src_x = int(uv.position.x);
		int src_y = int(uv.position.y);
		int g_w = int(uv.size.x);
		int g_h = int(uv.size.y);
		if (g_w <= 0 || g_h <= 0) continue;

		// Top of glyph inside the cell: baseline + offset.Y (which is
		// negative for ascending glyphs, so this subtracts).
		int cell_top = cell_y + baseline_y + int(Math::floor(goff.y));
		int cell_left_base = int(Math::floor(goff.x));
		if (cell_left_base < 0) cell_left_base = 0;
		int cell_left = cell_x + cell_left_base;

		int atlas_w = atlas->get_width();
		int atlas_hh = atlas->get_height();

		for (int py = 0; py < g_h; py++) {
			int sy = src_y + py;
			if (sy < 0 || sy >= atlas_hh) continue;
			int dy = cell_top + py;
			if (dy < 0 || dy >= atlas_h) continue;
			// Clip to the cell's row. Required after the VRAM-budget
			// clamp shortens cell_h below the glyph's natural height —
			// without this, a tall glyph would bleed into the next
			// row's cell and overwrite its neighbour.
			if (dy < cell_y || dy >= cell_y + cell_h) continue;

			for (int px = 0; px < g_w; px++) {
				int sx = src_x + px;
				if (sx < 0 || sx >= atlas_w) continue;
				int dx = cell_left + px;
				if (dx < cell_x || dx >= cell_x + cell_w) continue;

				Color src = atlas->get_pixel(sx, sy);
				if (src.a >= p_alpha_threshold) {
					img->set_pixel(dx, dy, ink);
				}
			}
		}
	}

	result["glyph_width"] = cell_w;
	result["glyph_height"] = cell_h;
	result["bitmap"] = img;
	result["advance_widths"] = advances;

	UtilityFunctions::print(String("[PS1Godot] PS1FontRasterizer: rasterized at ") +
			String::num_int64(p_font_size) + " px → " +
			String::num_int64(cell_w) + "×" + String::num_int64(cell_h) + " cells, " +
			String::num_int64(cols_per_row) + "×" + String::num_int64(row_count) + " grid, " +
			"atlas 256×" + String::num_int64(atlas_h) + " px.");

	return result;
}
