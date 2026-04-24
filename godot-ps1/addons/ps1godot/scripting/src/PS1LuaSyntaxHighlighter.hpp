#pragma once

#include <godot_cpp/classes/editor_syntax_highlighter.hpp>
#include <godot_cpp/variant/color.hpp>
#include <godot_cpp/variant/dictionary.hpp>
#include <godot_cpp/variant/packed_string_array.hpp>
#include <godot_cpp/variant/string.hpp>

namespace godot {

// Editor-only syntax highlighter for .lua files edited in Godot's script
// editor. ScriptLanguageExtension's `_get_reserved_words` is queried by
// the Lua *parser*, not by the editor — so keyword / identifier /
// number / punctuation coloring has to ship through an
// EditorSyntaxHighlighter instead. register_types.cpp registers one of
// these against the script editor at editor-hint time.
class PS1LuaSyntaxHighlighter : public EditorSyntaxHighlighter {
	GDCLASS(PS1LuaSyntaxHighlighter, EditorSyntaxHighlighter);

protected:
	static void _bind_methods() {}

public:
	String _get_name() const override;
	PackedStringArray _get_supported_languages() const override;
	Dictionary _get_line_syntax_highlighting(int32_t p_line) const override;
	Ref<EditorSyntaxHighlighter> _create() const override;
	void _clear_highlighting_cache() override {}
	void _update_cache() override {}
};

} // namespace godot
