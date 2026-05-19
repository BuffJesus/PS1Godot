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

    // Skippable-line gating (slice D1h). When a line emits with
    // `skippable=false`, the X-button advance is blocked until this
    // counter ticks down. Audio-aware: when the line also carries an
    // audio clip, the lock is set to the clip's duration in frames;
    // otherwise a hardcoded "read time" budget (2 seconds) gives the
    // player time to read before manual advance.
    int  m_lineAdvanceLockFrames = 0;

    // Notifies (slice D1i — Anim-Notify style). Each line can carry
    // up to kMaxNotifies entries of "at this frame, fire this Lua
    // snippet." Lets authors punctuate a line with timed SFX, camera
    // moves, set-flag pulses without splitting the line across many
    // graph nodes. m_lineFrameCounter ticks once per frame from line
    // entry; the runner pcalls each pending notify when the counter
    // crosses its threshold and marks it fired so it doesn't re-fire.
    static constexpr int kMaxNotifies = 8;
    int  m_lineFrameCounter = 0;
    int  m_numNotifies = 0;
    int  m_notifyAt[kMaxNotifies] = {};
    bool m_notifyFired[kMaxNotifies] = {};
    char m_notifyLua[kMaxNotifies][96] = {};

    // Text-reveal state (typewriter). When a line carries
    // `reveal_mode = "typewriter"`, the canvas text starts empty and a
    // per-frame cursor uncovers one character at a time at the rate
    // the author requested (chars/sec, default 30 → ~2 frames/char at
    // 60Hz). X-press during reveal snaps to the end rather than
    // advancing the dialogue — same "press X to skip the crawl"
    // pattern as classic JRPGs. Reveal completion also gates the
    // X-advance: holding X through a slow typewriter doesn't skip
    // ahead to the next node until the current line is fully shown.
    enum class RevealMode { None = 0, Typewriter = 1 };
    RevealMode m_revealMode = RevealMode::None;
    int  m_revealFramesPerChar = 1;
    int  m_revealCursor = 0;
    int  m_revealTotal = 0;
    int  m_revealTickAccum = 0;
    char m_revealFullText[256] = {};

    // Subgraph call stack (slice D1j). `sub_dialogue` nodes pause the
    // current dialogue, push (parent_table_ref, parent_resume_id)
    // onto this stack, and replace the live table with a reference to
    // another dialogue (resolved as `_G.dialogue_<target>`). When the
    // sub reaches a node with nil-next we pop and resume at the
    // parent's `next` after the sub_dialogue node. Lets authors
    // factor shared sequences (a shopkeeper greeting, a death sting)
    // into reusable dialogue.tres files instead of duplicating nodes.
    static constexpr int kMaxSubgraphDepth = 4;
    struct SubgraphFrame {
        int  tableRef;                              // LUA_REGISTRYINDEX ref for the parent's table
        char resumeNodeId[16];                      // node id to land on after pop ("" → keep popping)
    };
    SubgraphFrame m_subStack[kMaxSubgraphDepth] = {};
    int           m_subStackDepth = 0;

    // Stored option text for selection-indicator fallback. When the canvas
    // has no cursor_1..3 elements, refreshCursor re-writes each option as
    // "> text" / "  text" so the selection is still visible. When cursors
    // exist, options keep their raw text and refreshCursor only toggles
    // cursor visibility.
    char m_optionTexts[kMaxOptions][96] = {};
    bool m_hasAnyCursor = false;

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

    // Slice D1h — fire the line's audio clip via Lua's Audio.PlaySfx
    // (so routing stays in one place) and set m_lineAdvanceLockFrames
    // to the clip's duration if the line is non-skippable. Skippable
    // lines play the audio but don't gate advance.
    void playLineAudio(lua_State* L, const char* clipName, bool skippable);

    // Slice D1i — read the line's `notifies` Lua array into the
    // m_notify* fields. Resets the counter + fired flags. No-op when
    // the line carries no notifies key. Truncates silently at
    // kMaxNotifies.
    void loadLineNotifies(lua_State* L, int lineTableIndex);

    // Slice D1i — called from tick() while a line is active. Fires
    // each notify whose at-frame threshold has been reached this
    // frame, marks it fired so the next tick doesn't double-fire.
    void tickLineNotifies(lua_State* L);

    // Toggle every element on the canvas that ISN'T one of the named
    // dialogue slots (speaker/text/option_*/cursor_*). Lets authors drop
    // a background Box, frame, portrait, etc. onto the dialogue_box and
    // have it auto-appear with the dialogue / disappear when it stops,
    // without remembering to flip VisibleOnLoad. Called once on start
    // (visible=true) and once on stop (visible=false).
    void manageAuxElements(bool visible);
};

} // namespace psxsplash
