#include "PS1LuaSyntaxHighlighter.hpp"

#include <godot_cpp/classes/text_edit.hpp>
#include <godot_cpp/core/class_db.hpp>
#include <godot_cpp/variant/variant.hpp>

using namespace godot;

// ─── Palette ────────────────────────────────────────────────────────────
static Color col_default()  { return Color(0.85f, 0.85f, 0.88f); }
static Color col_keyword()  { return Color(0.95f, 0.45f, 0.15f); }  // orange — local, function, …
static Color col_control()  { return Color(1.00f, 0.70f, 0.25f); }  // brighter — if/for/while/…
static Color col_global()   { return Color(0.45f, 0.85f, 0.95f); }  // cyan — Entity, Camera, Vec3, …
static Color col_string()   { return Color(1.00f, 0.88f, 0.40f); }
static Color col_comment()  { return Color(0.50f, 0.50f, 0.55f); }
static Color col_number()   { return Color(0.85f, 0.75f, 1.00f); }
static Color col_symbol()   { return Color(0.75f, 0.75f, 0.80f); }

// ─── Keyword sets ───────────────────────────────────────────────────────
static bool word_eq(const String &line, int start, int len, const char *kw) {
	for (int i = 0; i < len; i++) {
		if (!kw[i]) return false;  // kw shorter than len
		if ((char)line[start + i] != kw[i]) return false;  // mismatch
	}
	return kw[len] == 0;  // kw exactly `len` chars
}

static bool is_keyword(const String &line, int start, int len) {
	static const char *kws[] = {
		"and", "false", "function", "local", "nil", "not", "or",
		"true", "self",
	};
	for (const char *kw : kws) {
		if (word_eq(line, start, len, kw)) return true;
	}
	return false;
}

static bool is_control_kw(const String &line, int start, int len) {
	static const char *kws[] = {
		"break", "do", "else", "elseif", "end", "for", "goto", "if",
		"in", "repeat", "return", "then", "until", "while",
	};
	for (const char *kw : kws) {
		if (word_eq(line, start, len, kw)) return true;
	}
	return false;
}

static bool is_global(const String &line, int start, int len) {
	// PS1Godot runtime globals injected via luaapi.cpp. Keeps colorization
	// honest — if a symbol isn't in this list, it's local / author code.
	static const char *globals[] = {
		"Entity", "Vec3", "FixedPoint", "Input", "Timer", "Camera",
		"Audio", "Scene", "Music", "Cutscene", "Animation", "UI",
		"Physics", "PSXMath", "Random", "Debug", "Player",
		"Task", "Console", "Controls", "SkinnedAnim",
	};
	for (const char *g : globals) {
		if (word_eq(line, start, len, g)) return true;
	}
	return false;
}

static bool is_ident(char32_t c) {
	return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') ||
	       (c >= '0' && c <= '9') || c == '_';
}
static bool is_ident_start(char32_t c) {
	return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || c == '_';
}
static bool is_digit(char32_t c) { return c >= '0' && c <= '9'; }
static bool is_hex(char32_t c) {
	return (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
}

static void set_color(Dictionary &out, int col, Color color) {
	Dictionary entry;
	entry["color"] = color;
	out[col] = entry;
}

// ─── Overrides ──────────────────────────────────────────────────────────

String PS1LuaSyntaxHighlighter::_get_name() const {
	return "PS1 Lua";
}

PackedStringArray PS1LuaSyntaxHighlighter::_get_supported_languages() const {
	PackedStringArray a;
	a.push_back("PS1Lua");
	return a;
}

Ref<EditorSyntaxHighlighter> PS1LuaSyntaxHighlighter::_create() const {
	// Godot's ScriptEditor clones the highlighter per-open-file via _create,
	// so each tab carries its own instance (needed so per-tab state like
	// `text_edit` doesn't cross-contaminate). Return a fresh one.
	Ref<PS1LuaSyntaxHighlighter> dup;
	dup.instantiate();
	return dup;
}

Dictionary PS1LuaSyntaxHighlighter::_get_line_syntax_highlighting(int32_t p_line) const {
	Dictionary result;
	TextEdit *te = get_text_edit();
	if (te == nullptr) return result;
	// Iterate the String directly — Godot CodeEdit's column indices are
	// character-based, not byte-based. Walking a utf8() CharString would
	// drift whenever the source contains multi-byte glyphs (em-dashes,
	// non-ASCII identifiers, etc.) and Godot silently discards the
	// whole line's color map.
	const String &line = te->get_line(p_line);
	int n = line.length();
	if (n <= 0) return result;

	set_color(result, 0, col_default());

	int i = 0;
	while (i < n) {
		char32_t c = line[i];

		// Line comment `--...`
		if (c == '-' && i + 1 < n && (char32_t)line[i + 1] == '-') {
			set_color(result, i, col_comment());
			return result;
		}

		// String literal `"..."` or `'...'`. Escapes consume the next char.
		if (c == '"' || c == '\'') {
			set_color(result, i, col_string());
			int end = i + 1;
			while (end < n && (char32_t)line[end] != c) {
				if ((char32_t)line[end] == '\\' && end + 1 < n) end += 2;
				else end++;
			}
			if (end < n) end++;
			i = end;
			set_color(result, i, col_default());
			continue;
		}

		// Numeric literal
		if (is_digit(c) || (c == '.' && i + 1 < n && is_digit(line[i + 1]))) {
			set_color(result, i, col_number());
			int j = i;
			if (c == '0' && i + 1 < n && ((char32_t)line[i + 1] == 'x' || (char32_t)line[i + 1] == 'X')) {
				j += 2;
				while (j < n && is_hex(line[j])) j++;
			} else {
				while (j < n && is_digit(line[j])) j++;
				if (j < n && (char32_t)line[j] == '.') { j++; while (j < n && is_digit(line[j])) j++; }
				if (j < n && ((char32_t)line[j] == 'e' || (char32_t)line[j] == 'E')) {
					j++;
					if (j < n && ((char32_t)line[j] == '+' || (char32_t)line[j] == '-')) j++;
					while (j < n && is_digit(line[j])) j++;
				}
			}
			i = j;
			set_color(result, i, col_default());
			continue;
		}

		// Identifier — keyword / control-flow / global / plain
		if (is_ident_start(c)) {
			int j = i + 1;
			while (j < n && is_ident(line[j])) j++;
			int len = j - i;
			if      (is_control_kw(line, i, len)) set_color(result, i, col_control());
			else if (is_keyword(line, i, len))    set_color(result, i, col_keyword());
			else if (is_global(line, i, len))     set_color(result, i, col_global());
			else                                  set_color(result, i, col_default());
			i = j;
			if (i < n) set_color(result, i, col_default());
			continue;
		}

		// Punctuation / operators — symbol color. Whitespace passes through.
		if (c != ' ' && c != '\t' && c != '\n' && c != '\r') {
			set_color(result, i, col_symbol());
			i++;
			if (i < n) set_color(result, i, col_default());
			continue;
		}

		i++;
	}
	return result;
}
