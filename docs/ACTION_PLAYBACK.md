# Action playback synchronization

The fixed battle tick calls `BattleSequenceRunner` through `IBattleTickHost.TickActionSequence`.
It loads the required script with `PackActionScriptProvider`, validates the complete command
stream, then consumes the frozen action context. Unsupported commands, malformed operands
and missing terminators fail before playback has side effects.

## Distinct completion boundaries

- Rules resolve once before playback, retaining landed/missed, actual HP loss, counter markers
  and original fighter identities. Playback never draws battle RNG or reapplies damage.
- A positive motion request selects the source one-based motion. Motion zero is a physical
  completion poll, not a request to play motion zero. Missing physical models remain pending.
- Explicit waits initialize on the first tick and decrement on subsequent ticks. Even a zero
  wait yields once. A tick is an executor unit, not a claim of calibrated source PAL cadence.
- Every physical request receives `Applied`, `Pending` or `Unavailable` from its consumer.
  A pending request must not start duplicate children when retried. An unavailable consumer
  leaves a diagnosed, recoverable sequence failure rather than silently completing the action.
- `HitDispatch` requests a reaction with the already resolved hit results. It does not publish
  HUD damage. Dispatch values 1 through 4 are explicit source no-ops, not impacts.
- End waits for registered reaction/sprite children. Normal completion or cancellation calls
  the consumer's cleanup once. Effect-model and sound lifetimes are not invented End delays.

## Presentation and camera

`BattleSequencePresentation` preserves the displayed roster, active markers, HP and MP from
before rule resolution until playback completes. The live state can already contain the
resolved damage and an automatic replacement. Model refresh is deferred during the sequence,
so a request cannot accidentally animate the replacement. The completed view publishes the
resolved values without replaying the rules. Final KO still enters playback before the
terminal battle phase.

The renderer consumes camera presets and explicit composed views through
`BattleSequenceCamera.Apply`. Cut and authored fade update the physical interpolation state.
Fades interpolate authored position, reference, translation and rotation fields with a
4096-unit fixed-point fraction before composing the world transform, rather than interpolating
two already-rotated world endpoints. The truncated step can require an extra tick.
While playback owns the camera, menu activation, menu stepping and overview restoration
cannot overwrite it. Completion and interruption restore the opening overview and visibility.
Nonzero authored view roll is explicitly unsupported in this first-lot contract rather than
accepted without a physical consumer.

## Missing resources and remaining owners

A required absent or invalid script retains its pending context and a runner diagnostic.
Restoring that resource allows the same already resolved action to resume without another RNG
draw. A technique explicitly lacking a script remains separate and does not enter playback.
Teardown cancels physical work and abandons the old runner before a new battle is initialized.

The renderer now owns the basic attack's travel, jumps, target reaction scripts, every
prepared sprite effect (the common contact included) and the script white flash. Stage,
effect-model and audio families keep their own consumers or the chained asset-consumer seam. An unregistered backend is unavailable. The deterministic
backend in engine-free tests acknowledges synchronization requests only. Camera interpolation
uses the presentation clock. Source-frame/PAL calibration and complete visual parity are
not established by these tests.

## Verification

Fabricated packs are produced by `BattleActionDomainWriter` and loaded through the real
`BattleDataPackLoader` and provider. Tests drive the same runner and tick coordinator as the
host, including negative readiness controls, acknowledged camera state, final KO and reserves,
HP publication, repeated ticks, interruption and missing-resource recovery. Tests do not
require a disc. Private real-input and offscreen physical-state evidence is kept outside the
source repository and does not constitute acceptance of complete battle visual fidelity.

## Basic attack physical contract

Pack schema 21 preserves sparse motion slots instead of compacting absent entries. A source
motion number therefore still identifies the same archive slot. Model commands retain their
bounded travel operands and independent jump-child waits. Finishing a jump never satisfies a
wait for a subsequently started motion. Cancellation restores root positions and the captured
motion/idle selection. A normal End does not overwrite the authored terminal reaction pose.

Hit dispatch starts the target's actual pack script: hit, defeated or miss. It consumes the
frozen hit facts, including whether that original target was defeated, rather than looking up
a replacement in the live roster. The shared physical consumer advances this child, and End
waits for it. No damage or RNG is resolved in this path.

When the script ends, the action shows its result message before it completes, as the source's
technique, enemy-attack and counterattack tasks do. The page is built from the frozen hit events
(`BattleActionResultPages`): the target's name, then message type 4 with the landed hit's formula
damage (a double magic prints the sum of its two hits), type 0x10 with the per-hit damage, landed hits
and their product for a multi-hit action (multi-hit effect or triple-hit equipment), or the miss line.
A counterattack adds its own page. A knock-out effect shows none. The lines appear one by one, the
next after more than three frames, then the arrow, and only then does Cross close the page
(`BattleMessageReveal`). The HUD shows the live HP from the result on, and the message box closes with
the action. Without a player input source (an offscreen driver) each page closes at the first frame
the source reads Cross. Cancellation drops the message without completing the action.

Sprite effects, the common contact effect included, are drawn from the prepared sprite domain
described in [formats/action-sprites.md](formats/action-sprites.md#runtime-consumer). There is no
separate contact family any more. A missing or unsupported identity is never acknowledged. The
optional-stage sentinel with no configured stage and a return to the unchanged original stage
are qualified no-change operations, not simulated stage fades.

For offscreen diagnostics, set `DW3_BATTLE_ACTION_PROBE=1` and
`DW3_BATTLE_ACTION_PHYSICAL=1`, `DW3_RNG_SEED=12345` and an explicit newly prepared
`DW3_DATA_DIR`. The ordinary
battle-action diagnostic remains unchanged without the second flag. The physical diagnostic
uses real scripts, rules and renderer for both camps, normal/miss/fatal-with-reserve, repeated
actions in the same battle and final victory/defeat. Contact captures are triggered by an
actual started sprite child rather than a guessed screenshot time. HP, accuracy and AI
preconditions are controlled fixtures. Simulation RNG is counted
before and after playback. Its sound-only control acknowledgements are labelled as not played.
This isolates graphics from the separately owned audio implementation, not an audio proof.

Clock units remain distinct: runner advancement is a simulation tick, jump/travel increments
are normalized source-time samples, fighter motion uses the retained presentation/PAL clock,
and framebuffer capture waits on rendered frames. The diagnostic does not equate these units
or claim original task scheduling, exact PAL cadence or a frame-matched PSX sequence oracle.
