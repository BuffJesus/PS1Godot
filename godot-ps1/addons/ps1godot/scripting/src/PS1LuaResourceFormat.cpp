#include "PS1LuaResourceFormat.hpp"
#include "PS1LuaScript.hpp"

#include <godot_cpp/classes/file_access.hpp>

using namespace godot;

// ─── Loader ────────────────────────────────────────────────────────────────

PackedStringArray PS1LuaResourceFormatLoader::_get_recognized_extensions() const {
	PackedStringArray a;
	a.push_back("lua");
	return a;
}

bool PS1LuaResourceFormatLoader::_handles_type(const StringName &p_type) const {
	return p_type == StringName("Script") || p_type == StringName("PS1LuaScript");
}

String PS1LuaResourceFormatLoader::_get_resource_type(const String &p_path) const {
	if (p_path.get_extension().to_lower() == "lua") {
		return "PS1LuaScript";
	}
	return "";
}

Variant PS1LuaResourceFormatLoader::_load(const String &p_path, const String &p_original_path, bool p_use_sub_threads, int32_t p_cache_mode) const {
	Ref<FileAccess> f = FileAccess::open(p_path, FileAccess::READ);
	if (f.is_null()) {
		return Variant();
	}
	String src = f->get_as_text();

	Ref<PS1LuaScript> s;
	s.instantiate();
	s->_set_source_code(src);
	s->set_path(p_original_path);
	return s;
}

// ─── Saver ─────────────────────────────────────────────────────────────────

bool PS1LuaResourceFormatSaver::_recognize(const Ref<Resource> &p_resource) const {
	return Object::cast_to<PS1LuaScript>(p_resource.ptr()) != nullptr;
}

PackedStringArray PS1LuaResourceFormatSaver::_get_recognized_extensions(const Ref<Resource> &p_resource) const {
	PackedStringArray a;
	if (Object::cast_to<PS1LuaScript>(p_resource.ptr())) {
		a.push_back("lua");
	}
	return a;
}

Error PS1LuaResourceFormatSaver::_save(const Ref<Resource> &p_resource, const String &p_path, uint32_t p_flags) {
	PS1LuaScript *script = Object::cast_to<PS1LuaScript>(p_resource.ptr());
	if (!script) {
		return ERR_INVALID_PARAMETER;
	}

	Ref<FileAccess> f = FileAccess::open(p_path, FileAccess::WRITE);
	if (f.is_null()) {
		return FileAccess::get_open_error();
	}
	f->store_string(script->_get_source_code());
	return OK;
}
