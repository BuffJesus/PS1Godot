#pragma once

#include <stdint.h>
#include <psyqo-lua/lua.hh>
#include "controls.hh"
#include "uisystem.hh"

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
    // `ui` is optional — if null OR no "dialogue_box" canvas exists,
    // the walker falls back to printf-only output (slice D1b behavior).
    void init(Controls* controls, UISystem* ui);

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

    // Terminate any active dialogue, release the registry ref, hide
    // the dialogue canvas if it was driving the display.
    void stop(lua_State* L);

private:
    Controls* m_controls = nullptr;
    UISystem* m_ui = nullptr;

    bool m_active = false;
    int  m_tableRef = -1;       // LUA_NOREF == -2 in stock Lua, -1 is fine as sentinel
    char m_currentNodeId[16] = "";
    bool m_freshNode = false;    // print on next tick

    // ── Canvas-driven UI (slice D1c) ────────────────────────────────
    // When a PS1UICanvas named "dialogue_box" exists, the walker
    // populates its text elements + cursor visibility each frame and
    // shows/hides the canvas with the dialogue. When absent, falls
    // back to printf only. Authors can ship any subset of the
    // expected elements — each one is optional and skipped silently
    // when not found.
    //
    // Expected element names on the "dialogue_box" canvas:
    //   - "speaker"               Text element, current line's speaker
    //   - "text"                  Text element, current line / "Choose:"
    //   - "option_1" .. "option_3"  Text elements, choice option texts
    //   - "cursor_1" .. "cursor_3"  any-type elements toggled to mark
    //                              which option is currently selected
    static constexpr int kMaxOptions = 3;
    int  m_canvasIdx = -1;      // -1 = no dialogue_box canvas authored
    int  m_speakerHandle = -1;
    int  m_textHandle = -1;
    int  m_optionHandles[kMaxOptions] = { -1, -1, -1 };
    int  m_cursorHandles[kMaxOptions] = { -1, -1, -1 };

    // Active-choice state. Set when entering a choice node.
    int  m_selectedChoice = 0;
    int  m_numActiveOptions = 0;
    bool m_onChoiceNode = false;

    // True when this dialogue is driving the canvas this run. False
    // when no canvas was authored — printf-only mode.
    bool m_useCanvas = false;

    // Emit the current node's content. When the canvas is in use, sets
    // text on canvas elements; otherwise prints to stdout. Sets the
    // m_onChoiceNode / m_numActiveOptions / m_selectedChoice state
    // when entering a choice.
    void emitCurrentNode(lua_State* L);

    // Follow the current node's exec edge after an advance trigger:
    //   line   → field "next" on current node
    //   choice → field "next" on options[m_selectedChoice + 1]
    // Sets m_currentNodeId + m_freshNode, or calls stop() when nil.
    void advanceFromCurrent(lua_State* L);

    // Update which "cursor_N" element is visible to match
    // m_selectedChoice. Cheap no-op when no cursor elements exist.
    void refreshCursor();

    // Helpers: push the current node table on the Lua stack (caller
    // must lua_pop(L, 1) when done). Returns false if the lookup
    // fails for any reason (no ref, missing nodes table, missing
    // node id key).
    bool pushCurrentNode(lua_State* L);
    bool readStringField(lua_State* L, int tableIndex, const char* key,
                          char* out, size_t outSize);

    // Clear all dialogue_box text + hide all cursor elements. Called
    // when entering a new node and on stop.
    void clearCanvasUI();
};

} // namespace psxsplash
