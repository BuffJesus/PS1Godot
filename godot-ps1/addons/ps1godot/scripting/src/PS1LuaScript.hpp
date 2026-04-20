#pragma once

#include <godot_cpp/classes/script.hpp>
#include <godot_cpp/classes/script_extension.hpp>
#include <godot_cpp/classes/script_language.hpp>
#include <godot_cpp/variant/dictionary.hpp>
#include <godot_cpp/variant/string.hpp>
#include <godot_cpp/variant/string_name.hpp>
#include <godot_cpp/variant/typed_array.hpp>
#include <godot_cpp/variant/variant.hpp>

namespace godot {

// Stub script resource — real Lua execution happens on the PSX via psxsplash's
// embedded VM. This exists so Godot's drag-a-.lua-onto-a-node UX works without
// resource-type warnings, and so `_make_template` returns something typed.
class PS1LuaScript : public ScriptExtension {
	GDCLASS(PS1LuaScript, ScriptExtension);

	String source_code;

protected:
	static void _bind_methods() {}

public:
	String _get_source_code() const override { return source_code; }
	void _set_source_code(const String &p_code) override { source_code = p_code; }
	bool _has_source_code() const override { return !source_code.is_empty(); }

	Error _reload(bool p_keep_state) override { return OK; }
	bool _is_valid() const override { return true; }
	bool _is_tool() const override { return false; }
	bool _can_instantiate() const override { return false; }

	bool _has_method(const StringName &p_method) const override { return false; }
	bool _has_static_method(const StringName &p_method) const override { return false; }
	Dictionary _get_method_info(const StringName &p_method) const override { return Dictionary(); }

	Ref<Script> _get_base_script() const override { return Ref<Script>(); }
	StringName _get_global_name() const override { return StringName(); }
	StringName _get_instance_base_type() const override { return StringName(); }

	TypedArray<Dictionary> _get_script_method_list() const override { return TypedArray<Dictionary>(); }
	TypedArray<Dictionary> _get_script_property_list() const override { return TypedArray<Dictionary>(); }
	TypedArray<Dictionary> _get_script_signal_list() const override { return TypedArray<Dictionary>(); }
	Dictionary _get_constants() const override { return Dictionary(); }
	TypedArray<StringName> _get_members() const override { return TypedArray<StringName>(); }
	Variant _get_rpc_config() const override { return Variant(); }

	Variant _get_property_default_value(const StringName &p_property) const override { return Variant(); }
	bool _has_property_default_value(const StringName &p_property) const override { return false; }
	void _update_exports() override {}

	ScriptLanguage *_get_language() const override;
};

} // namespace godot
