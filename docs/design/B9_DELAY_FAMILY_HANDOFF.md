# B9 delay-family handoff

## Implemented scheduler connections

| Source family | Scheduler connection | Evidence boundary |
| --- | --- | --- |
| ordinary turn | `NormalTurn` through `BattleState` after a spent player or enemy turn | active fighters supply both speeds when the replacement is queued |
| run-away attempt | `RunAway` after an unsuccessful player or enemy attempt | the outcome path does not queue a replacement |
| opening order | `BattleTurnScheduler.InitializeOpening` | the shared injected battle RNG supplies the non-ambush speed roll |

The implementation must not label its deterministic replay source as the original random table. The production host injects its battle RNG into the opening setup and tests provide fabricated streams.

## Qualified but intentionally unwired families

The original queue has thirteen delay rows. Their contracts are recorded here for later owning cards. B9-027 does not create status effects, blast effects, recoveries, or boost effects.

| Row | Contract | Current state |
| --- | --- | --- |
| 2 | auto-recovery: random component plus active fighter spirit | calculator exists, no producer or consumer |
| 3 to 7 | blast end: fixed 2000, 2500, 3000, 3500, or 4000 units | calculator exists, no blast runtime connection |
| 8 | random expiry: random value reduced to 0 through 8000 then clamped to 2000 through 6000 | calculator exists, no generic status producer or consumer |
| 9 | paralysis expiry: random value, active spirit, strength, and the target paralysis resistance pair | no C# contract yet |
| 10 | confusion expiry: random value, active spirit, strength, and the target confusion resistance pair | no C# contract yet |
| 11 | sleep expiry: random value, active spirit, strength, and the target sleep resistance pair | no C# contract yet |
| 12 | technique boost expiry: random component plus 2000 and active spirit | calculator exists, no boost producer or consumer |

The generic `RandomStatusExpiry` name is only a delay calculation. It does not grant a status, clear one, or decide an effect. Rows 9 to 11 need explicit typed status inputs before any runtime connection. This prevents the scheduler from inventing status behavior owned by later combat cards.
