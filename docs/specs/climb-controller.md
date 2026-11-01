# Climb Controller — Clean-Room Specification

This document describes the **observable behaviour** of the wall-climbing subsystem:
the `ClimbController` state machine and the `ClimbAnimation.Sample` helper.
It is written for a developer who has never seen any other file in this codebase.

---

## 1  Overview

The climb subsystem simulates a two-phase wall traverse:

* **Mount** — the actor grips the wall and plays a mount animation.
* **Travel** — held Up climbs; held Down descends; neither grips in place.
* **Dismount** — a one-shot animation plays and the actor lands on or at the
  foot of the wall, restoring normal locomotion.

The controller is a pure simulation object; it does not drive Godot physics.
Position advances in **screen pixels** (`float`) while the internal climb
height advances in **source units** (1 source unit = 1/256 pixel).

---

## 2  Types

### 2.1  `ClimbSubstate` (enum)

```
None
GetOnWall
GetOverEdge
Climb
ClimbUp
ClimbDown
ClimbOffTop
ClimbOffBottom
```

`None` means the controller is inactive.  All other values are active substates.

### 2.2  `ClimbRecord` (record)

| Field      | Type  | Meaning                                   |
|------------|-------|-------------------------------------------|
| `Frame`    | `int` | Sprite frame index.                       |
| `Duration` | `int` | Number of simulation ticks this frame is shown. |
| `OffsetX`  | `int` | Horizontal sprite offset relative to the actor foot, in pixels. |
| `OffsetY`  | `int` | Vertical sprite offset relative to the actor foot, in pixels.   |

### 2.3  `ClimbSequence` (record)

| Field     | Type                        | Meaning |
|-----------|-----------------------------|---------|
| `Set`     | `int`                       | Numeric identifier of this animation set. |
| `Loops`   | `bool`                      | `true` = the sequence cycles forever; `false` = one-shot (holds last frame). |
| `Records` | `IReadOnlyList<ClimbRecord>` | Ordered list of frame records. |

### 2.4  `ClimbData` (record)

```
ClimbData(IReadOnlyDictionary<int, ClimbSequence> Sequences)
```

A dictionary of animation sequences keyed by set id.
`ClimbController.SetData` must be called before any `Start*` call.
Calling `Start*` without data throws `InvalidOperationException`.

### 2.5  `ClimbAnimationSample` (readonly record struct)

```
ClimbAnimationSample(int Frame, int OffsetX, int OffsetY)
```

Result returned by `ClimbAnimation.Sample`.

---

## 3  Constants (observable values)

| Name                | Value | Meaning |
|---------------------|-------|---------|
| Climb step          | 256   | Source units added or removed from climb height each tick of travel. |
| Side-shift pixels   | 16    | Horizontal pixel offset applied when mounting or dismounting the wall top. |
| Edge-clearance pixels | 24  | Fixed vertical offset added above the wall height when hopping the edge. |
| Camera-smoothing hold | 16 frames | Smoothing tick count set after edge-hop and ClimbOffTop. |

---

## 4  Animation set identifiers (observable integer values)

| Substate       | Set id (decimal) | Set id (hex) |
|----------------|-----------------|--------------|
| GetOnWall      | 28              | 0x1C         |
| GetOverEdge    | 30              | 0x1E         |
| Climb          | 32              | 0x20         |
| ClimbUp        | 26              | 0x1A         |
| ClimbDown      | 27              | 0x1B         |
| ClimbOffTop    | 29              | 0x1D         |
| ClimbOffBottom | 31              | 0x1F         |

---

## 5  `ClimbAnimation.Sample`

```csharp
public static ClimbAnimationSample Sample(ClimbData data, int set, int ticks)
```

### 5.1  Preconditions

* `data.Sequences` must contain `set`; otherwise `InvalidOperationException`.
* The sequence must have at least one record with total duration > 0;
  otherwise `InvalidOperationException`.

### 5.2  Frame selection

Let **T** = sum of `Duration` over all records in the sequence.

* If `Loops = true`:  **at** = ticks mod T
* If `Loops = false`: **at** = min(ticks, T − 1)

Iterate the records in order:

```
for each record r:
    if at < r.Duration:
        return Sample(r.Frame, r.OffsetX, r.OffsetY)
    at -= r.Duration
```

If the loop exits without returning (which can only happen due to a
floating-point rounding artefact or an empty tail), return the last record.

### 5.3  Examples (with two-record looping sequence, durations 6 and 6, total = 12)

| ticks | at (= ticks mod 12) | frame |
|-------|---------------------|-------|
| 0     | 0                   | record 0 (at < 6) |
| 5     | 5                   | record 0 |
| 6     | 6                   | record 1 (at – 6 = 0 < 6) |
| 11    | 11                  | record 1 |
| 12    | 0                   | record 0 (wraps) |
| 20    | 8                   | record 1 |

With a one-shot sequence of duration 8 (two records at 4+4):

| ticks | at (= min(ticks, 7)) | frame     |
|-------|----------------------|-----------|
| 0     | 0                    | record 0  |
| 3     | 3                    | record 0  |
| 4     | 4                    | record 1  |
| 7     | 7                    | record 1  |
| 100   | 7                    | record 1 (holds last frame) |

---

## 6  `ClimbController` public API

```csharp
public bool IsActive { get; }
public int  Direction { get; }
public void SetData(ClimbData data)
public void StartClimbUp(GameState state, int dir, int x, int y, int height)
public void StartClimbDown(GameState state, int dir, int x, int y, int height)
public void Tick(GameState state, InputState input)
```

### 6.1  `SetData`

Stores the animation data.  Must be called before any `Start*` call.

### 6.2  `StartClimbUp`

Initialises an upward climb.  The actor starts at the wall foot.

**Parameters**

| Param    | Meaning |
|----------|---------|
| `dir`    | Source direction (0–7). |
| `x`, `y` | Screen pixel position of the actor at the wall foot. |
| `height` | Wall height in pixels (non-negative integer). |

**Effects on internal state**

* `_direction = dir`
* `_climbSide = (dir != 5) ? 1 : 0`
* `_climbHeight = 0` (source units)
* `_wallHeight = height × 256` (source units)
* `_overEdgeOffsetApplied = false`
* Substate → **GetOnWall**; `_animSet` ← animation set for GetOnWall; `_animTicks = 0`

**Effects on `GameState`**

* `Position = (x, y)`
* `Facing = dir`  (via `SetFacing`; throws if dir ∉ 0–7)
* `AnimationDirection = dir`
* `Offer = false`
* All fields from **WriteTo** (see §8)

### 6.3  `StartClimbDown`

Initialises a downward climb.  The actor starts visually at the wall top:
the position is shifted immediately to (x + Δx, y + clearance + height).

**Parameters** — same as `StartClimbUp`.

**Effects on internal state**

* `_direction = dir`
* `_climbSide = (dir != 5) ? 1 : 0`
* `_climbHeight = height × 256` (source units; starts at the top)
* `_wallHeight  = height × 256`
* `_overEdgeOffsetApplied = true`
* Substate → **GetOverEdge**; animation set for GetOverEdge; `_animTicks = 0`

**Effects on `GameState`**

Let **S** = side-shift pixels = 16, **E** = edge-clearance pixels = 24.
Let **Δx** = `_climbSide != 0 ? +S : −S`.

* `Position = (x + Δx, y + E + height)`
* `Facing = dir`
* `AnimationDirection = dir`
* `Offer = false`
* `CameraSmoothingTicks = 16`
* All fields from **WriteTo**

### 6.4  `Tick`

Advances the simulation by one step.
Throws `InvalidOperationException` if called while `IsActive = false`.

**Step 1 — input-driven transition** (only while in `Climb`, `ClimbUp`, or `ClimbDown`)

| Input held | Next substate |
|------------|--------------|
| Up (only Up) | ClimbUp |
| Down (only Down) | ClimbDown |
| Neither | Climb |

If the current substate already equals the target, no transition occurs.

**Step 2 — per-substate action**

| Substate      | Action |
|---------------|--------|
| `GetOnWall`   | If animation done → transition to **Climb**. |
| `GetOverEdge` | If `_overEdgeOffsetApplied = false`: apply position hop (same formula as `StartClimbDown`), set `CameraSmoothingTicks = 16`, mark applied.  Then if animation done → transition to **Climb**. |
| `Climb`       | No action. |
| `ClimbUp`     | `_climbHeight += 256`.  If `_climbHeight ≥ _wallHeight`: clamp `_climbHeight = _wallHeight`, transition to **ClimbOffTop**. |
| `ClimbDown`   | `_climbHeight -= 256`.  If `_climbHeight ≤ 0`: clamp `_climbHeight = 0`, transition to **ClimbOffBottom**. |
| `ClimbOffTop` | If animation done → **finish ClimbOffTop** (see §7.1). |
| `ClimbOffBottom` | If animation done → **finish ClimbOffBottom** (see §7.2). |

**Step 3 — increment animation ticks**

`_animTicks += 1`

**Step 4 — WriteTo** (see §8)

---

## 7  Finish actions

### 7.1  Finish ClimbOffTop

Applies a position hop and ends the climb.

Let **S** = 16, **E** = 24, **H** = `_wallHeight ÷ 256` (integer pixels).
Let **Δx** = `_climbSide != 0 ? −S : +S`  (opposite sign from mount hop).

```
state.Position.X += Δx
state.Position.Y -= (E + H)
state.CameraSmoothingTicks = 16
→ EndClimb
```

### 7.2  Finish ClimbOffBottom

```
state.SetFacing(0)     // resets direction to 0
→ EndClimb
```

### 7.3  EndClimb (shared)

```
_substate       = None
_climbHeight    = 0
state.ClimbActive        = false
state.ClimbHeight        = 0
state.AnimationSet       = 1        (stand animation)
state.AnimationDirection = state.Facing   (immediately overridden by WriteTo — see note)
state.AnimationTicks     = 0
```

**Note on AnimationDirection:** `WriteTo` (§8) executes immediately after `EndClimb`
and sets `state.AnimationDirection = _direction` (the original climb direction).
Therefore the observable `state.AnimationDirection` after any `Tick` is always
`_direction`, regardless of substate.

---

## 8  WriteTo — fields written every tick

After every `Tick` call (including the tick that ends the climb):

```
state.ClimbActive        = (_substate != None)
state.ClimbHeight        = _climbHeight
state.ClimbSide          = _climbSide
state.ClimbAnimationSet  = _animSet
state.ClimbAnimationTicks = _animTicks    ← already incremented in Step 3
state.AnimationDirection = _direction
```

Fields **not** written by `WriteTo` (only written at specific moments):
`Position`, `Facing`, `CameraSmoothingTicks`, `Offer`, `AnimationSet`, `AnimationTicks`.

---

## 9  Animation-done condition

A substate animation is **done** when:

```
IsAnimDone = (total duration of the current set's records) ≤ _animTicks
```

where total duration = sum of `Record.Duration` for all records in the set.

If the set id is not present in `ClimbData`, or the set exists but has no records,
`IsAnimDone = false` (the animation never completes, blocking the transition).

Looping sequences (`Loops = true`) never trigger `IsAnimDone = true`, so the
substates that check animation-done only use non-looping sets.

---

## 10  Substate → animation set mapping

| Substate       | Animation set id |
|----------------|-----------------|
| GetOnWall      | 28 (0x1C) — one-shot |
| GetOverEdge    | 30 (0x1E) — one-shot |
| Climb          | 32 (0x20) — loops |
| ClimbUp        | 26 (0x1A) — loops |
| ClimbDown      | 27 (0x1B) — loops |
| ClimbOffTop    | 29 (0x1D) — one-shot |
| ClimbOffBottom | 31 (0x1F) — one-shot |

In the standard animation archive the one-shot total durations are:
GetOnWall = 8 ticks, GetOverEdge = 4 ticks, ClimbOffTop = 4 ticks, ClimbOffBottom = 4 ticks.
These values come from the archive data, not from the controller.

---

## 11  Transition behaviour

```csharp
Transition(ClimbSubstate next):
    if _substate == next: return   // no-op on same substate
    _substate = next
    _animSet  = AnimFor(next)      // see §10
    _animTicks = 0
```

`_animTicks` resets to 0 on every substate change.
On the same tick that a transition fires, `_animTicks++` fires afterwards,
so `state.ClimbAnimationTicks = 1` on the first tick in any new substate.

---

## 12  Side and direction rules

```
_climbSide = (dir != 5) ? 1 : 0
```

Direction 5 gives side 0; all other directions (0–4, 6–7) give side 1.

* Side 1 → initial x-hop is **+16** (StartClimbDown) and dismount x-hop is **−16** (ClimbOffTop).
* Side 0 → initial x-hop is **−16** and dismount x-hop is **+16**.

`_direction` (= `dir`) never changes after `Start*`.  It is written to
`state.AnimationDirection` every tick via WriteTo.

---

## 13  Edge cases

| Scenario | Behaviour |
|----------|-----------|
| `height = 0` | `_wallHeight = 0`. First ClimbUp tick: `_climbHeight = 256 ≥ 0`? No (0 ≥ 0). Wait — `_climbHeight += 256 = 256`, and `256 ≥ 0` is always true, so transition to ClimbOffTop immediately on the first ClimbUp tick. |
| `height = 0`, ClimbDown | `_climbHeight = 0 = _wallHeight`. First ClimbDown tick: `_climbHeight -= 256 = -256 ≤ 0` → clamped to 0, transition to ClimbOffBottom immediately. |
| Animation set missing from ClimbData | `IsAnimDone` returns `false`; the transition that waits for it never fires; the climb loops that substate forever. |
| `dir = 5` | `_climbSide = 0` → position hop is mirrored (−16 on mount, +16 on dismount). |
| `Tick` on inactive controller | Throws `InvalidOperationException`. |
| Hold Up and Down simultaneously | `HeldUp` check runs first; the actor climbs up. |
| Exactly reaching wallHeight | `_climbHeight` is clamped to `_wallHeight` (no overshoot). |
| Exactly reaching 0 | `_climbHeight` is clamped to 0 (no negative values). |

---

## 14  Worked examples

### 14.1  ClimbUp, height = 10, dir = 3 (side = 1), position (50, 80)

Standard data: GetOnWall set (28) total duration = 8 ticks.

| After event | ClimbActive | ClimbHeight | AnimSet | AnimTicks | Position |
|-------------|-------------|-------------|---------|-----------|----------|
| StartClimbUp | true | 0 | 28 | 0 | (50, 80) |
| Tick 0 (none) | true | 0 | 28 | 1 | (50, 80) |
| Tick 7 (none) | true | 0 | 28 | 8 | (50, 80) |
| Tick 8 (up)  | true | 256 | 26 | 1 | (50, 80) |
| …            | … | … | 26 | … | (50, 80) |
| Tick 17 (up) | true | 2560 | 26 | 10 | (50, 80) |
| ClimbUp reaches wallHeight (10 × 256 = 2560, tick 17) → ClimbOffTop | |
| Tick 17 (up) | true | 2560 | 29 | 1 | (50, 80) |
| Tick 18..20 (none) | true | 2560 | 29 | 2..4 | (50, 80) |
| Tick 21 (any) — IsAnimDone (animTicks 4 ≥ 4) → FinishClimbOffTop + EndClimb | |
| After tick 21 | false | 0 | 29 | 5 | (50 − 16, 80 − 34) = (34, 46) |

Note: `AnimationSet = 1` (stand), `AnimationTicks = 0` after EndClimb.
Position-Y: 80 − (24 + 10) = 46.

### 14.2  ClimbDown, height = 10, dir = 3 (side = 1), position (50, 80)

| After event | ClimbActive | ClimbHeight | AnimSet | AnimTicks | Position |
|-------------|-------------|-------------|---------|-----------|----------|
| StartClimbDown | true | 2560 | 30 | 0 | (66, 114) |

Position: x = 50 + 16 = 66, y = 80 + 24 + 10 = 114.

| Tick 0..3 (down) | true | 2560 | 30 | 1..4 | (66, 114) |
| Tick 4 — GetOverEdge done → Climb | true | 2560 | 32 | 1 | (66, 114) |
| Tick 5 (down) → ClimbDown | true | 2304 | 27 | 1 | (66, 114) |
| … 10 ticks of down → ClimbHeight = 0 at tick 14 → ClimbOffBottom |
| Tick after ClimbHeight = 0 | true | 0 | 31 | 1 | (66, 114) |
| Tick +4 — IsAnimDone → FinishClimbOffBottom + EndClimb |
| Final | false | 0 | 31 | 5 | (66, 114), Facing = 0 |

---

## 15  Self-review checklist

A developer implementing from this spec alone can answer:

- [ ] What fields does `WriteTo` write every tick?
- [ ] Which fields are written only at specific events (Start, FinishClimbOffTop, etc.)?
- [ ] What is the formula for `IsAnimDone`?
- [ ] What is the formula for side from direction?
- [ ] How does `_animTicks` reset on transition, and what value is observable after the first tick?
- [ ] What happens at `height = 0` for both Up and Down?
- [ ] What happens when `dir = 5`?
- [ ] What is the position formula for `StartClimbDown` and `FinishClimbOffTop`?
- [ ] When does `CameraSmoothingTicks` get set to 16?
- [ ] What is `state.AnimationDirection` after EndClimb?

All answers are given explicitly in the sections above.
