#include "PS1LuaResourceFormat.hpp"
#include "PS1LuaScript.hpp"
#include "PS1LuaScriptLanguage.hpp"

#include <gdextension_interface.h>
#include <godot_cpp/classes/engine.hpp>
#include <godot_cpp/classes/resource_loader.hpp>
#include <godot_cpp/classes/resource_saver.hpp>
#include <godot_cpp/core/class_db.hpp>
#include <godot_cpp/core/defs.hpp>
#include <godot_cpp/core/memory.hpp>
#include <godot_cpp/godot.hpp>

using namespace godot;

static PS1LuaScriptLanguage *ps1lua_language_singleton = nullptr;
static Ref<PS1LuaResourceFormatLoader> ps1lua_loader;
static Ref<PS1LuaResourceFormatSaver> ps1lua_saver;

static void initialize_ps1lua_module(ModuleInitializationLevel p_level) {
	if (p_level != MODULE_INITIALIZATION_LEVEL_SCENE) {
		return;
	}

	GDREGISTER_CLASS(PS1LuaScript);
	GDREGISTER_CLASS(PS1LuaScriptLanguage);
	GDREGISTER_CLASS(PS1LuaResourceFormatLoader);
	GDREGISTER_CLASS(PS1LuaResourceFormatSaver);

	ps1lua_language_singleton = memnew(PS1LuaScriptLanguage);
	Engine::get_singleton()->register_script_language(ps1lua_language_singleton);

	ps1lua_loader.instantiate();
	ResourceLoader::get_singleton()->add_resource_format_loader(ps1lua_loader);

	ps1lua_saver.instantiate();
	ResourceSaver::get_singleton()->add_resource_format_saver(ps1lua_saver);
}

static void deinitialize_ps1lua_module(ModuleInitializationLevel p_level) {
	if (p_level != MODULE_INITIALIZATION_LEVEL_SCENE) {
		return;
	}

	if (ps1lua_saver.is_valid()) {
		ResourceSaver::get_singleton()->remove_resource_format_saver(ps1lua_saver);
		ps1lua_saver.unref();
	}
	if (ps1lua_loader.is_valid()) {
		ResourceLoader::get_singleton()->remove_resource_format_loader(ps1lua_loader);
		ps1lua_loader.unref();
	}

	if (ps1lua_language_singleton) {
		Engine::get_singleton()->unregister_script_language(ps1lua_language_singleton);
		memdelete(ps1lua_language_singleton);
		ps1lua_language_singleton = nullptr;
	}
}

extern "C" {
GDExtensionBool GDE_EXPORT ps1lua_entrypoint(
		GDExtensionInterfaceGetProcAddress p_get_proc_address,
		const GDExtensionClassLibraryPtr p_library,
		GDExtensionInitialization *r_initialization) {
	GDExtensionBinding::InitObject init_obj(p_get_proc_address, p_library, r_initialization);
	init_obj.register_initializer(initialize_ps1lua_module);
	init_obj.register_terminator(deinitialize_ps1lua_module);
	init_obj.set_minimum_library_initialization_level(MODULE_INITIALIZATION_LEVEL_SCENE);
	return init_obj.init();
}
}
