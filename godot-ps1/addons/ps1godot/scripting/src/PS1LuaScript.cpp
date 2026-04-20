#include "PS1LuaScript.hpp"
#include "PS1LuaScriptLanguage.hpp"

#include <godot_cpp/classes/engine.hpp>

using namespace godot;

ScriptLanguage *PS1LuaScript::_get_language() const {
	Engine *engine = Engine::get_singleton();
	int count = engine->get_script_language_count();
	for (int i = 0; i < count; i++) {
		ScriptLanguage *lang = engine->get_script_language(i);
		if (Object::cast_to<PS1LuaScriptLanguage>(lang)) {
			return lang;
		}
	}
	return nullptr;
}
