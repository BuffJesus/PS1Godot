# Boss-encounter smoke scene

Exercises every combat primitive landed in the 2026-05-19 RFC arc.
Drop into PCSX-Redux via `▶ Run on PSX`; what to do once it boots:

1. **Walk forward** — left stick toward the fog wall.
2. **Cross the fog gate** — the fog wall is the textured plane with
   scrolling UVs ahead of you. The trigger behind it locks the
   camera onto the boss, plays a whoosh, and cues boss music.
3. **R2 to swing** — melee box in front of the camera. Hits scale
   by which boss hurtbox you tagged (head 2×, body 1×, legs 0.5×).
4. **Circle to dodge** — directional roll with i-frame window
   (12 frames of invuln in an 18-frame total dodge). Costs 25
   stamina; you have 100 max and regen 1/30 frames.
5. **R3 to toggle lock-on** — drop / re-engage.

Boss behavior:

- **Phase 1** — slow chase, telegraph (small camera shake), swing.
  Player can dodge through the swing.
- **Phase transition** at 50% HP — bigger shake, brief boss invuln
  (60-frame i-frames), then faster chase + shorter telegraph.
- **Death** at 0 HP — big shake + frozen frame; HP bar hides;
  `Persist.smoke_boss_dead = 1` so re-entering the gate doesn't
  re-trigger the music.

If you take 100 damage you die (controls disabled until reload).

## What's tested

| Primitive | How it shows |
|---|---|
| `PS1Stats` (HP / Stamina / Mana) | Boss + player both have `Stats` slots. Mana=0 (no magic system); stamina only on the player. |
| `PS1HurtBox` (crit multipliers) | Boss has 3 hurtboxes — head 200%, body 100%, legs 50%. `Physics.OverlapBoxDetailed` returns the highest match per entity. |
| `Stats.DealDamage` + `onDamage` callback | Player swing → DealDamage on each hurtbox hit; boss's `onDamage` Lua fires for phase transition + death. |
| `Controls.StartIFrames` + i-frame collision | Player dodge sets the window; `DealDamage` returns 0 while invuln. Boss also sets brief i-frames after each hit. |
| `Camera.LockOn` / `LockOff` / `IsLocked` | Fog gate engages, R3 toggles. Yaw + strafe-relative input automatic. |
| `PS1MeshInstance.UVScrollSpeed` | Fog wall scrolls diagonally (12, 8) at runtime. |
| `Input.GetAnalog(Input.RIGHT_STICK)` | Default twin-stick camera works out of the box on analog pads. |

## File layout

```
godot-ps1/
├── demo/
│   ├── boss_smoke/
│   │   ├── boss_smoke.tscn      ← the scene
│   │   ├── boss_stats.tres      ← Boss MaxHP=200
│   │   ├── player_stats.tres    ← Player MaxHP=100, MaxStamina=100
│   │   └── README.md            ← this file
│   └── scripts/
│       ├── boss_smoke_brain.lua    ← boss AI + onDamage
│       ├── boss_smoke_player.lua   ← player combat + dodge + lock-on
│       └── boss_smoke_fog_gate.lua ← fog-gate trigger
```

## Known limitations

- **No audio clips wired.** `Audio.PlaySfx("fog_gate_whoosh")` and
  `Music.Play("boss_theme", 100)` are no-ops — author and drop
  PS1AudioClips into the scene's `AudioClips` array to enable.
- **No PS1Cutscene for boss intro.** Lock-on engages immediately
  on gate crossing; the recipe pattern in
  `docs/authoring/combat-patterns.md` shows the cutscene + intro
  for production encounters.

## What to swap in next

To make this look + sound like a real encounter without changing
any logic:

1. Replace the cube meshes with imported FBX / GLB models. The
   `PS1MeshInstance` script + `Stats` / `ScriptFile` configs all
   stay; only the `mesh` property changes.
2. Add PS1AudioClips to the scene's `AudioClips` array: a whoosh,
   a music loop, swing-impact SFX, hit-on-player SFX, boss roar.
3. Wire `Audio.PlaySfx("boss_swing")` / `Music.Play("boss_theme")`
   from the brain script's attack + onDamage hooks.
4. Author a `PS1Cutscene` for the boss intro (camera arc + name
   reveal); `boss_smoke_fog_gate.lua` calls `Cutscene.Play`
   before the lock-on engages.
5. Textures + UV mapping on the fog wall + boss; the UV scrolling
   feature visibly does nothing on a flat-colored plane.
