# B9 delay-family handoff

## Implemented scheduler connections

| Source family | Scheduler connection | Evidence boundary |
| --- | --- | --- |
| ordinary turn | `NormalTurn` through `BattleState` after a spent player or enemy turn | active fighters supply both speeds when the replacement is queued |
| run-away attempt | `RunAway` after an unsuccessful player or enemy attempt | the outcome path does not queue a replacement |
| opening order | `BattleTurnScheduler.InitializeOpening` | the shared injected battle RNG supplies the non-ambush speed roll |

The production host passes its shared battle RNG to `BattleSetupCoordinator.CreateOpeningState`, which requires a non-null stream and forwards it to `BattleState.InitFighters`. `BattleHostOpeningTests` executes that production setup seam with fabricated rolls 0 and 127 and checks the compiled `Build` call site loads `_host._battleRng` as its final argument. Replacing that argument with the compatibility source makes both cases fail. This is a setup integration proof, not a full Godot session or an original-table RNG parity claim.

The earlier package `4d1f96e` did **not** contain the host injection. Its previous handoff sentence claiming production wiring was incorrect. The fixed roll 127 remains only for legacy/synthetic callers of `InitFighters` that omit the optional stream. Neither that fixed value nor the host's deterministic LCG is the original random table.

## EU queue corrections

The reviewed EU behavior contradicts both earlier scheduler assumptions:

- At equal time, the current best ordinary turn yields to any later occupied slot, not only another turn. Once a non-turn wins, later equal-time turns do not displace it. The discriminating fabricated trace is player turn, interrupt, enemy turn at time 10: interrupt first, enemy second, player third.
- Insertion reuses the lowest free slot. Removing an event leaves a hole and does not move surviving slots. A player in slot 0 is popped, an enemy remains in slot 1 at time 10, then the player is reinserted at time 10 into slot 0. The enemy must win the tie. A compact list followed by append incorrectly selects the player.

The scheduler now retains 99 nullable slots and scans occupied slots in slot order. These are ordering contracts for its implemented event subset, not a claim that all original event pop modes or status semantics are implemented. Status/effect ownership stays with B9-028 through B9-037. The capacity rejection and one-pending-turn-per-side protections remain project safety contracts.

`BattleTurnSchedulerTests` covers both discriminating cases, fast/slow openings, current-speed rearming, priority interruption without duplicate turns, capacity, and equal-time render-cadence traces. Cadence accumulation is clamped to the exact comparison horizon. Only fabricated queue events are used.

## Test-count correction

The logs of CI run `42115665503` on `4d1f96e` were read back, not inferred from the old report. Both Ubuntu and Windows record Game 289, Formats 367, Extractor 124, Data 31, Disc 35, Repository 46, QualityRatchet 26 and Analyzers 21: **939 passed per platform**, not the previously reported 913. Counts for this correction must be taken from its own fresh test logs.

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
