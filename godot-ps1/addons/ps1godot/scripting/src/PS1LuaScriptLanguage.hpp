#pragma once

#include <godot_cpp/classes/script.hpp>
#include <godot_cpp/classes/script_language_extension.hpp>
#include <godot_cpp/variant/dictionary.hpp>
#include <godot_cpp/variant/packed_string_array.hpp>
#include <godot_cpp/variant/string.hpp>
#include <godot_cpp/variant/string_name.hpp>
#include <godot_cpp/variant/typed_array.hpp>

namespace godot {

class PS1LuaScriptLanguage : public ScriptLanguageExtension {
	GDCLASS(PS1LuaScriptLanguage, ScriptLanguageExtension);

protected:
	static void _bind_methods() {}

public:
	String _get_name() const override;
	String _get_type() const override;
	String _get_extension() const override;
	PackedStringArray _get_recognized_extensions() const override;
	PackedStringArray _get_reserved_words() const override;
	PackedStringArray _get_comment_delimiters() const override;
	PackedStringArray _get_doc_comment_delimiters() const override;
	PackedStringArray _get_string_delimiters() const override;

	bool _supports_builtin_mode() const override;
	bool _supports_documentation() const override;
	bool _can_inherit_from_file() const override;
	bool _is_using_templates() override;

	Ref<Script> _make_template(const String &p_template, const String &p_class_name, const String &p_base_class_name) const override;
	Dictionary _validate(const String &p_script, const String &p_path, bool p_validate_functions, bool p_validate_errors, bool p_validate_warnings, bool p_validate_safe_lines) const override;
	String _validate_path(const String &p_path) const override { return ""; }
	bool _overrides_external_editor() override { return false; }
	bool _is_control_flow_keyword(const String &p_keyword) const override { return false; }

	TypedArray<Dictionary> _get_public_functions() const override;
	Dictionary _get_public_constants() const override;
	TypedArray<Dictionary> _get_public_annotations() const override;
	TypedArray<Dictionary> _get_built_in_templates(const StringName &p_object) const override;

	bool _handles_global_class_type(const String &p_type) const override;
	Dictionary _get_global_class_name(const String &p_path) const override;

	void _init() override {}
	void _finish() override {}
	void _frame() override {}
	void _reload_all_scripts() override {}
	void _reload_scripts(const Array &p_scripts, bool p_soft_reload) override {}
	void _reload_tool_script(const Ref<Script> &p_script, bool p_soft_reload) override {}
	void _thread_enter() override {}
	void _thread_exit() override {}

	// Editor-integration stubs. Lua execution happens on the PSX; the Godot
	// editor only needs highlighting + basic navigation, so these can be
	// minimal. Omitting them triggers "Required virtual method … must be
	// overridden" errors on 4.7 every time the user Ctrl+Clicks a symbol
	// or the autocomplete kicks in.
	Object *_create_script() const override { return nullptr; }
	bool _has_named_classes() const override { return false; }
	int32_t _find_function(const String &p_function, const String &p_code) const override { return -1; }
	String _make_function(const String &p_class_name, const String &p_function_name, const PackedStringArray &p_function_args) const override { return String(); }
	bool _can_make_function() const override { return false; }
	Error _open_in_external_editor(const Ref<Script> &p_script, int32_t p_line, int32_t p_column) override { return ERR_UNAVAILABLE; }
	ScriptLanguage::ScriptNameCasing _preferred_file_name_casing() const override { return ScriptLanguage::SCRIPT_NAME_CASING_SNAKE_CASE; }

	// Drives the Godot script editor's autocomplete dropdown. Scans the
	// tail of `p_code` for context (bare identifier prefix vs `ns.prefix`
	// member access), walks the API table baked from luaapi.hh, and
	// returns matching options. Implementation in PS1LuaScriptLanguage.cpp.
	Dictionary _complete_code(const String &p_code, const String &p_path, Object *p_owner) const override;

	// Hover-tooltip + Ctrl-Click lookup. Looks `p_symbol` up against the
	// API table baked from luaapi.hh; when found, returns a descriptor
	// the editor can use to render the doc tooltip. Implementation in
	// PS1LuaScriptLanguage.cpp.
	Dictionary _lookup_code(const String &p_code, const String &p_symbol, const String &p_path, Object *p_owner) const override;

	String _auto_indent_code(const String &p_code, int32_t p_from_line, int32_t p_to_line) const override {
		// Pass source through unchanged — CodeEdit has a built-in indenter
		// that handles the basics fine without language-specific help.
		return p_code;
	}

	void _add_global_constant(const StringName &p_name, const Variant &p_value) override {}
	void _add_named_global_constant(const StringName &p_name, const Variant &p_value) override {}
	void _remove_named_global_constant(const StringName &p_name) override {}

	void *_debug_get_stack_level_instance(int32_t p_level) override { return nullptr; }

	void _profiling_start() override {}
	void _profiling_stop() override {}
	void _profiling_set_save_native_calls(bool p_enable) override {}
	int32_t _profiling_get_accumulated_data(ScriptLanguageExtensionProfilingInfo *p_info_array, int32_t p_info_max) override { return 0; }
	int32_t _profiling_get_frame_data(ScriptLanguageExtensionProfilingInfo *p_info_array, int32_t p_info_max) override { return 0; }

	String _debug_get_error() const override { return ""; }
	int32_t _debug_get_stack_level_count() const override { return 0; }
	int32_t _debug_get_stack_level_line(int32_t p_level) const override { return 0; }
	String _debug_get_stack_level_function(int32_t p_level) const override { return ""; }
	String _debug_get_stack_level_source(int32_t p_level) const override { return ""; }
	Dictionary _debug_get_stack_level_locals(int32_t p_level, int32_t p_max_subitems, int32_t p_max_depth) override { return Dictionary(); }
	Dictionary _debug_get_stack_level_members(int32_t p_level, int32_t p_max_subitems, int32_t p_max_depth) override { return Dictionary(); }
	Dictionary _debug_get_globals(int32_t p_max_subitems, int32_t p_max_depth) override { return Dictionary(); }
	String _debug_parse_stack_level_expression(int32_t p_level, const String &p_expression, int32_t p_max_subitems, int32_t p_max_depth) override { return ""; }
	TypedArray<Dictionary> _debug_get_current_stack_info() override { return TypedArray<Dictionary>(); }
};

} // namespace godot
