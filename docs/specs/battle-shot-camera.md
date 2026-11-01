# Battle Shot Camera — Clean-Room Specification

This document describes the **observable behaviour** of the battle shot camera: the scripted
sequence of camera shots shown while the player is choosing a command in battle. It is
self-contained: a developer who has never seen the game or any other file of this codebase
can reimplement the component from this page alone.

Namespace: `Dw3Production.Presentation`. The component is engine-free (no rendering, no
clock): the caller advances it one *frame* at a time and reads back a camera view and two
visibility flags.

---

## 1  Overview

The camera plays **shot lists**. A shot list is an ordered list of **shots**; each shot has a
duration in frames and a **behaviour** (a small integer, exposed as `Substate`). A behaviour
decides, on the first frame of its shot, which base view to take from the caller and how to
modify it, then optionally rotates the view a little on every frame of the shot.

When the last shot of a list ends, the next list is picked at random among three successors of
the finished list. The camera starts on a fixed opening list.

All angles use a **turn of 4096 units** (4096 = 360°).

---

## 2  Public API

### 2.1  `IShotCameraRng`

```csharp
public interface IShotCameraRng
{
    int Next();
}
```

Source of random integers. Any `int` may be returned, negative values included. It is called
exactly once each time a shot list ends (§5.3) and at no other time.

### 2.2  `SourceBattleCameraView`

```csharp
public sealed class SourceBattleCameraView
{
    public int Vpx, Vpy, Vpz;                    // viewpoint position
    public int Vrx, Vry, Vrz;                    // reference (look-at) point
    public int Tx, Ty, Tz;                       // translation of the camera frame
    public int RotationX, RotationY, RotationZ;  // rotation of the camera frame, 4096 = one turn
    public int Rz;                               // roll
    public int Projection;                       // projection distance
}
```

A plain mutable record of 14 public `int` fields (fields, not properties). All fields default
to 0. The shot camera only ever *reads or writes* `Vpx`, `Vpy`, `Vpz`, `Vrx`, `Vry`, `Vrz`,
`Tz`, `RotationY` and `Projection`; the other fields are carried through untouched from
whatever view the provider returned.

### 2.3  `BattleShotCameraViews`

```csharp
public sealed class BattleShotCameraViews
{
    public required Func<SourceBattleCameraView> GetEnemyView { get; init; }
    public required Func<int, int, SourceBattleCameraView> GetFighterView { get; init; }
}
```

Two view providers supplied by the caller.

* `GetEnemyView()` returns the *overview* base view (the view framing the battle from the
  enemy camera set).
* `GetFighterView(slot, index)` returns the view number `index` of the camera set that frames
  one fighter. `slot` is `0` for the player's fighter and `16` for the enemy fighter. The
  camera only ever calls it with `(0, 8)`, `(0, 10)` and `(16, 0)`.

Each call must return a **fresh** instance. The camera takes ownership of the returned
instance, exposes it as `View`, and mutates it in place afterwards (behaviour is unspecified
if a provider returns a shared instance). Exceptions thrown by a provider propagate out of the
camera call that triggered them.

### 2.4  `BattleShotCamera`

```csharp
public sealed class BattleShotCamera
{
    public BattleShotCamera(IShotCameraRng rng, BattleShotCameraViews views);
    public BattleShotCamera(IShotCameraRng rng, int enemyCameraCount);

    public int List { get; }        // current shot list, 0..3
    public int Shot { get; }        // index of the current shot inside List
    public int Time { get; }        // frames left in the current shot, >= 1 between calls
    public int Substate { get; }    // behaviour of the current shot (table §4)
    public SourceBattleCameraView View { get; }   // the view to render now
    public bool PlayerVisible { get; }            // draw the player's fighter
    public bool EnemyVisible { get; }             // draw the enemy fighter

    public void Step();
    public void StepFrames(int frames);
    public void Stop();
}
```

All getters are read-only from outside.

---

## 3  Notation

* `wrap(a) = a mod 4096` with the mathematical (always non-negative) modulo, so
  `wrap(a) ∈ [0, 4095]`. Examples: `wrap(4097) = 1`, `wrap(-1) = 4095`, `wrap(-4096) = 0`.
* All other additions and subtractions on view fields are **32-bit two's-complement**
  operations that silently wrap around on overflow (`int.MaxValue + 1 = int.MinValue`).
  Because 2³² is a multiple of 4096, applying `wrap` after a wrapped 32-bit addition gives the
  same result as applying it to the exact integer sum.
* `D = 5120` (hexadecimal `0x1400`), the side-shot dolly distance.

---

## 4  Shot tables

### 4.1  Shot lists

Each entry is `(duration in frames, behaviour)`.

| List | Shots in order |
|------|----------------|
| 0 | (1800, 1), (480, 9), (360, 3), (300, 4), (480, 9) |
| 1 | (360, 6), (900, 1), (300, 5), (480, 9) |
| 2 | (1920, 1), (360, 6), (180, 7), (480, 9) |
| 3 | (90, 10), (90, 5) |

List 3 is the opening list; it is also reachable later as a successor.

### 4.2  Successor lists

When list `L` ends, the next list is `succ(L)[k]` where `k` is defined in §5.3.

| Finished list `L` | `succ(L)[0]` | `succ(L)[1]` | `succ(L)[2]` |
|---|---|---|---|
| 0 | 1 | 2 | 1 |
| 1 | 0 | 2 | 2 |
| 2 | 0 | 1 | 0 |
| 3 | 0 | 1 | 2 |

### 4.3  Behaviours

Behaviours 1, 3, 4, 5, 6, 7, 9 and 10 are the only values that ever appear in `Substate`.

A behaviour has a **first-frame action** (run on the first `Step` of its shot) and a
**per-frame action** (run on every `Step` of its shot, including the first one, after the
first-frame action). "Fresh E" means "call `GetEnemyView()` and make the result the new
`View`". "Fresh F(s, i)" means "call `GetFighterView(s, i)` and make the result the new
`View`". `R` denotes `View.RotationY`.

Two composite first-frame actions are used:

* **Player side** = set `EnemyVisible = false`; Fresh F(0, 8); then
  `Tz ← Tz − D`, `Vpz ← Vpz + D`, `Vrz ← Vrz + D`.
* **Enemy side** = set `PlayerVisible = false`; Fresh F(16, 0); then
  `Tz ← Tz + D`, `Vpz ← Vpz − D`, `Vrz ← Vrz − D`.

| Behaviour | First-frame action | Per-frame action |
|---|---|---|
| 1 — overview orbit | Fresh E; `R ← M` (the remembered orbit yaw, §5.4) | `R ← wrap(R + 2)`; then `M ← R` |
| 3 — player side, pan left | Player side | `R ← wrap(R + 1)` |
| 4 — enemy side, pan right | Enemy side | `R ← wrap(R − 1)` |
| 5 — player side, swept | Player side; `R ← wrap(R + 227)` | `R ← wrap(R − 1)` |
| 6 — enemy side, swept | Enemy side; `R ← wrap(R − 227)` | `R ← wrap(R + 1)` |
| 7 — player close-up | set `EnemyVisible = false`; Fresh F(0, 10) | none |
| 9 — high wide view | Fresh E; then `Vpx ← −5120`, `Vpy ← −10240`, `Vpz ← 0`, `Vrx ← 0`, `Vry ← 0`, `Vrz ← 0`, `Projection ← 200`; then `R ← wrap(R − 341)` | `R ← wrap(R + 1)` |
| 10 — plain overview | Fresh E | none |

(227 = `0xE3`, 341 = `0x155`, 5120 = `0x1400`, 10240 = `0x2800`, 200 = `0xC8`.)

Notes that change results:

* In behaviour 9 the absolute values *replace* the provider's position fields; only `R` and
  the untouched fields (`Tx`, `Ty`, `RotationX`, `RotationZ`, `Rz`) keep the provider's
  values, and `R` is then offset by −341 and wrapped.
* The first-frame offsets of behaviours 5, 6 and 9 are wrapped immediately, then the
  per-frame rotation of the same `Step` is applied and wrapped again.
* Behaviours 3 and 4 do **not** wrap `R` on their first frame before the per-frame action, but
  the per-frame action wraps it, so after any `Step` in behaviours 1, 3, 4, 5, 6, 9 the yaw is
  in `[0, 4095]`. Behaviours 7 and 10 leave the provider's `RotationY` as is (possibly outside
  `[0, 4095]`).
* The side offsets on `Tz`, `Vpz`, `Vrz` are plain 32-bit additions, never wrapped to a turn.
* `Projection` is only changed by behaviour 9.

---

## 5  State and transitions

### 5.1  Hidden state

Besides the public properties, the camera keeps:

* `first` — whether the next `Step` is the first one of the current shot.
* `M` — the remembered orbit yaw (§5.4).

### 5.2  Construction

`new BattleShotCamera(rng, views)`:

1. `List = 3`, `Shot = 0`, `Time = 90`, `Substate = 10`, `first = true`.
2. `PlayerVisible = true`, `EnemyVisible = true`.
3. `View = GetEnemyView()` (exactly one provider call).
4. `M = View.RotationY` — the raw provider value, **not** wrapped.

Neither argument is validated at construction. A `null` `rng` fails only when a list ends.

`new BattleShotCamera(rng, enemyCameraCount)` is a convenience for tests: it behaves exactly
like the first constructor with providers that ignore their arguments and always return a
fresh view whose fields are all 0 except `Projection = 280`. `enemyCameraCount` is ignored
(any value, including negative, is accepted).

### 5.3  `Step()`

One frame. In order:

1. Run the current behaviour (table §4.3): the first-frame action if `first` is true, then the
   per-frame action. Set `first = false`.
2. `Time ← Time − 1`.
3. If `Time = 0`, start the next shot:
   1. `PlayerVisible = true`, `EnemyVisible = true`.
   2. `Shot ← Shot + 1`. If `Shot` equals the length of list `List`:
      * `r = rng.Next()`; `k = |r| mod 3` (with `|r| ≥ 0`, so `k ∈ {0, 1, 2}`);
      * `List ← succ(List)[k]`, `Shot ← 0`.
      * If `r = int.MinValue` (−2147483648), its magnitude is not representable: `Step`
        throws `System.OverflowException` at this point. The camera's state after that
        exception is unspecified and the instance must not be used further.
   3. `Time ← duration` and `Substate ← behaviour` of shot `Shot` of list `List`.
   4. `first ← true`.

Consequences:

* A shot of duration `T` is in effect for exactly `T` calls to `Step`; its behaviour runs on
  each of those calls.
* `View` is **not** refreshed when a shot ends. Between the `Step` that ends a shot and the
  next `Step`, `View` still shows the last frame of the old shot while `Substate`, `Time`,
  `Shot` and `List` already describe the new shot, and both visibility flags are `true`.
* A visibility flag cleared by a behaviour stays cleared until the end of that shot or a
  `Stop()`.

### 5.4  Remembered orbit yaw `M`

`M` survives across shots and lists. It is set once at construction (raw provider yaw) and
afterwards only by the per-frame action of behaviour 1. Every behaviour-1 shot therefore
resumes the orbit where the previous behaviour-1 shot left off, regardless of the yaw the
enemy provider returns for its fresh view. `M` is not changed by `Stop()`.

### 5.5  `StepFrames(frames)`

Calls `Step()` `frames` times. `frames ≤ 0` does nothing.

### 5.6  `Stop()`

1. `PlayerVisible = true`, `EnemyVisible = true`.
2. `View = GetEnemyView()` (exactly one provider call).

Nothing else changes: `List`, `Shot`, `Time`, `Substate`, `first` and `M` are kept. If `Step`
is called after `Stop()`, the camera simply carries on: if the shot already had its first
frame, only its per-frame action runs, applied to the view returned by `Stop()` (for example
behaviour 3 then turns the overview view by +1 per frame, without the side offsets and without
hiding a fighter).

---

## 6  Provider call schedule

Provider calls are observable (the caller may log them). They happen only:

* at construction: one `GetEnemyView()`;
* on the first `Step` of a shot: one call, per behaviour — 1, 9, 10: `GetEnemyView()`;
  3, 5: `GetFighterView(0, 8)`; 4, 6: `GetFighterView(16, 0)`; 7: `GetFighterView(0, 10)`;
* on every `Stop()`: one `GetEnemyView()`.

`IShotCameraRng.Next()` is called exactly once per completed shot list and never otherwise.

---

## 7  Timeline of the opening (from construction)

| After | List | Shot | Time | Substate | Note |
|---|---|---|---|---|---|
| construction | 3 | 0 | 90 | 10 | `View` = provider view #1 |
| 1 `Step` | 3 | 0 | 89 | 10 | `View` = provider view #2 (fresh E) |
| 90 `Step`s | 3 | 1 | 90 | 5 | view #2 still shown |
| 91 `Step`s | 3 | 1 | 89 | 5 | player side view, enemy hidden |
| 180 `Step`s | `succ(3)[k]` | 0 | first shot of that list | — | `rng.Next()` called once |

Full list lengths in frames: list 0 = 3420, list 1 = 2040, list 2 = 2940, list 3 = 180.

---

## 8  Worked examples (invented values)

### Example A — neutral views, opening list

`new BattleShotCamera(rng, 3)` with `rng` always returning `0`.

* After 90 `Step`s: `List 3, Shot 1, Time 90, Substate 5`, view all zero, `Projection 280`.
* 91st `Step` (behaviour 5, first frame): Player side gives `Tz = −5120`, `Vpz = 5120`,
  `Vrz = 5120`, `EnemyVisible = false`; then `R = wrap(0 + 227) = 227`; per-frame
  `R = wrap(227 − 1) = 226`. `Time = 89`.
* After 180 `Step`s total: `R = 227 − 90 = 137`; the shot ends, `k = |0| mod 3 = 0`, so
  `List = succ(3)[0] = 0`, `Shot 0`, `Time 1800`, `Substate 1`, both flags `true`, `View`
  still has `R = 137` and the side offsets.
* 181st `Step` (behaviour 1, first frame): fresh zero view, `R ← M = 0`, then `R = 2`,
  `M = 2`.

### Example B — orbit memory across lists

Enemy provider returns views whose `RotationY` is `-100` at construction and `3000` at every
later call. `rng` returns `-4` (so `k = 4 mod 3 = 1`).

* Construction: `M = −100` (unwrapped).
* Opening list ends after 180 steps: `List = succ(3)[1] = 1`, first shot behaviour 6.
* List 1, shot 1 (behaviour 1) starts at step 180 + 360 + 1 = 541: fresh view
  (`RotationY = 3000` from the provider) is overridden: `R ← M = −100`, then
  `R = wrap(−100 + 2) = 3998`, `M = 3998`.
* After the 900 frames of that shot: `M = wrap(−100 + 2·900) = 1700`.
* The next behaviour-1 shot (in a later list) resumes at `R = 1700 + 2 = 1702` on its first
  frame.

### Example C — high wide view

Enemy provider view for the first frame of a behaviour-9 shot:
`Vpx 10, Vpy 20, Vpz 30, Vrx 40, Vry 50, Vrz 60, Tx 7, Ty 8, Tz 9, RotationX 1, RotationY 100,
RotationZ 2, Rz 3, Projection 400`.

After that first `Step`: `Vpx −5120, Vpy −10240, Vpz 0, Vrx 0, Vry 0, Vrz 0, Tx 7, Ty 8, Tz 9,
RotationX 1, RotationY wrap(100 − 341) + 1 = 3855 + 1 = 3856, RotationZ 2, Rz 3,
Projection 200`.

### Example D — enemy side with overflow

Fighter provider `(16, 0)` returns `Tz = 2147483000` (near `int.MaxValue`), `Vpz = −2147483000`,
`Vrz = 0`, `RotationY = 5000`, behaviour 4, first `Step`:

* `Tz = 2147483000 + 5120` wraps to `−2147479176`.
* `Vpz = −2147483000 − 5120` wraps to `2147479176`.
* `Vrz = −5120`.
* `R = wrap(5000 − 1) = 903`. `PlayerVisible = false`.

### Example E — Stop in the middle of a side shot

During a behaviour-3 shot, after its first frame, call `Stop()`: both flags become `true`, the
view becomes a fresh overview view (say `RotationY = 4095`). The next `Step` gives
`R = wrap(4095 + 1) = 0`, still no side offsets, no flag change, `Time` decreases by one as
usual.

### Example F — unrepresentable random value

Opening list, `rng.Next()` returns `int.MinValue`: the 180th `Step` throws
`OverflowException`. With `rng` returning `int.MinValue + 1` instead: `|r| = 2147483647`,
`k = 2147483647 mod 3 = 1`, next list `succ(3)[1] = 1`.

---

## 9  Edge cases summary

| Situation | Result |
|---|---|
| `StepFrames(0)` or negative | no effect, no provider or RNG call |
| `rng.Next()` negative | magnitude used: `k = |r| mod 3` |
| `rng.Next() = int.MinValue` | `OverflowException` from the `Step` ending the list |
| provider yaw outside `[0, 4095]` | wrapped by the first wrapped rotation; kept raw by behaviours 7 and 10 and in `M` at construction |
| view field overflow in side offsets | 32-bit wraparound, no exception |
| `Stop()` then `Step()` | same shot continues, per-frame action applies to the `Stop()` view |
| `Stop()` called repeatedly | one `GetEnemyView()` call each time |
| provider throws | exception propagates, state afterwards unspecified |
| `enemyCameraCount` (test constructor) | ignored |

---

## 10  Differential vectors

`tests/Dw3.Game.Tests/Vectors/battle-shot-camera.json` holds 340 fabricated scenarios replayed
by `BattleShotCameraVectorTests`. Each case gives the constructor kind (`neutral` = test
constructor, `views` = provider constructor), the cycled RNG values, the views returned by the
enemy and fighter providers in call order, a list of operations (`"1"` = `Step()`, `"s:N"` =
`StepFrames(N)`, `"x"` = `Stop()`), and after construction and after every operation a
snapshot `[List, Shot, Time, Substate, PlayerVisible, EnemyVisible, Vpx, Vpy, Vpz, Vrx, Vry,
Vrz, Tx, Ty, Tz, RotationX, RotationY, RotationZ, Rz, Projection]` (booleans as 0/1), plus the
provider calls made during that operation (`"E"`, `"F{slot}:{index}"`). A failing case records
the index of the operation that threw and the exception type name.
