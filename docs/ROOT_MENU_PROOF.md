# Native root menu proof

Run the normal analyzer-enabled build first, then the pack-free native scene:

```sh
dotnet build Dw3.sln --nologo
godot --headless --path src/Dw3.Game res://Diagnostics/Battle/NativeRootMenuProof.tscn
```

`godot` must be the Mono build matching the project SDK. On guarded development hosts,
wrap both commands in the project heavy lane and run Godot through the protected runner.
No display server, pack, disc, physical controller or window focus changes are required.
A failure exits 1 with the failing assertion. Success exits 0 and prints named PASS lines.

## Boundary exercised

The scene constructs a native `GameHost` through a small internal fixture constructor.
It calls the normal constructor, which creates the production `BattleMenuCoordinator`.
The fixture supplies only fabricated battle/text/item data and a counting random source.
It never supplies menu callbacks, copies root dispatch, or fabricates submenu pages.
The host is not added to the scene tree, avoiding field startup and unrelated rendering.

Constructed native `InputEventKey`, `InputEventJoypadButton` and `InputEventJoypadMotion`
events enter `GameHost._Input` with the normal input owner. They traverse the same
`InputAdapter` and semantic `InputState` as interactive play. Ticks enter the real
`IBattleTickHost.TickBattleMenu`, then `BattleMenuDriver`, then the host's actual
`IBattleMenuDriverHost` callbacks and private coordinator. Focus enters the real
`GameHost._Notification` override, not `InputState.Clear` called by a test.

Assertions observe the real menu and simulation consumer. Resource snapshots include
phase, outcome, scheduler next side, active identities, every fighter's HP/MP, item stock
and random draw count. The four submenu openers are production methods using fixture
catalogues. Their deeper effects remain outside this root-menu proof.

## Scenarios

- FIRST: Down, Cross opens the real technique page, Circle returns to the real root.
- All six root commands: four real openers, real attack damage/sequence context,
  and real successful flee outcome with random draws observed.
- Three rounds through all four submenus, hold/repeat suppression, cancellation,
  root cancellation, up/down press edges and both clamps.
- Grey root rows, unavailable technique cost, empty root and empty catalogues,
  followed by normal root reentry.
- Keyboard/controller overlap, source release, stick/keyboard alternation.
- Native focus out, events while absent, focus in, held controls, repeat events,
  complete release and a fresh usable confirmation.
- Concurrent Cross/Circle: cancellation wins without leaking Cross to the next tick.
- ActionSequence rejects menu input without changing HP/MP, identities or RNG.
  A press during playback cannot execute on reentry. Held confirmation stays inert
  across repeated turns. Release and fresh confirmation execute again.
- EnemyTurn and Ended reject input without spending resources or queuing a command.

## Discriminating control and reproduced defects

The FIRST scenario was run with the production coordinator's root dispatch made inert
by temporarily changing its switch selector from `Cursor` to `Cursor + 100`.
The analyzer-enabled build succeeded. The native process failed with
`FIRST: production technique opener not reached`, exit 1. The mutation was restored
before final build and validation. Earlier attempts to erase the callback were rejected
by unused-method and class-complexity analyzers, not counted as runtime RED evidence.

The native scenarios then reproduced four independent defects before their fixes:

1. Releasing Enter while controller Cross remained held re-armed the aggregate and
   spent a technique on a duplicate pad event.
2. Focus return replayed a held Cross and spent a technique.
3. A fresh press during ActionSequence survived in `InputState` and executed on the
   next player turn.
4. Cross and Circle in one sample cancelled the submenu, then executed Attack on the
   next tick because Cross remained pending.

Each failed its named native assertion with exit 1 before the corresponding correction.
The fixes aggregate physical Cross/Circle sources, disarm held controls across focus,
discard battle-menu edges at the native ingress during noninteractive phases, and consume
a competing Cross when cancellation wins. Post-battle report confirmation is exempted
from the noninteractive menu gate. No battle rules or submenu effects are replaced.

## Limits

This is an input/coordination integration proof, not a rendering or full field-startup
proof. Sequence completion and scheduler-owned enemy progression are explicitly advanced
as fixture lifecycle operations, never used as substitutes for the UI command under test.
It does not play a full animation timeline or qualify audio/camera appearance.
The existing `BattleMenuRootNavigationTests` remain narrower driver unit regressions,
not evidence of production host wiring, native focus, or physical device ingress.
The standard CI builds the proof and runs unit regressions. The native scene is a separate
local command, not automatically executed by the existing CI workflow.
