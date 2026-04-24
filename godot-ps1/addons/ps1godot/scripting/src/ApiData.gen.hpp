#pragma once

// Shared header for the auto-generated ApiData.gen.cpp. The table is
// populated from luaapi.hh at scons build time (gen_api_data.py).
// Consumed by PS1LuaScriptLanguage::_complete_code to drive the
// Godot script editor's autocomplete dropdown.

namespace ps1lua {

struct ApiEntry {
	const char *ns;    // "Entity"
	const char *name;  // "Spawn"
	const char *args;  // raw, as written in the // comment — e.g. "tag, {x,y,z} [, rotY]"
	const char *ret;   // raw return type phrase — "object or nil"; empty string if none
	const char *doc;   // accumulated preceding // comment lines, `\n`-joined
};

extern const ApiEntry API_ENTRIES[];
extern const int API_ENTRY_COUNT;

} // namespace ps1lua
