# PS1 Animation Format and Runtime Strategy

**Project target:** PS1Godot / psxsplash / chunk-based PS1-style action RPG  
**Focus:** compact runtime animation formats, binary animation banks, skeletal animation, segmented characters, vertex animation, sprite animation, animation events, and chunk-aware residency.

This document extends the mesh/binary asset strategy into animation.

The core rule is:

```text
Do not ship editor animation formats at runtime.
Export compact binary animation banks that match how the PS1 runtime actually plays animation.
```

Avoid runtime use of:

```text
FBX
glTF
JSON animation dumps
text animation files
full float matrices per bone per frame
large uncompressed vertex caches
```

Use compact fixed-point or quantized binary data.

---

## 1. Core Animation Philosophy

For a PS1-style RPG, animation should be:

```text
small
predictable
chunk-owned
banked
quantized
event-friendly
shared wherever possible
```

The goal is not modern animation fidelity.

The goal is:

```text
readable character motion
cheap runtime playback
small resident data
simple authoring/export workflow
stable performance
```

Use different animation methods for different asset classes.

---

## 2. Animation Method Overview

| Method | Best for | Strengths | Risks |
|---|---|---|---|
| Procedural animation | pickups, bobbing objects, spinning props | no stored data, cheap | limited complexity |
| Rigid transform keys | doors, elevators, traps, cutscene props | compact, easy | not good for deforming characters |
| Segmented rigid characters | stylized RPG characters, NPCs, enemies | cheap, no skin weights | visible joints |
| Skeletal animation | player, important NPCs, enemies | reusable, compact vs vertex cache | skinning cost, bone limits |
| Vertex animation | weird monsters, morphing props, water/lava | simple playback | data gets huge fast |
| Sprite/billboard animation | far NPCs, FX, crowds, tiny enemies | cheap visual life | less 3D |

Recommended default:

```text
Props: procedural or rigid transform keys
Characters: skeletal or segmented rigid
Far life: sprites/billboards
Special deformation: limited vertex animation
```

---

# 3. Procedural Animation

## 3.1 Use whenever possible

Simple animations should not require stored animation clips.

Good procedural candidates:

- spinning pickups
- bobbing collectibles
- rotating fans
- blinking lights
- simple UI model rotations
- idle floating objects
- simple platform loops
- pulsing magic objects

Example logic:

```lua
-- Pseudo-Lua
function onUpdate(self, dt)
    self.frame = self.frame + 1
    Entity.SetRotationY(self, self.frame * self.spinSpeed)
    local y = self.baseY + Math.Sin(self.frame * self.bobRate) * self.bobAmount
    Entity.SetPosition(self, Vec3.new(self.baseX, y, self.baseZ))
end
```

## 3.2 Benefits

- no animation file
- no bank memory
- no keyframe decode
- very easy to tweak
- good PS1-era feel

## 3.3 Rule

If an animation can be described with a few parameters, do not store it as a clip.

---

# 4. Rigid Transform Animation

## 4.1 Use cases

Rigid transform animation is ideal for:

- doors
- gates
- elevators
- platforms
- traps
- rotating machinery
- cutscene props
- moving scenery
- segmented character body parts

## 4.2 Suggested binary data

```c
struct TransformKey {
    uint16_t frame;
    int16_t tx, ty, tz;
    int16_t rx, ry, rz;
    int16_t sx, sy, sz; // optional
};
```

For many objects, scale can be omitted.

Smaller version:

```c
struct RigidKey {
    uint16_t frame;
    int16_t tx, ty, tz;
    int16_t rx, ry, rz;
};
```

## 4.3 Quantization

Use fixed-point or quantized integer values:

```text
translation: int16 chunk-local position
rotation: int16 quantized angle / pi-units / fixed rotation format
scale: optional, quantized only when needed
```

Do not store runtime floats.

## 4.4 Runtime behavior

Support:

```text
Play
Loop
PingPong
HoldLastFrame
Stop
Reset
```

Optional interpolation:

```text
stepped
linear
```

For PS1 style, stepped or low-rate interpolation can look authentic.

---

# 5. Segmented Rigid Character Animation

## 5.1 Concept

Instead of smooth skin deformation, build the character from rigid mesh pieces:

```text
head
torso
pelvis
upper_arm_L
lower_arm_L
hand_L
upper_arm_R
lower_arm_R
hand_R
upper_leg_L
lower_leg_L
foot_L
upper_leg_R
lower_leg_R
foot_R
```

Each piece follows a bone/transform.

## 5.2 Benefits

- no skin weighting issues
- lower runtime deformation cost
- easy to debug
- compact transform animation
- strong PS1/N64/Saturn-era aesthetic
- good for stylized RPG characters
- simple LOD downgrade path

## 5.3 Downsides

- visible joints
- less smooth than skinned meshes
- needs good art direction
- can look robotic if overused

## 5.4 Recommended metadata

```text
CharacterMode:
  Skinned
  SegmentedRigid
  SpriteBillboard
```

Segmented actor data:

```text
SegmentedActor:
  SkeletonId
  PieceCount
  PieceMeshIds
  ParentBoneIds
  LocalBindOffsets
  AnimationBankId
```

## 5.5 Best use

Use segmented rigid animation for:

- generic NPCs
- stylized enemies
- low-cost party members
- background characters
- old-school action figures / doll-like characters

Use skinned animation for:

- player
- hero characters
- important cutscene characters
- enemies where deformation matters

---

# 6. Skeletal Animation

## 6.1 Best use

Use skeletal animation for:

- player character
- important NPCs
- enemies
- creatures with multiple reusable clips
- humanoid animation banks
- boss characters, when budgeted

## 6.2 Do not store full matrices

Avoid:

```text
4x4 float matrix per bone per frame
```

This is too large and not PS1-friendly.

Prefer:

```text
root motion keys
per-bone rotation keys
optional translation keys only where needed
shared skeleton bind pose
quantized int16 channels
```

## 6.3 Suggested header

```c
struct AnimHeader {
    uint16_t boneCount;
    uint16_t frameCount;
    uint16_t fps;
    uint16_t flags;
};
```

## 6.4 Suggested bone key

```c
struct BoneKey {
    int16_t rx, ry, rz; // quantized/fixed rotation
    int16_t tx, ty, tz; // optional; usually root only
};
```

Most bones should store rotation only.

Translation should usually be static from bind pose except for:

- root
- hips
- special props
- unusual creature bones
- cutscene-specific motion

## 6.5 Runtime modes

Start with:

```text
BindPose
Play
Loop
Stop
HoldLastFrame
```

Then add:

```text
Crossfade
```

Later, if truly needed:

```text
upper/lower body layers
additive reactions
blend trees
IK
```

## 6.6 Recommended bone budgets

These are not hard limits, but useful targets.

```text
Tiny enemy:       4-8 bones
Generic NPC:      8-16 bones
Player:          12-24 bones
Important enemy: 12-24 bones
Boss:            special budget only
```

Warn on high bone counts.

---

# 7. Vertex Animation

## 7.1 Use sparingly

Vertex animation stores mesh positions over time.

Good for:

- slimes
- blobs
- weird monsters
- morphing props
- water/lava surfaces
- very low-poly deformation
- facial expression swaps if extremely small

Avoid for:

- high-vertex characters
- many long clips
- generic NPCs
- player locomotion
- large animation libraries

## 7.2 Why it gets expensive

Naive data size:

```text
vertex_count * frame_count * 3 axes * bytes_per_axis
```

Example:

```text
300 vertices * 30 frames * 3 axes * 2 bytes = 54,000 bytes
```

That is roughly 54 KB for one clip before headers/compression.

A few clips can become larger than the rest of the character.

## 7.3 If used, optimize hard

Use:

```text
int16 positions
chunk/model-local coordinates
delta frames
low frame counts
shared base mesh
animated subsets only
short loops
keyframe reduction
disc compression if useful
```

Possible vertex animation data:

```c
struct VertexAnimClip {
    uint16_t vertexCount;
    uint16_t frameCount;
    uint16_t fps;
    uint32_t frameOffset;
};
```

Frame data:

```text
base mesh
delta frame 0
delta frame 1
...
```

Or:

```text
only changed vertices per frame
```

## 7.4 Rule

Vertex animation is a special effect path, not the default character path.

---

# 8. Sprite and Billboard Animation

## 8.1 Use cases

Sprite animation is useful for:

- far NPCs
- background crowds
- birds/insects
- tiny enemies
- FX
- world map objects
- distant town life
- decorative motion

## 8.2 RPG LOD strategy

Use animation LOD tiers:

```text
Near:
  3D skeletal/segmented actor

Mid:
  simplified mesh or frozen pose

Far:
  billboard / sprite / silhouette / nothing
```

## 8.3 Sprite rules

- Use small atlas-based sprites.
- Prefer 4bpp indexed.
- Use 8bpp only if needed.
- Keep animation frame counts low.
- Reuse frames with flipping and palette swaps.
- Avoid unique long animation strips.

---

# 9. Recommended RPG Animation Tiers

| Asset / situation | Recommended method |
|---|---|
| Doors | rigid transform keys or procedural |
| Platforms | rigid transform keys or procedural |
| Pickups | procedural |
| Torches/flames | sprite/texture animation |
| Player | skeletal or segmented rigid |
| Important NPCs | skeletal or segmented rigid |
| Generic NPCs | segmented rigid or low-frame skeletal |
| Background NPCs | sprite/frozen pose |
| Enemies | skeletal/segmented with strict budget |
| Weird monsters | skeletal or limited vertex animation |
| Water/lava | procedural or limited vertex animation |
| Cutscene props | transform tracks |
| Facial expressions | texture/palette swaps, not complex morphs |
| Far crowds | sprite sheets or simple billboards |

---

# 10. Binary Animation Bank

## 10.1 Goal

Package animation data into compact runtime banks.

Do not keep animation as loose files.

Suggested bank layout:

```text
ANIM_BANK
  header
  skeleton table
  animation clip table
  keyframe data
  event track data
  name/hash table or ID table
```

## 10.2 Suggested header

```c
struct AnimBankHeader {
    uint32_t magic;       // 'ANIM'
    uint16_t version;
    uint16_t skeletonCount;
    uint16_t clipCount;
    uint16_t eventCount;
};
```

## 10.3 Suggested clip record

```c
struct AnimClip {
    uint16_t skeletonId;
    uint16_t frameCount;
    uint16_t fps;
    uint16_t flags;
    uint32_t keyOffset;
    uint32_t eventOffset;
};
```

## 10.4 IDs instead of strings

Avoid storing many strings at runtime.

Use:

```text
uint16 animation ID
uint16 skeleton ID
uint16 bone ID
uint16 event ID
```

Names can exist in editor/build tools.

Runtime can use hashed IDs or integer IDs.

---

# 11. Animation Compression

## 11.1 Good simple compression

Worth doing early:

```text
int16 quantized rotations
int16 or int12 translation
fixed FPS
keyframe reduction
delta-compressed root motion
omit unchanged bones
omit translation channels except root
shared skeletons
shared humanoid clips
animation banks per chunk/group
```

## 11.2 Good disc-only compression

Useful for storage, then decompress on load:

```text
LZ-style archive compression
RLE unchanged channels
simple delta compression
```

## 11.3 Probably not first

Avoid starting with:

```text
advanced quaternion compression
runtime IK
motion matching
complex blend trees
full additive layer stack
heavy animation decompression every frame
```

---

# 12. Keyframe Reduction

## 12.1 Exporter should remove useless keys

Example rule:

```text
If a bone channel changes less than threshold:
  omit key
```

For many PS1-style animations, lower temporal resolution is acceptable.

Recommended animation rates:

```text
10 fps: stylized/stepped/background
15 fps: common NPC/enemy animation
30 fps: important player/enemy motion
60 fps: almost never needed for stored animation
```

Playback can run at 30/60 fps while sampling lower-rate animation.

## 12.2 Stepped animation is acceptable

Stepped low-FPS animation can look authentic.

Do not over-smooth everything.

---

# 13. Animation Events

## 13.1 Events matter early

Animation events are more important than fancy blending for gameplay.

Examples:

```text
frame 6: footstep_left
frame 12: footstep_right
frame 18: attack_hit_start
frame 22: attack_hit_end
frame 24: attack_recover
frame 30: play_sfx
frame 36: spawn_effect
```

## 13.2 Suggested event data

```c
struct AnimEvent {
    uint16_t frame;
    uint16_t eventId;
    uint16_t param0;
    uint16_t param1;
};
```

## 13.3 Useful event types

```text
footstep_left
footstep_right
attack_active
attack_recover
hitbox_open
hitbox_close
play_sound
spawn_effect
camera_shake
emit_particle
toggle_visibility
set_flag
```

## 13.4 Lua integration

Possible future hook:

```lua
function onAnimationEvent(self, eventName)
    if eventName == "attack_hit" then
        -- open hitbox, play SFX, etc.
    end
end
```

Or route common events directly in runtime for speed:

```text
footstep -> AudioRouter.PlaySfx(...)
hitbox_open -> Combat.EnableHitbox(...)
```

---

# 14. Blending and Crossfade

## 14.1 Start simple

Phase 1:

```text
Play clip
Loop clip
Stop clip
Bind pose
```

Phase 2:

```text
Crossfade current -> next over N frames
```

Phase 3:

```text
upper/lower body layers
additive hit reactions
blend trees
```

## 14.2 Suggested Lua API direction

```lua
Anim.Play("player", "walk", { loop = true })
Anim.Play("player", "attack", { loop = false })
Anim.Crossfade("player", "idle", 6)
Anim.BindPose("player")
```

Do not fake APIs that do not exist yet.

If scaffolded, log clearly:

```text
Anim.Crossfade is not implemented yet.
```

---

# 15. Animation Residency and Chunking

## 15.1 Animation banks should follow chunks

Do not keep every animation in the RPG resident.

Use banks:

```text
common_player.anim
common_humanoid.anim
town_npcs.anim
forest_enemies.anim
boss_01.anim
cutscene_intro.anim
```

## 15.2 Resident vs streamed/load-on-demand

Keep resident:

```text
player locomotion
common interactions
current enemy set
current NPC set
common combat reactions
```

Load per chunk:

```text
area-specific NPC gestures
enemy-specific attacks
cutscene animation
boss animation
special events
```

## 15.3 Suggested metadata

```text
AnimBank:
  BankId
  Residency: Always | Scene | Chunk | OnDemand
  ChunkId
  DiscId
  Skeletons
  Clips
  EstimatedSize
  CompressedSize
```

---

# 16. Exporter Reporting

Add animation reporting to the editor/exporter.

Per clip:

```text
clip name
skeleton
frame count
fps
duration
bone count
key count
event count
estimated raw size
estimated packed size
residency
bank
warnings
```

Per bank:

```text
bank name
skeleton count
clip count
total frames
estimated size
chunk owner
disc owner
resident or load-on-demand
```

---

# 17. Exporter Warnings

Add warnings like:

```text
Animation clip "npc_wave" has 120 frames at 60fps.
Consider reducing to 15 or 30fps.
```

```text
Skinned mesh "town_npc_03" has 48 bones.
Target is much lower for PS1-style runtime.
```

```text
Clip "walk" stores translation keys on 32 bones.
Only root/hips likely need translation.
```

```text
Vertex animation "slime_idle" is 86 KB.
Consider skeletal/segmented animation or reducing frames.
```

```text
Character "guard" has 18 unique clips.
Consider sharing common humanoid animation bank.
```

```text
Animation bank "town_npcs" is resident in all chunks.
Consider Scene/Chunk residency.
```

```text
Clip "attack_heavy" has no animation events.
Combat timing may need hitbox_open / hitbox_close events.
```

---

# 18. Suggested Implementation Order for PS1Godot

## Step 1 — Animation metadata and reporting

Add:

```text
clip count
frame count
FPS
duration
bone count
estimated size
residency
bank ownership
warnings
```

This is low-risk and immediately useful.

## Step 2 — Rigid transform animation export

Support simple props first:

```text
doors
platforms
rotating objects
cutscene props
```

## Step 3 — Animation events

Add event tracks before advanced blending.

Events unlock:

```text
footsteps
SFX
hitboxes
effects
camera shake
gameplay timing
```

## Step 4 — Basic skeletal clip export

Start compact:

```text
shared skeleton
int16 rotations
root translation only
fixed FPS
```

## Step 5 — Animation bank/chunk ownership

Add:

```text
common/player/chunk/enemy/boss banks
residency
estimated bank size
disc ownership
```

## Step 6 — Segmented rigid character path

Add support for rigid body-part characters if it fits the art direction.

This can be a huge performance and debugging win.

## Step 7 — Simple crossfade

Add basic clip-to-clip crossfade over N frames.

Do not start with full blend trees.

---

# 19. Animation Metadata Proposal

Suggested future metadata fields:

```text
AnimationClip:
  ClipId
  SourcePath
  SkeletonId
  BankId
  FrameCount
  FPS
  Duration
  Loop
  Compression
  KeyMode: Dense | Sparse | Procedural | VertexCache
  Interpolation: Step | Linear
  HasRootMotion
  HasEvents
  Residency: Always | Scene | Chunk | OnDemand
  ChunkId
  DiscId
  EstimatedSize
```

```text
Skeleton:
  SkeletonId
  BoneCount
  BoneNames/editor only
  ParentIndices
  BindPose
  CharacterMode: Skinned | SegmentedRigid | SpriteBillboard
```

```text
AnimationEventTrack:
  ClipId
  Events:
    - Frame
    - EventId
    - Param0
    - Param1
```

---

# 20. IDE-Agent Prompt

Use this prompt to implement the first safe slice.

```text
You are helping me improve the PS1Godot / psxsplash animation pipeline for a PS1-style chunk-based RPG.

Goal:
Add documentation, metadata, reporting, and safe scaffolding for compact runtime animation banks. Do not break existing demo/jam scenes.

Core strategy:
- Do not ship FBX/glTF/JSON/text animation at runtime.
- Export compact binary animation banks.
- Use procedural animation for simple props where possible.
- Use rigid transform keys for doors/platforms/cutscene props.
- Use skeletal or segmented rigid animation for characters.
- Use sprite/billboard animation for far/background actors.
- Use vertex animation only for special low-poly deformation cases.
- Quantize animation data.
- Avoid full float matrices per bone per frame.
- Add animation events early.
- Add chunk/bank residency metadata.

Implement or scaffold:
1. Animation metadata:
   - ClipId
   - SourcePath
   - SkeletonId
   - BankId
   - FrameCount
   - FPS
   - Duration
   - Loop
   - Compression
   - Interpolation
   - HasRootMotion
   - HasEvents
   - Residency
   - ChunkId
   - DiscId
   - EstimatedSize

2. Animation bank metadata:
   - BankId
   - SkeletonCount
   - ClipCount
   - EstimatedSize
   - Residency
   - ChunkId
   - DiscId

3. Export/reporting:
   - per-clip frame count
   - per-clip FPS
   - bone count
   - estimated size
   - event count
   - warnings

4. Warnings:
   - 60fps clips with many frames
   - high bone counts
   - translation keys on too many bones
   - large vertex animation clips
   - too many unique clips per generic NPC
   - resident animation banks that should be chunk-local
   - combat clips missing events

5. Documentation:
   - add animation strategy doc
   - explain procedural vs rigid vs skeletal vs segmented vs vertex vs sprite animation
   - explain binary animation banks
   - explain events
   - explain chunk residency
   - explain exporter warnings

Rules:
- Keep current builds working.
- Do not implement complex blending first.
- Do not fake runtime APIs that do not exist.
- Prefer metadata/reporting before risky runtime changes.
- Clearly separate implemented behavior from scaffolded metadata.

Final response:
- Summary
- Files changed
- What is implemented
- What is scaffolded only
- How to test
- Risks/TODOs
```

---

# 21. Bottom Line

For PS1-style RPG animation, the best strategy is:

```text
Procedural transforms for simple props.
Rigid transform keys for doors/platforms/cutscene objects.
Segmented or skeletal animation for important characters.
Sprites/billboards for far background life.
Vertex animation only for special low-poly cases.
Animation banks loaded per chunk/character group.
Quantized binary data, not editor formats at runtime.
Animation events before fancy blending.
```

Start with:

```text
metadata
reporting
rigid transform clips
animation events
compact skeletal banks
chunk residency
```

That gives the most gameplay value without blowing RAM, VRAM, disc bandwidth, or runtime cost.
