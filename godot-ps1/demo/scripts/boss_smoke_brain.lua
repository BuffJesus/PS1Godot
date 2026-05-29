-- Boss brain for the boss-smoke scene. The behavior + tuning live in
-- a sibling BossBT graph (boss_smoke_bossbt.tres) compiled to
-- boss_smoke_bossbt.lua and loaded via PS1Scene.UserScripts on the
-- BossSmoke root. That compile populates `_G.bossbt_boss_smoke_bossbt`
-- before this script's onCreate fires, so the brain itself just wires
-- the graph into Combat.MeleeBoss and forwards lifecycle events.
--
-- See docs/internal/rfc/bossbt-graph-kind.md for the graph format and
-- docs/authoring/boss-encounters.md for the broader recipe.

local boss = Combat.MeleeBoss(_G.bossbt_boss_smoke_bossbt)

function onCreate(self)
    Debug.Log("boss brain ready — HP " .. Stats.GetMaxHP(self))
end

function onUpdate(self, dt)
    boss:update(self, dt)
end

function onDamage(self, applied, source)
    boss:handleDamage(self, applied, source)
end
