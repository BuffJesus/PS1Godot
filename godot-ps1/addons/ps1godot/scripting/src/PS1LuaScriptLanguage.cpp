#include "PS1LuaScriptLanguage.hpp"
#include "PS1LuaScript.hpp"

#include <godot_cpp/variant/array.hpp>

using namespace godot;

String PS1LuaScriptLanguage::_get_name() const {
	return "PS1Lua";
}

String PS1LuaScriptLanguage::_get_type() const {
	return "PS1LuaScript";
}

String PS1LuaScriptLanguage::_get_extension() const {
	return "lua";
}

PackedStringArray PS1LuaScriptLanguage::_get_recognized_extensions() const {
	PackedStringArray a;
	a.push_back("lua");
	return a;
}

PackedStringArray PS1LuaScriptLanguage::_get_reserved_words() const {
	static const char *kw[] = {
		"and", "break", "do", "else", "elseif", "end", "false", "for",
		"function", "goto", "if", "in", "local", "nil", "not", "or",
		"repeat", "return", "then", "true", "until", "while", "self"
	};
	PackedStringArray a;
	for (const char *k : kw) {
		a.push_back(k);
	}
	return a;
}

PackedStringArray PS1LuaScriptLanguage::_get_comment_delimiters() const {
	PackedStringArray a;
	a.push_back("--");
	return a;
}

PackedStringArray PS1LuaScriptLanguage::_get_doc_comment_delimiters() const {
	PackedStringArray a;
	a.push_back("---");
	return a;
}

PackedStringArray PS1LuaScriptLanguage::_get_string_delimiters() const {
	PackedStringArray a;
	a.push_back("\" \"");
	a.push_back("' '");
	return a;
}

bool PS1LuaScriptLanguage::_supports_builtin_mode() const { return false; }
bool PS1LuaScriptLanguage::_supports_documentation() const { return false; }
bool PS1LuaScriptLanguage::_can_inherit_from_file() const { return false; }
bool PS1LuaScriptLanguage::_is_using_templates() { return true; }

Ref<Script> PS1LuaScriptLanguage::_make_template(const String &p_template, const String &p_class_name, const String &p_base_class_name) const {
	Ref<PS1LuaScript> s;
	s.instantiate();
	String src = String("-- ") + p_class_name + ".lua\n"
			"-- Attached to a PS1Godot node. See psxsplash-main/src/luaapi.hh\n"
			"-- for the full API reference.\n"
			"\n"
			"function onEnable(self)\n"
			"end\n"
			"\n"
			"function onUpdate(self, dt)\n"
			"end\n";
	s->_set_source_code(src);
	return s;
}

Dictionary PS1LuaScriptLanguage::_validate(const String &p_script, const String &p_path, bool p_validate_functions, bool p_validate_errors, bool p_validate_warnings, bool p_validate_safe_lines) const {
	Dictionary d;
	d["valid"] = true;
	d["errors"] = Array();
	d["warnings"] = Array();
	d["functions"] = Array();
	d["safe_lines"] = Array();
	d["class_name"] = String();
	d["class_icon_path"] = String();
	d["class_base_type"] = String();
	d["class_member_lines"] = Array();
	return d;
}

TypedArray<Dictionary> PS1LuaScriptLanguage::_get_public_functions() const { return TypedArray<Dictionary>(); }
Dictionary PS1LuaScriptLanguage::_get_public_constants() const { return Dictionary(); }
TypedArray<Dictionary> PS1LuaScriptLanguage::_get_public_annotations() const { return TypedArray<Dictionary>(); }

TypedArray<Dictionary> PS1LuaScriptLanguage::_get_built_in_templates(const StringName &p_object) const {
	Dictionary t;
	t["inherit"] = "Node";
	t["name"] = "Empty";
	t["description"] = "Empty PS1 Lua behavior script.";
	t["content"] = String(
			"-- _CLASS_NAME_.lua\n"
			"function onEnable(self)\n"
			"end\n"
			"\n"
			"function onUpdate(self, dt)\n"
			"end\n");
	t["id"] = 0;
	t["origin"] = 0;

	TypedArray<Dictionary> arr;
	arr.push_back(t);
	return arr;
}

bool PS1LuaScriptLanguage::_handles_global_class_type(const String &p_type) const { return false; }
Dictionary PS1LuaScriptLanguage::_get_global_class_name(const String &p_path) const { return Dictionary(); }
