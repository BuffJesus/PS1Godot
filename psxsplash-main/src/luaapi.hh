#pragma once

#include <psyqo-lua/lua.hh>
#include <psyqo/fixed-point.hh>
#include <psyqo/vector.hh>

namespace psxsplash {

class SceneManager;  // Forward declaration
class CutscenePlayer;  // Forward declaration
class AnimationPlayer;  // Forward declaration
class UISystem;  // Forward declaration

/**
 * Lua API - Provides game scripting functionality
 * 
 * Available namespaces:
 * - Entity: Object finding, spawning, destruction
 * - Vec3: Vector math operations  
 * - Input: Controller state queries
 * - Timer: Timer control
 * - Camera: Camera manipulation
 * - Audio: Sound playback (future)
 * - Scene: Scene management
 */
class LuaAPI {
public:
    // Initialize all API modules
    static void RegisterAll(psyqo::Lua& L, SceneManager* scene, CutscenePlayer* cutscenePlayer = nullptr, AnimationPlayer* animationPlayer = nullptr, UISystem* uiSystem = nullptr);
    
    // Called once per frame to advance the Lua frame counter
    static void IncrementFrameCount();
    
    // Reset frame counter (called on scene load)
    static void ResetFrameCount();
    
private:
    // Store scene manager for API access
    static SceneManager* s_sceneManager;
    
    // Cutscene player pointer (set during RegisterAll)
    static CutscenePlayer* s_cutscenePlayer;

    // Animation player pointer (set during RegisterAll)
    static AnimationPlayer* s_animationPlayer;
    
    // UI system pointer (set during RegisterAll)
    static UISystem* s_uiSystem;
    
    // ========================================================================
    // ENTITY API
    // ========================================================================
    
    // Entity.FindByScriptIndex(index) -> object or nil
    // Finds first object with matching Lua script file index
    static int Entity_FindByScriptIndex(lua_State* L);
    
    // Entity.FindByIndex(index) -> object or nil
    // Gets object by its array index
    static int Entity_FindByIndex(lua_State* L);
    
    // Entity.Find(name) -> object or nil
    // Finds first object with matching name (user-friendly)
    static int Entity_Find(lua_State* L);
    
    // Entity.GetCount() -> number
    // Returns total number of game objects
    static int Entity_GetCount(lua_State* L);
    
    // Entity.SetActive(object, active)
    // Sets object active state (fires onEnable/onDisable)
    static int Entity_SetActive(lua_State* L);
    
    // Entity.IsActive(object) -> boolean
    static int Entity_IsActive(lua_State* L);
    
    // Entity.GetPosition(object) -> {x, y, z}
    static int Entity_GetPosition(lua_State* L);
    
    // Entity.SetPosition(object, {x, y, z})
    static int Entity_SetPosition(lua_State* L);
    
    // Entity.GetRotationY(object) -> number (radians)
    static int Entity_GetRotationY(lua_State* L);
    
    // Entity.SetRotationY(object, angle) -> nil
    static int Entity_SetRotationY(lua_State* L);
    
    // Entity.ForEach(callback) -> nil
    // Calls callback(object, index) for each active game object
    static int Entity_ForEach(lua_State* L);

    // Entity.GetTag(object) -> number
    static int Entity_GetTag(lua_State* L);

    // Entity.SetTag(object, tag)
    static int Entity_SetTag(lua_State* L);

    // Entity.FindByTag(tag) -> object or nil
    // Returns the first ACTIVE GameObject whose tag matches.
    static int Entity_FindByTag(lua_State* L);

    // Entity.Spawn(tag, {x,y,z} [, rotY]) -> object or nil
    // Finds the first INACTIVE GameObject whose tag matches, activates it
    // (fires onEnable), and writes the new position/rotation. Returns the
    // object handle, or nil if the pool is exhausted or tag is 0.
    //
    // rotY uses the "pi fraction" convention shared with Entity.SetRotationY:
    // 1.0 = π radians = 180°. So 0.5 = 90°, 0.25 = 45°. NOT raw radians.
    //
    // Pool pattern: author places N copies of a template prefab with
    // StartsInactive=true + matching Tag in the editor; Spawn draws from
    // that pool. Per-spawn reset logic should live in the template's
    // onEnable hook (not onCreate, which fires once at scene init).
    static int Entity_Spawn(lua_State* L);

    // Entity.Destroy(object) -> nil
    // Deactivates the object (fires onDisable). Lets the pool re-use it on
    // the next Entity.Spawn with the same tag.
    static int Entity_Destroy(lua_State* L);

    // Entity.FindNearest({x,y,z}, tag) -> object or nil
    // Linear scan of active GameObjects with matching tag, returns the
    // closest. For lock-on, "closest enemy" AI queries, etc.
    static int Entity_FindNearest(lua_State* L);
    
    // ========================================================================
    // VEC3 API - Vector math
    // ========================================================================
    
    // Vec3.new(x, y, z) -> {x, y, z}
    static int Vec3_New(lua_State* L);
    
    // Vec3.add(a, b) -> {x, y, z}
    static int Vec3_Add(lua_State* L);
    
    // Vec3.sub(a, b) -> {x, y, z}
    static int Vec3_Sub(lua_State* L);
    
    // Vec3.mul(v, scalar) -> {x, y, z}
    static int Vec3_Mul(lua_State* L);
    
    // Vec3.dot(a, b) -> number
    static int Vec3_Dot(lua_State* L);
    
    // Vec3.cross(a, b) -> {x, y, z}
    static int Vec3_Cross(lua_State* L);
    
    // Vec3.length(v) -> number  
    static int Vec3_Length(lua_State* L);
    
    // Vec3.lengthSq(v) -> number (faster, no sqrt)
    static int Vec3_LengthSq(lua_State* L);
    
    // Vec3.normalize(v) -> {x, y, z}
    static int Vec3_Normalize(lua_State* L);
    
    // Vec3.distance(a, b) -> number
    static int Vec3_Distance(lua_State* L);
    
    // Vec3.distanceSq(a, b) -> number (faster)
    static int Vec3_DistanceSq(lua_State* L);
    
    // Vec3.lerp(a, b, t) -> {x, y, z}
    static int Vec3_Lerp(lua_State* L);
    
    // ========================================================================
    // INPUT API - Controller state
    // ========================================================================
    
    // Input.IsPressed(button) -> boolean
    // True only on the frame the button was pressed
    static int Input_IsPressed(lua_State* L);
    
    // Input.IsReleased(button) -> boolean
    // True only on the frame the button was released
    static int Input_IsReleased(lua_State* L);
    
    // Input.IsHeld(button) -> boolean
    // True while the button is held down
    static int Input_IsHeld(lua_State* L);
    
    // Input.GetAnalog(stick) -> x, y
    // Returns analog stick values (-128 to 127)
    static int Input_GetAnalog(lua_State* L);
    
    // Button constants (registered as Input.CROSS, Input.CIRCLE, etc.)
    static void RegisterInputConstants(psyqo::Lua& L);
    
    // ========================================================================
    // TIMER API - Frame counter
    // ========================================================================
    
    // Timer.GetFrameCount() -> number
    // Returns total frames since scene start
    static int Timer_GetFrameCount(lua_State* L);
    
    // ========================================================================
    // CAMERA API - Camera control
    // ========================================================================
    
    // Camera.GetPosition() -> {x, y, z}
    static int Camera_GetPosition(lua_State* L);
    
    // Camera.SetPosition(x, y, z)
    static int Camera_SetPosition(lua_State* L);
    
    // Camera.GetRotation() -> {x, y, z}
    static int Camera_GetRotation(lua_State* L);
    
    // Camera.SetRotation(x, y, z)
    static int Camera_SetRotation(lua_State* L);
    
    // Camera.GetForward() 
    static int Camera_GetForward(lua_State* L);

    // Camera.MoveForward(step) 
    static int Camera_MoveForward(lua_State* L);

    // Camera.MoveBackward(step) 
    static int Camera_MoveBackward(lua_State* L);

    // Camera.MoveLeft(step) 
    static int Camera_MoveLeft(lua_State* L);

    // Camera.MoveRight(step) 
    static int Camera_MoveRight(lua_State* L);

    // Camera.FollowPsxPlayer
    static int Camera_FollowPsxPlayer(lua_State* L);

    // Camera.SetMode("first"|"third") — flips between 1st and 3rd-person
    // camera. Avatar mesh (if any) auto-hides in 1st-person.
    static int Camera_SetMode(lua_State* L);

    // Camera.LookAt(target) or Camera.LookAt(x, y, z)
    static int Camera_LookAt(lua_State* L);

    // Camera.GetH() -> number (current projection H register value)
    static int Camera_GetH(lua_State* L);

    // Camera.SetH(h) -> nil (set projection H register, clamped 1-1024)
    static int Camera_SetH(lua_State* L);

    // Camera.Shake(intensity, frames) -> nil
    // Adds random per-frame jitter to the camera position for `frames` frames,
    // decaying linearly to zero. `intensity` is FP12 max offset in world
    // units (e.g., 0.2 for a punchy hit, 0.05 for footstep ambience).
    static int Camera_Shake(lua_State* L);

    // Camera.ShakeRaw(rawFp12, frames) -> nil
    // Same as Camera.Shake but takes intensity as a raw FP12 integer
    // (4096 = 1.0 world unit). Useful from psxlua scripts that can't
    // parse decimal literals like 0.04 — pass 164 to get 164/4096 ≈
    // 0.04 world units of shake.
    static int Camera_ShakeRaw(lua_State* L);
    
    // ========================================================================
    // AUDIO API - Sound playback (placeholder for SPU)
    // ========================================================================
    
    // Audio.Play(soundId, volume, pan) -> channelId
    // soundId can be a number (clip index) or string (clip name)
    static int Audio_Play(lua_State* L);
    
    // Audio.Find(name) -> clipIndex or nil
    // Finds audio clip by name, returns its index for use with Play/Stop/etc.
    static int Audio_Find(lua_State* L);
    
    // Audio.Stop(channelId)
    static int Audio_Stop(lua_State* L);
    
    // Audio.SetVolume(channelId, volume)
    static int Audio_SetVolume(lua_State* L);
    
    // Audio.StopAll()
    static int Audio_StopAll(lua_State* L);

    // Audio.GetClipDuration(nameOrIndex) -> frames (60 Hz) or 0 if unknown/looped
    static int Audio_GetClipDuration(lua_State* L);

    // v25 routing-aware shortcuts. They look up the clip's routing byte
    // (set by the Godot exporter / PS1AudioClip.Route) and dispatch to
    // the right backend so gameplay scripts don't have to know whether
    // a sound lives in SPU RAM or streams from disc.
    //
    // Audio.PlaySfx(name, volume?, pan?) -> channelId
    //   Plays SPU-routed clips. Logs a warning if `name` was authored
    //   as XA/CDDA — call PlayMusic for those instead.
    static int Audio_PlaySfx(lua_State* L);

    // Audio.PlayMusic(name) -> 0 on success, -1 on failure
    //   Resolves clip routing and dispatches: SPU plays via the SFX
    //   path, CDDA logs an error (use PlayCDDA(track) directly), XA
    //   logs "not implemented" (Phase 3 streaming work).
    static int Audio_PlayMusic(lua_State* L);

    // Audio.StopMusic()
    //   Scaffold: stops the music sequencer + CDDA playback. XA path is
    //   a no-op until streaming lands.
    static int Audio_StopMusic(lua_State* L);

    // Audio.PlayCDDA(trackNo)
    static int Audio_PlayCDDA(lua_State* L);

    // Audio.ResumeCDDA()
    static int Audio_ResumeCDDA(lua_State* L);

    // Audio.PauseCDDA()
    static int Audio_PauseCDDA(lua_State* L);

    // Audio.StopCDDA()
    static int Audio_StopCDDA(lua_State* L);

    // Audio.TellCDDA()
    static int Audio_TellCDDA(lua_State* L);

    // Audio.SetCDDAVolume()
    static int Audio_SetCDDAVolume(lua_State* L);

    // ========================================================================
    // MUSIC API - Sequenced music playback (PS1M format)
    // ========================================================================

    // Music.Play(name[, volume]) or Music.Play(index[, volume])
    // volume defaults to 100 (0-127).
    static int Music_Play(lua_State* L);

    // Music.Stop()
    static int Music_Stop(lua_State* L);

    // Music.IsPlaying() -> boolean
    static int Music_IsPlaying(lua_State* L);

    // Music.SetVolume(v) — master volume (0-127)
    static int Music_SetVolume(lua_State* L);

    // Music.GetBeat() -> integer (beats since playback started, or 0 when idle)
    static int Music_GetBeat(lua_State* L);

    // Music.Find(name) -> index or nil
    static int Music_Find(lua_State* L);

    // ========================================================================
    // SOUND API (Phase 5) — composite SFX (macros) + variation pools (families)
    //
    // Macros and families both pull from the SFX voice pool via
    // AudioManager::play. They never reserve voices and never compete
    // with the music sequencer — Phase 4's allocateVoice with the
    // author-set priority handles eviction. Stage A registers stubs
    // that log "not implemented" and return nil/false; Stage B wires
    // the dispatch path against new SoundMacroSequencer + SoundFamily
    // runtime systems.
    // ========================================================================

    // Sound.PlayMacro(name) -> handle or nil
    static int Sound_PlayMacro(lua_State* L);

    // Sound.PlayFamily(name) -> channel or nil
    static int Sound_PlayFamily(lua_State* L);

    // Sound.StopAll() — silences every macro currently in flight.
    static int Sound_StopAll(lua_State* L);

    // ========================================================================
    // DEBUG API - Development helpers
    // ========================================================================
    
    // Debug.Log(message)
    static int Debug_Log(lua_State* L);
    
    // Debug.DrawLine(start, end, color) - draws debug line next frame
    static int Debug_DrawLine(lua_State* L);
    
    // Debug.DrawBox(center, size, color)
    static int Debug_DrawBox(lua_State* L);
    
    // ========================================================================
    // CONVERT API - Extra functions for working with fixed point numbers 
    // ========================================================================

    // Convert.IntToFp(intValue) - Returns fp value
    static int Convert_IntToFp(lua_State* L);

    // Convert.FpToInt(fpValue) - Returns int value
    static int Convert_FpToInt(lua_State* L);

    // ========================================================================
    // MATH API - Additional math functions
    // ========================================================================
    
    // Math.Clamp(value, min, max)
    static int Math_Clamp(lua_State* L);
    
    // Math.Lerp(a, b, t)
    static int Math_Lerp(lua_State* L);
    
    // Math.Sign(value)
    static int Math_Sign(lua_State* L);
    
    // Math.Abs(value)
    static int Math_Abs(lua_State* L);
    
    // Math.Min(a, b)
    static int Math_Min(lua_State* L);
    
    // Math.Max(a, b)
    static int Math_Max(lua_State* L);

    // Math.Floor(fp) -> integer
    // Floors a FixedPoint<12> to the next integer toward -infinity.
    // Accepts either a FixedPoint object or a plain integer (identity).
    static int Math_Floor(lua_State* L);

    // Math.Ceil(fp) -> integer
    // Ceilings a FixedPoint<12> to the next integer toward +infinity.
    static int Math_Ceil(lua_State* L);

    // Math.Round(fp) -> integer
    // Rounds a FixedPoint<12> to the nearest integer. Half-values tie
    // toward +infinity so round(0.5) = 1, round(-0.5) = 0.
    static int Math_Round(lua_State* L);

    // Math.ToInt(fp) -> integer
    // Truncates a FixedPoint<12> toward zero. Inverse of Math.ToFixed
    // for non-negative whole values. Use Floor if you want the
    // round-toward-minus-infinity semantics.
    static int Math_ToInt(lua_State* L);

    // Math.ToFixed(integer) -> FixedPoint
    // Promotes a plain integer to a FixedPoint<12>. Equivalent to
    // FixedPoint.new(integer, 0) but shorter.
    static int Math_ToFixed(lua_State* L);
    
    // ========================================================================
    // RANDOM API - Get random numbers
    // ========================================================================

    // Random.Number(max) returns from 1 to max inclusive
    static int Random_Number(lua_State* L);

    // Random.GeneratorNumber(max) returns from 1 to max inclusive
    static int Random_GeneratorNumber(lua_State* L);

    // Random.Range(min,max) returns from min inclusive to max inclusive 
    static int Random_Range(lua_State* L);

    // Random.GeneratorRange(min,max) returns from min inclusive to max inclusive
    static int Random_GeneratorRange(lua_State* L);

    // Random.Seed(newSeed) sets the seed for the random number generator 
    static int Random_GeneratorSeed(lua_State* L);

    // ========================================================================
    // SCENE API - Scene management
    // ========================================================================
    
    // Scene.Load(sceneIndex)
    // Requests a scene transition to the given index (0-based).
    // The actual load happens at the end of the current frame.
    static int Scene_Load(lua_State* L);
    
    // Scene.GetIndex() -> number
    // Returns the index of the currently loaded scene.
    static int Scene_GetIndex(lua_State* L);

    // Scene.PauseFor(frames) -> nil
    // Hit-stop / freeze. Holds gameplay tick (animation, cutscene, skin,
    // collision, Lua onUpdate, controls, player movement) for `frames`
    // frames while keeping render + camera shake + music alive. Souls /
    // Hades-style impact crunch. Stacks via max(remaining, requested).
    static int Scene_PauseFor(lua_State* L);
    
    // ========================================================================
    // PERSIST API - Data that survives scene loads
    // ========================================================================
    
    // Persist.Get(key) -> number or nil
    static int Persist_Get(lua_State* L);
    
    // Persist.Set(key, value)
    static int Persist_Set(lua_State* L);
    
    // Reset all persistent data
    static void PersistClear();
    
    // ========================================================================
    // CUTSCENE API - Cutscene playback control
    // ========================================================================
    
    // Cutscene.Play(name) or Cutscene.Play(name, {loop=bool, onComplete=fn})
    static int Cutscene_Play(lua_State* L);

    // Cutscene.Stop() -> nil
    static int Cutscene_Stop(lua_State* L);

    // Cutscene.IsPlaying() -> boolean
    static int Cutscene_IsPlaying(lua_State* L);

    // ========================================================================
    // ANIMATION API - Multi-instance animation playback
    // ========================================================================

    // Animation.Play(name) or Animation.Play(name, {loop=bool, onComplete=fn})
    static int Animation_Play(lua_State* L);

    // Animation.Stop(name) -> nil
    static int Animation_Stop(lua_State* L);

    // Animation.IsPlaying(name) -> boolean
    static int Animation_IsPlaying(lua_State* L);

    // ========================================================================
    // SKINNED ANIMATION API - Bone-based mesh animation
    // ========================================================================

    // SkinnedAnim.Play(objectName, clipName) or (objectName, clipName, {loop, onComplete})
    static int SkinnedAnim_Play(lua_State* L);

    // SkinnedAnim.Stop(objectName) -> nil
    static int SkinnedAnim_Stop(lua_State* L);

    // SkinnedAnim.IsPlaying(objectName) -> boolean
    static int SkinnedAnim_IsPlaying(lua_State* L);

    // SkinnedAnim.GetClip(objectName) -> string or nil
    static int SkinnedAnim_GetClip(lua_State* L);

    // SkinnedAnim.BindPose(objectName) -> nil
    // Stop any active clip and render the mesh in its bind pose (T-pose)
    // with identity bone matrices. Use for idle / title-screen states where
    // frame 0 of a walk clip would show a mid-stride pose.
    static int SkinnedAnim_BindPose(lua_State* L);

    // ========================================================================
    // PHYSICS API — ray casts against collider AABBs
    // ========================================================================

    // Physics.Raycast({x,y,z}, {x,y,z}, maxDist) ->
    //   nil on miss, or { object = <goIndex>, distance = <t>, point = {x,y,z} }
    // Tests against all Solid colliders (NOT world geometry triangles). Pass
    // a roughly-unit direction so `distance` is in world units. Safe to call
    // a few times per frame; linear scan over up to 64 colliders.
    static int Physics_Raycast(lua_State* L);

    // Physics.OverlapBox({x,y,z}, {x,y,z} [, tag]) -> array of object handles
    // AABB-vs-AABB overlap query against active Solid colliders. Optional
    // tag filter (0/nil = no filter). Used for melee swings, area damage,
    // pickup proximity. Result table is empty if no hits. Hard-capped at 16
    // results to bound the Lua table allocation on PSX RAM.
    static int Physics_OverlapBox(lua_State* L);

    // Controls.SetEnabled(bool) - enable/disable all player input
    static int Controls_SetEnabled(lua_State* L);

    // Controls.IsEnabled() -> boolean
    static int Controls_IsEnabled(lua_State* L);

    // Interact.SetEnabled(entity, bool) - enable/disable interaction + prompt for an object
    static int Interact_SetEnabled(lua_State* L);

    // Interact.IsEnabled(entity) -> boolean
    static int Interact_IsEnabled(lua_State* L);

    // ========================================================================
    // UI API - Canvas and element control
    // ========================================================================
    
    // v23+: UI 3D-model control. `name` is the model's authored
    // ModelName (unique per scene). Silently no-ops on unknown name.
    //
    // UI.SetModelVisible(name, bool) — show/hide the HUD model
    static int UI_SetModelVisible(lua_State* L);
    // UI.SetModelOrbit(name, yawPi, pitchPi [, distance]) — update the
    // per-frame camera orbit. Angles are "pi fractions" (1.0 = π), same
    // convention as Entity.SetRotationY. distance optional; omit to keep
    // the authored value.
    static int UI_SetModelOrbit(lua_State* L);
    // UI.SetModel(name, gameObjectName) — swap which GameObject the slot
    // renders. Used for inventory icon scrolling (preview slot persists;
    // target object changes per selection).
    static int UI_SetModel(lua_State* L);

    static int UI_FindCanvas(lua_State* L);
    static int UI_SetCanvasVisible(lua_State* L);
    static int UI_IsCanvasVisible(lua_State* L);
    static int UI_FindElement(lua_State* L);
    static int UI_SetVisible(lua_State* L);
    static int UI_IsVisible(lua_State* L);
    static int UI_SetText(lua_State* L);
    static int UI_GetText(lua_State* L);
    static int UI_SetProgress(lua_State* L);
    static int UI_GetProgress(lua_State* L);
    static int UI_SetColor(lua_State* L);
    static int UI_GetColor(lua_State* L);
    static int UI_SetPosition(lua_State* L);
    static int UI_GetPosition(lua_State* L);
    static int UI_SetSize(lua_State* L);
    static int UI_GetSize(lua_State* L);
    static int UI_SetImageUV(lua_State* L);
    static int UI_GetImageUV(lua_State* L);
    static int UI_SetProgressColors(lua_State* L);
    static int UI_GetElementType(lua_State* L);
    static int UI_GetElementCount(lua_State* L);
    static int UI_GetElementByIndex(lua_State* L);
    
    // ========================================================================
    // PLAYER API - Controlling the PsxPlayer
    // ========================================================================
    
    static int Player_SetPosition(lua_State* L);
    static int Player_GetPosition(lua_State* L);
    static int Player_SetRotation(lua_State* L);
    static int Player_GetRotation(lua_State* L);
    
    // ========================================================================
    // HELPERS
    // ========================================================================
    
    // Push a Vec3 table onto the stack
    static void PushVec3(psyqo::Lua& L, psyqo::FixedPoint<12> x, 
                         psyqo::FixedPoint<12> y, psyqo::FixedPoint<12> z);
    
    // Read a Vec3 table from the stack
    static void ReadVec3(psyqo::Lua& L, int idx, 
                         psyqo::FixedPoint<12>& x, 
                         psyqo::FixedPoint<12>& y, 
                         psyqo::FixedPoint<12>& z);
};

}  // namespace psxsplash
