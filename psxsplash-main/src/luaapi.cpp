#include "luaapi.hh"
#include "scenemanager.hh"
#include "gameobject.hh"
#include "controls.hh"
#include "camera.hh"
#include "cutscene.hh"
#include "animation.hh"
#include "skinmesh.hh"
#include "streq.hh"
#include "uisystem.hh"

#include <psyqo/soft-math.hh>
#include <psyqo/xprintf.h>
#include <psyqo/trigonometry.hh>
#include <psyqo/fixed-point.hh>
#include "gtemath.hh"


namespace psxsplash {

// Static member
SceneManager* LuaAPI::s_sceneManager = nullptr;
CutscenePlayer* LuaAPI::s_cutscenePlayer = nullptr;
AnimationPlayer* LuaAPI::s_animationPlayer = nullptr;
UISystem* LuaAPI::s_uiSystem = nullptr;

// Scale factor: FixedPoint<12> stores 1.0 as raw 4096.
// Lua scripts work in world-space units (1 = one unit), so we convert.
static constexpr lua_Number kFixedScale = 4096;

// Read a FixedPoint<12> from the stack, accepting either a FixedPoint object
// or a plain integer (which gets scaled by 4096 to become fp12).
static psyqo::FixedPoint<12> readFP(psyqo::Lua& L, int idx) {
    if (L.isFixedPoint(idx)) {
        return L.toFixedPoint(idx);
    }
    return psyqo::FixedPoint<12>(static_cast<int32_t>(L.toNumber(idx) * kFixedScale), psyqo::FixedPoint<12>::RAW);
}

// Angle scale: psyqo::Angle is FixedPoint<10>, so 1.0_pi = raw 1024
static constexpr lua_Number kAngleScale = 1024;
static psyqo::Trig<> s_trig;

// ============================================================================
// REGISTRATION
// ============================================================================

void LuaAPI::RegisterAll(psyqo::Lua& L, SceneManager* scene, CutscenePlayer* cutscenePlayer, AnimationPlayer* animationPlayer, UISystem* uiSystem) {
    s_sceneManager = scene;
    s_cutscenePlayer = cutscenePlayer;
    s_animationPlayer = animationPlayer;
    s_uiSystem = uiSystem;
    
    // ========================================================================
    // ENTITY API
    // ========================================================================
    L.newTable();  // Entity table
    
    L.push(Entity_FindByScriptIndex);
    L.setField(-2, "FindByScriptIndex");
    
    L.push(Entity_FindByIndex);
    L.setField(-2, "FindByIndex");
    
    L.push(Entity_Find);
    L.setField(-2, "Find");
    
    L.push(Entity_GetCount);
    L.setField(-2, "GetCount");
    
    L.push(Entity_SetActive);
    L.setField(-2, "SetActive");
    
    L.push(Entity_IsActive);
    L.setField(-2, "IsActive");
    
    L.push(Entity_GetPosition);
    L.setField(-2, "GetPosition");
    
    L.push(Entity_SetPosition);
    L.setField(-2, "SetPosition");
    
    L.push(Entity_GetRotationY);
    L.setField(-2, "GetRotationY");
    
    L.push(Entity_SetRotationY);
    L.setField(-2, "SetRotationY");
    
    L.push(Entity_ForEach);
    L.setField(-2, "ForEach");

    L.push(Entity_GetTag);
    L.setField(-2, "GetTag");

    L.push(Entity_SetTag);
    L.setField(-2, "SetTag");

    L.push(Entity_FindByTag);
    L.setField(-2, "FindByTag");

    L.push(Entity_Spawn);
    L.setField(-2, "Spawn");

    L.push(Entity_Destroy);
    L.setField(-2, "Destroy");

    L.push(Entity_FindNearest);
    L.setField(-2, "FindNearest");

    L.setGlobal("Entity");
    
    // ========================================================================
    // VEC3 API
    // ========================================================================
    L.newTable();  // Vec3 table
    
    L.push(Vec3_New);
    L.setField(-2, "new");
    
    L.push(Vec3_Add);
    L.setField(-2, "add");
    
    L.push(Vec3_Sub);
    L.setField(-2, "sub");
    
    L.push(Vec3_Mul);
    L.setField(-2, "mul");
    
    L.push(Vec3_Dot);
    L.setField(-2, "dot");
    
    L.push(Vec3_Cross);
    L.setField(-2, "cross");
    
    L.push(Vec3_Length);
    L.setField(-2, "length");
    
    L.push(Vec3_LengthSq);
    L.setField(-2, "lengthSq");
    
    L.push(Vec3_Normalize);
    L.setField(-2, "normalize");
    
    L.push(Vec3_Distance);
    L.setField(-2, "distance");
    
    L.push(Vec3_DistanceSq);
    L.setField(-2, "distanceSq");
    
    L.push(Vec3_Lerp);
    L.setField(-2, "lerp");
    
    L.setGlobal("Vec3");
    
    // ========================================================================
    // INPUT API
    // ========================================================================
    L.newTable();  // Input table
    
    L.push(Input_IsPressed);
    L.setField(-2, "IsPressed");
    
    L.push(Input_IsReleased);
    L.setField(-2, "IsReleased");
    
    L.push(Input_IsHeld);
    L.setField(-2, "IsHeld");
    
    L.push(Input_GetAnalog);
    L.setField(-2, "GetAnalog");
    
    // Register button constants
    RegisterInputConstants(L);
    
    L.setGlobal("Input");
    
    // ========================================================================
    // TIMER API
    // ========================================================================
    L.newTable();  // Timer table

    L.push(Timer_GetFrameCount);
    L.setField(-2, "GetFrameCount");

    L.setGlobal("Timer");

    // ========================================================================
    // GAMESTATE API
    // ========================================================================
    L.newTable();

    L.push(GameState_Frame);
    L.setField(-2, "Frame");

    L.push(GameState_GetMode);
    L.setField(-2, "GetMode");

    L.push(GameState_SetMode);
    L.setField(-2, "SetMode");

    L.push(GameState_IsMode);
    L.setField(-2, "IsMode");

    L.push(GameState_GetChunk);
    L.setField(-2, "GetChunk");

    L.push(GameState_SetChunk);
    L.setField(-2, "SetChunk");

    L.push(GameState_IsChunk);
    L.setField(-2, "IsChunk");

    L.setGlobal("GameState");
    
    // ========================================================================
    // CAMERA API
    // ========================================================================
    L.newTable();  // Camera table
    
    L.push(Camera_GetPosition);
    L.setField(-2, "GetPosition");
    
    L.push(Camera_SetPosition);
    L.setField(-2, "SetPosition");
    
    L.push(Camera_GetRotation);
    L.setField(-2, "GetRotation");
    
    L.push(Camera_SetRotation);
    L.setField(-2, "SetRotation");

    L.push(Camera_GetForward);
    L.setField(-2, "GetForward");

    L.push(Camera_MoveForward);
    L.setField(-2, "MoveForward");

    L.push(Camera_MoveBackward);
    L.setField(-2, "MoveBackward");

    L.push(Camera_MoveLeft);
    L.setField(-2, "MoveLeft");

    L.push(Camera_MoveRight);
    L.setField(-2, "MoveRight");
    
    L.push(Camera_FollowPsxPlayer);
    L.setField(-2, "FollowPsxPlayer");

    L.push(Camera_SetMode);
    L.setField(-2, "SetMode");

    L.push(Camera_LookAt);
    L.setField(-2, "LookAt");

    L.push(Camera_GetH);
    L.setField(-2, "GetH");

    L.push(Camera_SetH);
    L.setField(-2, "SetH");

    L.push(Camera_Shake);
    L.setField(-2, "Shake");

    L.push(Camera_ShakeRaw);
    L.setField(-2, "ShakeRaw");

    L.setGlobal("Camera");
    
    // ========================================================================
    // AUDIO API (Placeholder)
    // ========================================================================
    L.newTable();  // Audio table
    
    L.push(Audio_Play);
    L.setField(-2, "Play");
    
    L.push(Audio_Find);
    L.setField(-2, "Find");
    
    L.push(Audio_Stop);
    L.setField(-2, "Stop");
    
    L.push(Audio_SetVolume);
    L.setField(-2, "SetVolume");
    
    L.push(Audio_StopAll);
    L.setField(-2, "StopAll");

    L.push(Audio_GetClipDuration);
    L.setField(-2, "GetClipDuration");

    // v25 routing-aware shortcuts
    L.push(Audio_PlaySfx);
    L.setField(-2, "PlaySfx");

    L.push(Audio_PlayMusic);
    L.setField(-2, "PlayMusic");

    L.push(Audio_StopMusic);
    L.setField(-2, "StopMusic");

    L.push(Audio_PlayCDDA);
    L.setField(-2, "PlayCDDA");

    L.push(Audio_ResumeCDDA);
    L.setField(-2, "ResumeCDDA");

    L.push(Audio_PauseCDDA);
    L.setField(-2, "PauseCDDA");

    L.push(Audio_StopCDDA);
    L.setField(-2, "StopCDDA");

    L.push(Audio_TellCDDA);
    L.setField(-2, "TellCDDA");

    L.push(Audio_SetCDDAVolume);
    L.setField(-2, "SetCDDAVolume");

    L.setGlobal("Audio");

    // ========================================================================
    // MUSIC API
    // ========================================================================
    L.newTable();

    L.push(Music_Play);
    L.setField(-2, "Play");

    L.push(Music_Stop);
    L.setField(-2, "Stop");

    L.push(Music_IsPlaying);
    L.setField(-2, "IsPlaying");

    L.push(Music_SetVolume);
    L.setField(-2, "SetVolume");

    L.push(Music_GetBeat);
    L.setField(-2, "GetBeat");

    L.push(Music_Find);
    L.setField(-2, "Find");

    L.push(Music_GetLastMarkerHash);
    L.setField(-2, "GetLastMarkerHash");

    L.push(Music_MarkerHash);
    L.setField(-2, "MarkerHash");

    L.setGlobal("Music");

    // ========================================================================
    // SOUND API (Phase 5 Stage A — stubs)
    // ========================================================================
    L.newTable();

    L.push(Sound_PlayMacro);
    L.setField(-2, "PlayMacro");

    L.push(Sound_PlayFamily);
    L.setField(-2, "PlayFamily");

    L.push(Sound_StopAll);
    L.setField(-2, "StopAll");

    L.setGlobal("Sound");

    // ========================================================================
    // DEBUG API
    // ========================================================================
    L.newTable();  // Debug table
    
    L.push(Debug_Log);
    L.setField(-2, "Log");
    
    L.push(Debug_DrawLine);
    L.setField(-2, "DrawLine");
    
    L.push(Debug_DrawBox);
    L.setField(-2, "DrawBox");
    
    L.setGlobal("Debug");
    
    // ========================================================================
    // CONVERT API
    // ========================================================================
    L.newTable();  // Convert table
    
    L.push(Convert_IntToFp);
    L.setField(-2, "IntToFp");

    L.push(Convert_FpToInt);
    L.setField(-2, "FpToInt");
    
    L.setGlobal("Convert");

    // ========================================================================
    // MATH API
    // ========================================================================
    L.newTable();  // PSXMath table (avoid conflict with Lua's math)
    
    L.push(Math_Clamp);
    L.setField(-2, "Clamp");
    
    L.push(Math_Lerp);
    L.setField(-2, "Lerp");
    
    L.push(Math_Sign);
    L.setField(-2, "Sign");
    
    L.push(Math_Abs);
    L.setField(-2, "Abs");
    
    L.push(Math_Min);
    L.setField(-2, "Min");
    
    L.push(Math_Max);
    L.setField(-2, "Max");

    L.push(Math_Floor);
    L.setField(-2, "Floor");

    L.push(Math_Ceil);
    L.setField(-2, "Ceil");

    L.push(Math_Round);
    L.setField(-2, "Round");

    L.push(Math_ToInt);
    L.setField(-2, "ToInt");

    L.push(Math_ToFixed);
    L.setField(-2, "ToFixed");

    L.setGlobal("PSXMath");
    
    // ========================================================================
    // RANDOM API
    // ========================================================================

    L.newTable();  // Random table
    
    L.push(Random_Number);
    L.setField(-2, "Number");
    
    L.push(Random_GeneratorNumber);
    L.setField(-2, "GeneratorNumber");

    L.push(Random_Range);
    L.setField(-2, "Range");

    L.push(Random_GeneratorRange);
    L.setField(-2, "GeneratorRange");
    
    L.push(Random_GeneratorSeed);
    L.setField(-2, "GeneratorSeed");
    
    L.setGlobal("Random");

    // ========================================================================
    // SCENE API
    // ========================================================================
    L.newTable();  // Scene table
    
    L.push(Scene_Load);
    L.setField(-2, "Load");
    
    L.push(Scene_GetIndex);
    L.setField(-2, "GetIndex");

    L.push(Scene_PauseFor);
    L.setField(-2, "PauseFor");

    L.setGlobal("Scene");
    
    // ========================================================================
    // PERSIST API
    // ========================================================================
    L.newTable();  // Persist table
    
    L.push(Persist_Get);
    L.setField(-2, "Get");
    
    L.push(Persist_Set);
    L.setField(-2, "Set");
    
    L.setGlobal("Persist");
    
    // ========================================================================
    // CUTSCENE API
    // ========================================================================
    L.newTable();  // Cutscene table
    
    L.push(Cutscene_Play);
    L.setField(-2, "Play");
    
    L.push(Cutscene_Stop);
    L.setField(-2, "Stop");
    
    L.push(Cutscene_IsPlaying);
    L.setField(-2, "IsPlaying");
    
    L.setGlobal("Cutscene");

    // ========================================================================
    // ANIMATION API
    // ========================================================================
    L.newTable();

    L.push(Animation_Play);
    L.setField(-2, "Play");

    L.push(Animation_Stop);
    L.setField(-2, "Stop");

    L.push(Animation_IsPlaying);
    L.setField(-2, "IsPlaying");

    L.setGlobal("Animation");

    // ========================================================================
    // SKINNED ANIMATION API
    // ========================================================================
    L.newTable();

    L.push(SkinnedAnim_Play);
    L.setField(-2, "Play");

    L.push(SkinnedAnim_Stop);
    L.setField(-2, "Stop");

    L.push(SkinnedAnim_IsPlaying);
    L.setField(-2, "IsPlaying");

    L.push(SkinnedAnim_GetClip);
    L.setField(-2, "GetClip");

    L.push(SkinnedAnim_BindPose);
    L.setField(-2, "BindPose");

    L.setGlobal("SkinnedAnim");

    // ========================================================================
    // PHYSICS API
    // ========================================================================
    L.newTable();

    L.push(Physics_Raycast);
    L.setField(-2, "Raycast");

    L.push(Physics_OverlapBox);
    L.setField(-2, "OverlapBox");

    L.setGlobal("Physics");

    // ========================================================================
    // CONTROLS API
    // ========================================================================
    L.newTable();

    L.push(Controls_SetEnabled);
    L.setField(-2, "SetEnabled");

    L.push(Controls_IsEnabled);
    L.setField(-2, "IsEnabled");

    L.setGlobal("Controls");

    // ========================================================================
    // INTERACT API
    // ========================================================================
    L.newTable();

    L.push(Interact_SetEnabled);
    L.setField(-2, "SetEnabled");

    L.push(Interact_IsEnabled);
    L.setField(-2, "IsEnabled");

    L.setGlobal("Interact");

    // ========================================================================
    // UI API
    // ========================================================================
    L.newTable();  // UI table
    
    L.push(UI_FindCanvas);
    L.setField(-2, "FindCanvas");
    
    L.push(UI_SetCanvasVisible);
    L.setField(-2, "SetCanvasVisible");
    
    L.push(UI_IsCanvasVisible);
    L.setField(-2, "IsCanvasVisible");
    
    L.push(UI_FindElement);
    L.setField(-2, "FindElement");
    
    L.push(UI_SetVisible);
    L.setField(-2, "SetVisible");
    
    L.push(UI_IsVisible);
    L.setField(-2, "IsVisible");
    
    L.push(UI_SetText);
    L.setField(-2, "SetText");

    L.push(UI_GetText);
    L.setField(-2, "GetText");
    
    L.push(UI_SetProgress);
    L.setField(-2, "SetProgress");
    
    L.push(UI_GetProgress);
    L.setField(-2, "GetProgress");
    
    L.push(UI_SetColor);
    L.setField(-2, "SetColor");

    L.push(UI_GetColor);
    L.setField(-2, "GetColor");
    
    L.push(UI_SetPosition);
    L.setField(-2, "SetPosition");

    L.push(UI_GetPosition);
    L.setField(-2, "GetPosition");

    L.push(UI_SetSize);
    L.setField(-2, "SetSize");

    L.push(UI_GetSize);
    L.setField(-2, "GetSize");

    L.push(UI_SetImageUV);
    L.setField(-2, "SetImageUV");

    L.push(UI_GetImageUV);
    L.setField(-2, "GetImageUV");

    L.push(UI_SetProgressColors);
    L.setField(-2, "SetProgressColors");

    L.push(UI_GetElementType);
    L.setField(-2, "GetElementType");

    L.push(UI_GetElementCount);
    L.setField(-2, "GetElementCount");

    L.push(UI_GetElementByIndex);
    L.setField(-2, "GetElementByIndex");

    L.push(UI_SetModelVisible);
    L.setField(-2, "SetModelVisible");

    L.push(UI_SetModelOrbit);
    L.setField(-2, "SetModelOrbit");

    L.push(UI_SetModel);
    L.setField(-2, "SetModel");

    L.setGlobal("UI");

    // ========================================================================
    // PLAYER API
    // ========================================================================

    L.newTable();  
    
    L.push(Player_SetPosition);
    L.setField(-2, "SetPosition");

    L.push(Player_GetPosition);
    L.setField(-2, "GetPosition");

    L.push(Player_SetRotation);
    L.setField(-2, "SetRotation");

    L.push(Player_GetRotation);
    L.setField(-2, "GetRotation");

    L.setGlobal("Player");
}

// ============================================================================
// ENTITY API IMPLEMENTATION
// ============================================================================

int LuaAPI::Entity_FindByScriptIndex(lua_State* L) {
    psyqo::Lua lua(L);
    
    if (!s_sceneManager || !lua.isNumber(1)) {
        lua.push();
        return 1;
    }
    
    // Find first object with matching luaFileIndex
    int16_t luaIdx = static_cast<int16_t>(lua.toNumber(1));
    for (size_t i = 0; i < s_sceneManager->getGameObjectCount(); i++) {
        auto* go = s_sceneManager->getGameObject(static_cast<uint16_t>(i));
        if (go && go->luaFileIndex == luaIdx) {
            lua.push(reinterpret_cast<uint8_t*>(go));
            lua.rawGet(LUA_REGISTRYINDEX);
            if (lua.isTable(-1)) return 1;
            lua.pop();
        }
    }
    
    lua.push();
    return 1;
}

int LuaAPI::Entity_FindByIndex(lua_State* L) {
    psyqo::Lua lua(L);
    
    if (!lua.isNumber(1)) {
        lua.push();
        return 1;
    }
    
    int index = static_cast<int>(lua.toNumber(1));
    
    if (s_sceneManager) {
        GameObject* go = s_sceneManager->getGameObject(static_cast<uint16_t>(index));
        if (go) {
            lua.push(reinterpret_cast<uint8_t*>(go));
            lua.rawGet(LUA_REGISTRYINDEX);
            if (lua.isTable(-1)) {
                return 1;
            }
            lua.pop();
        }
    }
    
    lua.push();
    return 1;
}

int LuaAPI::Entity_Find(lua_State* L) {
    psyqo::Lua lua(L);

    if (!s_sceneManager) {
        lua.push();
        return 1;
    }

    // Accept number (index) or string (name lookup) for backwards compat
    // Check isNumber FIRST — in Lua, numbers pass isString too.
    if (lua.isNumber(1)) {
        int index = static_cast<int>(lua.toNumber(1));
        GameObject* go = s_sceneManager->getGameObject(static_cast<uint16_t>(index));
        if (go) {
            lua.push(reinterpret_cast<uint8_t*>(go));
            lua.rawGet(LUA_REGISTRYINDEX);
            if (lua.isTable(-1)) return 1;
            lua.pop();
        }
    } else if (lua.isString(1)) {
        const char* name = lua.toString(1);
        GameObject* go = s_sceneManager->findObjectByName(name);
        if (go) {
            lua.push(reinterpret_cast<uint8_t*>(go));
            lua.rawGet(LUA_REGISTRYINDEX);
            if (lua.isTable(-1)) return 1;
            lua.pop();
        }
    }

    lua.push();
    return 1;
}

int LuaAPI::Entity_GetCount(lua_State* L) {
    psyqo::Lua lua(L);
    
    if (s_sceneManager) {
        lua.pushNumber(static_cast<lua_Number>(s_sceneManager->getGameObjectCount()));
    } else {
        lua.pushNumber(0);
    }
    return 1;
}


int LuaAPI::Entity_SetActive(lua_State* L) {
    psyqo::Lua lua(L);
    
    if (!lua.isTable(1)) {
        return 0;
    }
    
    lua.getField(1, "__cpp_ptr");
    auto go = lua.toUserdata<GameObject>(-1);
    lua.pop();
    
    bool active = lua.toBoolean(2);
    
    if (go && s_sceneManager) {
        s_sceneManager->setObjectActive(go, active);
    }
    
    return 0;
}

int LuaAPI::Entity_IsActive(lua_State* L) {
    psyqo::Lua lua(L);
    
    if (!lua.isTable(1)) {
        lua.push(false);
        return 1;
    }
    
    lua.getField(1, "__cpp_ptr");
    auto go = lua.toUserdata<GameObject>(-1);
    lua.pop();
    
    if (go) {
        lua.push(go->isActive());
    } else {
        lua.push(false);
    }
    
    return 1;
}

int LuaAPI::Entity_GetPosition(lua_State* L) {
    psyqo::Lua lua(L);
    
    if (!lua.isTable(1)) {
        lua.push();
        return 1;
    }
    
    lua.getField(1, "__cpp_ptr");
    auto go = lua.toUserdata<GameObject>(-1);
    lua.pop();
    
    if (go) {
        PushVec3(lua, go->position.x, go->position.y, go->position.z);
        return 1;
    }
    
    lua.push();
    return 1;
}

int LuaAPI::Entity_SetPosition(lua_State* L) {
    psyqo::Lua lua(L);
    
    if (!lua.isTable(1) || !lua.isTable(2)) {
        return 0;
    }
    
    lua.getField(1, "__cpp_ptr");
    auto go = lua.toUserdata<GameObject>(-1);
    lua.pop();
    
    if (!go) return 0;
    
    psyqo::FixedPoint<12> x, y, z;
    ReadVec3(lua, 2, x, y, z);
    
    // Compute position delta to shift the world-space AABB
    int32_t dx = x.value - go->position.x.value;
    int32_t dy = y.value - go->position.y.value;
    int32_t dz = z.value - go->position.z.value;
    
    go->position.x = x;
    go->position.y = y;
    go->position.z = z;
    
    // Shift AABB by the position delta so frustum culling uses correct bounds
    go->aabbMinX += dx; go->aabbMaxX += dx;
    go->aabbMinY += dy; go->aabbMaxY += dy;
    go->aabbMinZ += dz; go->aabbMaxZ += dz;
    
    // Mark as dynamically moved so the renderer knows to bypass BVH for this object
    go->setDynamicMoved(true);
    
    return 0;
}

int LuaAPI::Entity_GetRotationY(lua_State* L) {
    psyqo::Lua lua(L);
    
    if (!lua.isTable(1)) {
        lua.pushNumber(0);
        return 1;
    }
    
    lua.getField(1, "__cpp_ptr");
    auto go = lua.toUserdata<GameObject>(-1);
    lua.pop();
    
    if (!go) { lua.pushNumber(0); return 1; }
    
    // Y rotation matrix: vs[0].x = cos(θ), vs[0].z = sin(θ)
    int32_t sinRaw = go->rotation.vs[0].z.raw();
    int32_t cosRaw = go->rotation.vs[0].x.raw();
    
    // Fast atan2 approximation (linear in first octant, fold to full circle)
    psyqo::Angle angle;
    if (cosRaw == 0 && sinRaw == 0) {
        angle.value = 0;
    } else {
        int32_t abs_s = sinRaw < 0 ? -sinRaw : sinRaw;
        int32_t abs_c = cosRaw < 0 ? -cosRaw : cosRaw;
        int32_t minV = abs_s < abs_c ? abs_s : abs_c;
        int32_t maxV = abs_s > abs_c ? abs_s : abs_c;
        int32_t a = (minV * 256) / maxV;  // [0, 256] for [0, π/4]
        if (abs_s > abs_c) a = 512 - a;
        if (cosRaw < 0) a = 1024 - a;
        if (sinRaw < 0) a = -a;
        angle.value = a;
    }
    
    // Return as FixedPoint<12> (Angle is FixedPoint<10>, shift left 2 for fp12)
    psyqo::FixedPoint<12> fp12;
    fp12.value = angle.value << 2;
    lua.push(fp12);
    return 1;
}

int LuaAPI::Entity_SetRotationY(lua_State* L) {
    psyqo::Lua lua(L);

    if (!lua.isTable(1)) return 0;

    lua.getField(1, "__cpp_ptr");
    auto go = lua.toUserdata<GameObject>(-1);
    lua.pop();

    if (!go) return 0;

    // Accept FixedPoint or number, convert to Angle (FixedPoint<10>)
    psyqo::FixedPoint<12> fp12 = readFP(lua, 2);
    psyqo::Angle angle;
    angle.value = fp12.value >> 2;
    go->rotation = psxsplash::transposeMatrix33(
        psyqo::SoftMath::generateRotationMatrix33(angle, psyqo::SoftMath::Axis::Y, s_trig));
    return 0;
}

int LuaAPI::Entity_ForEach(lua_State* L) {
    psyqo::Lua lua(L);

    if (!s_sceneManager || !lua.isFunction(1)) return 0;

    size_t count = s_sceneManager->getGameObjectCount();
    for (size_t i = 0; i < count; i++) {
        auto* go = s_sceneManager->getGameObject(static_cast<uint16_t>(i));
        if (!go || !go->isActive()) continue;

        // Push callback copy
        lua.copy(1);
        // Look up registered Lua table for this object (keyed by C++ pointer)
        lua.push(reinterpret_cast<uint8_t*>(go));
        lua.rawGet(LUA_REGISTRYINDEX);
        if (!lua.isTable(-1)) {
            lua.pop(2);  // pop non-table + callback copy
            continue;
        }
        lua.pushNumber(i);  // push index as second argument
        if (lua.pcall(2, 0) != LUA_OK) {
            lua.pop();  // pop error message
        }
    }

    return 0;
}

// Shared helper: given a GameObject*, push the Lua table that was registered
// for it at scene init (keyed by the C++ pointer in LUA_REGISTRYINDEX). On
// failure, pushes nil. Always leaves exactly one value on the stack.
static void PushGameObjectHandle(psyqo::Lua& lua, psxsplash::GameObject* go) {
    if (!go) {
        lua.push();
        return;
    }
    lua.push(reinterpret_cast<uint8_t*>(go));
    lua.rawGet(LUA_REGISTRYINDEX);
    if (!lua.isTable(-1)) {
        lua.pop();
        lua.push();
    }
}

int LuaAPI::Entity_GetTag(lua_State* L) {
    psyqo::Lua lua(L);
    if (!lua.isTable(1)) { lua.pushNumber(0); return 1; }
    lua.getField(1, "__cpp_ptr");
    auto go = lua.toUserdata<GameObject>(-1);
    lua.pop();
    lua.pushNumber(go ? (lua_Number)go->tag : 0);
    return 1;
}

int LuaAPI::Entity_SetTag(lua_State* L) {
    psyqo::Lua lua(L);
    if (!lua.isTable(1) || !lua.isNumber(2)) return 0;
    lua.getField(1, "__cpp_ptr");
    auto go = lua.toUserdata<GameObject>(-1);
    lua.pop();
    if (go) go->tag = (uint16_t)lua.toNumber(2);
    return 0;
}

int LuaAPI::Entity_FindByTag(lua_State* L) {
    psyqo::Lua lua(L);
    if (!s_sceneManager || !lua.isNumber(1)) { lua.push(); return 1; }
    uint16_t tag = (uint16_t)lua.toNumber(1);
    // Tag 0 is the "untagged" sentinel — every default object has tag 0, so
    // FindByTag(0) would return arbitrary unrelated geometry. Reject it.
    if (tag == 0) { lua.push(); return 1; }
    size_t count = s_sceneManager->getGameObjectCount();
    for (size_t i = 0; i < count; i++) {
        auto* go = s_sceneManager->getGameObject((uint16_t)i);
        if (go && go->isActive() && go->tag == tag) {
            PushGameObjectHandle(lua, go);
            return 1;
        }
    }
    lua.push();
    return 1;
}

int LuaAPI::Entity_Spawn(lua_State* L) {
    psyqo::Lua lua(L);
    if (!s_sceneManager || !lua.isNumber(1) || !lua.isTable(2)) {
        lua.push();
        return 1;
    }
    uint16_t tag = (uint16_t)lua.toNumber(1);
    // Tag 0 is "untagged" — Spawn(0, ...) would activate any random inactive
    // object the author placed, including non-template geometry. Reject.
    if (tag == 0) { lua.push(); return 1; }

    psyqo::Vec3 pos;
    ReadVec3(lua, 2, pos.x, pos.y, pos.z);

    // Optional rotY in Lua's fp12 "pi fraction" convention (see
    // Entity_SetRotationY — 1.0 = π radians = 180°).
    bool hasRot = !lua.isNoneOrNil(3);
    psyqo::Angle rotAngle;
    if (hasRot) {
        psyqo::FixedPoint<12> fp12 = readFP(lua, 3);
        rotAngle.value = fp12.value >> 2;
    }

    // Scan for first inactive pool instance with matching tag.
    size_t count = s_sceneManager->getGameObjectCount();
    for (size_t i = 0; i < count; i++) {
        auto* go = s_sceneManager->getGameObject((uint16_t)i);
        if (!go) continue;
        if (go->tag != tag) continue;
        if (go->isActive()) continue;

        go->position = pos;
        if (hasRot) {
            go->rotation = psxsplash::transposeMatrix33(
                psyqo::SoftMath::generateRotationMatrix33(
                    rotAngle, psyqo::SoftMath::Axis::Y, s_trig));
        }
        // Mark BVH position as stale so culling/collision re-evaluate.
        go->setDynamicMoved(true);
        // Activate; scene manager fires onEnable on the attached script.
        s_sceneManager->setObjectActive(go, true);

        PushGameObjectHandle(lua, go);
        return 1;
    }

    // Pool exhausted.
    lua.push();
    return 1;
}

int LuaAPI::Entity_Destroy(lua_State* L) {
    psyqo::Lua lua(L);
    if (!lua.isTable(1)) return 0;
    lua.getField(1, "__cpp_ptr");
    auto go = lua.toUserdata<GameObject>(-1);
    lua.pop();
    if (go && s_sceneManager) {
        s_sceneManager->setObjectActive(go, false);
    }
    return 0;
}

int LuaAPI::Entity_FindNearest(lua_State* L) {
    psyqo::Lua lua(L);
    if (!s_sceneManager || !lua.isTable(1) || !lua.isNumber(2)) {
        lua.push();
        return 1;
    }
    psyqo::Vec3 pos;
    ReadVec3(lua, 1, pos.x, pos.y, pos.z);
    uint16_t tag = (uint16_t)lua.toNumber(2);
    if (tag == 0) { lua.push(); return 1; }

    GameObject* best = nullptr;
    // Cast a single 32-bit operand to int64 per multiply so the codegen stays
    // inside MIPS's native 32×32→64 `mult` (same pattern as camera.cpp's
    // frustum-extract). int64*int64 would pull in __muldi3, which isn't linked.
    int64_t bestDistSq = INT64_MAX;
    size_t count = s_sceneManager->getGameObjectCount();
    for (size_t i = 0; i < count; i++) {
        auto* go = s_sceneManager->getGameObject((uint16_t)i);
        if (!go || !go->isActive() || go->tag != tag) continue;

        int32_t dx = go->position.x.value - pos.x.value;
        int32_t dy = go->position.y.value - pos.y.value;
        int32_t dz = go->position.z.value - pos.z.value;
        int64_t distSq = (int64_t)dx * dx + (int64_t)dy * dy + (int64_t)dz * dz;
        if (distSq < bestDistSq) {
            bestDistSq = distSq;
            best = go;
        }
    }
    PushGameObjectHandle(lua, best);
    return 1;
}

// ============================================================================
// VEC3 API IMPLEMENTATION
// ============================================================================

void LuaAPI::PushVec3(psyqo::Lua& L, psyqo::FixedPoint<12> x,
                      psyqo::FixedPoint<12> y, psyqo::FixedPoint<12> z) {
    L.newTable();
    L.push(x);
    L.setField(-2, "x");
    L.push(y);
    L.setField(-2, "y");
    L.push(z);
    L.setField(-2, "z");
}

void LuaAPI::ReadVec3(psyqo::Lua& L, int idx,
                      psyqo::FixedPoint<12>& x,
                      psyqo::FixedPoint<12>& y,
                      psyqo::FixedPoint<12>& z) {
    L.getField(idx, "x");
    x = readFP(L, -1);
    L.pop();

    L.getField(idx, "y");
    y = readFP(L, -1);
    L.pop();

    L.getField(idx, "z");
    z = readFP(L, -1);
    L.pop();
}

int LuaAPI::Vec3_New(lua_State* L) {
    psyqo::Lua lua(L);

    psyqo::FixedPoint<12> x = lua.isNoneOrNil(1) ? psyqo::FixedPoint<12>() : readFP(lua, 1);
    psyqo::FixedPoint<12> y = lua.isNoneOrNil(2) ? psyqo::FixedPoint<12>() : readFP(lua, 2);
    psyqo::FixedPoint<12> z = lua.isNoneOrNil(3) ? psyqo::FixedPoint<12>() : readFP(lua, 3);

    PushVec3(lua, x, y, z);
    return 1;
}

int LuaAPI::Vec3_Add(lua_State* L) {
    psyqo::Lua lua(L);
    
    if (!lua.isTable(1) || !lua.isTable(2)) {
        lua.push();
        return 1;
    }
    
    psyqo::FixedPoint<12> ax, ay, az;
    psyqo::FixedPoint<12> bx, by, bz;
    
    ReadVec3(lua, 1, ax, ay, az);
    ReadVec3(lua, 2, bx, by, bz);
    
    PushVec3(lua, ax + bx, ay + by, az + bz);
    return 1;
}

int LuaAPI::Vec3_Sub(lua_State* L) {
    psyqo::Lua lua(L);
    
    if (!lua.isTable(1) || !lua.isTable(2)) {
        lua.push();
        return 1;
    }
    
    psyqo::FixedPoint<12> ax, ay, az;
    psyqo::FixedPoint<12> bx, by, bz;
    
    ReadVec3(lua, 1, ax, ay, az);
    ReadVec3(lua, 2, bx, by, bz);
    
    PushVec3(lua, ax - bx, ay - by, az - bz);
    return 1;
}

int LuaAPI::Vec3_Mul(lua_State* L) {
    psyqo::Lua lua(L);
    
    if (!lua.isTable(1)) {
        lua.push();
        return 1;
    }
    
    psyqo::FixedPoint<12> x, y, z;
    ReadVec3(lua, 1, x, y, z);
    
    psyqo::FixedPoint<12> scalar = readFP(lua, 2);
    
    PushVec3(lua, x * scalar, y * scalar, z * scalar);
    return 1;
}

int LuaAPI::Vec3_Dot(lua_State* L) {
    psyqo::Lua lua(L);
    
    if (!lua.isTable(1) || !lua.isTable(2)) {
        lua.pushNumber(0);
        return 1;
    }
    
    psyqo::FixedPoint<12> ax, ay, az;
    psyqo::FixedPoint<12> bx, by, bz;
    
    ReadVec3(lua, 1, ax, ay, az);
    ReadVec3(lua, 2, bx, by, bz);
    
    auto dot = ax * bx + ay * by + az * bz;
    lua.push(dot);
    return 1;
}

int LuaAPI::Vec3_Cross(lua_State* L) {
    psyqo::Lua lua(L);
    
    if (!lua.isTable(1) || !lua.isTable(2)) {
        lua.push();
        return 1;
    }
    
    psyqo::FixedPoint<12> ax, ay, az;
    psyqo::FixedPoint<12> bx, by, bz;
    
    ReadVec3(lua, 1, ax, ay, az);
    ReadVec3(lua, 2, bx, by, bz);
    
    psyqo::FixedPoint<12> cx = ay * bz - az * by;
    psyqo::FixedPoint<12> cy = az * bx - ax * bz;
    psyqo::FixedPoint<12> cz = ax * by - ay * bx;
    
    PushVec3(lua, cx, cy, cz);
    return 1;
}

int LuaAPI::Vec3_LengthSq(lua_State* L) {
    psyqo::Lua lua(L);
    
    if (!lua.isTable(1)) {
        lua.pushNumber(0);
        return 1;
    }
    
    psyqo::FixedPoint<12> x, y, z;
    ReadVec3(lua, 1, x, y, z);
    
    auto lengthSq = x * x + y * y + z * z;
    lua.push(lengthSq);
    return 1;
}

int LuaAPI::Vec3_Length(lua_State* L) {
    psyqo::Lua lua(L);
    
    if (!lua.isTable(1)) {
        lua.pushNumber(0);
        return 1;
    }
    
    psyqo::FixedPoint<12> x, y, z;
    ReadVec3(lua, 1, x, y, z);
    
    // lengthSq in fp12: (x*x + y*y + z*z) is fp24 (two fp12 multiplied).
    // We need sqrt(lengthSq) as fp12.
    // lengthSq raw = sum of (raw*raw >> 12) values = fp12 result
    auto lengthSq = x * x + y * y + z * z;
    int32_t lsRaw = lengthSq.raw();

    if (lsRaw <= 0) {
        lua.push(psyqo::FixedPoint<12>());
        return 1;
    }

    // Integer sqrt of (lsRaw << 12) to get result in fp12
    // sqrt(fp12_value) = sqrt(raw/4096) = sqrt(raw)/64
    // So: result_raw = isqrt(raw * 4096) = isqrt(raw << 12)
    // isqrt(lsRaw) gives integer sqrt. Multiply by 64 (sqrt(4096)) to get fp12.
    // Newton's method in 32-bit: isqrt(n)
    uint32_t n = (uint32_t)lsRaw;
    uint32_t guess = n;
    for (int i = 0; i < 16; i++) {
        if (guess == 0) break;
        guess = (guess + n / guess) / 2;
    }
    // guess = isqrt(lsRaw). lsRaw is in fp12, so sqrt needs * sqrt(4096) = 64
    psyqo::FixedPoint<12> result;
    result.value = (int32_t)(guess * 64);
    lua.push(result);
    return 1;
}

int LuaAPI::Vec3_Normalize(lua_State* L) {
    psyqo::Lua lua(L);
    
    if (!lua.isTable(1)) {
        lua.push();
        return 1;
    }
    
    psyqo::FixedPoint<12> x, y, z;
    ReadVec3(lua, 1, x, y, z);
    
    auto lengthSq = x * x + y * y + z * z;
    int32_t lsRaw = lengthSq.raw();

    if (lsRaw <= 0) {
        PushVec3(lua, psyqo::FixedPoint<12>(), psyqo::FixedPoint<12>(), psyqo::FixedPoint<12>());
        return 1;
    }

    // isqrt(lsRaw) * 64 = length in fp12
    uint32_t n = (uint32_t)lsRaw;
    uint32_t guess = n;
    for (int i = 0; i < 16; i++) {
        if (guess == 0) break;
        guess = (guess + n / guess) / 2;
    }
    int32_t len = (int32_t)(guess * 64);
    if (len == 0) len = 1;

    // Divide each component by length: component / length in fp12
    // (x.raw * 4096) / len using 32-bit math (safe since raw values fit int16 range)
    psyqo::FixedPoint<12> nx, ny, nz;
    nx.value = (x.raw() * 4096) / len;
    ny.value = (y.raw() * 4096) / len;
    nz.value = (z.raw() * 4096) / len;
    PushVec3(lua, nx, ny, nz);
    return 1;
}

int LuaAPI::Vec3_DistanceSq(lua_State* L) {
    psyqo::Lua lua(L);
    
    if (!lua.isTable(1) || !lua.isTable(2)) {
        lua.pushNumber(0);
        return 1;
    }
    
    psyqo::FixedPoint<12> ax, ay, az;
    psyqo::FixedPoint<12> bx, by, bz;
    
    ReadVec3(lua, 1, ax, ay, az);
    ReadVec3(lua, 2, bx, by, bz);
    
    auto dx = ax - bx;
    auto dy = ay - by;
    auto dz = az - bz;
    
    auto distSq = dx * dx + dy * dy + dz * dz;
    lua.push(distSq);
    return 1;
}

int LuaAPI::Vec3_Distance(lua_State* L) {
    psyqo::Lua lua(L);
    
    if (!lua.isTable(1) || !lua.isTable(2)) {
        lua.pushNumber(0);
        return 1;
    }
    
    psyqo::FixedPoint<12> ax, ay, az;
    psyqo::FixedPoint<12> bx, by, bz;
    
    ReadVec3(lua, 1, ax, ay, az);
    ReadVec3(lua, 2, bx, by, bz);
    
    auto dx = ax - bx;
    auto dy = ay - by;
    auto dz = az - bz;

    auto distSq = dx * dx + dy * dy + dz * dz;
    int32_t dsRaw = distSq.raw();

    if (dsRaw <= 0) {
        lua.push(psyqo::FixedPoint<12>());
        return 1;
    }

    uint32_t n = (uint32_t)dsRaw;
    uint32_t guess = n;
    for (int i = 0; i < 16; i++) {
        if (guess == 0) break;
        guess = (guess + n / guess) / 2;
    }

    psyqo::FixedPoint<12> result;
    result.value = (int32_t)(guess * 64);
    lua.push(result);
    return 1;
}

int LuaAPI::Vec3_Lerp(lua_State* L) {
    psyqo::Lua lua(L);
    
    if (!lua.isTable(1) || !lua.isTable(2)) {
        lua.push();
        return 1;
    }
    
    psyqo::FixedPoint<12> ax, ay, az;
    psyqo::FixedPoint<12> bx, by, bz;
    
    ReadVec3(lua, 1, ax, ay, az);
    ReadVec3(lua, 2, bx, by, bz);
    
    psyqo::FixedPoint<12> t = readFP(lua, 3);
    psyqo::FixedPoint<12> oneMinusT = psyqo::FixedPoint<12>(4096, psyqo::FixedPoint<12>::RAW) - t;
    
    psyqo::FixedPoint<12> rx = ax * oneMinusT + bx * t;
    psyqo::FixedPoint<12> ry = ay * oneMinusT + by * t;
    psyqo::FixedPoint<12> rz = az * oneMinusT + bz * t;
    
    PushVec3(lua, rx, ry, rz);
    return 1;
}

// ============================================================================
// INPUT API IMPLEMENTATION
// ============================================================================

void LuaAPI::RegisterInputConstants(psyqo::Lua& L) {
    // Button constants - must match psyqo::AdvancedPad::Button enum
    L.pushNumber(static_cast<lua_Number>(psyqo::AdvancedPad::Button::Cross));
    L.setField(-2, "CROSS");
    
    L.pushNumber(static_cast<lua_Number>(psyqo::AdvancedPad::Button::Circle));
    L.setField(-2, "CIRCLE");
    
    L.pushNumber(static_cast<lua_Number>(psyqo::AdvancedPad::Button::Square));
    L.setField(-2, "SQUARE");
    
    L.pushNumber(static_cast<lua_Number>(psyqo::AdvancedPad::Button::Triangle));
    L.setField(-2, "TRIANGLE");
    
    L.pushNumber(static_cast<lua_Number>(psyqo::AdvancedPad::Button::L1));
    L.setField(-2, "L1");
    
    L.pushNumber(static_cast<lua_Number>(psyqo::AdvancedPad::Button::R1));
    L.setField(-2, "R1");
    
    L.pushNumber(static_cast<lua_Number>(psyqo::AdvancedPad::Button::L2));
    L.setField(-2, "L2");
    
    L.pushNumber(static_cast<lua_Number>(psyqo::AdvancedPad::Button::R2));
    L.setField(-2, "R2");
    
    L.pushNumber(static_cast<lua_Number>(psyqo::AdvancedPad::Button::Start));
    L.setField(-2, "START");
    
    L.pushNumber(static_cast<lua_Number>(psyqo::AdvancedPad::Button::Select));
    L.setField(-2, "SELECT");
    
    L.pushNumber(static_cast<lua_Number>(psyqo::AdvancedPad::Button::Up));
    L.setField(-2, "UP");
    
    L.pushNumber(static_cast<lua_Number>(psyqo::AdvancedPad::Button::Down));
    L.setField(-2, "DOWN");
    
    L.pushNumber(static_cast<lua_Number>(psyqo::AdvancedPad::Button::Left));
    L.setField(-2, "LEFT");
    
    L.pushNumber(static_cast<lua_Number>(psyqo::AdvancedPad::Button::Right));
    L.setField(-2, "RIGHT");
    
    L.pushNumber(static_cast<lua_Number>(psyqo::AdvancedPad::Button::L3));
    L.setField(-2, "L3");
    
    L.pushNumber(static_cast<lua_Number>(psyqo::AdvancedPad::Button::R3));
    L.setField(-2, "R3");
}

int LuaAPI::Input_IsPressed(lua_State* L) {
    psyqo::Lua lua(L);
    
    if (!s_sceneManager || !lua.isNumber(1)) {
        lua.push(false);
        return 1;
    }
    
    auto button = static_cast<psyqo::AdvancedPad::Button>(static_cast<uint16_t>(lua.toNumber(1)));
    lua.push(s_sceneManager->getControls().wasButtonPressed(button));
    return 1;
}

int LuaAPI::Input_IsReleased(lua_State* L) {
    psyqo::Lua lua(L);
    
    if (!s_sceneManager || !lua.isNumber(1)) {
        lua.push(false);
        return 1;
    }
    
    auto button = static_cast<psyqo::AdvancedPad::Button>(static_cast<uint16_t>(lua.toNumber(1)));
    lua.push(s_sceneManager->getControls().wasButtonReleased(button));
    return 1;
}

int LuaAPI::Input_IsHeld(lua_State* L) {
    psyqo::Lua lua(L);
    
    if (!s_sceneManager || !lua.isNumber(1)) {
        lua.push(false);
        return 1;
    }
    
    auto button = static_cast<psyqo::AdvancedPad::Button>(static_cast<uint16_t>(lua.toNumber(1)));
    lua.push(s_sceneManager->getControls().isButtonHeld(button));
    return 1;
}

int LuaAPI::Input_GetAnalog(lua_State* L) {
    psyqo::Lua lua(L);
    
    if (!s_sceneManager) {
        lua.pushNumber(0);
        lua.pushNumber(0);
        return 2;
    }
    
    int stick = lua.isNumber(1) ? static_cast<int>(lua.toNumber(1)) : 0;
    auto& controls = s_sceneManager->getControls();
    
    int16_t x, y;
    if (stick == 1) {
        x = controls.getRightStickX();
        y = controls.getRightStickY();
    } else {
        x = controls.getLeftStickX();
        y = controls.getLeftStickY();
    }
    
    // Scale to approximately [-1.0, 1.0] in Lua number space
    // Stick range is -127 to +127; divide by 127
    lua.pushNumber(x * kFixedScale / 127);
    lua.pushNumber(y * kFixedScale / 127);
    return 2;
}

// ============================================================================
// TIMER API IMPLEMENTATION
// ============================================================================

static uint32_t s_frameCount = 0;

void LuaAPI::IncrementFrameCount() {
    s_frameCount++;
}

void LuaAPI::ResetFrameCount() {
    s_frameCount = 0;
}

int LuaAPI::Timer_GetFrameCount(lua_State* L) {
    psyqo::Lua lua(L);
    lua.pushNumber(s_frameCount);
    return 1;
}

// ============================================================================
// GAMESTATE API IMPLEMENTATION
// ============================================================================

// Free-form short strings; 32 bytes covers typical mode names ("explore",
// "battle", "dialogue", "menu", "cutscene", "paused") and chunk ids
// ("north_gate", "tavern_basement"). Truncates silently on overflow —
// authors keep names short anyway.
static char s_gsMode[32]   = {0};
static char s_gsChunk[32]  = {0};

static void copyBoundedString(char* dst, size_t dstSize, const char* src) {
    if (!dst || dstSize == 0) return;
    if (!src) { dst[0] = 0; return; }
    size_t i = 0;
    for (; i + 1 < dstSize && src[i]; i++) dst[i] = src[i];
    dst[i] = 0;
}

void LuaAPI::ResetGameState() {
    s_gsMode[0] = 0;
    s_gsChunk[0] = 0;
}

// Local strlen — bounded scan, no <cstring> dependency.
static size_t boundedStrlen(const char* s, size_t maxLen) {
    if (!s) return 0;
    size_t i = 0;
    for (; i < maxLen && s[i]; i++) {}
    return i;
}

static bool stringsEqualNullTerm(const char* a, const char* b, size_t maxLen) {
    if (!a || !b) return false;
    size_t i = 0;
    for (; i < maxLen; i++) {
        if (a[i] != b[i]) return false;
        if (a[i] == 0) return true;     // both are 0 (a[i]==b[i] above)
    }
    return false;                       // both ran past maxLen without terminator
}

int LuaAPI::GameState_Frame(lua_State* L) {
    psyqo::Lua lua(L);
    lua.pushNumber(s_frameCount);
    return 1;
}

int LuaAPI::GameState_GetMode(lua_State* L) {
    psyqo::Lua lua(L);
    lua.push(s_gsMode, boundedStrlen(s_gsMode, sizeof(s_gsMode)));
    return 1;
}

int LuaAPI::GameState_SetMode(lua_State* L) {
    psyqo::Lua lua(L);
    if (!lua.isString(1)) return 0;
    copyBoundedString(s_gsMode, sizeof(s_gsMode), lua.toString(1));
    return 0;
}

int LuaAPI::GameState_IsMode(lua_State* L) {
    psyqo::Lua lua(L);
    if (!lua.isString(1)) { lua.push(false); return 1; }
    lua.push(stringsEqualNullTerm(lua.toString(1), s_gsMode, sizeof(s_gsMode)));
    return 1;
}

int LuaAPI::GameState_GetChunk(lua_State* L) {
    psyqo::Lua lua(L);
    lua.push(s_gsChunk, boundedStrlen(s_gsChunk, sizeof(s_gsChunk)));
    return 1;
}

int LuaAPI::GameState_SetChunk(lua_State* L) {
    psyqo::Lua lua(L);
    if (!lua.isString(1)) return 0;
    copyBoundedString(s_gsChunk, sizeof(s_gsChunk), lua.toString(1));
    return 0;
}

int LuaAPI::GameState_IsChunk(lua_State* L) {
    psyqo::Lua lua(L);
    if (!lua.isString(1)) { lua.push(false); return 1; }
    lua.push(stringsEqualNullTerm(lua.toString(1), s_gsChunk, sizeof(s_gsChunk)));
    return 1;
}

// ============================================================================
// CAMERA API IMPLEMENTATION
// ============================================================================

int LuaAPI::Camera_GetPosition(lua_State* L) {
    psyqo::Lua lua(L);
    
    if (s_sceneManager) {
        auto& pos = s_sceneManager->getCamera().GetPosition();
        PushVec3(lua, pos.x, pos.y, pos.z);
    } else {
        PushVec3(lua, psyqo::FixedPoint<12>(0), psyqo::FixedPoint<12>(0), psyqo::FixedPoint<12>(0));
    }
    return 1;
}

int LuaAPI::Camera_SetPosition(lua_State* L) {
    psyqo::Lua lua(L);
    
    if (!s_sceneManager || !lua.isTable(1)) return 0;
    
    psyqo::FixedPoint<12> x, y, z;
    ReadVec3(lua, 1, x, y, z);
    s_sceneManager->getCamera().SetPosition(x, y, z);
    return 0;
}

int LuaAPI::Camera_GetRotation(lua_State* L) {
    psyqo::Lua lua(L);
    
    if (s_sceneManager) {
        psyqo::FixedPoint<12> rotX = psyqo::FixedPoint<12>(static_cast<int32_t>(s_sceneManager->getCamera().GetAngleX() * 4), psyqo::FixedPoint<12>::RAW);
        psyqo::FixedPoint<12> rotY = psyqo::FixedPoint<12>(static_cast<int32_t>(s_sceneManager->getCamera().GetAngleY() * 4), psyqo::FixedPoint<12>::RAW);
        psyqo::FixedPoint<12> rotZ = psyqo::FixedPoint<12>(static_cast<int32_t>(s_sceneManager->getCamera().GetAngleZ() * 4), psyqo::FixedPoint<12>::RAW);

        PushVec3(lua, rotX, rotY, rotZ);
    } else {
        // Mirror Camera_GetPosition behavior when no scene manager is available.
        PushVec3(lua, psyqo::FixedPoint<12>(0), psyqo::FixedPoint<12>(0), psyqo::FixedPoint<12>(0));
    }
    return 1;
}

int LuaAPI::Camera_SetRotation(lua_State* L) {
    psyqo::Lua lua(L);
    
    if (!s_sceneManager || !lua.isTable(1)) return 0;
    
    // Accept three angles in pi-units (e.g., 0.5 = π/2 = 90°)
    // This matches psyqo::Angle convention used by the engine.
    psyqo::FixedPoint<12> x, y, z;
    ReadVec3(lua, 1, x, y, z);

    // Convert to Angle (FixedPoint<10>) 
    psyqo::Angle rx, ry, rz;
    rx.value = x.value >> 2;
    ry.value = y.value >> 2;
    rz.value = z.value >> 2;

    s_sceneManager->getCamera().SetRotation(rx, ry, rz);
    return 0;
}

int LuaAPI::Camera_GetForward(lua_State* L) {
    psyqo::Lua lua(L);

    if (!s_sceneManager) {
        psyqo::FixedPoint<12> zero(0);
        PushVec3(lua, zero, zero, zero);
        return 1;
    }
    psyqo::Matrix33 camRotationMatrix = s_sceneManager->getCamera().GetRotation();

    psyqo::FixedPoint<12> fwdX = camRotationMatrix.vs[2].x;
    psyqo::FixedPoint<12> fwdY = camRotationMatrix.vs[2].y;
    psyqo::FixedPoint<12> fwdZ = camRotationMatrix.vs[2].z;

    PushVec3(lua, fwdX, fwdY, fwdZ);
    return 1;
}

int LuaAPI::Camera_MoveForward(lua_State* L) {
    psyqo::Lua lua(L);

    if (!s_sceneManager || !lua.isTable(1)) return 0;

    psyqo::FixedPoint<12> stepAmount = readFP(lua, 1);

    auto& cam = s_sceneManager->getCamera();

    psyqo::Matrix33 camRotationMatrix = cam.GetRotation();

    psyqo::FixedPoint<12> fwdX = camRotationMatrix.vs[2].x * stepAmount;
    psyqo::FixedPoint<12> fwdY = camRotationMatrix.vs[2].y * stepAmount;
    psyqo::FixedPoint<12> fwdZ = camRotationMatrix.vs[2].z * stepAmount;
    
    psyqo::Vec3 pos = cam.GetPosition();

    pos.x = pos.x + fwdX;
    pos.y = pos.y + fwdY;
    pos.z = pos.z + fwdZ;

    cam.SetPosition(pos.x,pos.y,pos.z);

    return 0;
}

int LuaAPI::Camera_MoveBackward(lua_State* L) {
    psyqo::Lua lua(L);

    if (!s_sceneManager || !lua.isTable(1)) return 0;

    psyqo::FixedPoint<12> stepAmount = readFP(lua, 1);

    auto& cam = s_sceneManager->getCamera();

    psyqo::Matrix33 camRotationMatrix = cam.GetRotation();

    psyqo::FixedPoint<12> fwdX = camRotationMatrix.vs[2].x * stepAmount;
    psyqo::FixedPoint<12> fwdY = camRotationMatrix.vs[2].y * stepAmount;
    psyqo::FixedPoint<12> fwdZ = camRotationMatrix.vs[2].z * stepAmount;
    
    psyqo::Vec3 pos = cam.GetPosition();

    pos.x = pos.x - fwdX;
    pos.y = pos.y - fwdY;
    pos.z = pos.z - fwdZ;

    cam.SetPosition(pos.x,pos.y,pos.z);

    return 0;
}

int LuaAPI::Camera_MoveLeft(lua_State* L) {
    psyqo::Lua lua(L);

    if (!s_sceneManager || !lua.isTable(1)) return 0;

    psyqo::FixedPoint<12> stepAmount = readFP(lua, 1);

    auto& cam = s_sceneManager->getCamera();

    psyqo::Matrix33 camRotationMatrix = cam.GetRotation();

    // Use the camera's right vector for strafing; negate it to move left.
    psyqo::FixedPoint<12> rightX = camRotationMatrix.vs[0].x * stepAmount;
    psyqo::FixedPoint<12> rightY = camRotationMatrix.vs[0].y * stepAmount;
    psyqo::FixedPoint<12> rightZ = camRotationMatrix.vs[0].z * stepAmount;

    psyqo::Vec3 pos = cam.GetPosition();

    pos.x = pos.x - rightX;
    pos.y = pos.y - rightY;
    pos.z = pos.z - rightZ;

    cam.SetPosition(pos.x,pos.y,pos.z);

    return 0;
}

int LuaAPI::Camera_MoveRight(lua_State* L) {
    psyqo::Lua lua(L);

    if (!s_sceneManager || !lua.isTable(1)) return 0;

    psyqo::FixedPoint<12> stepAmount = readFP(lua, 1);

    auto& cam = s_sceneManager->getCamera();

    psyqo::Matrix33 camRotationMatrix = cam.GetRotation();

    // Use the camera's right vector for strafing; negate it to move left.
    psyqo::FixedPoint<12> rightX = camRotationMatrix.vs[0].x * stepAmount;
    psyqo::FixedPoint<12> rightY = camRotationMatrix.vs[0].y * stepAmount;
    psyqo::FixedPoint<12> rightZ = camRotationMatrix.vs[0].z * stepAmount;

    psyqo::Vec3 pos = cam.GetPosition();

    pos.x = pos.x + rightX;
    pos.y = pos.y + rightY;
    pos.z = pos.z + rightZ;

    cam.SetPosition(pos.x,pos.y,pos.z);

    return 0;
}

int LuaAPI::Camera_FollowPsxPlayer(lua_State* L) {
    psyqo::Lua lua(L);

    if (s_sceneManager && lua.isBoolean(1)) {
        s_sceneManager->setCameraFollowPlayer(lua.toBoolean(1));
    }
    return 0;
}

int LuaAPI::Camera_SetMode(lua_State* L) {
    psyqo::Lua lua(L);
    if (!s_sceneManager) return 0;

    // Accept either an integer (0=third, 1=first) or a string ("third",
    // "first"). Strings are kinder at the Lua call-site.
    if (lua.isString(1)) {
        const char* s = lua.toString(1);
        if (streq(s, "first")) {
            s_sceneManager->setCameraMode(PlayerCameraMode::FirstPerson);
        } else if (streq(s, "third")) {
            s_sceneManager->setCameraMode(PlayerCameraMode::ThirdPerson);
        }
    } else if (lua.isNumber(1)) {
        int n = static_cast<int>(lua.toNumber(1));
        if (n == 0) s_sceneManager->setCameraMode(PlayerCameraMode::ThirdPerson);
        else if (n == 1) s_sceneManager->setCameraMode(PlayerCameraMode::FirstPerson);
    }
    return 0;
}

int LuaAPI::Camera_LookAt(lua_State* L) {
    psyqo::Lua lua(L);
    
    if (!s_sceneManager) return 0;
    
    psyqo::FixedPoint<12> tx, ty, tz;
    
    if (lua.isTable(1)) {
        ReadVec3(lua, 1, tx, ty, tz);
    } else {
        tx = lua.isNoneOrNil(1) ? psyqo::FixedPoint<12>() : readFP(lua, 1);
        ty = lua.isNoneOrNil(2) ? psyqo::FixedPoint<12>() : readFP(lua, 2);
        tz = lua.isNoneOrNil(3) ? psyqo::FixedPoint<12>() : readFP(lua, 3);
    }
    
    auto& cam = s_sceneManager->getCamera();
    auto& pos = cam.GetPosition();
    
    // Compute direction vector from camera to target
    auto dx = tx - pos.x;
    auto dy = ty - pos.y;
    auto dz = tz - pos.z;

    // Compute horizontal distance for pitch calculation
    auto horizDistSq = dx * dx + dz * dz;
    int32_t hdsRaw = horizDistSq.raw();
    uint32_t hn = (uint32_t)(hdsRaw > 0 ? hdsRaw : 1);
    uint32_t horizGuess = hn;
    for (int i = 0; i < 16; i++) {
        if (horizGuess == 0) break;
        horizGuess = (horizGuess + hn / horizGuess) / 2;
    }
    
    // Yaw = atan2(dx, dz) — approximate with lookup or use psyqo trig
    // For now, use a simple atan2 approximation in fp12 domain
    // and set rotation via SetRotation (pitch, yaw, 0)
    // Approximate: yaw is proportional to dx/dz in small-angle
    // Full implementation requires psyqo Trig atan2 which is not trivially
    // accessible here. Set rotation to face the target on the Y axis.
    // This is a simplified look-at that only handles yaw.
    psyqo::Angle yaw;
    psyqo::Angle pitch;
    
    // Use scaled integer atan2 approximation
    // atan2(dx, dz) in the range [-π, π]
    // For PS1, the exact method depends on psyqo's Trig class.
    // Returning luaError since we can't do a proper atan2 without Trig instance.
    // Compromise: just set rotation angles directly
    yaw.value = 0;
    pitch.value = 0;
    
    // For a real implementation, Camera would need a LookAt method.
    return 0;
}

int LuaAPI::Camera_GetH(lua_State* L) {
    psyqo::Lua lua(L);
    if (!s_sceneManager) { lua.pushNumber(120); return 1; }
    lua.pushNumber(static_cast<lua_Number>(s_sceneManager->getCamera().GetProjectionH()));
    return 1;
}

int LuaAPI::Camera_SetH(lua_State* L) {
    psyqo::Lua lua(L);
    if (!s_sceneManager) return 0;
    int32_t h = static_cast<int32_t>(lua.toNumber(1));
    if (h < 1) h = 1;
    if (h > 1024) h = 1024;
    s_sceneManager->getCamera().SetProjectionH(h);
    return 0;
}

int LuaAPI::Camera_Shake(lua_State* L) {
    psyqo::Lua lua(L);
    if (!s_sceneManager) return 0;
    psyqo::FixedPoint<12> intensity = readFP(lua, 1);
    int frames = static_cast<int>(lua.toNumber(2));
    s_sceneManager->getCamera().Shake(intensity, frames);
    return 0;
}

int LuaAPI::Camera_ShakeRaw(lua_State* L) {
    psyqo::Lua lua(L);
    if (!s_sceneManager) return 0;
    // Lua integer → raw FP12 directly, skipping the readFP multiply-by-
    // 4096 that's baked into Camera.Shake. Lets psxlua callers get sub-
    // integer shake intensities without parsing decimal literals.
    psyqo::FixedPoint<12> intensity;
    intensity.value = static_cast<int32_t>(lua.toNumber(1));
    int frames = static_cast<int>(lua.toNumber(2));
    s_sceneManager->getCamera().Shake(intensity, frames);
    return 0;
}

// ============================================================================
// AUDIO API IMPLEMENTATION
// ============================================================================

int LuaAPI::Audio_Play(lua_State* L) {
    psyqo::Lua lua(L);

    if (!s_sceneManager) {
        lua.pushNumber(-1);
        return 1;
    }

    int soundId = -1;

    // Accept number (index) or string (name lookup) like Entity.Find
    // Check isNumber FIRST - in Lua, numbers pass isString too.
    if (lua.isNumber(1)) {
        soundId = static_cast<int>(lua.toNumber(1));
    } else if (lua.isString(1)) {
        const char* name = lua.toString(1);
        soundId = s_sceneManager->findAudioClipByName(name);
        if (soundId < 0) {
            lua.pushNumber(-1);
            return 1;
        }
    } else {
        lua.pushNumber(-1);
        return 1;
    }

    int volume = static_cast<int>(lua.optNumber(2, 100));
    int pan = static_cast<int>(lua.optNumber(3, 64));

    int voice = s_sceneManager->getAudio().play(soundId, volume, pan);
    lua.pushNumber(voice);
    return 1;
}

int LuaAPI::Audio_Find(lua_State* L) {
    psyqo::Lua lua(L);
    
    if (!s_sceneManager || !lua.isString(1)) {
        lua.push();  // nil
        return 1;
    }
    
    const char* name = lua.toString(1);
    int clipIndex = s_sceneManager->findAudioClipByName(name);
    
    if (clipIndex >= 0) {
        lua.pushNumber(static_cast<lua_Number>(clipIndex));
    } else {
        lua.push();  // nil
    }
    return 1;
}

int LuaAPI::Audio_Stop(lua_State* L) {
    psyqo::Lua lua(L);
    if (!s_sceneManager) return 0;
    int channelId = static_cast<int>(lua.toNumber(1));
    s_sceneManager->getAudio().stopVoice(channelId);
    return 0;
}

int LuaAPI::Audio_SetVolume(lua_State* L) {
    psyqo::Lua lua(L);
    if (!s_sceneManager) return 0;
    int channelId = static_cast<int>(lua.toNumber(1));
    int volume = static_cast<int>(lua.toNumber(2));
    int pan = static_cast<int>(lua.optNumber(3, 64));
    s_sceneManager->getAudio().setVoiceVolume(channelId, volume, pan);
    return 0;
}

int LuaAPI::Audio_StopAll(lua_State* L) {
    psyqo::Lua lua(L);
    if (!s_sceneManager) return 0;
    s_sceneManager->getAudio().stopAll();
    return 0;
}

int LuaAPI::Audio_GetClipDuration(lua_State* L) {
    psyqo::Lua lua(L);
    if (!s_sceneManager) {
        lua.pushNumber(0);
        return 1;
    }

    int clip = -1;
    // Prefer the number check — Lua numbers satisfy isString too.
    if (lua.isNumber(1)) {
        clip = static_cast<int>(lua.toNumber(1));
    } else if (lua.isString(1)) {
        clip = s_sceneManager->findAudioClipByName(lua.toString(1));
    }

    if (clip < 0) {
        lua.pushNumber(0);
    } else {
        uint32_t frames = s_sceneManager->getAudio().getClipDurationFrames(clip);
        lua.pushNumber(static_cast<lua_Number>(frames));
    }
    return 1;
}

// v25 routing-aware Audio.PlaySfx — same signature as Audio.Play but
// checks the clip's routing first and warns if it's not SPU. Routing
// mismatches are still played (so authoring mistakes don't go silent),
// they're just logged to the kernel log so scripters notice.
int LuaAPI::Audio_PlaySfx(lua_State* L) {
    psyqo::Lua lua(L);
    if (!s_sceneManager) {
        lua.pushNumber(-1);
        return 1;
    }

    int soundId = -1;
    if (lua.isNumber(1)) {
        soundId = static_cast<int>(lua.toNumber(1));
    } else if (lua.isString(1)) {
        soundId = s_sceneManager->findAudioClipByName(lua.toString(1));
    }
    if (soundId < 0) {
        lua.pushNumber(-1);
        return 1;
    }

    uint8_t route = s_sceneManager->getAudioClipRouting(soundId);
    if (route != 0) {
        // 1 = XA, 2 = CDDA — call PlaySfx anyway but warn so the author
        // can correct the authoring side (PS1AudioClip.Route).
        ramsyscall_printf("[Audio] PlaySfx on non-SPU clip (routing=%d, index=%d) - falling through to SPU.\n",
                          static_cast<int>(route), soundId);
    }

    int volume = static_cast<int>(lua.optNumber(2, 100));
    int pan    = static_cast<int>(lua.optNumber(3, 64));
    int voice  = s_sceneManager->getAudio().play(soundId, volume, pan);
    lua.pushNumber(voice);
    return 1;
}

// Audio.PlayMusic — dispatch by routing.
//   SPU  -> play via the SFX path (warn: this is musically large for SPU)
//   XA   -> XaAudioBackend::play(sidecarOffset, sidecarSize). Resolves the
//           clip's XA payload via SceneManager::getXaClipInfo. Returns -1
//           if no XA payload was packed (psxavenc absent at export).
//   CDDA -> auto-dispatches to MusicManager::playCDDATrack via the clip's
//           CddaTrackNumber. Returns -1 if the clip has no track set.
int LuaAPI::Audio_PlayMusic(lua_State* L) {
    psyqo::Lua lua(L);
    if (!s_sceneManager || !lua.isString(1)) {
        lua.pushNumber(-1);
        return 1;
    }

    const char* name = lua.toString(1);
    int idx = s_sceneManager->findAudioClipByName(name);
    if (idx < 0) {
        ramsyscall_printf("[Audio] PlayMusic: clip '%s' not found\n", name);
        lua.pushNumber(-1);
        return 1;
    }

    uint8_t route = s_sceneManager->getAudioClipRouting(idx);
    switch (route) {
        case 0:  // SPU
            s_sceneManager->getAudio().play(idx, 100, 64);
            lua.pushNumber(0);
            return 1;
        case 1: { // XA — dispatch to XaAudioBackend (scaffold)
            uint32_t xaOff = 0, xaSize = 0;
            if (!s_sceneManager->getXaClipInfo(name, xaOff, xaSize)) {
                ramsyscall_printf("[Audio] PlayMusic('%s'): XA-routed clip but no XA payload in scene (psxavenc missing at export?). Silence.\n", name);
                lua.pushNumber(-1);
                return 1;
            }
            bool started = s_sceneManager->getXa().play(xaOff, xaSize);
            lua.pushNumber(started ? 0 : -1);
            return 1;
        }
        case 2: { // CDDA — auto-dispatch to PlayCDDA via the clip's track
            uint8_t track = s_sceneManager->getAudioClipCddaTrack(idx);
            if (track == 0) {
                ramsyscall_printf("[Audio] PlayMusic('%s'): CDDA-routed clip has no track number set. Set CddaTrackNumber on the PS1AudioClip resource.\n", name);
                lua.pushNumber(-1);
                return 1;
            }
            s_sceneManager->getMusic().playCDDATrack(static_cast<int>(track));
            lua.pushNumber(0);
            return 1;
        }
        default:
            lua.pushNumber(-1);
            return 1;
    }
}

// Audio.StopMusic — best-effort blanket stop across all three buses:
// stops the PS2M sequencer, any CDDA track, and the XA stream.
int LuaAPI::Audio_StopMusic(lua_State* L) {
    (void)L;
    if (!s_sceneManager) return 0;
    s_sceneManager->getMusicSequencer().stop();
    s_sceneManager->getMusic().stopCDDA();
    s_sceneManager->getXa().stop();
    return 0;
}

int LuaAPI::Audio_PlayCDDA(lua_State *L) {
    psyqo::Lua lua(L);
    if (!s_sceneManager) return 0;
    s_sceneManager->getMusic().playCDDATrack(static_cast<int>(lua.toNumber(1)));
    return 0;
}

int LuaAPI::Audio_ResumeCDDA(lua_State *L) {
    psyqo::Lua lua(L);
    if (!s_sceneManager) return 0;
    s_sceneManager->getMusic().resumeCDDA();
    return 0;
}

int LuaAPI::Audio_PauseCDDA(lua_State *L) {
    psyqo::Lua lua(L);
    if (!s_sceneManager) return 0;
    s_sceneManager->getMusic().pauseCDDA();
    return 0;
}

int LuaAPI::Audio_StopCDDA(lua_State *L) {
    psyqo::Lua lua(L);
    if (!s_sceneManager) return 0;
    s_sceneManager->getMusic().stopCDDA();
    return 0;
}

int LuaAPI::Audio_TellCDDA(lua_State *L) {
    if (!s_sceneManager) return 0;
    s_sceneManager->getMusic().tellCDDA(L);
    return 0;
}

int LuaAPI::Audio_SetCDDAVolume(lua_State *L) {
    psyqo::Lua lua(L);
    if (!s_sceneManager) return 0;
    s_sceneManager->getMusic().setCDDAVolume(static_cast<int>(lua.toNumber(1)), static_cast<int>(lua.toNumber(2)));
    return 0;
}

// ============================================================================
// MUSIC API IMPLEMENTATION
// ============================================================================

int LuaAPI::Music_Play(lua_State* L) {
    psyqo::Lua lua(L);
    if (!s_sceneManager) {
        lua.push(false);
        return 1;
    }

    int index = -1;
    if (lua.isNumber(1)) {
        index = static_cast<int>(lua.toNumber(1));
    } else if (lua.isString(1)) {
        index = s_sceneManager->findMusicSequenceByName(lua.toString(1));
    }
    if (index < 0) {
        lua.push(false);
        return 1;
    }

    int volume = static_cast<int>(lua.optNumber(2, 100));
    if (volume < 0) volume = 0;
    if (volume > 127) volume = 127;

    bool ok = s_sceneManager->getMusicSequencer().playByIndex(index, static_cast<uint8_t>(volume));
    lua.push(ok);
    return 1;
}

int LuaAPI::Music_Stop(lua_State* /*L*/) {
    if (!s_sceneManager) return 0;
    s_sceneManager->getMusicSequencer().stop();
    return 0;
}

int LuaAPI::Music_IsPlaying(lua_State* L) {
    psyqo::Lua lua(L);
    if (!s_sceneManager) {
        lua.push(false);
        return 1;
    }
    lua.push(s_sceneManager->getMusicSequencer().isPlaying());
    return 1;
}

int LuaAPI::Music_SetVolume(lua_State* L) {
    psyqo::Lua lua(L);
    if (!s_sceneManager) return 0;
    int v = static_cast<int>(lua.toNumber(1));
    if (v < 0) v = 0;
    if (v > 127) v = 127;
    s_sceneManager->getMusicSequencer().setMasterVolume(static_cast<uint8_t>(v));
    return 0;
}

int LuaAPI::Music_GetBeat(lua_State* L) {
    psyqo::Lua lua(L);
    if (!s_sceneManager) {
        lua.pushNumber(0);
        return 1;
    }
    lua.pushNumber(static_cast<lua_Number>(s_sceneManager->getMusicSequencer().getBeat()));
    return 1;
}

int LuaAPI::Music_Find(lua_State* L) {
    psyqo::Lua lua(L);
    if (!s_sceneManager || !lua.isString(1)) {
        lua.push();  // nil
        return 1;
    }
    int idx = s_sceneManager->findMusicSequenceByName(lua.toString(1));
    if (idx < 0) {
        lua.push();  // nil
    } else {
        lua.pushNumber(static_cast<lua_Number>(idx));
    }
    return 1;
}

int LuaAPI::Music_GetLastMarkerHash(lua_State* L) {
    psyqo::Lua lua(L);
    if (!s_sceneManager) {
        lua.pushNumber(0);
        return 1;
    }
    lua.pushNumber(static_cast<lua_Number>(
        s_sceneManager->getMusicSequencer().getLastMarkerHash()));
    return 1;
}

int LuaAPI::Music_MarkerHash(lua_State* L) {
    psyqo::Lua lua(L);
    if (!lua.isString(1)) {
        lua.pushNumber(0);
        return 1;
    }
    // FNV-1a 32-bit folded to 16-bit. Must match
    // godot-ps1/addons/ps1godot/exporter/PS1MSerializer.cs
    // MarkerHash16 bit-for-bit. Trim+lowercase to match the case-
    // insensitive convention loop markers already use.
    const char *src = lua.toString(1);
    if (!src) { lua.pushNumber(0); return 1; }
    // Skip leading ASCII whitespace.
    while (*src == ' ' || *src == '\t' || *src == '\n' || *src == '\r') ++src;
    // Find end of trimmed range.
    const char *end = src;
    const char *lastNonSpace = src;
    while (*end) {
        if (*end != ' ' && *end != '\t' && *end != '\n' && *end != '\r')
            lastNonSpace = end;
        ++end;
    }
    end = (*src == 0) ? src : lastNonSpace + 1;
    uint32_t hash = 2166136261u;  // FNV offset basis
    for (const char *p = src; p < end; ++p) {
        unsigned char c = (unsigned char)*p;
        if (c >= 'A' && c <= 'Z') c = (unsigned char)(c + 32);
        hash ^= c;
        hash *= 16777619u;  // FNV prime
    }
    uint16_t folded = (uint16_t)((hash & 0xFFFFu) ^ (hash >> 16));
    lua.pushNumber(static_cast<lua_Number>(folded));
    return 1;
}

// ============================================================================
// SOUND API IMPLEMENTATION (Phase 5 Stage B — wired)
//
// Sound.PlayMacro / PlayFamily / StopAll dispatch through the
// SceneManager's SoundMacroSequencer + SoundFamily runtimes. Both
// pull from the SFX voice pool via AudioManager::play — they never
// reserve voices and never compete with the music sequencer.
// Per-macro Priority / MaxVoices / CooldownFrames and per-family
// jitter ranges live on the on-disk records (authored via PS1Scene
// in the Godot editor).
// ============================================================================

int LuaAPI::Sound_PlayMacro(lua_State* L) {
    psyqo::Lua lua(L);
    if (!s_sceneManager) {
        lua.push();
        return 1;
    }
    int handle = -1;
    if (lua.isString(1)) {
        handle = s_sceneManager->getSoundMacros().playByName(lua.toString(1));
    } else if (lua.isNumber(1)) {
        handle = s_sceneManager->getSoundMacros().playByIndex((int)lua.toNumber(1));
    }
    if (handle < 0) {
        lua.push();  // nil = drop / unknown / cooldown / cap
    } else {
        lua.pushNumber(static_cast<lua_Number>(handle));
    }
    return 1;
}

int LuaAPI::Sound_PlayFamily(lua_State* L) {
    psyqo::Lua lua(L);
    if (!s_sceneManager) {
        lua.push();
        return 1;
    }
    int ch = -1;
    if (lua.isString(1)) {
        ch = s_sceneManager->getSoundFamilies().playByName(lua.toString(1));
    } else if (lua.isNumber(1)) {
        ch = s_sceneManager->getSoundFamilies().playByIndex((int)lua.toNumber(1));
    }
    if (ch < 0) {
        lua.push();
    } else {
        lua.pushNumber(static_cast<lua_Number>(ch));
    }
    return 1;
}

int LuaAPI::Sound_StopAll(lua_State* L) {
    (void)L;
    if (s_sceneManager) {
        s_sceneManager->getSoundMacros().stopAll();
    }
    return 0;
}

// ============================================================================
// DEBUG API IMPLEMENTATION
// ============================================================================

int LuaAPI::Debug_Log(lua_State* L) {
    psyqo::Lua lua(L);
    if (lua.isString(1)) {
        printf("%s\n", lua.toString(1));
    }
    return 0;
}

int LuaAPI::Debug_DrawLine(lua_State* L) {
    psyqo::Lua lua(L);
    
    // Parse start and end Vec3 tables, optional color
    psyqo::FixedPoint<12> sx, sy, sz, ex, ey, ez;
    if (lua.isTable(1) && lua.isTable(2)) {
        ReadVec3(lua, 1, sx, sy, sz);
        ReadVec3(lua, 2, ex, ey, ez);
    }
    
    // TODO: Queue LINE_G2 primitive through Renderer
    return 0;
}

int LuaAPI::Debug_DrawBox(lua_State* L) {
    psyqo::Lua lua(L);
    
    // Parse center and size Vec3 tables, optional color
    psyqo::FixedPoint<12> cx, cy, cz, hx, hy, hz;
    if (lua.isTable(1) && lua.isTable(2)) {
        ReadVec3(lua, 1, cx, cy, cz);
        ReadVec3(lua, 2, hx, hy, hz);
    }
    
    // TODO: Queue 12 LINE_G2 primitives (box wireframe) through Renderer
    return 0;
}

// ============================================================================
// FP API IMPLEMENTATION
// ============================================================================

int LuaAPI::Convert_IntToFp(lua_State* L) {
    psyqo::Lua lua(L);
    
    if (!lua.isNumber(1)) {
        return 0;
    }

    psyqo::FixedPoint<12> numberFp; 
    numberFp = psyqo::FixedPoint<12>(static_cast<int32_t>(lua.toNumber(1)), psyqo::FixedPoint<12>::RAW);
    
    lua.push(numberFp);
    return 1;
}

int LuaAPI::Convert_FpToInt(lua_State* L) {
    psyqo::Lua lua(L);
    
    if (!lua.isFixedPoint(1)) { 
        return 0;
    }

    uint32_t numberInt = lua.toFixedPoint(1).raw();

    lua.pushNumber(numberInt);
    return 1;
}


// ============================================================================
// MATH API IMPLEMENTATION
// ============================================================================

int LuaAPI::Math_Clamp(lua_State* L) {
    psyqo::Lua lua(L);
    
    lua_Number value = lua.toNumber(1);
    lua_Number minVal = lua.toNumber(2);
    lua_Number maxVal = lua.toNumber(3);
    
    if (value < minVal) value = minVal;
    if (value > maxVal) value = maxVal;
    
    lua.pushNumber(value);
    return 1;
}

int LuaAPI::Math_Lerp(lua_State* L) {
    psyqo::Lua lua(L);
    
    lua_Number a = lua.toNumber(1);
    lua_Number b = lua.toNumber(2);
    lua_Number t = lua.toNumber(3);
    
    lua.pushNumber(a + (b - a) * t);
    return 1;
}

int LuaAPI::Math_Sign(lua_State* L) {
    psyqo::Lua lua(L);
    
    lua_Number value = lua.toNumber(1);
    
    if (value > 0) lua.pushNumber(1);
    else if (value < 0) lua.pushNumber(-1);
    else lua.pushNumber(0);
    
    return 1;
}

int LuaAPI::Math_Abs(lua_State* L) {
    psyqo::Lua lua(L);
    
    lua_Number value = lua.toNumber(1);
    lua.pushNumber(value < 0 ? -value : value);
    return 1;
}

int LuaAPI::Math_Min(lua_State* L) {
    psyqo::Lua lua(L);
    
    lua_Number a = lua.toNumber(1);
    lua_Number b = lua.toNumber(2);
    
    lua.pushNumber(a < b ? a : b);
    return 1;
}

int LuaAPI::Math_Max(lua_State* L) {
    psyqo::Lua lua(L);

    lua_Number a = lua.toNumber(1);
    lua_Number b = lua.toNumber(2);

    lua.pushNumber(a > b ? a : b);
    return 1;
}

// GCC emits arithmetic right shift on `>>` for signed integers, so the
// sign bit propagates. That's the behavior we want: (-6144) >> 12 = -2,
// which is floor(-1.5). Values here are fp12 raw ints.

int LuaAPI::Math_Floor(lua_State* L) {
    psyqo::Lua lua(L);
    int32_t raw = readFP(lua, 1).raw();
    lua.pushNumber(raw >> 12);
    return 1;
}

int LuaAPI::Math_Ceil(lua_State* L) {
    psyqo::Lua lua(L);
    int32_t raw = readFP(lua, 1).raw();
    // ceil(x) = -floor(-x). Avoids the overflow case of adding (1<<12)-1
    // to INT32_MAX-ish raws.
    lua.pushNumber(-((-raw) >> 12));
    return 1;
}

int LuaAPI::Math_Round(lua_State* L) {
    psyqo::Lua lua(L);
    int32_t raw = readFP(lua, 1).raw();
    // Add 0.5 in fp12 then floor. Ties round toward +infinity: 1.5 → 2,
    // -0.5 → 0, -1.5 → -1. Authors who want round-half-away-from-zero
    // can build it from Floor/Ceil.
    lua.pushNumber((raw + (1 << 11)) >> 12);
    return 1;
}

int LuaAPI::Math_ToInt(lua_State* L) {
    psyqo::Lua lua(L);
    int32_t raw = readFP(lua, 1).raw();
    // Truncate toward zero: -1.5 → -1, 1.5 → 1.
    int32_t result = raw >= 0 ? (raw >> 12) : -(((-raw)) >> 12);
    lua.pushNumber(result);
    return 1;
}

int LuaAPI::Math_ToFixed(lua_State* L) {
    psyqo::Lua lua(L);
    int32_t n = static_cast<int32_t>(lua.toNumber(1));
    psyqo::FixedPoint<12> fp(n << 12, psyqo::FixedPoint<12>::RAW);
    lua.push(fp);
    return 1;
}

// ============================================================================
// RANDOM API IMPLEMENTATION
// ============================================================================

int LuaAPI::Random_Number(lua_State* L) {
    psyqo::Lua lua(L);
    
    if (!s_sceneManager || !lua.isNumber(1)) {
        return 0;
    }

    SceneManager::m_random.multiplySeed(s_frameCount+1);

    uint32_t max = lua.toNumber(1);
    uint32_t value = s_sceneManager->m_random.number(max)+1;

    lua.pushNumber(value);
    return 1;
}

int LuaAPI::Random_GeneratorNumber(lua_State* L) {
    psyqo::Lua lua(L);
    
    if (!s_sceneManager || !lua.isNumber(1)) {
        return 0;
    }

    uint32_t max = lua.toNumber(1);
    uint32_t value = s_sceneManager->m_randomGenerator.number(max)+1;

    lua.pushNumber(value);
    return 1;
}

int LuaAPI::Random_Range(lua_State* L) {
    psyqo::Lua lua(L);
    
    if (!s_sceneManager || !lua.isNumber(1) || !lua.isNumber(2)) {
        return 0;
    }

    SceneManager::m_random.multiplySeed(s_frameCount+1);

    uint32_t min = lua.toNumber(1);
    uint32_t max = lua.toNumber(2);
    uint32_t difference = max - min;
    
    uint32_t value = s_sceneManager->m_random.number(difference+1) + min;

    lua.pushNumber(value);
    return 1;
}

int LuaAPI::Random_GeneratorRange(lua_State* L) {
    psyqo::Lua lua(L);
    
    if (!s_sceneManager || !lua.isNumber(1) || !lua.isNumber(2)) {
        return 0;
    }
    uint32_t min = lua.toNumber(1);
    uint32_t max = lua.toNumber(2);
    uint32_t difference = max - min;
    
    uint32_t value = s_sceneManager->m_randomGenerator.number(difference+1) + min;

    lua.pushNumber(value);
    return 1;
}

int LuaAPI::Random_GeneratorSeed(lua_State* L) {
    psyqo::Lua lua(L);

    if (!s_sceneManager || !lua.isNumber(1)) {
        return 0;
    }

    uint32_t newSeed = static_cast<uint32_t>(lua.toNumber(1));

    if(newSeed == 0){
        newSeed = 108;
    }

    s_sceneManager->m_randomGenerator.seed(newSeed);

    return 0;
}

// ============================================================================
// SCENE API IMPLEMENTATION
// ============================================================================

int LuaAPI::Scene_Load(lua_State* L) {
    psyqo::Lua lua(L);
    
    if (!s_sceneManager || !lua.isNumber(1)) {
        return 0;
    }
    
    int sceneIndex = static_cast<int>(lua.toNumber(1));
    s_sceneManager->requestSceneLoad(sceneIndex);
    return 0;
}

int LuaAPI::Scene_GetIndex(lua_State* L) {
    psyqo::Lua lua(L);

    if (!s_sceneManager) {
        lua.pushNumber(0);
        return 1;
    }

    lua.pushNumber(static_cast<lua_Number>(s_sceneManager->getCurrentSceneIndex()));
    return 1;
}

int LuaAPI::Scene_PauseFor(lua_State* L) {
    psyqo::Lua lua(L);
    if (!s_sceneManager) return 0;
    int frames = static_cast<int>(lua.toNumber(1));
    if (frames > 0) s_sceneManager->requestPauseFor(frames);
    return 0;
}

// ============================================================================
// PERSIST API IMPLEMENTATION
// ============================================================================

struct PersistEntry {
    char key[32];
    lua_Number value;
    bool used;
};

static PersistEntry s_persistData[16] = {};

// streq comes from streq.hh. Local helper for strcopy only:
static void strcopy(char* dst, const char* src, int maxLen) {
    int i = 0;
    for (; i < maxLen - 1 && src[i]; i++) dst[i] = src[i];
    dst[i] = '\0';
}

int LuaAPI::Persist_Get(lua_State* L) {
    psyqo::Lua lua(L);
    const char* key = lua.toString(1);
    if (!key) { lua.push(); return 1; }
    
    for (int i = 0; i < 16; i++) {
        if (s_persistData[i].used && streq(s_persistData[i].key, key)) {
            lua.pushNumber(s_persistData[i].value);
            return 1;
        }
    }
    lua.push();  // nil
    return 1;
}

int LuaAPI::Persist_Set(lua_State* L) {
    psyqo::Lua lua(L);
    const char* key = lua.toString(1);
    if (!key) return 0;
    
    lua_Number value = lua.toNumber(2);
    
    // Update existing key
    for (int i = 0; i < 16; i++) {
        if (s_persistData[i].used && streq(s_persistData[i].key, key)) {
            s_persistData[i].value = value;
            return 0;
        }
    }
    
    // Find empty slot
    for (int i = 0; i < 16; i++) {
        if (!s_persistData[i].used) {
            strcopy(s_persistData[i].key, key, 32);
            s_persistData[i].value = value;
            s_persistData[i].used = true;
            return 0;
        }
    }
    
    return 0;  // No room — silently fail
}

void LuaAPI::PersistClear() {
    for (int i = 0; i < 16; i++) {
        s_persistData[i].used = false;
    }
}

// ============================================================================
// CUTSCENE API IMPLEMENTATION
// ============================================================================

int LuaAPI::Cutscene_Play(lua_State* L) {
    psyqo::Lua lua(L);

    if (!s_cutscenePlayer || !lua.isString(1)) {
        return 0;
    }

    const char* name = lua.toString(1);
    bool loop = false;
    int onCompleteRef = LUA_NOREF;

    // Optional second argument: options table {loop=bool, onComplete=function}
    if (lua.isTable(2)) {
        lua.getField(2, "loop");
        if (lua.isBoolean(-1)) loop = lua.toBoolean(-1);
        lua.pop();

        lua.getField(2, "onComplete");
        if (lua.isFunction(-1)) {
            onCompleteRef = lua.ref();  // pops and stores in registry
        } else {
            lua.pop();
        }
    }

    // Clear any previous callback before starting a new cutscene
    int oldRef = s_cutscenePlayer->getOnCompleteRef();
    if (oldRef != LUA_NOREF) {
        luaL_unref(L, LUA_REGISTRYINDEX, oldRef);
    }

    s_cutscenePlayer->setLuaState(L);
    s_cutscenePlayer->setOnCompleteRef(onCompleteRef);
    s_cutscenePlayer->play(name, loop);
    return 0;
}

int LuaAPI::Cutscene_Stop(lua_State* L) {
    psyqo::Lua lua(L);
    
    if (s_cutscenePlayer) {
        s_cutscenePlayer->stop();
    }
    return 0;
}

int LuaAPI::Cutscene_IsPlaying(lua_State* L) {
    psyqo::Lua lua(L);
    
    if (s_cutscenePlayer) {
        lua.push(s_cutscenePlayer->isPlaying());
    } else {
        lua.push(false);
    }
    return 1;
}

// ============================================================================
// ANIMATION API IMPLEMENTATION
// ============================================================================

int LuaAPI::Animation_Play(lua_State* L) {
    psyqo::Lua lua(L);

    if (!s_animationPlayer || !lua.isString(1)) {
        return 0;
    }

    const char* name = lua.toString(1);
    bool loop = false;
    int onCompleteRef = LUA_NOREF;

    if (lua.isTable(2)) {
        lua.getField(2, "loop");
        if (lua.isBoolean(-1)) loop = lua.toBoolean(-1);
        lua.pop();

        lua.getField(2, "onComplete");
        if (lua.isFunction(-1)) {
            onCompleteRef = lua.ref();  // pops and stores in registry
        } else {
            lua.pop();
        }
    }

    s_animationPlayer->setLuaState(L);
    s_animationPlayer->play(name, loop);

    if (onCompleteRef != LUA_NOREF) {
        s_animationPlayer->setOnCompleteRef(name, onCompleteRef);
    }

    return 0;
}

int LuaAPI::Animation_Stop(lua_State* L) {
    psyqo::Lua lua(L);
    if (!s_animationPlayer) return 0;

    if (lua.isString(1)) {
        s_animationPlayer->stop(lua.toString(1));
    } else {
        s_animationPlayer->stopAll();
    }
    return 0;
}

int LuaAPI::Animation_IsPlaying(lua_State* L) {
    psyqo::Lua lua(L);

    if (s_animationPlayer && lua.isString(1)) {
        lua.push(s_animationPlayer->isPlaying(lua.toString(1)));
    } else {
        lua.push(false);
    }
    return 1;
}

// ============================================================================
// SKINNED ANIMATION API IMPLEMENTATION
// ============================================================================

int LuaAPI::SkinnedAnim_Play(lua_State* L) {
    psyqo::Lua lua(L);
    if (!s_sceneManager || !lua.isString(1) || !lua.isString(2)) return 0;

    const char* objectName = lua.toString(1);
    const char* clipName = lua.toString(2);

    int si = s_sceneManager->findSkinAnimByObjectName(objectName);
    if (si < 0) return 0;

    SkinAnimSet& animSet = s_sceneManager->getSkinAnimSet(si);
    SkinAnimState& animState = s_sceneManager->getSkinAnimState(si);

    // Find clip by name
    int clipIdx = -1;
    for (int ci = 0; ci < animSet.clipCount; ci++) {
        if (animSet.clips[ci].name && streq(animSet.clips[ci].name, clipName)) {
            clipIdx = ci;
            break;
        }
    }
    if (clipIdx < 0) return 0;

    bool loop = false;
    int onCompleteRef = LUA_NOREF;

    if (lua.isTable(3)) {
        lua.getField(3, "loop");
        if (lua.isBoolean(-1)) loop = lua.toBoolean(-1);
        lua.pop();

        lua.getField(3, "onComplete");
        if (lua.isFunction(-1)) {
            onCompleteRef = lua.ref();  // pops and stores in registry
        } else {
            lua.pop();
        }
    }

    animState.currentClip = (uint8_t)clipIdx;
    animState.currentFrame = 0;
    animState.subFrame = 0;
    animState.playing = true;
    animState.loop = loop;
    animState.bindPose = false;

    // Release old callback if any
    if (animState.luaCallbackRef != LUA_NOREF) {
        luaL_unref(L, LUA_REGISTRYINDEX, animState.luaCallbackRef);
    }
    animState.luaCallbackRef = onCompleteRef;

    return 0;
}

int LuaAPI::SkinnedAnim_BindPose(lua_State* L) {
    psyqo::Lua lua(L);
    if (!s_sceneManager || !lua.isString(1)) return 0;

    int si = s_sceneManager->findSkinAnimByObjectName(lua.toString(1));
    if (si < 0) return 0;

    SkinAnimState& animState = s_sceneManager->getSkinAnimState(si);
    animState.playing = false;
    animState.bindPose = true;
    animState.currentFrame = 0;
    animState.subFrame = 0;

    if (animState.luaCallbackRef != LUA_NOREF) {
        luaL_unref(L, LUA_REGISTRYINDEX, animState.luaCallbackRef);
        animState.luaCallbackRef = LUA_NOREF;
    }

    return 0;
}

int LuaAPI::SkinnedAnim_Stop(lua_State* L) {
    psyqo::Lua lua(L);
    if (!s_sceneManager || !lua.isString(1)) return 0;

    int si = s_sceneManager->findSkinAnimByObjectName(lua.toString(1));
    if (si < 0) return 0;

    SkinAnimState& animState = s_sceneManager->getSkinAnimState(si);
    animState.playing = false;

    // Release callback
    if (animState.luaCallbackRef != LUA_NOREF) {
        luaL_unref(L, LUA_REGISTRYINDEX, animState.luaCallbackRef);
        animState.luaCallbackRef = LUA_NOREF;
    }

    return 0;
}

int LuaAPI::SkinnedAnim_IsPlaying(lua_State* L) {
    psyqo::Lua lua(L);
    if (!s_sceneManager || !lua.isString(1)) {
        lua.push(false);
        return 1;
    }

    int si = s_sceneManager->findSkinAnimByObjectName(lua.toString(1));
    if (si < 0) {
        lua.push(false);
        return 1;
    }

    lua.push(s_sceneManager->getSkinAnimState(si).playing);
    return 1;
}

int LuaAPI::SkinnedAnim_GetClip(lua_State* L) {
    psyqo::Lua lua(L);
    if (!s_sceneManager || !lua.isString(1)) {
        lua_pushnil(L);
        return 1;
    }

    int si = s_sceneManager->findSkinAnimByObjectName(lua.toString(1));
    if (si < 0) {
        lua_pushnil(L);
        return 1;
    }

    const SkinAnimSet& animSet = s_sceneManager->getSkinAnimSet(si);
    const SkinAnimState& animState = s_sceneManager->getSkinAnimState(si);

    if (animState.currentClip < animSet.clipCount &&
        animSet.clips[animState.currentClip].name) {
        lua.push(animSet.clips[animState.currentClip].name);
    } else {
        lua_pushnil(L);
    }
    return 1;
}

// ============================================================================
// CONTROLS API IMPLEMENTATION
// ============================================================================

int LuaAPI::Controls_SetEnabled(lua_State* L) {
    psyqo::Lua lua(L);
    if (s_sceneManager && lua.isBoolean(1)) {
        s_sceneManager->setControlsEnabled(lua.toBoolean(1));
    }
    return 0;
}

int LuaAPI::Controls_IsEnabled(lua_State* L) {
    psyqo::Lua lua(L);
    if (s_sceneManager) {
        lua.push(s_sceneManager->isControlsEnabled());
    } else {
        lua.push(false);
    }
    return 1;
}

// ============================================================================
// INTERACT API IMPLEMENTATION
// ============================================================================

int LuaAPI::Interact_SetEnabled(lua_State* L) {
    psyqo::Lua lua(L);
    if (!s_sceneManager || !lua.isTable(1) || !lua.isBoolean(2)) return 0;

    lua.getField(1, "__cpp_ptr");
    auto go = lua.toUserdata<psxsplash::GameObject>(-1);
    lua.pop();

    if (go && go->hasInteractable()) {
        auto* inter = s_sceneManager->getInteractable(go->interactableIndex);
        if (inter) {
            inter->setDisabled(!lua.toBoolean(2));
        }
    }
    return 0;
}

int LuaAPI::Interact_IsEnabled(lua_State* L) {
    psyqo::Lua lua(L);
    if (!s_sceneManager || !lua.isTable(1)) {
        lua.push(false);
        return 1;
    }

    lua.getField(1, "__cpp_ptr");
    auto go = lua.toUserdata<psxsplash::GameObject>(-1);
    lua.pop();

    if (go && go->hasInteractable()) {
        auto* inter = s_sceneManager->getInteractable(go->interactableIndex);
        if (inter) {
            lua.push(!inter->isDisabled());
            return 1;
        }
    }
    lua.push(false);
    return 1;
}

// ============================================================================
// UI API IMPLEMENTATION
// ============================================================================

int LuaAPI::UI_FindCanvas(lua_State* L) {
    psyqo::Lua lua(L);
    if (!s_uiSystem || !lua.isString(1)) {
        lua.pushNumber(-1);
        return 1;
    }
    const char* name = lua.toString(1);
    int idx = s_uiSystem->findCanvas(name);
    lua.pushNumber(static_cast<lua_Number>(idx));
    return 1;
}

int LuaAPI::UI_SetCanvasVisible(lua_State* L) {
    psyqo::Lua lua(L);
    if (!s_uiSystem) return 0;
    int idx;
    // Accept number (index) or string (name)
    if (lua.isNumber(1)) {
        idx = static_cast<int>(lua.toNumber(1));
    } else if (lua.isString(1)) {
        idx = s_uiSystem->findCanvas(lua.toString(1));
    } else {
        return 0;
    }
    bool visible = lua.toBoolean(2);
    s_uiSystem->setCanvasVisible(idx, visible);
    return 0;
}

int LuaAPI::UI_IsCanvasVisible(lua_State* L) {
    psyqo::Lua lua(L);
    if (!s_uiSystem) {
        lua.push(false);
        return 1;
    }
    int idx;
    if (lua.isNumber(1)) {
        idx = static_cast<int>(lua.toNumber(1));
    } else if (lua.isString(1)) {
        idx = s_uiSystem->findCanvas(lua.toString(1));
    } else {
        lua.push(false);
        return 1;
    }
    lua.push(s_uiSystem->isCanvasVisible(idx));
    return 1;
}

int LuaAPI::UI_FindElement(lua_State* L) {
    psyqo::Lua lua(L);
    if (!s_uiSystem || !lua.isNumber(1) || !lua.isString(2)) {
        lua.pushNumber(-1);
        return 1;
    }
    int canvasIdx = static_cast<int>(lua.toNumber(1));
    const char* name = lua.toString(2);
    int handle = s_uiSystem->findElement(canvasIdx, name);
    lua.pushNumber(static_cast<lua_Number>(handle));
    return 1;
}

int LuaAPI::UI_SetVisible(lua_State* L) {
    psyqo::Lua lua(L);
    if (!s_uiSystem || !lua.isNumber(1)) return 0;
    int handle = static_cast<int>(lua.toNumber(1));
    bool visible = lua.toBoolean(2);
    s_uiSystem->setElementVisible(handle, visible);
    return 0;
}

int LuaAPI::UI_IsVisible(lua_State* L) {
    psyqo::Lua lua(L);
    if (!s_uiSystem || !lua.isNumber(1)) {
        lua.push(false);
        return 1;
    }
    int handle = static_cast<int>(lua.toNumber(1));
    lua.push(s_uiSystem->isElementVisible(handle));
    return 1;
}

int LuaAPI::UI_SetText(lua_State* L) {
    psyqo::Lua lua(L);
    if (!s_uiSystem || !lua.isNumber(1)) return 0;
    int handle = static_cast<int>(lua.toNumber(1));
    const char* text = lua.isString(2) ? lua.toString(2) : "";
    s_uiSystem->setText(handle, text);
    return 0;
}

int LuaAPI::UI_SetProgress(lua_State* L) {
    psyqo::Lua lua(L);
    if (!s_uiSystem || !lua.isNumber(1)) return 0;
    int handle = static_cast<int>(lua.toNumber(1));
    int value = static_cast<int>(lua.toNumber(2));
    if (value < 0) value = 0;
    if (value > 100) value = 100;
    s_uiSystem->setProgress(handle, (uint8_t)value);
    return 0;
}

int LuaAPI::UI_GetProgress(lua_State* L) {
    psyqo::Lua lua(L);
    if (!s_uiSystem || !lua.isNumber(1)) {
        lua.pushNumber(0);
        return 1;
    }
    int handle = static_cast<int>(lua.toNumber(1));
    lua.pushNumber(static_cast<lua_Number>(s_uiSystem->getProgress(handle)));
    return 1;
}

int LuaAPI::UI_SetColor(lua_State* L) {
    psyqo::Lua lua(L);
    if (!s_uiSystem || !lua.isNumber(1)) return 0;
    int handle = static_cast<int>(lua.toNumber(1));
    uint8_t r = static_cast<uint8_t>(lua.toNumber(2));
    uint8_t g = static_cast<uint8_t>(lua.toNumber(3));
    uint8_t b = static_cast<uint8_t>(lua.toNumber(4));
    s_uiSystem->setColor(handle, r, g, b);
    return 0;
}

int LuaAPI::UI_SetPosition(lua_State* L) {
    psyqo::Lua lua(L);
    if (!s_uiSystem || !lua.isNumber(1)) return 0;
    int handle = static_cast<int>(lua.toNumber(1));
    int16_t x = static_cast<int16_t>(lua.toNumber(2));
    int16_t y = static_cast<int16_t>(lua.toNumber(3));
    s_uiSystem->setPosition(handle, x, y);
    return 0;
}

int LuaAPI::UI_GetText(lua_State* L) {
    psyqo::Lua lua(L);
    if (!s_uiSystem || !lua.isNumber(1)) {
        lua.push("");
        return 1;
    }
    int handle = static_cast<int>(lua.toNumber(1));
    lua.push(s_uiSystem->getText(handle));
    return 1;
}

int LuaAPI::UI_GetColor(lua_State* L) {
    psyqo::Lua lua(L);
    if (!s_uiSystem || !lua.isNumber(1)) {
        lua.pushNumber(0); lua.pushNumber(0); lua.pushNumber(0);
        return 3;
    }
    int handle = static_cast<int>(lua.toNumber(1));
    uint8_t r, g, b;
    s_uiSystem->getColor(handle, r, g, b);
    lua.pushNumber(r); lua.pushNumber(g); lua.pushNumber(b);
    return 3;
}

int LuaAPI::UI_GetPosition(lua_State* L) {
    psyqo::Lua lua(L);
    if (!s_uiSystem || !lua.isNumber(1)) {
        lua.pushNumber(0); lua.pushNumber(0);
        return 2;
    }
    int handle = static_cast<int>(lua.toNumber(1));
    int16_t x, y;
    s_uiSystem->getPosition(handle, x, y);
    lua.pushNumber(static_cast<lua_Number>(x));
    lua.pushNumber(static_cast<lua_Number>(y));
    return 2;
}

int LuaAPI::UI_SetSize(lua_State* L) {
    psyqo::Lua lua(L);
    if (!s_uiSystem || !lua.isNumber(1)) return 0;
    int handle = static_cast<int>(lua.toNumber(1));
    int16_t w = static_cast<int16_t>(lua.toNumber(2));
    int16_t h = static_cast<int16_t>(lua.toNumber(3));
    s_uiSystem->setSize(handle, w, h);
    return 0;
}

int LuaAPI::UI_GetSize(lua_State* L) {
    psyqo::Lua lua(L);
    if (!s_uiSystem || !lua.isNumber(1)) {
        lua.pushNumber(0); lua.pushNumber(0);
        return 2;
    }
    int handle = static_cast<int>(lua.toNumber(1));
    int16_t w, h;
    s_uiSystem->getSize(handle, w, h);
    lua.pushNumber(static_cast<lua_Number>(w));
    lua.pushNumber(static_cast<lua_Number>(h));
    return 2;
}

int LuaAPI::UI_SetImageUV(lua_State* L) {
    psyqo::Lua lua(L);
    if (!s_uiSystem || !lua.isNumber(1)) return 0;
    int handle = static_cast<int>(lua.toNumber(1));
    int u0 = static_cast<int>(lua.toNumber(2));
    int v0 = static_cast<int>(lua.toNumber(3));
    int u1 = static_cast<int>(lua.toNumber(4));
    int v1 = static_cast<int>(lua.toNumber(5));
    if (u0 < 0) u0 = 0; else if (u0 > 255) u0 = 255;
    if (v0 < 0) v0 = 0; else if (v0 > 255) v0 = 255;
    if (u1 < 0) u1 = 0; else if (u1 > 255) u1 = 255;
    if (v1 < 0) v1 = 0; else if (v1 > 255) v1 = 255;
    s_uiSystem->setImageUV(handle,
        static_cast<uint8_t>(u0), static_cast<uint8_t>(v0),
        static_cast<uint8_t>(u1), static_cast<uint8_t>(v1));
    return 0;
}

int LuaAPI::UI_GetImageUV(lua_State* L) {
    psyqo::Lua lua(L);
    if (!s_uiSystem || !lua.isNumber(1)) {
        lua.pushNumber(0); lua.pushNumber(0);
        lua.pushNumber(0); lua.pushNumber(0);
        return 4;
    }
    int handle = static_cast<int>(lua.toNumber(1));
    uint8_t u0, v0, u1, v1;
    s_uiSystem->getImageUV(handle, u0, v0, u1, v1);
    lua.pushNumber(static_cast<lua_Number>(u0));
    lua.pushNumber(static_cast<lua_Number>(v0));
    lua.pushNumber(static_cast<lua_Number>(u1));
    lua.pushNumber(static_cast<lua_Number>(v1));
    return 4;
}

int LuaAPI::UI_SetProgressColors(lua_State* L) {
    psyqo::Lua lua(L);
    if (!s_uiSystem || !lua.isNumber(1)) return 0;
    int handle = static_cast<int>(lua.toNumber(1));
    uint8_t bgR = static_cast<uint8_t>(lua.toNumber(2));
    uint8_t bgG = static_cast<uint8_t>(lua.toNumber(3));
    uint8_t bgB = static_cast<uint8_t>(lua.toNumber(4));
    uint8_t fR  = static_cast<uint8_t>(lua.toNumber(5));
    uint8_t fG  = static_cast<uint8_t>(lua.toNumber(6));
    uint8_t fB  = static_cast<uint8_t>(lua.toNumber(7));
    s_uiSystem->setProgressColors(handle, bgR, bgG, bgB, fR, fG, fB);
    return 0;
}

int LuaAPI::UI_GetElementType(lua_State* L) {
    psyqo::Lua lua(L);
    if (!s_uiSystem || !lua.isNumber(1)) {
        lua.pushNumber(-1);
        return 1;
    }
    int handle = static_cast<int>(lua.toNumber(1));
    lua.pushNumber(static_cast<lua_Number>(static_cast<uint8_t>(s_uiSystem->getElementType(handle))));
    return 1;
}

int LuaAPI::UI_GetElementCount(lua_State* L) {
    psyqo::Lua lua(L);
    if (!s_uiSystem || !lua.isNumber(1)) {
        lua.pushNumber(0);
        return 1;
    }
    int canvasIdx = static_cast<int>(lua.toNumber(1));
    lua.pushNumber(static_cast<lua_Number>(s_uiSystem->getCanvasElementCount(canvasIdx)));
    return 1;
}

int LuaAPI::UI_GetElementByIndex(lua_State* L) {
    psyqo::Lua lua(L);
    if (!s_uiSystem || !lua.isNumber(1) || !lua.isNumber(2)) {
        lua.pushNumber(-1);
        return 1;
    }
    int canvasIdx = static_cast<int>(lua.toNumber(1));
    int elemIdx = static_cast<int>(lua.toNumber(2));
    int handle = s_uiSystem->getCanvasElementHandle(canvasIdx, elemIdx);
    lua.pushNumber(static_cast<lua_Number>(handle));
    return 1;
}

int LuaAPI::UI_SetModelVisible(lua_State* L) {
    psyqo::Lua lua(L);
    if (!s_sceneManager || !lua.isString(1)) return 0;
    int idx = s_sceneManager->findUIModelByName(lua.toString(1));
    if (idx < 0) return 0;
    auto* st = s_sceneManager->getUIModelState(idx);
    if (st) st->visible = lua.toBoolean(2) ? 1 : 0;
    return 0;
}

int LuaAPI::UI_SetModelOrbit(lua_State* L) {
    psyqo::Lua lua(L);
    if (!s_sceneManager || !lua.isString(1)) return 0;
    int idx = s_sceneManager->findUIModelByName(lua.toString(1));
    if (idx < 0) return 0;
    auto* st = s_sceneManager->getUIModelState(idx);
    if (!st) return 0;

    // Angles: "pi fractions" (1.0 = π) shifted fp12→fp10 (>>2), matching
    // Entity.SetRotationY. Reject non-number gracefully with no-op.
    if (lua.isNumber(2)) {
        psyqo::FixedPoint<12> yaw12 = readFP(lua, 2);
        st->currentYawFp10 = (int16_t)(yaw12.value >> 2);
    }
    if (lua.isNumber(3)) {
        psyqo::FixedPoint<12> pitch12 = readFP(lua, 3);
        st->currentPitchFp10 = (int16_t)(pitch12.value >> 2);
    }
    // Distance optional — keeps authored value if omitted.
    if (!lua.isNoneOrNil(4)) {
        psyqo::FixedPoint<12> dist = readFP(lua, 4);
        st->currentDistFp12 = dist.value;
    }
    return 0;
}

int LuaAPI::UI_SetModel(lua_State* L) {
    psyqo::Lua lua(L);
    if (!s_sceneManager || !lua.isString(1) || !lua.isString(2)) return 0;
    int idx = s_sceneManager->findUIModelByName(lua.toString(1));
    if (idx < 0) return 0;
    auto* st = s_sceneManager->getUIModelState(idx);
    if (!st) return 0;
    GameObject* go = s_sceneManager->findObjectByName(lua.toString(2));
    if (!go) return 0;
    // Walk the scene's GO vector to find the index matching this pointer.
    size_t count = s_sceneManager->getGameObjectCount();
    for (size_t i = 0; i < count; i++) {
        if (s_sceneManager->getGameObject((uint16_t)i) == go) {
            st->currentTargetObj = (uint16_t)i;
            return 0;
        }
    }
    return 0;
}

// ============================================================================
// PLAYER API IMPLEMENTATION
// ============================================================================

int LuaAPI::Player_SetPosition(lua_State* L) {
    psyqo::Lua lua(L);

    if (!s_sceneManager) return 0;
    
    // vec3
    if(lua.isTable(1)){
        psyqo::FixedPoint<12> x, y, z;
        ReadVec3(lua, 1, x, y, z);
        s_sceneManager->setPlayerPosition(x,y,z);
        return 0;
    }
    
    // Three numbers passed in world coordinates 
    if(lua.isNumber(1) && lua.isNumber(2) && lua.isNumber(3)){
        psyqo::FixedPoint<12> x, y, z;
        x = psyqo::FixedPoint<12>(static_cast<int32_t>(lua.toNumber(1)), psyqo::FixedPoint<12>::RAW);
        y = psyqo::FixedPoint<12>(static_cast<int32_t>(lua.toNumber(2)), psyqo::FixedPoint<12>::RAW);
        z = psyqo::FixedPoint<12>(static_cast<int32_t>(lua.toNumber(3)), psyqo::FixedPoint<12>::RAW);

        s_sceneManager->setPlayerPosition(x,y,z);
    }

    return 0;
}

int LuaAPI::Player_GetPosition(lua_State* L) {
    psyqo::Lua lua(L);
    
    if (s_sceneManager) {
        psyqo::Vec3 pos = s_sceneManager->getPlayerPosition();
        PushVec3(lua, pos.x, pos.y, pos.z);
    } else {
        PushVec3(lua, psyqo::FixedPoint<12>(0), psyqo::FixedPoint<12>(0), psyqo::FixedPoint<12>(0));
    }
    return 1;
}

int LuaAPI::Player_SetRotation(lua_State* L) {
    psyqo::Lua lua(L);

    if (!s_sceneManager || !lua.isTable(1)) return 0;
    
    psyqo::FixedPoint<12> x, y, z;
    ReadVec3(lua, 1, x, y, z);

    s_sceneManager->setPlayerRotation(x,y,z);
    
    return 0;
}

int LuaAPI::Player_GetRotation(lua_State* L) {
    psyqo::Lua lua(L);

    if (s_sceneManager) {
        psyqo::Vec3 pos = s_sceneManager->getPlayerRotation();
        PushVec3(lua, pos.x, pos.y, pos.z);
    } else {
        PushVec3(lua, psyqo::FixedPoint<12>(0), psyqo::FixedPoint<12>(0), psyqo::FixedPoint<12>(0));
    }
    return 1;
}

// ============================================================================
// PHYSICS API IMPLEMENTATION
// ============================================================================

int LuaAPI::Physics_Raycast(lua_State* L) {
    psyqo::Lua lua(L);
    if (!s_sceneManager || !lua.isTable(1) || !lua.isTable(2)) {
        lua_pushnil(L);
        return 1;
    }

    psyqo::Vec3 origin, dir;
    ReadVec3(lua, 1, origin.x, origin.y, origin.z);
    ReadVec3(lua, 2, dir.x,    dir.y,    dir.z);

    // Zero-dir would fall through the slab test's "parallel axis" guard three
    // times and report distance 0 whenever origin sits inside any box. Reject
    // explicitly so callers notice the bug instead of seeing phantom hits.
    if (dir.x.value == 0 && dir.y.value == 0 && dir.z.value == 0) {
        lua_pushnil(L);
        return 1;
    }

    // Default range of 10000 world units — large enough that jam authors who
    // forget to pass maxDist don't see silent misses. Real scenes top out
    // around a few hundred units.
    psyqo::FixedPoint<12> maxDist = lua.isNoneOrNil(3)
        ? psyqo::FixedPoint<12>(10000)
        : readFP(lua, 3);

    uint16_t hitIdx;
    psyqo::FixedPoint<12> hitT;
    if (!s_sceneManager->getCollision().raycast(origin, dir, maxDist, hitIdx, hitT)) {
        lua_pushnil(L);
        return 1;
    }

    // Build { object = <idx>, distance = <t>, point = {x,y,z} }
    lua.newTable();
    lua.push((lua_Number)hitIdx);
    lua.setField(-2, "object");
    lua.push(hitT);
    lua.setField(-2, "distance");
    PushVec3(lua,
        origin.x + dir.x * hitT,
        origin.y + dir.y * hitT,
        origin.z + dir.z * hitT);
    lua.setField(-2, "point");
    return 1;
}

int LuaAPI::Physics_OverlapBox(lua_State* L) {
    psyqo::Lua lua(L);
    // Always return at least an empty table — Lua scripts iterate result with
    // ipairs/for which crashes on nil but no-ops on empty.
    if (!s_sceneManager || !lua.isTable(1) || !lua.isTable(2)) {
        lua.newTable();
        return 1;
    }

    AABB query;
    ReadVec3(lua, 1, query.min.x, query.min.y, query.min.z);
    ReadVec3(lua, 2, query.max.x, query.max.y, query.max.z);

    bool hasTagFilter = !lua.isNoneOrNil(3);
    uint16_t tagFilter = hasTagFilter ? (uint16_t)lua.toNumber(3) : 0;
    // Tag 0 is the untagged sentinel (see Entity.SetTag); treat it as "no
    // filter" rather than "match only tag-0 objects."
    if (hasTagFilter && tagFilter == 0) hasTagFilter = false;

    static constexpr int MAX_OVERLAP_RESULTS = 16;
    uint16_t goIndices[MAX_OVERLAP_RESULTS];
    int hitCount = s_sceneManager->getCollision().overlapBox(
        query, goIndices, MAX_OVERLAP_RESULTS);

    lua.newTable();
    int outIdx = 1;
    for (int i = 0; i < hitCount; i++) {
        auto* go = s_sceneManager->getGameObject(goIndices[i]);
        if (!go || !go->isActive()) continue;
        if (hasTagFilter && go->tag != tagFilter) continue;
        PushGameObjectHandle(lua, go);
        lua.rawSetI(-2, outIdx++);
    }
    return 1;
}

}  // namespace psxsplash
