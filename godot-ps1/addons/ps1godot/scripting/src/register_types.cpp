#include "PS1FontRasterizer.hpp"
#include "PS1LuaResourceFormat.hpp"
#include "PS1LuaScript.hpp"
#include "PS1LuaScriptLanguage.hpp"
#include "PS1LuaSyntaxHighlighter.hpp"

#include <gdextension_interface.h>
#include <godot_cpp/classes/editor_interface.hpp>
#include <godot_cpp/classes/engine.hpp>
#include <godot_cpp/classes/resource_loader.hpp>
#include <godot_cpp/classes/resource_saver.hpp>
#include <godot_cpp/classes/script_editor.hpp>
#include <godot_cpp/core/class_db.hpp>
#include <godot_cpp/core/defs.hpp>
#include <godot_cpp/core/memory.hpp>
#include <godot_cpp/godot.hpp>

using namespace godot;

static PS1LuaScriptLanguage *ps1lua_language_singleton = nullptr;
static Ref<PS1LuaResourceFormatLoader> ps1lua_loader;
static Ref<PS1LuaResourceFormatSaver> ps1lua_saver;

static void initialize_ps1lua_module(ModuleInitializationLevel p_level) {
	// SCENE — register script language + the class types we instantiate.
	// EDITOR — register the script-editor syntax highlighter. Splitting
	// these so the EditorInterface calls happen after Godot has built
	// its editor singletons (they're nullptr at SCENE level).
	if (p_level == MODULE_INITIALIZATION_LEVEL_SCENE) {
		GDREGISTER_CLASS(PS1LuaScript);
		GDREGISTER_CLASS(PS1LuaScriptLanguage);
		GDREGISTER_CLASS(PS1LuaResourceFormatLoader);
		GDREGISTER_CLASS(PS1LuaResourceFormatSaver);
		GDREGISTER_CLASS(PS1FontRasterizer);

		ps1lua_language_singleton = memnew(PS1LuaScriptLanguage);
		Engine::get_singleton()->register_script_language(ps1lua_language_singleton);

		ps1lua_loader.instantiate();
		ResourceLoader::get_singleton()->add_resource_format_loader(ps1lua_loader);

		ps1lua_saver.instantiate();
		ResourceSaver::get_singleton()->add_resource_format_saver(ps1lua_saver);
	} else if (p_level == MODULE_INITIALIZATION_LEVEL_EDITOR) {
		// EditorSyntaxHighlighter parent class is only alive once Godot
		// has brought up its editor singletons, so register the class
		// here. Instance + ScriptEditor::register_syntax_highlighter
		// has to wait for the editor to finish constructing the
		// ScriptEditor — we do that from the C# plugin's _EnterTree
		// (see PS1GodotPlugin.cs), which fires after EDITOR-level
		// GDExtension init but with ScriptEditor populated.
		GDREGISTER_CLASS(PS1LuaSyntaxHighlighter);
	}
}

static void deinitialize_ps1lua_module(ModuleInitializationLevel p_level) {
	// Highlighter teardown happens in PS1GodotPlugin._ExitTree — it's
	// the mirror of where we register it.
	if (p_level == MODULE_INITIALIZATION_LEVEL_SCENE) {
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
