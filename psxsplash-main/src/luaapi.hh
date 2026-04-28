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
    // True if the object is currently active (visible + ticking).
    static int Entity_IsActive(lua_State* L);

    // Entity.GetPosition(object) -> {x, y, z}
    // World-space position as a Vec3 table. Components are FixedPoint<12>.
    static int Entity_GetPosition(lua_State* L);

    // Entity.SetPosition(object, {x, y, z})
    // Teleports the object to the given world-space position. Does NOT
    // run any physics resolve — use Physics.Raycast / OverlapBox first
    // if you need to avoid clipping into walls.
    static int Entity_SetPosition(lua_State* L);

    // Entity.GetRotationY(object) -> number
    // Yaw rotation in "pi fractions": 1.0 = π radians = 180°. So 0.5 = 90°,
    // 0.25 = 45°. NOT raw radians — matches Entity.SetRotationY's input.
    static int Entity_GetRotationY(lua_State* L);

    // Entity.SetRotationY(object, angle) -> nil
    // Sets yaw rotation in "pi fractions" (1.0 = π, 0.5 = 90°, 0.25 = 45°).
    // The PS1Godot runtime uses pi-fraction angles everywhere to dodge
    // floating-point conversion overhead on PSX hardware.
    static int Entity_SetRotationY(lua_State* L);

    // Entity.ForEach(callback) -> nil
    // Calls callback(object, index) for each active game object. Useful
    // for global iteration like "stop every enemy" or "log every NPC".
    // Skips inactive objects so pool reserves are invisible to the loop.
    static int Entity_ForEach(lua_State* L);

    // Entity.GetTag(object) -> number
    // Returns the gameplay tag (0 = untagged). Tags group objects by
    // role for FindByTag / Spawn / FindNearest queries.
    static int Entity_GetTag(lua_State* L);

    // Entity.SetTag(object, tag)
    // Reassigns the gameplay tag. Pass 0 to clear. Tag 0 is reserved
    // for "untagged" — Entity.Spawn rejects tag 0 lookups.
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
    // Construct a Vec3 table. Most APIs that take positions / directions
    // accept any {x,y,z} table; this is just the canonical builder.
    static int Vec3_New(lua_State* L);

    // Vec3.add(a, b) -> {x, y, z}
    // Component-wise sum. Use to translate a position by an offset.
    static int Vec3_Add(lua_State* L);

    // Vec3.sub(a, b) -> {x, y, z}
    // Component-wise difference (a - b). Use to compute "from b toward a"
    // as a direction; pair with Vec3.normalize for a unit vector.
    static int Vec3_Sub(lua_State* L);

    // Vec3.mul(v, scalar) -> {x, y, z}
    // Scale each component by `scalar`. Use to extend a direction vector
    // by a distance, or to invert (multiply by -1).
    static int Vec3_Mul(lua_State* L);

    // Vec3.dot(a, b) -> number
    // Scalar dot product. Returns positive when vectors point the same
    // way, zero when perpendicular, negative when opposite. Combine with
    // Vec3.normalize for "is this enemy in front of me" checks.
    static int Vec3_Dot(lua_State* L);

    // Vec3.cross(a, b) -> {x, y, z}
    // Vector cross product, returning a vector perpendicular to both
    // inputs. Right-hand rule (Y-up). Useful for "build a side vector
    // from forward + up" or surface-normal calculations.
    static int Vec3_Cross(lua_State* L);

    // Vec3.length(v) -> number
    // Euclidean length. Slower than lengthSq because of the sqrt; prefer
    // lengthSq when you only need to compare distances.
    static int Vec3_Length(lua_State* L);

    // Vec3.lengthSq(v) -> number
    // Squared length — faster than length() because it skips the sqrt.
    // Use this for distance comparisons (squared compares are stable
    // since sqrt is monotonic on non-negative values).
    static int Vec3_LengthSq(lua_State* L);

    // Vec3.normalize(v) -> {x, y, z}
    // Returns the unit-length vector pointing in the same direction as
    // `v`. Returns the zero vector when input length is ~0.
    static int Vec3_Normalize(lua_State* L);

    // Vec3.distance(a, b) -> number
    // Euclidean distance between two points. Slower than distanceSq.
    static int Vec3_Distance(lua_State* L);

    // Vec3.distanceSq(a, b) -> number
    // Squared distance between two points. Faster than distance(); use
    // for "is enemy within range" checks where you can square the range
    // once instead of square-rooting every frame.
    static int Vec3_DistanceSq(lua_State* L);

    // Vec3.lerp(a, b, t) -> {x, y, z}
    // Linear interpolation: returns a when t=0, b when t=1, blend in
    // between. Useful for smooth camera follow, animation tween,
    // anything that needs to move from one position to another over
    // time without writing the math each call.
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
    // GAMESTATE API - Cross-script game-mode + chunk awareness
    //
    // Lightweight shared state for "what is the game doing right now?" so
    // scripts don't reinvent it via _G hacks or per-script polling. Mode
    // is a free-form short string (typical values: "explore", "battle",
    // "dialogue", "menu", "cutscene", "paused"); chunk is the active
    // scene/area id authors set when transitions land. Both reset to
    // empty on scene load via ResetFrameCount + the scenemanager init
    // path. See docs/ps1_lua_scripting_cross_entity_state_architecture.md.
    // ========================================================================
public:
    // Reset GameState (called on scene load alongside frame-counter reset).
    static void ResetGameState();

private:
    // GameState.Frame() -> number
    // Frames since the current scene loaded. Alias for Timer.GetFrameCount;
    // grouped here for scripts that read all "what's the game doing?"
    // state from the GameState table.
    static int GameState_Frame(lua_State* L);

    // GameState.GetMode() -> string
    // Current game-mode string ("explore" / "battle" / "dialogue" / "menu" /
    // "cutscene" / "paused" / etc.). Empty string until something sets it.
    static int GameState_GetMode(lua_State* L);

    // GameState.SetMode(name)
    // Set the current game-mode string. Free-form; convention picks one of
    // "explore" / "battle" / "dialogue" / "menu" / "cutscene" / "paused".
    // Resets to "" on scene load.
    static int GameState_SetMode(lua_State* L);

    // GameState.IsMode(name) -> boolean
    // Convenience: true when GetMode() == name. Saves the explicit compare.
    static int GameState_IsMode(lua_State* L);

    // GameState.GetChunk() -> string
    // Current chunk / area id. Authors set this on transitions so other
    // scripts can branch on location. Empty string until something sets it.
    static int GameState_GetChunk(lua_State* L);

    // GameState.SetChunk(id)
    // Set the current chunk / area id. Free-form string. Resets to "" on
    // scene load.
    static int GameState_SetChunk(lua_State* L);

    // GameState.IsChunk(id) -> boolean
    // Convenience: true when GetChunk() == id.
    static int GameState_IsChunk(lua_State* L);

    // ========================================================================
    // CAMERA API - Camera control
    // ========================================================================
    
    // Camera.GetPosition() -> {x, y, z}
    // Active camera's world-space position. Coordinates are PSX-runtime
    // units, not Godot editor units (mesh export divides Godot positions
    // by GteScaling and Y/Z-flips them; Lua camera coords are post-flip).
    static int Camera_GetPosition(lua_State* L);

    // Camera.SetPosition(x, y, z)
    // Teleports the active camera to the given world-space position.
    // Coords are PSX-runtime units (post-export, post-flip).
    static int Camera_SetPosition(lua_State* L);

    // Camera.GetRotation() -> {x, y, z}
    // Active camera's Euler rotation as a Vec3 of pi-fractions
    // ({pitch, yaw, roll}, each 1.0 = π).
    static int Camera_GetRotation(lua_State* L);

    // Camera.SetRotation(x, y, z)
    // Sets active camera's Euler rotation in pi-fractions
    // (1.0 = π, 0.5 = 90°). Order is {pitch, yaw, roll}. NOTE: positive
    // pitch tilts the view UP in psyqo's matrix; to look DOWN at a
    // floor pass a small or negative pitch.
    static int Camera_SetRotation(lua_State* L);

    // Camera.GetForward() -> {x, y, z}
    // Returns the camera's forward direction as a unit Vec3 (post-rotation
    // local Z). Useful for "shoot from camera" or "project NPC onto camera
    // facing" math.
    static int Camera_GetForward(lua_State* L);

    // Camera.MoveForward(step)
    // Translates the camera by `step` units along its forward direction.
    // Positive step = forward, negative = backward.
    static int Camera_MoveForward(lua_State* L);

    // Camera.MoveBackward(step)
    // Translates the camera by `step` units along its backward direction.
    // Equivalent to MoveForward(-step) but reads more clearly in scripts.
    static int Camera_MoveBackward(lua_State* L);

    // Camera.MoveLeft(step)
    // Translates the camera by `step` units along its left side (the
    // negative-X local axis after rotation).
    static int Camera_MoveLeft(lua_State* L);

    // Camera.MoveRight(step)
    // Translates the camera by `step` units along its right side
    // (positive-X local axis after rotation).
    static int Camera_MoveRight(lua_State* L);

    // Camera.FollowPsxPlayer()
    // Switch to follow-player mode: the camera tracks PsxPlayer using the
    // configured rig offset (PS1Player camera offset + avatar offset). Use
    // to return to "default" behaviour after a manual SetPosition / LookAt.
    static int Camera_FollowPsxPlayer(lua_State* L);

    // Camera.SetMode("first"|"third") — flips between 1st and 3rd-person
    // camera. Avatar mesh (if any) auto-hides in 1st-person.
    static int Camera_SetMode(lua_State* L);

    // Camera.LookAt(target) or Camera.LookAt(x, y, z)
    // Aim the camera so its forward axis points at the target position
    // (a Vec3 table or three scalars). NOTE: the runtime currently
    // ignores this call (stub). Compute pitch/yaw via atan2 and use
    // Camera.SetRotation(Vec3.new(pitch, yaw, 0)) until this lands.
    static int Camera_LookAt(lua_State* L);

    // Camera.GetH() -> number
    // Returns the current GTE projection H register (the "screen-distance"
    // value). Affects perceived focal length / FOV. Default is ~320.
    static int Camera_GetH(lua_State* L);

    // Camera.SetH(h) -> nil
    // Sets the GTE projection H register, clamped to [1, 1024]. Higher H
    // = narrower FOV (telephoto); lower = wider (fisheye). Use for
    // cutscene zoom, scope-aim-down-sights, or aesthetic FOV pulses.
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
    // Stops the SFX voice on the given channel id (returned by Audio.Play).
    // No-op if the channel finished already or was never used.
    static int Audio_Stop(lua_State* L);

    // Audio.SetVolume(channelId, volume)
    // Adjusts the live volume of an SFX voice (channel id from Audio.Play).
    // `volume` is 0..127 — same range Music.SetVolume uses.
    static int Audio_SetVolume(lua_State* L);

    // Audio.StopAll()
    // Silences every SFX voice currently in flight. Doesn't stop music
    // (use Music.Stop) or CDDA (use Audio.StopCDDA). Use for cutscene
    // entry, pause-menu hush, etc.
    static int Audio_StopAll(lua_State* L);

    // Audio.GetClipDuration(nameOrIndex) -> frames
    // Length of a clip in 60 Hz frames. Returns 0 for clips authored as
    // looped (no defined end) or for unknown names. Useful for sync'ing
    // gameplay events to a one-shot SFX's tail (e.g. "wait for door
    // creak to finish before playing dialogue line").
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
    // Starts CDDA playback of the given track number (1-based). CDDA
    // tracks are Red Book audio at the end of the disc — high quality
    // but expensive seeks; reserve for title music and major scene
    // transitions.
    static int Audio_PlayCDDA(lua_State* L);

    // Audio.ResumeCDDA()
    // Resume a paused CDDA track at the position it stopped. No-op if
    // nothing was paused.
    static int Audio_ResumeCDDA(lua_State* L);

    // Audio.PauseCDDA()
    // Pause CDDA playback at the current position. Audio.ResumeCDDA
    // continues; Audio.StopCDDA discards position.
    static int Audio_PauseCDDA(lua_State* L);

    // Audio.StopCDDA()
    // Stop CDDA playback. Position is lost — next Audio.PlayCDDA starts
    // from track beginning.
    static int Audio_StopCDDA(lua_State* L);

    // Audio.TellCDDA() -> {min, sec, frame}
    // Current CDDA playback position as MSF (minute, second, frame) —
    // CD-Audio's native time format. Use for syncing gameplay to track
    // beats or showing playback time in UI.
    static int Audio_TellCDDA(lua_State* L);

    // Audio.SetCDDAVolume(volume)
    // CDDA-specific master volume (0..127). Independent from SFX volume
    // and the music sequencer's own volume. Use for crossfade-on-pause
    // or "duck the music" gameplay moments.
    static int Audio_SetCDDAVolume(lua_State* L);

    // ========================================================================
    // MUSIC API - Sequenced music playback (PS1M format)
    // ========================================================================

    // Music.Play(name[, volume]) or Music.Play(index[, volume])
    // Starts a sequenced music track from the scene's music bank by name
    // or index. `volume` is 0..127, defaults to 100. Stops any currently-
    // playing sequence. Returns nothing — use Music.IsPlaying to check.
    static int Music_Play(lua_State* L);

    // Music.Stop()
    // Stops the active music sequence. Voices fade out over their
    // remaining release time; instruments don't cut hard.
    static int Music_Stop(lua_State* L);

    // Music.IsPlaying() -> boolean
    // True if a music sequence is currently active (not stopped, not
    // ended).
    static int Music_IsPlaying(lua_State* L);

    // Music.SetVolume(v)
    // Master sequencer volume (0..127). Independent from per-instrument
    // volumes set in the music bank. Use for fades, ducking, mixer.
    static int Music_SetVolume(lua_State* L);

    // Music.GetBeat() -> integer
    // Returns the integer beat count since the active sequence started,
    // or 0 when nothing is playing. Use for rhythmic gameplay (sync a
    // light flicker, an enemy hop, etc. to the beat).
    static int Music_GetBeat(lua_State* L);

    // Music.Find(name) -> index or nil
    // Look up a sequence by name in the scene's music bank. Returns its
    // index (faster than passing the name to Music.Play repeatedly), or
    // nil if not found.
    static int Music_Find(lua_State* L);

    // Hash of the most recent kind=8 marker the active sequence
    // fired, or 0 if none. Reset on each Music.Play. Compare against
    // Music.MarkerHash for identification.
    // Music.GetLastMarkerHash() -> integer (16-bit hash, 0 if none)
    static int Music_GetLastMarkerHash(lua_State* L);

    // FNV-1a folded hash of a marker name. Bit-exact match for the
    // exporter's PS1MSerializer.MarkerHash16, so script-side compares
    // work.
    // Music.MarkerHash(text) -> integer (16-bit hash)
    static int Music_MarkerHash(lua_State* L);

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
    // Plays a "sound macro" — a composite SFX sequence the bank author
    // built from multiple clips with delays/volumes/pitches. Returns a
    // handle for later cancellation, or nil if the macro name isn't in
    // the scene's sound bank.
    static int Sound_PlayMacro(lua_State* L);

    // Sound.PlayFamily(name) -> channel or nil
    // Plays a random clip from a "sound family" — a variation pool used
    // for footsteps, impacts, etc. so repetition stays varied. Returns
    // the SFX channel id (same shape as Audio.Play returns), or nil if
    // the family name isn't found.
    static int Sound_PlayFamily(lua_State* L);

    // Sound.StopAll()
    // Silences every macro currently in flight. Doesn't affect family
    // playback (those use the standard SFX channels and respond to
    // Audio.StopAll instead).
    static int Sound_StopAll(lua_State* L);

    // ========================================================================
    // DEBUG API - Development helpers
    // ========================================================================
    
    // Debug.Log(message)
    // Writes `message` to the PSX debug console (visible in PCSX-Redux's
    // log pane via the printf hook). Free-form string; numbers are
    // auto-stringified. Strip Debug.Log calls before shipping — they
    // cost cycles on real hardware.
    static int Debug_Log(lua_State* L);

    // Debug.DrawLine(start, end, color)
    // Queues a 1-frame debug line from `start` to `end` (Vec3 tables) in
    // the given color. Drawn next render pass; vanishes after one frame.
    // Use for AI raycasts, navigation graphs, hit-test visualisation.
    static int Debug_DrawLine(lua_State* L);

    // Debug.DrawBox(center, size, color)
    // Queues a 1-frame debug box at `center` (Vec3) with `size` (Vec3,
    // full extents) in `color`. Drawn next render pass. Use for AABB
    // queries, trigger volume preview, level-design checks.
    static int Debug_DrawBox(lua_State* L);
    
    // ========================================================================
    // CONVERT API - Extra functions for working with fixed point numbers 
    // ========================================================================

    // Convert.IntToFp(intValue) -> FixedPoint
    // Promotes a plain integer to a FixedPoint<12>. `Convert.IntToFp(3)`
    // = the FP value for 3.0. Same as Math.ToFixed; both names exist
    // for muscle-memory parity with older PS1 dev conventions.
    static int Convert_IntToFp(lua_State* L);

    // Convert.FpToInt(fpValue) -> integer
    // Truncates a FixedPoint<12> toward zero, returning the integer
    // part. Use Math.Floor / Math.Ceil / Math.Round when you need
    // different rounding semantics.
    static int Convert_FpToInt(lua_State* L);

    // ========================================================================
    // MATH API - Additional math functions
    // ========================================================================
    
    // Math.Clamp(value, min, max) -> number
    // Returns `value` constrained to the [min, max] range.
    static int Math_Clamp(lua_State* L);

    // Math.Lerp(a, b, t) -> number
    // Linear interpolation: returns `a` when t=0, `b` when t=1, blend
    // in between. Scalar version of Vec3.lerp.
    static int Math_Lerp(lua_State* L);

    // Math.Sign(value) -> number
    // Returns -1 for negative, 0 for zero, +1 for positive. Useful for
    // flipping facing direction or "which way to step" decisions.
    static int Math_Sign(lua_State* L);

    // Math.Abs(value) -> number
    // Absolute value. Works on both integers and FixedPoint<12>.
    static int Math_Abs(lua_State* L);

    // Math.Min(a, b) -> number
    // Smaller of the two values. For lists, fold pair-wise.
    static int Math_Min(lua_State* L);

    // Math.Max(a, b) -> number
    // Larger of the two values.
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

    // Random.Number(max) -> integer
    // One-shot dice roll in the range [1, max]. The seed is auto-mixed
    // with the current frame count, so consecutive same-frame calls in
    // different scripts still differ. Use for "throw-away" randomness
    // (sparkle jitter, hit-flash variations) where determinism doesn't
    // matter. For reproducible sequences, use Random.GeneratorNumber.
    static int Random_Number(lua_State* L);

    // Random.GeneratorNumber(max) -> integer
    // Deterministic dice roll in [1, max] from the seedable generator.
    // Pair with Random.Seed for reproducible sequences (replays, daily-
    // challenge dungeons, save-respecting loot tables).
    static int Random_GeneratorNumber(lua_State* L);

    // Random.Range(min, max) -> integer
    // One-shot integer in [min, max]. Same frame-mixed pool as
    // Random.Number — convenient when you don't want a +1 offset.
    static int Random_Range(lua_State* L);

    // Random.GeneratorRange(min, max) -> integer
    // Deterministic integer in [min, max] from the seedable generator.
    static int Random_GeneratorRange(lua_State* L);

    // Random.Seed(seed)
    // Re-seeds the deterministic generator used by Random.Generator*.
    // A seed of 0 is silently rewritten to 108 (avoids the all-zero
    // degenerate state). Doesn't affect Random.Number / Random.Range,
    // which always frame-mix. Call once at scene start for reproducible
    // runs; call again to reset between attempts.
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
    // Reads a numeric value previously stored with Persist.Set.
    // Returns nil if the key was never set. Persistent storage is
    // RAM-only (cleared on power-cycle) — survives Scene.Load but not
    // a console reset. 16 slots total, 32-char key max.
    static int Persist_Get(lua_State* L);

    // Persist.Set(key, value)
    // Stores a number under `key` so it survives Scene.Load. Use for
    // run-state that crosses scene boundaries (player HP, score,
    // cutscene-flags, inventory counts). Silently no-ops when the
    // 16-slot table is full. Long-term saves need a real save system.
    static int Persist_Set(lua_State* L);

    // Reset all persistent data
    static void PersistClear();
    
    // ========================================================================
    // CUTSCENE API - Cutscene playback control
    // ========================================================================
    
    // Cutscene.Play(name) or Cutscene.Play(name, {loop=bool, onComplete=fn})
    // Starts a pre-authored cutscene by name. Cutscenes are scene-wide
    // timelines that can drive cameras, animations, and audio together.
    // Pass `loop=true` to repeat at end; pass `onComplete=function`
    // to run a callback when the cutscene finishes (skipped if looped).
    // Calling again replaces any active cutscene.
    static int Cutscene_Play(lua_State* L);

    // Cutscene.Stop()
    // Immediately ends the active cutscene. The onComplete callback is
    // NOT fired (those only run on natural finish).
    static int Cutscene_Stop(lua_State* L);

    // Cutscene.IsPlaying() -> boolean
    // True while a cutscene is running. Use to gate input
    // (`if not Cutscene.IsPlaying() then ... end`) so the player can't
    // act mid-scene.
    static int Cutscene_IsPlaying(lua_State* L);

    // ========================================================================
    // ANIMATION API - Multi-instance animation playback
    // ========================================================================

    // Animation.Play(name) or Animation.Play(name, {loop=bool, onComplete=fn})
    // Plays a non-skinned object animation by name (transform tracks
    // baked at export). Multiple animations can run concurrently —
    // the name is the lookup key, NOT a global "current clip" slot.
    // `loop=true` repeats forever; `onComplete` fires once on natural
    // finish (skipped when looped). For skinned characters, use
    // SkinnedAnim.Play instead.
    static int Animation_Play(lua_State* L);

    // Animation.Stop(name)  -- or Animation.Stop() to stop all
    // Halts an active animation by name; pass no args to stop every
    // animation in flight. The onComplete callback does NOT fire.
    static int Animation_Stop(lua_State* L);

    // Animation.IsPlaying(name) -> boolean
    // True while the named animation is running. Returns false for
    // unknown names (no error).
    static int Animation_IsPlaying(lua_State* L);

    // ========================================================================
    // SKINNED ANIMATION API - Bone-based mesh animation
    // ========================================================================

    // SkinnedAnim.Play(objectName, clipName)
    //   or SkinnedAnim.Play(objectName, clipName, {loop=bool, onComplete=fn})
    // Plays a bone-driven clip on a skinned mesh (Mixamo character,
    // creature with rig, etc.). `objectName` is the GameObject name
    // that owns the rig; `clipName` is the authored clip on that rig.
    // Replaces any clip already playing on that object. Note that
    // Mixamo clip names with dots are rewritten to underscores at
    // export time — match the exporter output, not the FBX label.
    static int SkinnedAnim_Play(lua_State* L);

    // SkinnedAnim.Stop(objectName)
    // Halts the rig's current clip. The mesh stays frozen on the last
    // rendered frame — call SkinnedAnim.BindPose to reset to T-pose.
    static int SkinnedAnim_Stop(lua_State* L);

    // SkinnedAnim.IsPlaying(objectName) -> boolean
    // True while the rig is animating. Returns false for unknown
    // objects or after Stop / BindPose.
    static int SkinnedAnim_IsPlaying(lua_State* L);

    // SkinnedAnim.GetClip(objectName) -> string or nil
    // Name of the clip the rig is currently playing, or nil if the
    // object isn't skinned / has no clip set. Useful for state-machine
    // logic ("if not idle, queue idle").
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

    // Controls.SetEnabled(bool)
    // Master switch for player input. When false, the player pawn
    // ignores stick + button events but the camera, cutscenes, music,
    // and Lua onUpdate keep running. Use during dialogue, menus, or
    // hit-stun. Pair with Controls.IsEnabled to gate UI actions.
    static int Controls_SetEnabled(lua_State* L);

    // Controls.IsEnabled() -> boolean
    // True if the player input pipeline is currently active.
    static int Controls_IsEnabled(lua_State* L);

    // Interact.SetEnabled(object, bool)
    // Toggles whether the given GameObject participates in the "press
    // X to interact" pipeline. Disabling hides the prompt AND blocks
    // the on-interact callback. Use to "consume" an interaction
    // (one-time chests, conversations that shouldn't repeat).
    static int Interact_SetEnabled(lua_State* L);

    // Interact.IsEnabled(object) -> boolean
    // True if the object is interactable AND not currently disabled.
    // False for objects with no Interactable component.
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

    // UI.FindCanvas(name) -> integer
    // Returns the integer handle for a canvas authored in the scene,
    // or -1 if the name isn't found. Cache the handle in onCreate and
    // pass it to every other UI.* call — repeated lookups are wasted
    // string scans. Always guard with `>= 0` before use.
    static int UI_FindCanvas(lua_State* L);

    // UI.SetCanvasVisible(canvas, bool)
    // Show or hide an entire canvas (header bar, pause menu, HUD layer).
    // `canvas` accepts either the integer handle from UI.FindCanvas or
    // the canvas name as a string. Hidden canvases skip layout and
    // draw entirely — cheap to toggle.
    static int UI_SetCanvasVisible(lua_State* L);

    // UI.IsCanvasVisible(canvas) -> boolean
    // True if the canvas is currently rendered. Accepts handle or name.
    static int UI_IsCanvasVisible(lua_State* L);

    // UI.FindElement(canvas, elementName) -> integer
    // Returns the integer handle for a named element on a canvas, or
    // -1 if not found. `canvas` must be the integer handle (NOT the
    // name — the runtime silently returns -1 on string input). Cache
    // returned handles in onCreate.
    static int UI_FindElement(lua_State* L);

    // UI.SetVisible(element, bool)
    // Show or hide a single element (text label, image, progress bar).
    // Cheaper than recreating; the slot is preserved.
    static int UI_SetVisible(lua_State* L);

    // UI.IsVisible(element) -> boolean
    // True if the element will draw this frame.
    static int UI_IsVisible(lua_State* L);

    // UI.SetText(element, str)
    // Replaces the text on a Text element. Empty string clears it.
    // No effect on non-Text elements. Strings are copied — safe to
    // pass scratch buffers.
    static int UI_SetText(lua_State* L);

    // UI.GetText(element) -> string
    // Returns the current Text element string, or "" for non-Text /
    // unknown handles.
    static int UI_GetText(lua_State* L);

    // UI.SetProgress(element, percent)
    // Sets a progress-bar element's fill in percent (0..100, clamped).
    // No effect on non-Progress elements. Use for HP bars, charge
    // gauges, loading meters.
    static int UI_SetProgress(lua_State* L);

    // UI.GetProgress(element) -> integer
    // Returns the bar's current 0..100 fill, or 0 for unknown handles.
    static int UI_GetProgress(lua_State* L);

    // UI.SetColor(element, r, g, b)
    // Sets the element's tint (0..255 per channel). For Text this is
    // the glyph color; for Image / Box it modulates the texture / fill.
    // Doesn't affect transparency — that's authored, not Lua-controlled.
    static int UI_SetColor(lua_State* L);

    // UI.GetColor(element) -> r, g, b
    // Returns the three 0..255 tint channels (multi-return). For
    // unknown handles returns 0, 0, 0.
    static int UI_GetColor(lua_State* L);

    // UI.SetPosition(element, x, y)
    // Moves the element to screen pixel (x, y). Origin is the canvas
    // top-left. Coordinates are signed 16-bit so off-screen positions
    // are valid (handy for slide-in animations).
    static int UI_SetPosition(lua_State* L);

    // UI.GetPosition(element) -> x, y
    // Returns the element's current top-left in canvas pixels
    // (multi-return).
    static int UI_GetPosition(lua_State* L);

    // UI.SetSize(element, w, h)
    // Resizes the element. For Image / Box this is the rect dimensions;
    // for Text it's the wrap width / max height. Pixel units.
    static int UI_SetSize(lua_State* L);

    // UI.GetSize(element) -> w, h
    // Returns current width, height in pixels (multi-return).
    static int UI_GetSize(lua_State* L);

    // UI.SetImageUV(element, u0, v0, u1, v1)
    // Sets the source rect inside the texture atlas for an Image
    // element. UVs are 0..255 (PSX texel coords inside one page),
    // clamped to that range. Use to scroll a texture, swap atlas
    // sub-rects (icon variants, animated frames).
    static int UI_SetImageUV(lua_State* L);

    // UI.GetImageUV(element) -> u0, v0, u1, v1
    // Returns the four 0..255 UV components (multi-return).
    static int UI_GetImageUV(lua_State* L);

    // UI.SetProgressColors(element, bgR, bgG, bgB, fillR, fillG, fillB)
    // Sets both colors of a progress bar in one call: empty-track
    // background and filled-portion foreground. Each channel 0..255.
    // No effect on non-Progress elements.
    static int UI_SetProgressColors(lua_State* L);

    // UI.GetElementType(element) -> integer
    // Returns the element type id (0=Text, 1=Image, 2=Box, 3=Progress,
    // matching ElementType in the runtime). -1 for unknown handles.
    // Use when iterating with UI.GetElementByIndex to branch on type.
    static int UI_GetElementType(lua_State* L);

    // UI.GetElementCount(canvas) -> integer
    // Number of elements authored on the canvas. 0 for unknown
    // canvases. Drives index-based iteration with UI.GetElementByIndex.
    static int UI_GetElementCount(lua_State* L);

    // UI.GetElementByIndex(canvas, index) -> integer
    // Returns the element handle at the given 0-based index inside the
    // canvas, or -1 if out of range. Useful for "walk every element"
    // logic (apply tint, mass-hide, etc.) without naming each one.
    static int UI_GetElementByIndex(lua_State* L);
    
    // ========================================================================
    // PLAYER API - Controlling the PsxPlayer
    // ========================================================================
    
    // Player.SetPosition({x, y, z})  or  Player.SetPosition(x, y, z)
    // Teleports the player pawn to PSX-runtime world coords. Pass either
    // a Vec3 table or three raw fp12 numbers. NOTE: coords are PSX-frame
    // (post-export Y/Z flip and /gteScaling), NOT Godot units. Convert
    // from Godot: x_psx = x_godot/4, y_psx = -y_godot/4, z_psx = -z_godot/4.
    // Use for spawn-points, scene-load placement, debug warps.
    static int Player_SetPosition(lua_State* L);

    // Player.GetPosition() -> Vec3
    // Returns the player's current world position as a Vec3 in PSX
    // coords. Use for distance checks, "is the player in this room"
    // tests, save-state capture. Output is a fresh Vec3 table —
    // mutating it doesn't move the player.
    static int Player_GetPosition(lua_State* L);

    // Player.SetRotation({x, y, z})
    // Sets the player's facing as a Euler triplet in pi-fractions
    // (1.0 = π, same convention as Entity.SetRotation*). Affects
    // movement direction (forward becomes +Z in local space). Use for
    // teleporting "facing the new room" so the camera doesn't snap.
    static int Player_SetRotation(lua_State* L);

    // Player.GetRotation() -> Vec3
    // Returns the player's Euler rotation as a Vec3 (x=pitch, y=yaw,
    // z=roll) in pi-fractions. Use to orient the camera off-rig or
    // record facing for save-state.
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
