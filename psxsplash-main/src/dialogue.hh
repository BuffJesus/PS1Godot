#pragma once

#include <stdint.h>
#include <psyqo-lua/lua.hh>
#include "controls.hh"

namespace psxsplash {

// PS1Graph dialogue walker — interprets a Lua table compiled by the
// Godot-side PS1Graph "dialogue" kind. Table shape:
//
//     { entry = "n0",
//       nodes = {
//           n0 = { kind = "line",   speaker = "Bob",  text = "Hi",  next = "n1" },
//           n1 = { kind = "choice", options = { { text = "Hello", next = "n2" },
//                                                { text = "Bye",   next = nil  } } },
//           n2 = { kind = "line",   speaker = "Bob",  text = "Bye.", next = nil },
//       },
//     }
//
// Slice D1b ships the *smoke-test* walker: each visited node prints
// to stdout (PCSX-Redux debug console / PSX BIOS putchar) on entry,
// X button advances; choices auto-pick option 0 since on-screen
// option navigation needs the UI work in slice D1c.
//
// Slice D1c (next) will drive a PS1UICanvas named "dialogue_box"
// with elements "speaker", "text", "option_1..3" + D-pad navigation
// for real in-game dialogue. The state-machine here stays unchanged —
// only the emit + input paths swap to use the canvas.
class DialogueRunner {
public:
    // One-time setup. Stashes references the tick uses each frame.
    void init(Controls* controls);

    // Called once per frame from SceneManager::GameTick AFTER
    // m_controls.UpdateButtonStates() so wasButtonPressed reflects
    // this frame's edge. No-op when no dialogue is active.
    void tick(lua_State* L);

    bool isActive() const { return m_active; }

    // Begin walking the dialogue table currently at top of `L`'s stack
    // (caller is the Lua API entry point). The table reference is
    // stashed in LUA_REGISTRYINDEX so the table stays alive across
    // frames; cleared on stop() via luaL_unref.
    void startFromStackTop(lua_State* L);

    // Terminate any active dialogue, release the registry ref.
    void stop(lua_State* L);

private:
    Controls* m_controls = nullptr;

    bool m_active = false;
    int  m_tableRef = -1;       // LUA_NOREF == -2 in stock Lua, -1 is fine as sentinel
    char m_currentNodeId[16] = "";
    bool m_freshNode = false;    // print on next tick

    // Emit the current node's content via printf. For "line" prints
    // "[Speaker]: text"; for "choice" prints "[Choice]" + every option.
    void emitCurrentNode(lua_State* L);

    // Follow the current node's exec edge after an advance trigger:
    //   line   → field "next" on current node
    //   choice → field "next" on options[0]   (slice D1b auto-pick)
    // Sets m_currentNodeId + m_freshNode, or calls stop() when nil.
    void advanceFromCurrent(lua_State* L);

    // Helpers: push the current node table on the Lua stack (caller
    // must lua_pop(L, 1) when done). Returns false if the lookup
    // fails for any reason (no ref, missing nodes table, missing
    // node id key).
    bool pushCurrentNode(lua_State* L);
    bool readStringField(lua_State* L, int tableIndex, const char* key,
                          char* out, size_t outSize);
};

} // namespace psxsplash
