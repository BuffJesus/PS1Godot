#include "lua.h"

#include <psyqo-lua/lua.hh>

#include <psyqo/alloc.h>
#include <psyqo/soft-math.hh>
#include <psyqo/trigonometry.hh>
#include <psyqo/xprintf.h>

#include "fileloader.hh"
#include "gameobject.hh"
#include "gtemath.hh"
#include "scenemanager.hh"

// Naive needle-in-haystack. Freestanding build doesn't pull <string.h>,
// and this only runs on load errors so a straight two-pointer scan is
// fine -- the error strings are short.
static bool containsSubstr(const char* haystack, const char* needle) {
    if (!haystack || !needle) return false;
    for (const char* h = haystack; *h; h++) {
        const char* a = h;
        const char* b = needle;
        while (*a && *b && *a == *b) { a++; b++; }
        if (!*b) return true;
    }
    return false;
}

// Print a Lua load-error with an actionable hint when psxlua's NOPARSER
// tokenizer rejects a literal the Godot exporter-side rewriter should
// have caught (decimal literals, rarely scientific notation). Authors
// hitting this today either (a) have a form the rewriter skips --
// scientific `1e-5`, bare trailing dot `5.`, `.5` -- or (b) edited the
// splashpack out-of-band without re-exporting.
static void printLuaLoadError(const char* errMsg, int fileIndex) {
    printf("Lua load error (asset %d): %s\n", fileIndex, errMsg ? errMsg : "(null)");
    if (containsSubstr(errMsg, "malformed number")) {
        printf("  hint: psxlua tokenizer is integer-only. Godot exporter auto-rewrites\n"
               "        '0.06' -> FixedPoint.newFromRaw(246). It skips scientific form\n"
               "        (1e-5), '.5', and '5.' -- write 0.5, 5.0, or\n"
               "        FixedPoint.newFromRaw(raw_fp12) manually. raw_fp12 = int*4096 + frac.\n");
    }
}

// OOM-guarded allocator for Lua. The linker redirects luaI_realloc
// here instead of straight to psyqo_realloc, so we can log before
// returning NULL.
extern "C" void *lua_oom_realloc(void *ptr, size_t size) {
    void *result = psyqo_realloc(ptr, size);
    if (!result && size > 0) {
        printf("Lua OOM: alloc %u bytes failed\n", (unsigned)size);
    }
    return result;
}

// Pre-compiled PS1 Lua bytecode for the GameObject metatable script.
// Compiled with luac_psx to avoid needing the Lua parser at runtime.
#include "gameobject_bytecode.h"

// Lua helpers

static constexpr int32_t kFixedScale = 4096;

// Accept FixedPoint object or plain number from Lua
static psyqo::FixedPoint<12> readFP(psyqo::Lua& L, int idx) {
    if (L.isFixedPoint(idx)) return L.toFixedPoint(idx);
    return psyqo::FixedPoint<12>(static_cast<int32_t>(L.toNumber(idx) * kFixedScale), psyqo::FixedPoint<12>::RAW);
}

static int gameobjectGetPosition(psyqo::Lua L) {

    auto go = L.toUserdata<psxsplash::GameObject>(1);

    L.newTable();
    L.push(go->position.x);
    L.setField(2, "x");
    L.push(go->position.y);
    L.setField(2, "y");
    L.push(go->position.z);
    L.setField(2, "z");

    return 1;

}

static int gameobjectSetPosition(psyqo::Lua L) {

    auto go = L.toUserdata<psxsplash::GameObject>(1);

    L.getField(2, "x");
    go->position.x = readFP(L, 3);
    L.pop();

    L.getField(2, "y");
    go->position.y = readFP(L, 3);
    L.pop();

    L.getField(2, "z");
    go->position.z = readFP(L, 3);
    L.pop();
    return 0;

}

static int gameobjectGetActive(psyqo::Lua L) {
    auto go = L.toUserdata<psxsplash::GameObject>(1);
    L.push(go->isActive());
    return 1;
}

static int gameobjectSetActive(psyqo::Lua L) {
    auto go = L.toUserdata<psxsplash::GameObject>(1);
    bool active = L.toBoolean(2);
    go->setActive(active);
    return 0;
}

static psyqo::Trig<> s_trig;

static psyqo::Angle fastAtan2(int32_t sinVal, int32_t cosVal) {
    psyqo::Angle result;
    if (cosVal == 0 && sinVal == 0) { result.value = 0; return result; }

    int32_t abs_s = sinVal < 0 ? -sinVal : sinVal;
    int32_t abs_c = cosVal < 0 ? -cosVal : cosVal;

    int32_t minV = abs_s < abs_c ? abs_s : abs_c;
    int32_t maxV = abs_s > abs_c ? abs_s : abs_c;
    int32_t angle = (minV * 256) / maxV;

    if (abs_s > abs_c) angle = 512 - angle;
    if (cosVal < 0) angle = 1024 - angle;
    if (sinVal < 0) angle = -angle;

    result.value = angle;
    return result;
}

static int gameobjectGetRotation(psyqo::Lua L) {
    auto go = L.toUserdata<psxsplash::GameObject>(1);
    // Decompose Y-axis rotation from the matrix (vs[0].x = cos, vs[0].z = sin)
    // For full XYZ, we extract approximate Euler angles from the rotation matrix.
    // Row 0: [cos(Y)cos(Z), -cos(Y)sin(Z), sin(Y)]
    // This is a simplified extraction assuming common rotation order Y*X*Z.
    int32_t sinY = go->rotation.vs[0].z.raw();
    int32_t cosY = go->rotation.vs[0].x.raw();
    int32_t sinX = -go->rotation.vs[1].z.raw();
    int32_t cosX = go->rotation.vs[2].z.raw();
    int32_t sinZ = -go->rotation.vs[0].y.raw();
    int32_t cosZ = go->rotation.vs[0].x.raw();

    auto toFP12 = [](psyqo::Angle a) -> psyqo::FixedPoint<12> {
        psyqo::FixedPoint<12> fp;
        fp.value = a.value << 2;
        return fp;
    };

    L.newTable();
    L.push(toFP12(fastAtan2(sinX, cosX)));
    L.setField(2, "x");
    L.push(toFP12(fastAtan2(sinY, cosY)));
    L.setField(2, "y");
    L.push(toFP12(fastAtan2(sinZ, cosZ)));
    L.setField(2, "z");

    return 1;
}

static int gameobjectSetRotation(psyqo::Lua L) {
    auto go = L.toUserdata<psxsplash::GameObject>(1);

    L.getField(2, "x");
    psyqo::FixedPoint<12> fpX = readFP(L, 3);
    L.pop();

    L.getField(2, "y");
    psyqo::FixedPoint<12> fpY = readFP(L, 3);
    L.pop();

    L.getField(2, "z");
    psyqo::FixedPoint<12> fpZ = readFP(L, 3);
    L.pop();

    // Convert FixedPoint<12> to Angle (FixedPoint<10>)
    psyqo::Angle rx, ry, rz;
    rx.value = fpX.value >> 2;
    ry.value = fpY.value >> 2;
    rz.value = fpZ.value >> 2;

    // Compose Y * X * Z rotation matrix
    auto matY = psyqo::SoftMath::generateRotationMatrix33(ry, psyqo::SoftMath::Axis::Y, s_trig);
    auto matX = psyqo::SoftMath::generateRotationMatrix33(rx, psyqo::SoftMath::Axis::X, s_trig);
    auto matZ = psyqo::SoftMath::generateRotationMatrix33(rz, psyqo::SoftMath::Axis::Z, s_trig);
    auto temp = psyqo::SoftMath::multiplyMatrix33(matY, matX);
    go->rotation = psxsplash::transposeMatrix33(
        psyqo::SoftMath::multiplyMatrix33(temp, matZ));
    return 0;
}


static int gameobjectGetRotationY(psyqo::Lua L) {
    auto go = L.toUserdata<psxsplash::GameObject>(1);
    int32_t sinRaw = go->rotation.vs[0].z.raw();
    int32_t cosRaw = go->rotation.vs[0].x.raw();
    psyqo::Angle angle = fastAtan2(sinRaw, cosRaw);
    // Angle is FixedPoint<10> (pi-units). Convert to FixedPoint<12> for Lua.
    psyqo::FixedPoint<12> fp12;
    fp12.value = angle.value << 2;
    L.push(fp12);
    return 1;
}

static int gameobjectSetRotationY(psyqo::Lua L) {
    auto go = L.toUserdata<psxsplash::GameObject>(1);
    // Accept FixedPoint<12> from Lua, convert to Angle (FixedPoint<10>)
    psyqo::FixedPoint<12> fp12 = readFP(L, 2);
    psyqo::Angle angle;
    angle.value = fp12.value >> 2;
    go->rotation = psxsplash::transposeMatrix33(
        psyqo::SoftMath::generateRotationMatrix33(angle, psyqo::SoftMath::Axis::Y, s_trig));
    return 0;
}

void psxsplash::Lua::Init() {
    auto L = m_state;
    // Load and run the game objects script
    if (L.loadBuffer(reinterpret_cast<const char*>(GAMEOBJECT_BYTECODE), sizeof(GAMEOBJECT_BYTECODE), "bytecode:gameObjects") == 0) {
        if (L.pcall(0, 1) == 0) {
            // This will be our metatable
            L.newTable();

            L.push(gameobjectGetPosition);
            L.setField(-2, "get_position");

            L.push(gameobjectSetPosition);
            L.setField(-2, "set_position");

            L.push(gameobjectGetActive);
            L.setField(-2, "get_active");

            L.push(gameobjectSetActive);
            L.setField(-2, "set_active");

            L.push(gameobjectGetRotation);
            L.setField(-2, "get_rotation");

            L.push(gameobjectSetRotation);
            L.setField(-2, "set_rotation");

            L.push(gameobjectGetRotationY);
            L.setField(-2, "get_rotationY");

            L.push(gameobjectSetRotationY);
            L.setField(-2, "set_rotationY");

            L.copy(-1);
            m_metatableReference = L.ref();

            if (L.pcall(1, 0) == 0) {
                // success
            } else {
                printf("Error registering Lua script: %s\n", L.optString(-1, "Unknown error"));
                L.clearStack();
                return;
            }
        } else {
            // Print Lua error if script execution fails
            printf("Error executing Lua script: %s\n", L.optString(-1, "Unknown error"));
            L.clearStack();
            return;
        }
    } else {
        // Print Lua error if script loading fails
        printf("Error loading Lua script: %s\n", L.optString(-1, "Unknown error"));
        L.clearStack();
        return;
    }

    L.newTable();
    m_luascriptsReference = L.ref();

    // Add __concat to the FixedPoint metatable so FixedPoint values work with ..
    // psyqo-lua doesn't provide this, but scripts need it for Debug.Log etc.
    L.getField(LUA_REGISTRYINDEX, "psyqo.FixedPoint");
    if (L.isTable(-1)) {
        L.push([](psyqo::Lua L) -> int {
            // Convert both operands to strings and concatenate
            char buf[64];
            int len = 0;
            for (int i = 1; i <= 2; i++) {
                if (L.isFixedPoint(i)) {
                    auto fp = L.toFixedPoint(i);
                    int32_t raw = fp.raw();
                    int integer = raw / 4096;
                    unsigned fraction = (raw < 0 ? -raw : raw) & 0xfff;
                    if (fraction == 0) {
                        len += snprintf(buf + len, sizeof(buf) - len, "%d", integer);
                    } else {
                        unsigned decimal = (fraction * 1000) >> 12;
                        if (raw < 0 && integer == 0)
                            len += snprintf(buf + len, sizeof(buf) - len, "-%d.%03u", integer, decimal);
                        else
                            len += snprintf(buf + len, sizeof(buf) - len, "%d.%03u", integer, decimal);
                    }
                } else {
                    const char* s = L.toString(i);
                    if (s) {
                        int slen = 0;
                        while (s[slen]) slen++;
                        if (len + slen < (int)sizeof(buf)) {
                            for (int j = 0; j < slen; j++) buf[len++] = s[j];
                        }
                    }
                }
            }
            buf[len] = '\0';
            L.push(buf, len);
            return 1;
        });
        L.setField(-2, "__concat");
    }
    L.pop();

    // FSM helper (slice D3-2) -- installs `_G.FSM = { new = function(def) ... end }`
    // so authored fsm.lua tables can be consumed without hand-rolling
    // the walker every time. The compiled fsm table shape (states,
    // transitions, optional on_enter/on_exit/on_update lookup tables)
    // matches what `PS1GraphCompiler.CompileFsm` emits. on_*
    // callbacks are aspirational for slice D3-3 -- the helper checks
    // for them defensively but the compiler doesn't populate them yet,
    // so today they just no-op.
    //
    // Embedded as a string literal rather than a separate asset so
    // there's no chance the user ships a splashpack missing the
    // helper. Pcall'd in the GLOBAL env (no per-script wrapping) so
    // FSM lands in _G, not in a sandbox.
    static const char kFsmHelperSrc[] =
        "FSM = FSM or {}\n"
        "function FSM.new(def)\n"
        "    local inst = { _def = def, _current = def and def.initial or nil }\n"
        "    function inst:Current() return self._current end\n"
        "    function inst:Is(state) return self._current == state end\n"
        "    function inst:Send(event)\n"
        "        local d = self._def\n"
        "        if not d or not d.transitions then return false end\n"
        "        for i = 1, #d.transitions do\n"
        "            local t = d.transitions[i]\n"
        "            if t.from == self._current and t.event == event then\n"
        "                local prev = self._current\n"
        "                self._current = t.to\n"
        "                if d.on_exit and d.on_exit[prev] then d.on_exit[prev](self, event) end\n"
        "                if d.on_enter and d.on_enter[self._current] then d.on_enter[self._current](self, event) end\n"
        "                return true\n"
        "            end\n"
        "        end\n"
        "        return false\n"
        "    end\n"
        "    function inst:Update(dt)\n"
        "        local d = self._def\n"
        "        if d and d.on_update and d.on_update[self._current] then\n"
        "            d.on_update[self._current](self, dt)\n"
        "        end\n"
        "    end\n"
        "    if def and def.on_enter and def.on_enter[def.initial] then\n"
        "        def.on_enter[def.initial](inst, nil)\n"
        "    end\n"
        "    return inst\n"
        "end\n";
    if (L.loadBuffer(kFsmHelperSrc, sizeof(kFsmHelperSrc) - 1, "builtin:fsm") == LUA_OK) {
        if (L.pcall(0, 0) != LUA_OK) {
            printf("Error installing FSM helper: %s\n", L.optString(-1, "Unknown error"));
            L.pop();
        }
    } else {
        printf("Error loading FSM helper: %s\n", L.optString(-1, "Unknown error"));
        L.pop();
    }

    // Quest helper (slice D2-2) -- installs `_G.Quest = { new = function(def) ... end }`
    // so authored quest.lua tables can be walked without hand-rolling
    // the prereq-evaluation loop. Shape matches what
    // `PS1GraphCompiler.CompileQuest` emits:
    //
    //   { initial_objectives = { id, ... },
    //     objectives = { id = { id, title, prereqs = {ids} }, ... },
    //     outcomes  = { { id, prereqs = {ids} }, ... } }
    //
    // Instance API:
    //   :Activate()                -- seed active set from initial_objectives
    //   :Complete(id)              -- mark completed, unlock newly-satisfied
    //                                objectives, return list of newly-unlocked ids
    //   :IsActive(id)              -- bool
    //   :IsComplete(id)            -- bool
    //   :ActiveSet()               -- array of currently-active objective ids
    //   :Outcome()                 -- first outcome whose prereqs are all
    //                                complete, or nil if none yet
    //   :Save() → table            -- { completed = {ids} } snapshot for Persist
    //   :Load(snap)                -- restore from a Save() snapshot,
    //                                recomputes active set deterministically
    static const char kQuestHelperSrc[] =
        "Quest = Quest or {}\n"
        "function Quest.new(def)\n"
        "    local inst = { _def = def, _active = {}, _completed = {}, _firedOutcomes = {} }\n"
        "    local function prereqsMet(prereqs)\n"
        "        if not prereqs then return true end\n"
        "        for i = 1, #prereqs do\n"
        "            if not inst._completed[prereqs[i]] then return false end\n"
        "        end\n"
        "        return true\n"
        "    end\n"
        "    local function dispatch(table, id)\n"
        "        if table and table[id] then table[id](inst) end\n"
        "    end\n"
        "    local function fireNewOutcomes()\n"
        "        if not def.outcomes then return end\n"
        "        for i = 1, #def.outcomes do\n"
        "            local o = def.outcomes[i]\n"
        "            if not inst._firedOutcomes[o.id] and prereqsMet(o.prereqs) then\n"
        "                inst._firedOutcomes[o.id] = true\n"
        "                dispatch(def.on_trigger, o.id)\n"
        "            end\n"
        "        end\n"
        "    end\n"
        "    local function recomputeActive(fireCallbacks)\n"
        "        local newlyUnlocked = {}\n"
        "        for id, obj in pairs(def.objectives or {}) do\n"
        "            if not inst._completed[id] and not inst._active[id] then\n"
        "                if prereqsMet(obj.prereqs) then\n"
        "                    inst._active[id] = true\n"
        "                    newlyUnlocked[#newlyUnlocked + 1] = id\n"
        "                    if fireCallbacks then dispatch(def.on_activate, id) end\n"
        "                end\n"
        "            end\n"
        "        end\n"
        "        if fireCallbacks then fireNewOutcomes() end\n"
        "        return newlyUnlocked\n"
        "    end\n"
        "    function inst:Activate()\n"
        "        for _, id in ipairs(def.initial_objectives or {}) do\n"
        "            if not self._completed[id] and not self._active[id] then\n"
        "                self._active[id] = true\n"
        "                dispatch(def.on_activate, id)\n"
        "            end\n"
        "        end\n"
        "        return recomputeActive(true)\n"
        "    end\n"
        "    function inst:Complete(id)\n"
        "        if not (def.objectives and def.objectives[id]) then return {} end\n"
        "        if self._completed[id] then return {} end\n"
        "        self._completed[id] = true\n"
        "        self._active[id] = nil\n"
        "        dispatch(def.on_complete, id)\n"
        "        return recomputeActive(true)\n"
        "    end\n"
        "    function inst:IsActive(id)   return self._active[id]    == true end\n"
        "    function inst:IsComplete(id) return self._completed[id] == true end\n"
        "    function inst:ActiveSet()\n"
        "        local out = {}\n"
        "        for id, _ in pairs(self._active) do out[#out + 1] = id end\n"
        "        return out\n"
        "    end\n"
        "    function inst:Outcome()\n"
        "        for i = 1, #(def.outcomes or {}) do\n"
        "            local o = def.outcomes[i]\n"
        "            if prereqsMet(o.prereqs) then return o.id end\n"
        "        end\n"
        "        return nil\n"
        "    end\n"
        "    function inst:Save()\n"
        "        local completed = {}\n"
        "        for id, _ in pairs(self._completed) do completed[#completed + 1] = id end\n"
        "        local fired = {}\n"
        "        for id, _ in pairs(self._firedOutcomes) do fired[#fired + 1] = id end\n"
        "        return { completed = completed, fired_outcomes = fired }\n"
        "    end\n"
        "    function inst:Load(snap)\n"
        "        self._completed = {}\n"
        "        self._active = {}\n"
        "        self._firedOutcomes = {}\n"
        "        if snap and snap.completed then\n"
        "            for _, id in ipairs(snap.completed) do self._completed[id] = true end\n"
        "        end\n"
        "        if snap and snap.fired_outcomes then\n"
        "            for _, id in ipairs(snap.fired_outcomes) do self._firedOutcomes[id] = true end\n"
        "        end\n"
        "        for _, id in ipairs(def.initial_objectives or {}) do\n"
        "            if not self._completed[id] then self._active[id] = true end\n"
        "        end\n"
        "        return recomputeActive(false)\n"
        "    end\n"
        "    inst:Activate()\n"
        "    return inst\n"
        "end\n";
    if (L.loadBuffer(kQuestHelperSrc, sizeof(kQuestHelperSrc) - 1, "builtin:quest") == LUA_OK) {
        if (L.pcall(0, 0) != LUA_OK) {
            printf("Error installing Quest helper: %s\n", L.optString(-1, "Unknown error"));
            L.pop();
        }
    } else {
        printf("Error loading Quest helper: %s\n", L.optString(-1, "Unknown error"));
        L.pop();
    }

    // BT helper (UE editor port plan pick #5) -- installs
    // `_G.BT = { new = function(def) ... end }` so authored
    // bt.lua tables can be ticked without hand-rolling the
    // tree walker. Shape matches what PS1GraphCompiler.CompileBt
    // emits:
    //
    //   { root = "n0",
    //     nodes = {
    //         n0 = { kind = "sequence", children = {"n1", "n2"} },
    //         n1 = { kind = "leaf", fn = function(self) return "success" end },
    //         ...
    //     } }
    //
    // Instance API:
    //   :Tick(actor)  → "success" / "failure" / "running"
    //   :Reset()      → clears all in-flight "running" state on
    //                   composites so the next Tick restarts each
    //                   subtree fresh.
    //
    // The Lua snippet inside each leaf receives `self` = the BT
    // instance (so leaves can stash per-instance scratch on
    // `self._scratch`). The `actor` arg passed to Tick is stored
    // on self.actor for convenience.
    //
    // ~50 lines of embedded Lua, zero per-instance C++ state.
    static const char kBtHelperSrc[] =
        "BT = BT or {}\n"
        "function BT.new(def)\n"
        "    local inst = { _def = def, _running = {}, _scratch = {}, actor = nil }\n"
        "    local function tickNode(id)\n"
        "        if not id then return \"failure\" end\n"
        "        local node = def.nodes and def.nodes[id]\n"
        "        if not node then return \"failure\" end\n"
        "        local k = node.kind\n"
        "        if k == \"leaf\" then\n"
        "            if not node.fn then return \"failure\" end\n"
        "            local ok, res = pcall(node.fn, inst)\n"
        "            if not ok then return \"failure\" end\n"
        "            if res ~= \"success\" and res ~= \"failure\" and res ~= \"running\" then\n"
        "                return \"failure\"\n"
        "            end\n"
        "            return res\n"
        "        elseif k == \"sequence\" then\n"
        "            local startIdx = inst._running[id] or 1\n"
        "            for i = startIdx, #(node.children or {}) do\n"
        "                local r = tickNode(node.children[i])\n"
        "                if r == \"failure\" then inst._running[id] = nil; return \"failure\" end\n"
        "                if r == \"running\" then inst._running[id] = i; return \"running\" end\n"
        "            end\n"
        "            inst._running[id] = nil\n"
        "            return \"success\"\n"
        "        elseif k == \"selector\" then\n"
        "            local startIdx = inst._running[id] or 1\n"
        "            for i = startIdx, #(node.children or {}) do\n"
        "                local r = tickNode(node.children[i])\n"
        "                if r == \"success\" then inst._running[id] = nil; return \"success\" end\n"
        "                if r == \"running\" then inst._running[id] = i; return \"running\" end\n"
        "            end\n"
        "            inst._running[id] = nil\n"
        "            return \"failure\"\n"
        "        end\n"
        "        return \"failure\"\n"
        "    end\n"
        "    function inst:Tick(actor)\n"
        "        self.actor = actor\n"
        "        return tickNode(def.root)\n"
        "    end\n"
        "    function inst:Reset()\n"
        "        self._running = {}\n"
        "    end\n"
        "    return inst\n"
        "end\n";
    if (L.loadBuffer(kBtHelperSrc, sizeof(kBtHelperSrc) - 1, "builtin:bt") == LUA_OK) {
        if (L.pcall(0, 0) != LUA_OK) {
            printf("Error installing BT helper: %s\n", L.optString(-1, "Unknown error"));
            L.pop();
        }
    } else {
        printf("Error loading BT helper: %s\n", L.optString(-1, "Unknown error"));
        L.pop();
    }
}

// Combat framework Phase 1 (RFC docs/internal/rfc/combat-framework.md
// §L1 + §L3). Five helpers extracted from the eleven foot-guns the
// boss_smoke encounter shipped on: distance helpers that avoid
// FixedPoint.__mul's /4096 rescale (Bug #7), a melee swing that
// anchors on the attacker and skips self by default (Bugs #5 + #11),
// a chase step wrapper, and a stat-bar updater for the 90% case of
// "set width = (cur/max) * authored width".
//
// Embedded as Lua source rather than C++ for the same reasons FSM/
// Quest/BT are above: zero per-scene plumbing, no risk of an author
// shipping a splashpack with a missing lib file. The Lua code below
// references Entity / Vec3 / Physics / Stats / UI globals but those
// resolve lazily at call time, not load time — so the installer is
// safe to run before any scene script.
//
// HOWEVER: `UI = UI or {}` followed by `UI.UpdateStatBar = ...`
// CAN'T run before LuaAPI::RegisterAll because RegisterAll does
// `L.setGlobal("UI")` which clobbers any pre-existing UI table.
// That's why this is a separate method from Init() — Init runs
// from L.Reset() which happens before LuaAPI registration in
// scenemanager.cpp:73 vs :86. InstallCombatLibrary is called after.
void psxsplash::Lua::InstallCombatLibrary() {
    auto L = m_state;
    static const char kCombatLibSrc[] =
        "Combat = Combat or {}\n"
        // DistanceSqRaw — fp12² (matches *_RADIUS_SQ thresholds).
        // (a - b)._raw bypasses the __mul rescale; result fits int32
        // for distances up to ~46 world units (squared ~ 2.1G).
        "function Combat.DistanceSqRaw(a, b)\n"
        "    local dxRaw = (a.x - b.x)._raw\n"
        "    local dzRaw = (a.z - b.z)._raw\n"
        "    return dxRaw * dxRaw + dzRaw * dzRaw\n"
        "end\n"
        // InRange — units is a plain number; we square (units*4096)
        // and compare. Same ~46-unit ceiling as DistanceSqRaw.
        "function Combat.InRange(a, b, units)\n"
        "    local fp = units * 4096\n"
        "    return Combat.DistanceSqRaw(a, b) <= fp * fp\n"
        "end\n"
        // MeleeSwing — AABB centered on the attacker (Bug #11 fixed
        // by construction: no more 'attack box at target position'
        // = infinite reach). skip_self defaults true (Bug #5: boss
        // self-damaged from its own swing). y_below/y_above default
        // to `range` for a symmetric cube; override for asymmetric
        // creature silhouettes (boss_smoke wants 1+2 e.g.). Returns
        // hits list (each .applied annotated) or nil when empty so
        // `if hits then ...` reads naturally.
        "function Combat.MeleeSwing(args)\n"
        "    local attacker = args.attacker\n"
        "    if not attacker then return nil end\n"
        "    local range  = args.range  or 2\n"
        "    local damage = args.damage or 0\n"
        "    local skip_self = args.skip_self\n"
        "    if skip_self == nil then skip_self = true end\n"
        "    local y_below = args.y_below or range\n"
        "    local y_above = args.y_above or range\n"
        "    local b = Entity.GetPosition(attacker)\n"
        "    local minV = Vec3.new(b.x - range, b.y - y_below, b.z - range)\n"
        "    local maxV = Vec3.new(b.x + range, b.y + y_above, b.z + range)\n"
        "    local raw = Physics.OverlapBoxDetailed(minV, maxV)\n"
        "    local hits = {}\n"
        "    for i = 1, #raw do\n"
        "        local h = raw[i]\n"
        "        if (not skip_self) or h.object ~= attacker then\n"
        "            local applied = Stats.DealDamage(h.object, damage, attacker)\n"
        "            h.applied = applied\n"
        "            hits[#hits + 1] = h\n"
        "            if args.on_hit then args.on_hit(h, applied) end\n"
        "        end\n"
        "    end\n"
        "    if #hits == 0 then\n"
        "        if args.on_whiff then args.on_whiff() end\n"
        "        return nil\n"
        "    end\n"
        "    return hits\n"
        "end\n"
        // ChaseStep — wraps `(d * speed) / 4096` so authors stop
        // copy-pasting it (and forgetting to clear y to keep the
        // chase on the XZ plane). speed_fp12 = 128 ≈ 0.03 units/
        // frame, the boss_smoke phase-1 cadence.
        "function Combat.ChaseStep(args)\n"
        "    local self = args.self\n"
        "    if not self then return end\n"
        "    local dx = args.dx\n"
        "    local dz = args.dz\n"
        "    local speed = args.speed_fp12 or 128\n"
        "    local p = Entity.GetPosition(self)\n"
        "    Entity.SetPosition(self, Vec3.new(\n"
        "        p.x + (dx * speed) / 4096,\n"
        "        p.y,\n"
        "        p.z + (dz * speed) / 4096))\n"
        "end\n"
        // Combat.MeleeBoss (RFC §L1 Phase 2) — souls-style state
        // machine composing the helpers above. Resolves Bug #10 by
        // construction: AGGRO decrements the recovery timer every
        // frame and only transitions to TELL when timer == 0 AND
        // player is in attack range, so the boss visibly tracks
        // the player between attacks instead of camping at the
        // range edge. Bug #6 (boss aggros on frame 1) is closed by
        // construction WHEN `encounter_id` is set: the state
        // machine auto-gates update() on `<encounter_id>_aggro`
        // and resets that flag at construction time (scene-load
        // local). Pair with an Encounter.new{id = same value}.
        //
        // Phase mechanism: `phases` is an ordered array. When HP
        // drops below `phase.hp_ratio * maxHP`, the next index
        // advances and its override fields replace the base
        // values via `effective(key)`. Phase index only advances
        // (souls bosses don't un-phase). Per-phase `on_enter`
        // fires once on entry.
        //
        // Game-feel is opt-in via callbacks (`on_tell`,
        // `on_hit_land`, `on_death`, phase `on_enter`) — the
        // state machine itself is pure mechanics, no shakes or
        // pauses without an authored callback. Death cleanup
        // that IS infrastructure (hide HP canvas, persist dead
        // key, deactivate entity) runs declaratively from def
        // fields so callers don't re-derive it.
        "function Combat.MeleeBoss(def)\n"
        "    local STATE_IDLE  = 0\n"
        "    local STATE_AGGRO = 1\n"
        "    local STATE_TELL  = 2\n"
        "    local STATE_HIT   = 3\n"
        "    local STATE_DEAD  = 4\n"
        // Encounter binding: pairs the boss with an Encounter.new
        // of the same id. The aggro key gates update() each frame
        // — boss stays dormant until Encounter:onEnter() flips it.
        // The dead key is the death-flag write so the encounter's
        // re-entry check sees this boss as cleared. Both keys are
        // derived; persist_dead_key can still be set explicitly
        // for bosses without an encounter.
        "    local enc_id = def.encounter_id\n"
        "    local key_aggro = enc_id and (enc_id .. \"_aggro\") or nil\n"
        "    local key_dead  = def.persist_dead_key\n"
        "                       or (enc_id and (enc_id .. \"_dead\"))\n"
        // Scene-load reset of the aggro flag. Construction runs at
        // chunk-load time (scenemanager LoadLuaFile loop) before
        // any onCreate fires, so a respawn after death drops the
        // boss back to dormant. The dead flag is NOT reset —
        // cleared bosses stay cleared per the souls convention.
        "    if key_aggro then Persist.Set(key_aggro, 0) end\n"
        "    local inst = {\n"
        "        _state = STATE_IDLE,\n"
        "        _timer = 0,\n"
        "        _phase = 0,\n"
        "        _aggro_sq  = (def.aggro_radius  * 4096) * (def.aggro_radius  * 4096),\n"
        "        _attack_sq = (def.attack_radius * 4096) * (def.attack_radius * 4096),\n"
        "    }\n"
        // effective(key): per-call lookup that walks the active
        // phase override first, falls back to base def. Phase 0
        // (no overrides yet entered) always falls through.
        "    local function effective(key)\n"
        "        local phase = def.phases and def.phases[inst._phase]\n"
        "        if phase and phase[key] ~= nil then return phase[key] end\n"
        "        return def[key]\n"
        "    end\n"
        "    function inst:update(entity, dt)\n"
        "        if self._state == STATE_DEAD then return end\n"
        // Encounter gate (when encounter_id is set). The boss
        // brain no longer needs an inline `if Persist.Get(...)
        // ~= 1 then return end` — that boilerplate moved here
        // so it can't be forgotten by the next boss author.
        "        if key_aggro and Persist.Get(key_aggro) ~= 1 then\n"
        "            return\n"
        "        end\n"
        // Live HP bar — collapses what was an updateHPBar local
        // in every brain script. Element name defaults to "fill"
        // matching the boss_smoke canvas convention.
        "        if def.hp_canvas then\n"
        "            UI.UpdateStatBar{\n"
        "                entity = entity,\n"
        "                canvas = def.hp_canvas,\n"
        "                element = def.hp_element or \"fill\",\n"
        "                stat = \"hp\",\n"
        "            }\n"
        "        end\n"
        "        local b = Entity.GetPosition(entity)\n"
        "        local p = Player.GetPosition()\n"
        "        local dx = p.x - b.x\n"
        "        local dz = p.z - b.z\n"
        "        local distSq = dx._raw * dx._raw + dz._raw * dz._raw\n"
        // Face the player whenever the boss has noticed it. IDLE
        // keeps the authored facing (so the boss doesn't snap to
        // the player before the encounter begins).
        "        if self._state ~= STATE_IDLE then\n"
        "            Entity.SetRotationY(entity, Math.Atan2(dx, dz))\n"
        "        end\n"
        "        if self._state == STATE_IDLE then\n"
        "            if distSq < self._aggro_sq then\n"
        "                self._state = STATE_AGGRO\n"
        "                self._timer = 0\n"
        "            end\n"
        "        elseif self._state == STATE_AGGRO then\n"
        // AGGRO has two responsibilities (Bug #10 lesson):
        // 1. Chase when out of attack range.
        // 2. Recovery window after a swing — keep chasing for
        //    `recover_frames` even if already in range so the
        //    boss tracks the player around the arena.
        // Transition to TELL only when both timer == 0 AND
        // distSq <= attack range.
        "            local doStep = self._timer > 0 or distSq > self._attack_sq\n"
        "            if doStep then\n"
        "                if self._timer > 0 then self._timer = self._timer - 1 end\n"
        "                Combat.ChaseStep{\n"
        "                    self = entity, dx = dx, dz = dz,\n"
        "                    speed_fp12 = effective(\"chase_speed_fp12\") or 128,\n"
        "                }\n"
        "            else\n"
        "                self._state = STATE_TELL\n"
        "                self._timer = effective(\"tell_frames\") or 30\n"
        "                if def.on_tell then def.on_tell(self, entity) end\n"
        "            end\n"
        "        elseif self._state == STATE_TELL then\n"
        "            self._timer = self._timer - 1\n"
        "            if self._timer <= 0 then\n"
        "                self._state = STATE_HIT\n"
        "                self._timer = effective(\"hit_frames\") or 12\n"
        "                local range = effective(\"swing_range\") or 2\n"
        // Forward MeleeSwing's on_hit to the boss-level
        // on_hit_land callback so game-feel (shake + pause)
        // sits at the boss level rather than per-swing.
        "                Combat.MeleeSwing{\n"
        "                    attacker = entity,\n"
        "                    range = range,\n"
        "                    damage = effective(\"swing_damage\") or 0,\n"
        "                    y_below = effective(\"swing_y_below\") or range,\n"
        "                    y_above = effective(\"swing_y_above\") or range,\n"
        "                    on_hit = function(h, applied)\n"
        "                        if def.on_hit_land then\n"
        "                            def.on_hit_land(inst, entity, h, applied)\n"
        "                        end\n"
        "                    end,\n"
        "                }\n"
        "            end\n"
        "        elseif self._state == STATE_HIT then\n"
        "            self._timer = self._timer - 1\n"
        "            if self._timer <= 0 then\n"
        "                self._state = STATE_AGGRO\n"
        "                self._timer = effective(\"recover_frames\") or 30\n"
        "            end\n"
        "        end\n"
        "    end\n"
        "    function inst:handleDamage(entity, applied, source)\n"
        "        if self._state == STATE_DEAD then return end\n"
        "        if def.iframes then\n"
        "            Controls.StartIFrames(entity, def.iframes)\n"
        "        end\n"
        "        local hp = Stats.GetHP(entity)\n"
        "        local maxHP = Stats.GetMaxHP(entity)\n"
        "        if hp <= 0 then\n"
        "            self._state = STATE_DEAD\n"
        // Infrastructure death cleanup: hide the HP canvas (so it
        // doesn't linger over the corpse), set a persist key for
        // the encounter module / save system to read, and disable
        // the entity (collision + ticks stop). on_death callback
        // runs first so it can read live state before deactivation.
        "            if def.on_death then def.on_death(self, entity) end\n"
        "            if def.hp_canvas then\n"
        "                local c = UI.FindCanvas(def.hp_canvas)\n"
        "                if c >= 0 then UI.SetCanvasVisible(c, false) end\n"
        "            end\n"
        "            if key_dead then\n"
        "                Persist.Set(key_dead, 1)\n"
        "            end\n"
        "            Entity.SetActive(entity, false)\n"
        "            return\n"
        "        end\n"
        // Phase advancement: scan forward from current phase,
        // enter the first phase whose hp_ratio threshold the
        // boss is now below. `hp < maxHP * hp_ratio` uses Lua's
        // mixed int*float arithmetic — maxHP is int, hp_ratio
        // is float (e.g. 0.5), the product is float, the < int
        // comparison works.
        "        if maxHP > 0 and def.phases then\n"
        "            for i = self._phase + 1, #def.phases do\n"
        "                local phase = def.phases[i]\n"
        "                if phase.hp_ratio and hp < maxHP * phase.hp_ratio then\n"
        "                    local from = self._phase\n"
        "                    self._phase = i\n"
        "                    if def.iframes_phase_change then\n"
        "                        Controls.StartIFrames(entity, def.iframes_phase_change)\n"
        "                    end\n"
        "                    if phase.on_enter then phase.on_enter(self, entity) end\n"
        "                    if def.on_phase_change then\n"
        "                        def.on_phase_change(self, from, i)\n"
        "                    end\n"
        "                    break\n"
        "                end\n"
        "            end\n"
        "        end\n"
        "    end\n"
        // Reset — restore the IDLE/timer=0/phase=0 starting state.
        // Useful for respawn-after-death scenarios where the
        // encounter wants to re-arm the boss without reloading
        // the scene. Does NOT restore HP or re-show the canvas;
        // the encounter script handles those (it has more
        // context about what "respawn" means for the game).
        "    function inst:Reset()\n"
        "        self._state = STATE_IDLE\n"
        "        self._timer = 0\n"
        "        self._phase = 0\n"
        "    end\n"
        "    return inst\n"
        "end\n"
        // Encounter.new (RFC §L2 Phase 3) — fog-gate lifecycle.
        // Owns four jobs that the boss_smoke debug arc revealed
        // were all separate fixes the author kept getting wrong:
        //   1. Reveal HP canvas + start music on entry  (Bug #1)
        //   2. Wake the boss via Persist flag           (Bug #6)
        //   3. Block retreat through the fog wall       (Bug #9)
        //   4. Suppress re-fire during an active fight  (Bug #9 redux)
        //
        // Persist keys derive from `def.id`:
        //   <id>_aggro  — 1 while the encounter is live; the boss
        //                 brain reads this in onUpdate to stay
        //                 dormant before the cross.
        //   <id>_dead   — 1 after the boss is cleared; entry is a
        //                 no-op when set.
        //
        // Boss interop: when authored with `Combat.MeleeBoss{...
        // persist_dead_key = "<id>_dead" }`, the brain's death
        // path sets the dead flag automatically — no encounter:
        // markCleared call from the brain needed. markCleared is
        // shipped anyway for callers that don't use MeleeBoss.
        //
        // The trigger callback's `self` is a number (a trigger
        // index, not a GameObject) — runtime limitation noted in
        // the RFC. So block-retreat snap-back hardcodes a Z
        // reference (`trigger_z_raw`) per the gotcha until the
        // runtime gains a Trigger.GetPosition or pushes a handle.
        "Encounter = Encounter or {}\n"
        "function Encounter.new(def)\n"
        "    local id          = def.id\n"
        "    local key_aggro   = id .. \"_aggro\"\n"
        "    local key_dead    = id .. \"_dead\"\n"
        "    local inst = { _def = def }\n"
        "    function inst:onEnter()\n"
        // Two gates to suppress accidental re-fire:
        // - Already cleared: stay quiet across save/load.
        // - Already active: a retreat snap-back may put the
        //   player back inside the trigger AABB mid-fight, which
        //   would otherwise restart music + flash the HP canvas
        //   re-reveal animation. The "already in fight" guard
        //   kills the loop.
        "        if Persist.Get(key_dead)  == 1 then return end\n"
        "        if Persist.Get(key_aggro) == 1 then return end\n"
        "        if def.sfx_on_enter then\n"
        "            Audio.PlaySfx(def.sfx_on_enter)\n"
        "        end\n"
        "        if def.music then\n"
        "            Music.Play(def.music, def.music_volume or 100)\n"
        "        end\n"
        "        if def.hp_canvas then\n"
        "            local c = UI.FindCanvas(def.hp_canvas)\n"
        "            if c >= 0 then UI.SetCanvasVisible(c, true) end\n"
        "        end\n"
        "        Persist.Set(key_aggro, 1)\n"
        "        if def.on_enter_extra then def.on_enter_extra(self) end\n"
        "    end\n"
        "    function inst:onExit()\n"
        // Block-retreat is the souls "fog wall is solid from
        // inside" semantic. We only snap back during an active,
        // un-cleared fight — after the boss dies the gate opens
        // and the player walks out freely.
        "        if not def.block_retreat then return end\n"
        "        if Persist.Get(key_aggro) ~= 1 then return end\n"
        "        if Persist.Get(key_dead)  == 1 then return end\n"
        "        if not def.trigger_z_raw then return end\n"
        "        local p = Player.GetPosition()\n"
        // Compare against the trigger's authored Z to decide
        // which side the player just crossed to. Z below the
        // trigger center = retreat toward spawn.
        "        if p.z._raw < def.trigger_z_raw then\n"
        "            Player.SetPosition(Vec3.new(\n"
        "                p.x, p.y, def.arena_anchor_z_raw or 0))\n"
        "            if def.on_retreat_block then\n"
        "                def.on_retreat_block(self)\n"
        "            else\n"
        // Default thud — same magnitude the boss_smoke fog
        // gate used pre-framework. Subtle but distinct enough
        // that the player feels the wall.
        "                Camera.ShakeRaw(82, 4)\n"
        "            end\n"
        "        end\n"
        "    end\n"
        // markCleared — explicit "boss is dead" API. Redundant
        // when the boss uses Combat.MeleeBoss{persist_dead_key=...}
        // (the brain's death path already does the work), but
        // ships for non-MeleeBoss callers.
        "    function inst:markCleared()\n"
        "        Persist.Set(key_dead, 1)\n"
        "        if def.hp_canvas then\n"
        "            local c = UI.FindCanvas(def.hp_canvas)\n"
        "            if c >= 0 then UI.SetCanvasVisible(c, false) end\n"
        "        end\n"
        "    end\n"
        "    function inst:isActive()\n"
        "        return Persist.Get(key_aggro) == 1\n"
        "           and Persist.Get(key_dead)  ~= 1\n"
        "    end\n"
        "    function inst:isCleared()\n"
        "        return Persist.Get(key_dead) == 1\n"
        "    end\n"
        "    return inst\n"
        "end\n"
        // UI.UpdateStatBar — collapse the four-line FindCanvas/
        // FindElement/Get*/SetSize dance into one call. Width
        // defaults to the element's authored width (read via
        // UI.GetSize) so callers don't have to remember the magic
        // number from their .tscn. Stat is "hp" | "stamina" | "mana".
        "UI = UI or {}\n"
        "function UI.UpdateStatBar(args)\n"
        "    local canvas = UI.FindCanvas(args.canvas)\n"
        "    if canvas < 0 then return end\n"
        "    local el = UI.FindElement(canvas, args.element)\n"
        "    if el < 0 then return end\n"
        "    local stat = args.stat\n"
        "    local cur, maxv = 0, 0\n"
        "    if stat == \"hp\" then\n"
        "        cur  = Stats.GetHP(args.entity)\n"
        "        maxv = Stats.GetMaxHP(args.entity)\n"
        "    elseif stat == \"stamina\" then\n"
        "        cur  = Stats.GetStamina(args.entity)\n"
        "        maxv = Stats.GetMaxStamina(args.entity)\n"
        "    elseif stat == \"mana\" then\n"
        "        cur  = Stats.GetMana(args.entity)\n"
        "        maxv = Stats.GetMaxMana(args.entity)\n"
        "    else\n"
        "        return\n"
        "    end\n"
        "    if maxv <= 0 then return end\n"
        "    local authoredW, authoredH = UI.GetSize(el)\n"
        "    local width  = args.width  or authoredW\n"
        "    local height = args.height or authoredH\n"
        "    UI.SetSize(el, (cur * width) / maxv, height)\n"
        "end\n";
    if (L.loadBuffer(kCombatLibSrc, sizeof(kCombatLibSrc) - 1, "builtin:combat") == LUA_OK) {
        if (L.pcall(0, 0) != LUA_OK) {
            printf("Error installing Combat library: %s\n", L.optString(-1, "Unknown error"));
            L.pop();
        }
    } else {
        printf("Error loading Combat library: %s\n", L.optString(-1, "Unknown error"));
        L.pop();
    }
}

void psxsplash::Lua::Shutdown() {

    if (m_state.getState()) {
        m_state.close();
    }
    m_metatableReference = LUA_NOREF;
    m_luascriptsReference = LUA_NOREF;
    m_luaSceneScriptsReference = LUA_NOREF;

    // Hot-swap buffers and version counter are scene-local: a scene
    // transition wipes m_bytecodeRefs and any newly-loaded scene's
    // bytecode is authoritative regardless of prior swaps.
    for (int i = 0; i < MAX_LUA_FILES; i++) {
        if (m_hotSwapBuffers[i]) { delete[] m_hotSwapBuffers[i]; m_hotSwapBuffers[i] = nullptr; }
    }
    m_lastHotSwapVersion = 0;
    m_bytecodeRefCount = 0;
}

void psxsplash::Lua::Reset() {
    Shutdown();
    m_state = psyqo::Lua();
    Init();
}

void psxsplash::Lua::LoadLuaFile(const char* code, size_t len, int index) {
    // Store bytecode reference for per-object re-execution in RegisterGameObject.
    if (index < MAX_LUA_FILES) {
        m_bytecodeRefs[index] = {code, len};
        if (index >= m_bytecodeRefCount) m_bytecodeRefCount = index + 1;
    }

    auto L = m_state;
    char filename[32];
    snprintf(filename, sizeof(filename), "lua_asset:%d", index);
    if (L.loadBuffer(code, len, filename) != LUA_OK) {
        printLuaLoadError(L.toString(-1), index);
        L.pop();
        return;
    }
    // (1) script func
    L.rawGetI(LUA_REGISTRYINDEX, m_luascriptsReference);
    // (1) script func (2) scripts table
    L.newTable();
    // (1) script func (2) scripts table (3) env {}

    // Give the environment a metatable that falls back to _G
    // so scripts can see Entity, Debug, Input, etc.
    L.newTable();
    // (1) script func (2) scripts table (3) env {} (4) mt {}
    L.pushGlobalTable();
    // (1) script func (2) scripts table (3) env {} (4) mt {} (5) _G
    L.setField(-2, "__index");
    // (1) script func (2) scripts table (3) env {} (4) mt { __index = _G }
    L.setMetatable(-2);
    // (1) script func (2) scripts table (3) env { mt }

    L.pushNumber(index);
    // (1) script func (2) scripts table (3) env (4) index
    L.copy(-2);
    // (1) script func (2) scripts table (3) env (4) index (5) env
    L.setTable(-4);
    // (1) script func (2) scripts table (3) env
    lua_setupvalue(L.getState(), -3, 1);
    // (1) script func (2) scripts table
    L.pop();
    // (1) script func
    if (L.pcall(0, 0)) {
        printf("Lua error: %s\n", L.toString(-1));
        L.pop();
    }
}

void psxsplash::Lua::RegisterSceneScripts(int index) {
    if (index < 0) return;
    auto L = m_state;
    L.newTable();
    // (1) {}
    L.copy(1);
    // (1) {} (2) {}
    m_luaSceneScriptsReference = L.ref();
    // (1) {}
    L.rawGetI(LUA_REGISTRYINDEX, m_luascriptsReference);
    // (1) {} (2) scripts table
    L.pushNumber(index);
    // (1) {} (2) script environments table (3) index
    L.getTable(-2);
    // (1) {} (2) script environments table (3) script environment table for the scene
    if (!L.isTable(-1)) {
        // Scene Lua file index is invalid or script not loaded
        printf("Warning: scene Lua file index %d not found\n", index);
        L.pop(3);
        return;
    }
    onSceneCreationStartFunctionWrapper.resolveGlobal(L);
    onSceneCreationEndFunctionWrapper.resolveGlobal(L);
    L.pop(3);
    // empty stack
}

void psxsplash::Lua::RegisterGameObject(GameObject* go) {
    uint8_t* ptr = reinterpret_cast<uint8_t*>(go);
    auto L = m_state;
    L.push(ptr);
    // (1) go
    L.newTable();
    // (1) go (2) {}
    L.push(ptr);
    // (1) go (2) {} (3) go
    L.setField(-2, "__cpp_ptr");
    // (1) go (2) { __cpp_ptr = go }
    L.rawGetI(LUA_REGISTRYINDEX, m_metatableReference);
    // (1) go (2) { __cpp_ptr = go } (3) metatable
    if (L.isTable(-1)) {
        L.setMetatable(-2);
    } else {
        printf("Warning: metatableForAllGameObjects not found\n");
        L.pop();
    }
    // (1) go (2) { __cpp_ptr = go + metatable }
    L.rawSet(LUA_REGISTRYINDEX);
    // empty stack
    L.newTable();
    // (1) {}
    L.push(ptr + 1);
    // (1) {} (2) go + 1
    L.copy(1);
    // (1) {} (2) go + 1 (3) {}
    L.rawSet(LUA_REGISTRYINDEX);
    // (1) {}
    
    // Initialize event mask for this object
    uint32_t eventMask = EVENT_NONE;

    if (go->luaFileIndex != -1 && go->luaFileIndex < m_bytecodeRefCount) {
        auto& ref = m_bytecodeRefs[go->luaFileIndex];
        char filename[32];
        snprintf(filename, sizeof(filename), "lua_asset:%d", go->luaFileIndex);

        if (L.loadBuffer(ref.code, ref.len, filename) == LUA_OK) {
            // (1) method_table (2) chunk_func

            // Create a per-object environment with __index = _G
            // so this object's file-level locals are isolated.
            L.newTable();
            L.newTable();
            L.pushGlobalTable();
            L.setField(-2, "__index");
            L.setMetatable(-2);
            // (1) method_table (2) chunk_func (3) env

            // Set env as the chunk's _ENV upvalue
            L.copy(-1);
            // (1) method_table (2) chunk_func (3) env (4) env_copy
            lua_setupvalue(L.getState(), -3, 1);
            // (1) method_table (2) chunk_func (3) env

            // Move chunk to top for pcall
            lua_insert(L.getState(), -2);
            // (1) method_table (2) env (3) chunk_func

            if (L.pcall(0, 0) == LUA_OK) {
                // (1) method_table (2) env
                // resolveGlobal expects: (1) method_table, (3) env
                // Insert a placeholder at position 2 to push env to position 3
                L.push();  // push nil
                // (1) method_table (2) env (3) nil
                lua_insert(L.getState(), 2);
                // (1) method_table (2) nil (3) env

                // Resolve each event - creates fresh function refs with isolated upvalues
                if (onCreateMethodWrapper.resolveGlobal(L))              eventMask |= EVENT_ON_CREATE;
                if (onCollideWithPlayerMethodWrapper.resolveGlobal(L))   eventMask |= EVENT_ON_COLLISION;
                if (onInteractMethodWrapper.resolveGlobal(L))            eventMask |= EVENT_ON_INTERACT;
                if (onTriggerEnterMethodWrapper.resolveGlobal(L))        eventMask |= EVENT_ON_TRIGGER_ENTER;
                if (onTriggerExitMethodWrapper.resolveGlobal(L))         eventMask |= EVENT_ON_TRIGGER_EXIT;
                if (onUpdateMethodWrapper.resolveGlobal(L))              eventMask |= EVENT_ON_UPDATE;
                if (onDestroyMethodWrapper.resolveGlobal(L))             eventMask |= EVENT_ON_DESTROY;
                if (onEnableMethodWrapper.resolveGlobal(L))              eventMask |= EVENT_ON_ENABLE;
                if (onDisableMethodWrapper.resolveGlobal(L))             eventMask |= EVENT_ON_DISABLE;
                if (onButtonPressMethodWrapper.resolveGlobal(L))         eventMask |= EVENT_ON_BUTTON_PRESS;
                if (onButtonReleaseMethodWrapper.resolveGlobal(L))       eventMask |= EVENT_ON_BUTTON_RELEASE;
                if (onDamageMethodWrapper.resolveGlobal(L))              eventMask |= EVENT_ON_DAMAGE;

                L.pop(2); // pop nil and env
            } else {
                printLuaLoadError(L.toString(-1), go->luaFileIndex);
                L.pop(2); // pop error msg and env
            }
        } else {
            printLuaLoadError(L.toString(-1), go->luaFileIndex);
            L.pop(); // pop error msg
        }
    }
    
    // Store the event mask directly in the GameObject
    go->eventMask = eventMask;

    L.pop();
    // empty stack
    // Note: onCreate is NOT fired here. Call FireAllOnCreate() after all objects
    // are registered so that Entity.Find works across all objects in onCreate.
}

void psxsplash::Lua::FireAllOnCreate(GameObject** objects, size_t count) {
    for (size_t i = 0; i < count; i++) {
        if (objects[i] && (objects[i]->eventMask & EVENT_ON_CREATE)) {
            onCreateMethodWrapper.callMethod(*this, objects[i]);
        }
    }
}

void psxsplash::Lua::OnCollideWithPlayer(GameObject* self) {
    if (!hasEvent(self, EVENT_ON_COLLISION)) return;
    onCollideWithPlayerMethodWrapper.callMethod(*this, self);
}

void psxsplash::Lua::OnInteract(GameObject* self) {
    if (!hasEvent(self, EVENT_ON_INTERACT)) return;
    onInteractMethodWrapper.callMethod(*this, self);
}

void psxsplash::Lua::OnTriggerEnter(GameObject* trigger, GameObject* other) {
    if (!hasEvent(trigger, EVENT_ON_TRIGGER_ENTER)) return;
    onTriggerEnterMethodWrapper.callMethod(*this, trigger, other);
}

void psxsplash::Lua::OnTriggerExit(GameObject* trigger, GameObject* other) {
    if (!hasEvent(trigger, EVENT_ON_TRIGGER_EXIT)) return;
    onTriggerExitMethodWrapper.callMethod(*this, trigger, other);
}

void psxsplash::Lua::OnTriggerEnterScript(int luaFileIndex, int triggerIndex) {
    auto L = m_state;
    L.rawGetI(LUA_REGISTRYINDEX, m_luascriptsReference);
    L.rawGetI(-1, luaFileIndex);
    if (!L.isTable(-1)) { L.clearStack(); return; }
    L.push("onTriggerEnter", 14);
    L.getTable(-2);
    if (!L.isFunction(-1)) { L.clearStack(); return; }
    L.pushNumber(triggerIndex);
    if (L.pcall(1, 0) != LUA_OK) {
        printf("Lua error: %s\n", L.toString(-1));
    }
    L.clearStack();
}

void psxsplash::Lua::OnTriggerExitScript(int luaFileIndex, int triggerIndex) {
    auto L = m_state;
    L.rawGetI(LUA_REGISTRYINDEX, m_luascriptsReference);
    L.rawGetI(-1, luaFileIndex);
    if (!L.isTable(-1)) { L.clearStack(); return; }
    L.push("onTriggerExit", 13);
    L.getTable(-2);
    if (!L.isFunction(-1)) { L.clearStack(); return; }
    L.pushNumber(triggerIndex);
    if (L.pcall(1, 0) != LUA_OK) {
        printf("Lua error: %s\n", L.toString(-1));
    }
    L.clearStack();
}

void psxsplash::Lua::OnDestroy(GameObject* go) {
    if (!hasEvent(go, EVENT_ON_DESTROY)) return;
    onDestroyMethodWrapper.callMethod(*this, go);
    go->eventMask = EVENT_NONE;
}

void psxsplash::Lua::OnEnable(GameObject* go) {
    if (!hasEvent(go, EVENT_ON_ENABLE)) return;
    onEnableMethodWrapper.callMethod(*this, go);
}

void psxsplash::Lua::OnDisable(GameObject* go) {
    if (!hasEvent(go, EVENT_ON_DISABLE)) return;
    onDisableMethodWrapper.callMethod(*this, go);
}

void psxsplash::Lua::OnButtonPress(GameObject* go, int button) {
    if (!hasEvent(go, EVENT_ON_BUTTON_PRESS)) return;
    onButtonPressMethodWrapper.callMethod(*this, go, button);
}

void psxsplash::Lua::OnButtonRelease(GameObject* go, int button) {
    if (!hasEvent(go, EVENT_ON_BUTTON_RELEASE)) return;
    onButtonReleaseMethodWrapper.callMethod(*this, go, button);
}

void psxsplash::Lua::OnDamage(GameObject* target, int amount, GameObject* source) {
    if (!hasEvent(target, EVENT_ON_DAMAGE)) return;
    // PushGameObject(nullptr) pushes nil — handles environmental damage
    // (poison, fall damage, world hazards) with no attacker.
    onDamageMethodWrapper.callMethod(*this, target, amount, source);
}

void psxsplash::Lua::OnUpdate(GameObject* go, int32_t dt12) {
    if (!hasEvent(go, EVENT_ON_UPDATE)) return;
    onUpdateMethodWrapper.callMethod(*this, go, dt12);
}

void psxsplash::Lua::RelocateGameObjects(GameObject** objects, size_t count, intptr_t delta) {
    auto L = m_state;
    for (size_t i = 0; i < count; i++) {
        uint8_t* newPtr = reinterpret_cast<uint8_t*>(objects[i]);
        uint8_t* oldPtr = newPtr - delta;

        // Re-key the main game object table: registry[oldPtr] -> registry[newPtr]
        L.push(oldPtr);
        L.rawGet(LUA_REGISTRYINDEX);
        if (L.isTable(-1)) {
            // Update __cpp_ptr inside the table
            L.push(newPtr);
            L.setField(-2, "__cpp_ptr");
            // Store at new key
            L.push(newPtr);
            L.copy(-2);
            L.rawSet(LUA_REGISTRYINDEX);
            // Remove old key
            L.push(oldPtr);
            L.push();  // nil
            L.rawSet(LUA_REGISTRYINDEX);
        }
        L.pop();

        // Re-key the methods table: registry[oldPtr+1] -> registry[newPtr+1]
        L.push(oldPtr + 1);
        L.rawGet(LUA_REGISTRYINDEX);
        if (L.isTable(-1)) {
            L.push(newPtr + 1);
            L.copy(-2);
            L.rawSet(LUA_REGISTRYINDEX);
            L.push(oldPtr + 1);
            L.push();
            L.rawSet(LUA_REGISTRYINDEX);
        }
        L.pop();
    }
}

// Hot-swap protocol -- see godot-ps1/addons/ps1godot/exporter/LuaHotSwapWatcher.cs
// for the writer side. File layout (little-endian):
//   [4]  magic 'PHSW' = 0x57534850
//   [4]  u32 version
//   [2]  u16 fileIndex
//   [2]  u16 codeLen
//   [N]  u8  code (LuaDecimalRewriter-rewritten source text)
namespace {
constexpr uint32_t kHotSwapMagic = 0x57534850u;  // 'P','H','S','W'
constexpr int      kHotSwapHeaderBytes = 12;

inline uint32_t readU32LE(const uint8_t* p) {
    return (uint32_t)p[0] | ((uint32_t)p[1] << 8) | ((uint32_t)p[2] << 16) | ((uint32_t)p[3] << 24);
}
inline uint16_t readU16LE(const uint8_t* p) {
    return (uint16_t)((uint16_t)p[0] | ((uint16_t)p[1] << 8));
}
}  // namespace

void psxsplash::Lua::TryHotSwap(SceneManager& sm) {
    auto& loader = FileLoader::Get();
    int size = 0;
    uint8_t* buf = loader.LoadFileSync("hotswap.luac", size);
    if (!buf) return;  // file absent -- fast path

    if (size < kHotSwapHeaderBytes) { loader.FreeFile(buf); return; }
    if (readU32LE(buf) != kHotSwapMagic) { loader.FreeFile(buf); return; }

    uint32_t version  = readU32LE(buf + 4);
    uint16_t fileIdx  = readU16LE(buf + 8);
    uint16_t codeLen  = readU16LE(buf + 10);

    // Already applied (or stale leftover from a prior session that
    // outlived the editor). Nothing to do.
    if (version <= m_lastHotSwapVersion) { loader.FreeFile(buf); return; }

    if (fileIdx >= m_bytecodeRefCount) {
        printf("Lua hot-swap: idx %u out of range (have %d) -- re-export the scene\n",
               (unsigned)fileIdx, m_bytecodeRefCount);
        m_lastHotSwapVersion = version;  // suppress repeat warnings
        loader.FreeFile(buf);
        return;
    }
    if (size < kHotSwapHeaderBytes + codeLen) {
        printf("Lua hot-swap: truncated payload (size=%d, want=%d)\n", size, kHotSwapHeaderBytes + codeLen);
        loader.FreeFile(buf);
        return;
    }

    // Copy the code into an owned buffer keyed by file index. The file
    // loader's buffer goes back to the heap; the owned copy stays put
    // for the rest of the scene (or until the next hot-swap of this idx).
    if (m_hotSwapBuffers[fileIdx]) delete[] m_hotSwapBuffers[fileIdx];
    uint8_t* owned = new uint8_t[codeLen];
    for (int i = 0; i < codeLen; i++) owned[i] = buf[kHotSwapHeaderBytes + i];
    m_hotSwapBuffers[fileIdx] = owned;
    m_bytecodeRefs[fileIdx] = { reinterpret_cast<const char*>(owned), (size_t)codeLen };

    loader.FreeFile(buf);

    // Re-load the chunk into the script-environment table so any future
    // RegisterGameObject (and OnTriggerEnterScript / OnTriggerExitScript
    // calls keyed by file index) see the new code. LoadLuaFile rewrites
    // m_bytecodeRefs[idx] back to the same pointer, which is fine -- it's
    // now our owned buffer.
    LoadLuaFile(reinterpret_cast<const char*>(owned), (size_t)codeLen, fileIdx);

    // Walk every GameObject and re-register the ones using this script.
    // RegisterGameObject does NOT fire onCreate (that's
    // FireAllOnCreate's job, called once per scene boot), so the swap
    // refreshes the per-object env without re-running init. Per-object
    // file-level locals are reset; C++-side state (position, rotation,
    // active flag) is untouched.
    int affected = 0;
    size_t objCount = sm.getGameObjectCount();
    for (size_t i = 0; i < objCount; i++) {
        GameObject* go = sm.getGameObject((uint16_t)i);
        if (go && go->luaFileIndex == (int16_t)fileIdx) {
            RegisterGameObject(go);
            affected++;
        }
    }
    m_lastHotSwapVersion = version;
    printf("Lua hot-swap v%u applied: idx %u (%u B) -> %d object(s)\n",
           (unsigned)version, (unsigned)fileIdx, (unsigned)codeLen, affected);
}

void psxsplash::Lua::TryRepl(SceneManager& sm) {
    // UE editor port-plan pick #4 -- editor REPL. The editor dock
    // writes the snippet to `repl.lua` and bumps `repl.ver` with a
    // monotonic byte sequence. We compare the .ver contents to the
    // last-seen bytes (no parse: any monotonic format the editor
    // chooses works), and when they differ load + pcall the .lua.
    //
    // Result printed via printf so PCSX-Redux's debug console shows
    // it -- slice 1 doesn't write back to a response file. PCdrv-
    // only by definition (no .ver file under CD-ROM ISO mode).
    auto& loader = FileLoader::Get();
    int verSize = 0;
    uint8_t* verBuf = loader.LoadFileSync("repl.ver", verSize);
    if (!verBuf) return;
    if (verSize <= 0 || verSize > (int)sizeof(m_lastReplVerBuf)) {
        loader.FreeFile(verBuf);
        return;
    }
    bool changed = (verSize != m_lastReplVerLen);
    if (!changed) {
        for (int i = 0; i < verSize; i++) {
            if (verBuf[i] != m_lastReplVerBuf[i]) { changed = true; break; }
        }
    }
    if (!changed) {
        loader.FreeFile(verBuf);
        return;
    }
    // Update last-seen stamp BEFORE executing -- a buggy snippet that
    // crashes the VM would otherwise re-fire on the next poll.
    for (int i = 0; i < verSize; i++) m_lastReplVerBuf[i] = verBuf[i];
    m_lastReplVerLen = verSize;
    loader.FreeFile(verBuf);

    int srcSize = 0;
    uint8_t* srcBuf = loader.LoadFileSync("repl.lua", srcSize);
    if (!srcBuf) {
        printf("[REPL] version bumped but repl.lua missing\n");
        return;
    }

    auto L = m_state;
    if (L.loadBuffer(reinterpret_cast<const char*>(srcBuf), srcSize, "repl") != LUA_OK) {
        printf("[REPL] load error: %s\n", L.optString(-1, "Unknown error"));
        L.pop();
        loader.FreeFile(srcBuf);
        return;
    }
    loader.FreeFile(srcBuf);

    if (L.pcall(0, 0) != LUA_OK) {
        printf("[REPL] runtime error: %s\n", L.optString(-1, "Unknown error"));
        L.pop();
        return;
    }
    printf("[REPL] OK\n");
}

void psxsplash::Lua::PushGameObject(GameObject* go) {
    auto L = m_state;
    L.push(go);
    L.rawGet(LUA_REGISTRYINDEX);

    if (!L.isTable(-1)) {
        L.pop();
        L.push(); // push nil so the caller always gets a value
    }
}
