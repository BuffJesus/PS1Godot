#include "dialogue.hh"

#include <psyqo/xprintf.h>
#include <psyqo-lua/lua.hh>
#include "streq.hh"

namespace psxsplash {

// Bounded string copy — no <string.h> in the freestanding build.
// Copies up to (outSize - 1) bytes from `src` into `out`, always
// null-terminates. Tolerant of null `src` (writes empty string).
static void bounded_strcpy(char* out, const char* src, size_t outSize) {
    if (outSize == 0) return;
    if (!src) { out[0] = '\0'; return; }
    size_t i = 0;
    while (i < outSize - 1 && src[i] != '\0') {
        out[i] = src[i];
        i++;
    }
    out[i] = '\0';
}

// ─────────────────────────────────────────────────────────────────────
//  Lifecycle
// ─────────────────────────────────────────────────────────────────────

void DialogueRunner::init(Controls* controls, UISystem* ui) {
    m_controls = controls;
    m_ui = ui;
    m_active = false;
    m_tableRef = -1;
    m_currentNodeId[0] = '\0';
    m_freshNode = false;
    // Canvas lookups happen on startFromStackTop — the canvas isn't
    // populated yet at SceneManager construction time.
    m_canvasIdx = -1;
    m_useCanvas = false;
}

void DialogueRunner::startFromStackTop(lua_State* L) {
    // Stop any in-progress dialogue first so the previous table's
    // registry slot is freed before we ref the new one.
    if (m_active) {
        stop(L);
    }

    if (!lua_istable(L, -1)) {
        printf("[Dialog] RunGraph: argument is not a table\n");
        return;
    }

    // luaL_ref pops the value at -1 and stashes it in LUA_REGISTRYINDEX,
    // returning the integer key we'll use to retrieve it later.
    int ref = luaL_ref(L, LUA_REGISTRYINDEX);
    m_tableRef = ref;

    // Pull the entry id off the stored table.
    lua_rawgeti(L, LUA_REGISTRYINDEX, m_tableRef);
    // stack: table
    lua_getfield(L, -1, "entry");
    // stack: table, entry-string-or-nil
    if (!lua_isstring(L, -1)) {
        printf("[Dialog] RunGraph: table has no .entry string\n");
        lua_pop(L, 2);
        stop(L);
        return;
    }
    const char* entry = lua_tostring(L, -1);
    bounded_strcpy(m_currentNodeId, entry, sizeof(m_currentNodeId));
    lua_pop(L, 2);  // pop entry-string and table

    m_active = true;
    m_freshNode = true;
    m_onChoiceNode = false;
    m_selectedChoice = 0;
    m_numActiveOptions = 0;

    // Look up the dialogue_box canvas now (after scene + canvases are
    // loaded). If found, cache element handles + show the canvas; if
    // not, fall back to printf-only mode (slice D1b behavior).
    m_canvasIdx = -1;
    m_speakerHandle = -1;
    m_textHandle = -1;
    for (int i = 0; i < kMaxOptions; i++) {
        m_optionHandles[i] = -1;
        m_cursorHandles[i] = -1;
    }
    m_useCanvas = false;
    if (m_ui) {
        m_canvasIdx = m_ui->findCanvas("dialogue_box");
        if (m_canvasIdx >= 0) {
            m_useCanvas = true;
            m_speakerHandle = m_ui->findElement(m_canvasIdx, "speaker");
            m_textHandle    = m_ui->findElement(m_canvasIdx, "text");
            // option_1..3 and cursor_1..3 — author can ship any subset.
            const char* optNames[kMaxOptions]    = { "option_1", "option_2", "option_3" };
            const char* cursorNames[kMaxOptions] = { "cursor_1", "cursor_2", "cursor_3" };
            for (int i = 0; i < kMaxOptions; i++) {
                m_optionHandles[i] = m_ui->findElement(m_canvasIdx, optNames[i]);
                m_cursorHandles[i] = m_ui->findElement(m_canvasIdx, cursorNames[i]);
            }
            m_ui->setCanvasVisible(m_canvasIdx, true);
            clearCanvasUI();
        }
    }

    printf("[Dialog] start — entry=%s (canvas=%s)\n",
           m_currentNodeId, m_useCanvas ? "dialogue_box" : "none, printf-only");
}

void DialogueRunner::stop(lua_State* L) {
    if (m_tableRef != -1) {
        luaL_unref(L, LUA_REGISTRYINDEX, m_tableRef);
        m_tableRef = -1;
    }
    if (m_useCanvas && m_ui && m_canvasIdx >= 0) {
        clearCanvasUI();
        m_ui->setCanvasVisible(m_canvasIdx, false);
    }
    m_active = false;
    m_currentNodeId[0] = '\0';
    m_freshNode = false;
    m_onChoiceNode = false;
    m_useCanvas = false;
}

// ─────────────────────────────────────────────────────────────────────
//  Per-frame tick
// ─────────────────────────────────────────────────────────────────────

void DialogueRunner::tick(lua_State* L) {
    if (!m_active) return;

    if (m_freshNode) {
        emitCurrentNode(L);
        m_freshNode = false;
        // Defer this frame's input. Without it, the X press that
        // advanced INTO this node could also be edge-detected for
        // advancing back OUT — fine for line→line (just feels twitchy)
        // but immediately auto-picks option 0 on a fresh choice node.
        return;
    }

    // Choice navigation: D-pad Up/Down cycles m_selectedChoice within
    // [0, m_numActiveOptions). Only active when the current node is a
    // choice with at least one option. Holding the d-pad does NOT
    // auto-repeat — wasButtonPressed is an edge-trigger.
    if (m_onChoiceNode && m_numActiveOptions > 0 && m_controls) {
        if (m_controls->wasButtonPressed(psyqo::AdvancedPad::Button::Up)) {
            m_selectedChoice = (m_selectedChoice - 1 + m_numActiveOptions) % m_numActiveOptions;
            refreshCursor();
        }
        else if (m_controls->wasButtonPressed(psyqo::AdvancedPad::Button::Down)) {
            m_selectedChoice = (m_selectedChoice + 1) % m_numActiveOptions;
            refreshCursor();
        }
    }

    // X / Cross button advances. Per-frame edge so holding the button
    // doesn't chew through the whole graph in one frame.
    if (m_controls && m_controls->wasButtonPressed(psyqo::AdvancedPad::Button::Cross)) {
        advanceFromCurrent(L);
    }
}

// ─────────────────────────────────────────────────────────────────────
//  Emit + advance
// ─────────────────────────────────────────────────────────────────────

void DialogueRunner::emitCurrentNode(lua_State* L) {
    if (!pushCurrentNode(L)) {
        printf("[Dialog] emit: current node '%s' missing — stopping\n", m_currentNodeId);
        stop(L);
        return;
    }
    // stack: node-table

    char kind[16] = "";
    if (!readStringField(L, -1, "kind", kind, sizeof(kind))) {
        printf("[Dialog] emit: node '%s' has no .kind — stopping\n", m_currentNodeId);
        lua_pop(L, 1);
        stop(L);
        return;
    }

    // Wipe any leftover canvas state from the previous node before
    // populating the new one — keeps stale option text from bleeding
    // through when transitioning line→line, choice→line, etc.
    if (m_useCanvas) clearCanvasUI();

    if (streq(kind, "line")) {
        m_onChoiceNode = false;
        m_numActiveOptions = 0;
        m_selectedChoice = 0;

        char speaker[32] = "";
        char text[128] = "";
        readStringField(L, -1, "speaker", speaker, sizeof(speaker));
        readStringField(L, -1, "text",    text,    sizeof(text));

        if (m_useCanvas && m_ui) {
            if (m_speakerHandle >= 0) m_ui->setText(m_speakerHandle, speaker);
            if (m_textHandle    >= 0) m_ui->setText(m_textHandle,    text);
        } else {
            printf("[Dialog] %s: %s  (press X to advance)\n",
                   speaker[0] ? speaker : "(none)",
                   text[0]    ? text    : "(empty)");
        }
    }
    else if (streq(kind, "choice")) {
        m_onChoiceNode = true;
        m_numActiveOptions = 0;
        m_selectedChoice = 0;

        if (m_useCanvas && m_ui) {
            // Show "Choose:" or whatever the author wrote in the
            // canvas. We don't override speaker/text — author can
            // pre-populate them as a fixed prompt if they want.
        } else {
            printf("[Dialog] choice (D-pad ↑↓ select, X confirm):\n");
        }

        // Walk options array: stack: node-table; push options at -2 then iterate.
        lua_getfield(L, -1, "options");
        if (lua_istable(L, -1)) {
            int n = (int)lua_rawlen(L, -1);
            int written = 0;
            for (int i = 1; i <= n && written < kMaxOptions; i++) {
                lua_rawgeti(L, -1, i);  // push options[i]
                if (lua_istable(L, -1)) {
                    char text[128] = "";
                    readStringField(L, -1, "text", text, sizeof(text));
                    if (m_useCanvas && m_ui) {
                        if (m_optionHandles[written] >= 0) {
                            m_ui->setText(m_optionHandles[written], text);
                        }
                    } else {
                        printf("  %d) %s\n", written + 1, text[0] ? text : "(empty)");
                    }
                    written++;
                }
                lua_pop(L, 1);  // pop options[i]
            }
            m_numActiveOptions = written;
        }
        lua_pop(L, 1);  // pop options

        refreshCursor();
    }
    else {
        printf("[Dialog] emit: unknown kind '%s' at node '%s'\n", kind, m_currentNodeId);
    }

    lua_pop(L, 1);  // pop node-table
}

void DialogueRunner::advanceFromCurrent(lua_State* L) {
    if (!pushCurrentNode(L)) {
        stop(L);
        return;
    }
    // stack: node-table

    char kind[16] = "";
    readStringField(L, -1, "kind", kind, sizeof(kind));

    char next[16] = "";

    if (streq(kind, "line")) {
        // Read node.next directly.
        lua_getfield(L, -1, "next");
        if (lua_isstring(L, -1)) {
            bounded_strcpy(next, lua_tostring(L, -1), sizeof(next));
        }
        lua_pop(L, 1);
    }
    else if (streq(kind, "choice")) {
        // Pick the option the cursor currently sits on (Lua tables are
        // 1-indexed, so add 1). m_selectedChoice was clamped to the
        // active option count when entering the node.
        int pickIdx = m_selectedChoice + 1;
        lua_getfield(L, -1, "options");
        if (lua_istable(L, -1)) {
            lua_rawgeti(L, -1, pickIdx);
            if (lua_istable(L, -1)) {
                lua_getfield(L, -1, "next");
                if (lua_isstring(L, -1)) {
                    bounded_strcpy(next, lua_tostring(L, -1), sizeof(next));
                }
                lua_pop(L, 1);  // pop options[pick].next
            }
            lua_pop(L, 1);  // pop options[pick]
        }
        lua_pop(L, 1);  // pop options
    }

    lua_pop(L, 1);  // pop node-table

    if (next[0] == '\0') {
        printf("[Dialog] end (no next from '%s' kind=%s)\n", m_currentNodeId, kind);
        stop(L);
        return;
    }

    bounded_strcpy(m_currentNodeId, next, sizeof(m_currentNodeId));
    m_freshNode = true;
    printf("[Dialog] → %s\n", m_currentNodeId);
}

// ─────────────────────────────────────────────────────────────────────
//  Lua stack helpers
// ─────────────────────────────────────────────────────────────────────

bool DialogueRunner::pushCurrentNode(lua_State* L) {
    if (m_tableRef == -1 || m_currentNodeId[0] == '\0') return false;
    lua_rawgeti(L, LUA_REGISTRYINDEX, m_tableRef);
    if (!lua_istable(L, -1)) { lua_pop(L, 1); return false; }
    lua_getfield(L, -1, "nodes");
    if (!lua_istable(L, -1)) { lua_pop(L, 2); return false; }
    lua_getfield(L, -1, m_currentNodeId);
    if (!lua_istable(L, -1)) { lua_pop(L, 3); return false; }
    // stack: table, nodes, node-table — collapse to leave just node-table on top.
    lua_replace(L, -3);  // node-table replaces table
    lua_pop(L, 1);       // pop nodes
    return true;
}

bool DialogueRunner::readStringField(lua_State* L, int tableIndex, const char* key,
                                      char* out, size_t outSize) {
    lua_getfield(L, tableIndex, key);
    bool ok = false;
    if (lua_isstring(L, -1)) {
        const char* s = lua_tostring(L, -1);
        if (s) {
            bounded_strcpy(out, s, outSize);
            ok = true;
        }
    }
    lua_pop(L, 1);
    return ok;
}

// ─────────────────────────────────────────────────────────────────────
//  Canvas UI helpers (slice D1c)
// ─────────────────────────────────────────────────────────────────────

void DialogueRunner::refreshCursor() {
    if (!m_useCanvas || !m_ui) return;
    for (int i = 0; i < kMaxOptions; i++) {
        if (m_cursorHandles[i] < 0) continue;
        bool visible = m_onChoiceNode
                       && i < m_numActiveOptions
                       && i == m_selectedChoice;
        m_ui->setElementVisible(m_cursorHandles[i], visible);
    }
}

void DialogueRunner::clearCanvasUI() {
    if (!m_useCanvas || !m_ui) return;
    if (m_speakerHandle >= 0) m_ui->setText(m_speakerHandle, "");
    if (m_textHandle    >= 0) m_ui->setText(m_textHandle,    "");
    for (int i = 0; i < kMaxOptions; i++) {
        if (m_optionHandles[i] >= 0) m_ui->setText(m_optionHandles[i], "");
        if (m_cursorHandles[i] >= 0) m_ui->setElementVisible(m_cursorHandles[i], false);
    }
}

} // namespace psxsplash
