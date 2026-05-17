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

void DialogueRunner::init(Controls* controls) {
    m_controls = controls;
    m_active = false;
    m_tableRef = -1;
    m_currentNodeId[0] = '\0';
    m_freshNode = false;
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
    printf("[Dialog] start — entry=%s\n", m_currentNodeId);
}

void DialogueRunner::stop(lua_State* L) {
    if (m_tableRef != -1) {
        luaL_unref(L, LUA_REGISTRYINDEX, m_tableRef);
        m_tableRef = -1;
    }
    m_active = false;
    m_currentNodeId[0] = '\0';
    m_freshNode = false;
}

// ─────────────────────────────────────────────────────────────────────
//  Per-frame tick
// ─────────────────────────────────────────────────────────────────────

void DialogueRunner::tick(lua_State* L) {
    if (!m_active) return;

    if (m_freshNode) {
        emitCurrentNode(L);
        m_freshNode = false;
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

    if (streq(kind, "line")) {
        char speaker[32] = "";
        char text[128] = "";
        readStringField(L, -1, "speaker", speaker, sizeof(speaker));
        readStringField(L, -1, "text",    text,    sizeof(text));
        printf("[Dialog] %s: %s  (press X to advance)\n",
               speaker[0] ? speaker : "(none)",
               text[0]    ? text    : "(empty)");
    }
    else if (streq(kind, "choice")) {
        printf("[Dialog] choice (press X to pick option 1):\n");
        // Walk options array: stack: node-table; push options at -2 then iterate.
        lua_getfield(L, -1, "options");
        if (lua_istable(L, -1)) {
            int n = (int)lua_rawlen(L, -1);
            for (int i = 1; i <= n; i++) {
                lua_rawgeti(L, -1, i);  // push options[i]
                if (lua_istable(L, -1)) {
                    char text[128] = "";
                    readStringField(L, -1, "text", text, sizeof(text));
                    printf("  %d) %s\n", i, text[0] ? text : "(empty)");
                }
                lua_pop(L, 1);  // pop options[i]
            }
        }
        lua_pop(L, 1);  // pop options
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
        // Slice D1b auto-pick: options[1].next  (Lua-1-indexed)
        lua_getfield(L, -1, "options");
        if (lua_istable(L, -1)) {
            lua_rawgeti(L, -1, 1);  // options[1]
            if (lua_istable(L, -1)) {
                lua_getfield(L, -1, "next");
                if (lua_isstring(L, -1)) {
                    bounded_strcpy(next, lua_tostring(L, -1), sizeof(next));
                }
                lua_pop(L, 1);  // pop options[1].next
            }
            lua_pop(L, 1);  // pop options[1]
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

} // namespace psxsplash
