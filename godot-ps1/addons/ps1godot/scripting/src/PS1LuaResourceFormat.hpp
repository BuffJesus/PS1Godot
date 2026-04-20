#pragma once

#include <godot_cpp/classes/file_access.hpp>
#include <godot_cpp/classes/resource.hpp>
#include <godot_cpp/classes/resource_format_loader.hpp>
#include <godot_cpp/classes/resource_format_saver.hpp>
#include <godot_cpp/variant/packed_string_array.hpp>
#include <godot_cpp/variant/string.hpp>
#include <godot_cpp/variant/string_name.hpp>
#include <godot_cpp/variant/variant.hpp>

namespace godot {

class PS1LuaResourceFormatLoader : public ResourceFormatLoader {
	GDCLASS(PS1LuaResourceFormatLoader, ResourceFormatLoader);

protected:
	static void _bind_methods() {}

public:
	PackedStringArray _get_recognized_extensions() const override;
	bool _handles_type(const StringName &p_type) const override;
	String _get_resource_type(const String &p_path) const override;
	Variant _load(const String &p_path, const String &p_original_path, bool p_use_sub_threads, int32_t p_cache_mode) const override;
};

class PS1LuaResourceFormatSaver : public ResourceFormatSaver {
	GDCLASS(PS1LuaResourceFormatSaver, ResourceFormatSaver);

protected:
	static void _bind_methods() {}

public:
	bool _recognize(const Ref<Resource> &p_resource) const override;
	PackedStringArray _get_recognized_extensions(const Ref<Resource> &p_resource) const override;
	Error _save(const Ref<Resource> &p_resource, const String &p_path, uint32_t p_flags) override;
};

} // namespace godot
