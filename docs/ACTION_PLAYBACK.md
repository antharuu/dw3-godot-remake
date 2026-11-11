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

Sprite, stage, effect-model, audio and reaction renderers register an asset consumer plus
child-readiness and cancellation callbacks. An unregistered backend is unavailable. This
executor does not claim those assets were rendered or their audio played. The deterministic
backend in tests acknowledges synchronization requests only. Camera interpolation currently
uses the existing presentation clock. Source-frame/PAL calibration and complete visual parity
are not established by these tests.

## Verification

Fabricated packs are produced by `BattleActionDomainWriter` and loaded through the real
`BattleDataPackLoader` and provider. Tests drive the same runner and tick coordinator as the
host, including negative readiness controls, acknowledged camera state, final KO and reserves,
HP publication, repeated ticks, interruption and missing-resource recovery. Tests do not
require a disc. Private real-input and offscreen physical-state evidence is kept outside the
source repository and does not constitute acceptance of complete battle visual fidelity.
