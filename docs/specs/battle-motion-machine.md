# Battle Motion Machine — Clean-Room Specification

This document describes the **observable behaviour** of the battle motion machine: the
component that turns a battle model's motion table into a per-frame decision "show animation
frame *f*" or "blend toward animation frame *f* with weight *w*". It is self-contained: a
developer who has never seen the game or any other file of this codebase can reimplement the
component from this page alone.

Namespace: `Dw3Production.Presentation`. The component is engine-free: it never samples
animation tracks, never touches a clock and never renders. The caller advances it by a number
of elapsed frames and reads back a handful of integer and boolean properties.

Notation: all integers are 32-bit signed (`int`). `a & b` is the bitwise AND on the 32-bit
two's-complement representation. `⌊x⌋` is truncation toward zero. "Wrapping" means the result
is reduced modulo 2³² into the signed 32-bit range (ordinary unchecked C# `int` arithmetic).

---

## 1  Overview

A **motion table** is a list of motions. Motion numbers are **one-based**: motion 1 is the first
entry of the table. Motions 1 and 2 have a special role: they are the two **idle motions**, and
the **idle selection** (`IdleMotion`, 0 or 1) says which one the model returns to after an
action. Idle selection 0 means motion 1, idle selection 1 means motion 2.

A motion is described by a list of **steps**. When a motion becomes active, its steps are
**expanded** into a flat **keyframe list** (§4). Each keyframe-list entry carries three
integers: a value *K*, a target *T* and a source *S*. The machine then keeps a **cursor**
(`Keyframe`, an index into that list) and, on every `Step`, publishes what the entry under the
cursor asks for and moves the cursor forward (§5).

Keyframe values encode three kinds of entry:

| Value *K*                            | Meaning                                                                   |
|--------------------------------------|---------------------------------------------------------------------------|
| `K & 0x8000 == 0`                    | **plain frame**: show animation frame *K*.                                |
| `K & 0x8000 != 0`                    | **blend entry**: blend toward frame *T* with weight `K & 0x7FFF` (out of 4096). |
| exactly `0x8000`                     | additionally a **loop marker** when the cursor lands on it: jump to entry *T*. |
| exactly `0xFFFF`                     | additionally a **finish marker** when the cursor lands on it: the motion is done. |

The loop and finish markers are also blend entries by the bit test above; that only matters if
the cursor is ever *read* while sitting on one (§5.2).

---

## 2  Public API

```csharp
public sealed class BattleMotionMachine
{
    public BattleMotionMachine(IReadOnlyList<IReadOnlyList<IReadOnlyList<int>>> motions, int idleMotion = 0);

    public int  Motion      { get; }   // active motion, one-based
    public int  IdleMotion  { get; }   // idle selection; 0 → motion 1, 1 → motion 2
    public int  Keyframe    { get; }   // cursor into Keyframes
    public int  Frame       { get; }   // frame to show, or blend destination frame
    public int  Blend       { get; }   // blend weight, 0..0x7FFF (4096 = full)
    public bool Blending    { get; }   // true when the last read entry was a blend entry
    public bool MotionDone  { get; }   // the cursor reached a finish marker
    public bool ToIdle      { get; }   // the active motion returns to the idle motion when done
    public int  BlendTarget { get; }   // destination frame of the current blend, 0 when none
    public int  BlendSource { get; }   // source of the current blend (see §5.2), -1 when none
    public IReadOnlyList<int> Keyframes { get; } // the K values of the active motion, in order
    public bool Looping     { get; }   // Keyframes non-empty and its last value is exactly 0x8000

    public void SetMotion(int motion, bool restart);
    public void Step(int framesElapsed);
    public void SetIdleMotion(int idleMotion);
}
```

All properties are read-only for callers. `Keyframes` is a live read-only view of the current
list (only its contents matter; it is replaced/refilled by every effective `SetMotion`). The *T*
and *S* columns of the keyframe list are private but observable through `Frame`,
`BlendTarget`, `BlendSource` and `Keyframe` (after a loop jump).

Callers use the machine like this: after `Step(n)`, if `Blending` is false they pose the model
at `Frame`; if `Blending` is true they blend from a pose chosen by `BlendSource` (the value
`0xFFFF` means "the pose currently displayed", any other value is a frame number) toward the
pose of `Frame` with weight `Blend / 4096`, capturing the source pose again whenever
`BlendTarget` changed during the step.

---

## 3  Data model

### 3.1  Steps

A step is a list of integers; only the first four are ever read, named here:

| Position | Name | Role                                                                     |
|----------|------|--------------------------------------------------------------------------|
| 0        | *I*  | step index; `0x7FFF` = end marker; otherwise only "zero / non-zero" matters |
| 1        | *C*  | count of keyframes produced by the step; `0` = terminator step           |
| 2        | *F*  | first frame (plain run), target frame (when read as a successor), or terminator value |
| 3        | *E*  | end frame; `0` marks a **blend run**, any other value a **plain run**; for a terminator, the loop target |

A step must contain every position that the rules below read for it; a missing position is a
malformed table (§7).

### 3.2  Idle frames

At construction the machine records two **idle frames**:

* φ₀ = *F* of the first step of motion 1, or 0 if the table has no motion 1 or motion 1 has
  no steps;
* φ₁ = *F* of the first step of motion 2, or 0 if the table has no motion 2 or motion 2 has
  no steps.

They are computed once, at construction, and never change.
The first step's *I*, *C* and *E* are irrelevant here (an end-marker step still provides its *F*).

---

## 4  `SetMotion(motion, restart)` — activating and expanding a motion

1. If `restart` is false and `motion == Motion`: **nothing happens** (no validation, no reset).
2. If `motion < 1` or `motion > motions.Count`: throw `ArgumentOutOfRangeException` with
   parameter name `"motion"`. The state is unchanged.
3. Otherwise the machine resets:
   * `Motion = motion`;
   * if `motion == 1` then `IdleMotion = 0`; if `motion == 2` then `IdleMotion = 1`; any other
     motion leaves `IdleMotion` unchanged;
   * `Keyframe = 1`, `MotionDone = false`, `ToIdle = false`, `Blending = false`,
     `BlendTarget = 0`, `BlendSource = -1`, keyframe list emptied;
   * **`Frame` and `Blend` keep their previous values.**
4. The steps of the motion are expanded in order, step *j* = 0, 1, 2, … (see below). Expansion
   stops at an end-marker step, after a terminator step, or when the step list is exhausted
   (no terminator is required). Steps after the stopping point are never read.
5. If the resulting keyframe list is empty, throw `InvalidOperationException`. The machine must
   not be used afterwards (its state is unspecified).

Expansion rules for step *j* = (*I*, *C*, *F*, *E*):

* **End marker** — *I* = `0x7FFF`: stop; the step contributes nothing (its other fields are
  not read).
* **Terminator** — *C* = 0: append one entry (*K* = *F* & `0xFFFF`, *T* = *E*, *S* = −1) and
  stop. Thus *F* = −1 yields the finish marker `0xFFFF`, *F* = `0x8000` yields a loop marker
  that jumps to entry *E*, and any other value yields an ordinary (plain or blend) entry after
  masking to 16 bits.
* **Blend run** — *C* ≠ 0 and *E* = 0:
  * target τ = *F* of step *j*+1 (raw, unmasked). If τ = −1: τ = φ[`IdleMotion`] (using the
    `IdleMotion` value *after* the reset of point 3) and `ToIdle = true`;
  * source σ = `0xFFFF` if *I* = 0, otherwise σ = *E* of step *j*−1;
  * for *i* = 1 … *C* append (*K* = ⌊wrap(*i*·4096) / (*C*+1)⌋ | `0x8000`, *T* = τ, *S* = σ),
    where the product wraps in 32 bits and the division truncates toward zero.
    For 1 ≤ *C* ≤ 4095 the weights are 1 … 4095 and strictly increasing; for *C* ≥ 4096 the
    first weights are 0, i.e. the entries are exactly `0x8000` (loop markers whose target is τ).
  * If *C* < 0 nothing is appended, but τ, `ToIdle` and σ are still evaluated (so the
    successor and predecessor rules still apply).
* **Plain run** — *C* ≠ 0 and *E* ≠ 0: for *i* = 0 … *C*−1 append (*K* = wrap(*F* + *i*),
  *T* = 0, *S* = −1). If *C* < 0 nothing is appended.

`Looping` is true exactly when the list is non-empty and its last *K* is exactly `0x8000`.

---

## 5  `Step(framesElapsed)` — advancing

Let *n* = `framesElapsed`, *N* = number of keyframe entries.

### 5.1  Validation and the finished state

* If *n* < 0: throw `ArgumentOutOfRangeException` with parameter name `"framesElapsed"`. The
  state is unchanged.
* If `MotionDone` is true: set `Blending = false` and `BlendTarget = 0` (leave `BlendSource`,
  `Frame`, `Blend` and `Keyframe` as they are). Then, if `ToIdle` is true, perform
  `SetMotion(IdleMotion + 1, restart: false)` — which does nothing if the idle motion is already
  the active motion, so a finished idle motion stays finished. Return. This step publishes no
  keyframe of the new motion; the next `Step` does.

### 5.2  Reading the current entry

Otherwise, with *k* = `Keyframe` (it must satisfy 0 ≤ *k* < *N*, else see §7) and entry
(*K*, *T*, *S*):

* if *K* & `0x8000` ≠ 0 (blend entry):
  * if `BlendTarget` ≠ *T*: `BlendTarget = T` and `BlendSource = S`;
    otherwise **`BlendSource` keeps its previous value** (in particular, a blend toward frame 0
    started from the reset state keeps `BlendSource = -1`);
  * `Frame = T`, `Blend = K & 0x7FFF`, `Blending = true`;
* otherwise (plain entry): `BlendTarget = 0`, `BlendSource = -1`, `Frame = K`,
  `Blending = false`; **`Blend` keeps its previous value**.

### 5.3  Moving the cursor

* *k*′ = min(wrap(*k* + *n*), *N* − 1). `Keyframe = k′`. (If the wrapped sum is negative,
  *k*′ is negative and the step fails, §7.) The cursor therefore saturates on the last entry
  and never runs past it.
* Look at the entry *k*′ = (*K*′, *T*′, ·):
  * *K*′ exactly `0x8000`: `Keyframe = T′` (a single jump; *T*′ is not validated — an index
    outside 0 … *N*−1 makes the **next** reading step fail);
  * *K*′ exactly `0xFFFF`: `MotionDone = true`;
  * anything else: nothing more.

Consequences worth noting:

* Entry 0 of the list is never read on the first `Step` after a `SetMotion`, because the cursor
  starts at 1; it is only reachable through a loop jump (or a jump target of 0).
* With *n* = 0 the cursor does not move, but the marker check of §5.3 still runs on the current
  entry.
* The marker check only looks at the landing entry: markers skipped over by a large *n* have no
  effect, but saturation on the last entry means a final marker is always seen.
* Plain frame values that have bit 15 set (for instance negative frames, or `0x8001`) are read
  as blend entries.

---

## 6  `SetIdleMotion(idleMotion)` and the constructor

* `SetIdleMotion(k)`: if *k* is not 0 or 1, throw `ArgumentOutOfRangeException` with parameter
  name `"idleMotion"` (state unchanged); otherwise `IdleMotion = k`. Nothing else changes; the
  current keyframe list (including idle targets already resolved by expansion) is not rebuilt.
* Constructor `(motions, idleMotion = 0)`:
  1. compute φ₀ and φ₁ (§3.2);
  2. initial state: `Motion = 1`, `IdleMotion = idleMotion` (**not validated**), `Keyframe = 1`,
     `Frame = 0`, `Blend = 0`, `Blending = false`, `MotionDone = false`, `ToIdle = false`,
     `BlendTarget = 0`, `BlendSource = -1`, empty keyframe list;
  3. perform `SetMotion(idleMotion + 1, restart: true)`; any exception propagates out of the
     constructor (for example `ArgumentOutOfRangeException("motion")` for `idleMotion = -1`
     or when the table is too short, `InvalidOperationException` when the motion expands to an
     empty list).

  An `idleMotion` of 2 or more is accepted when that motion exists; it then stays the idle
  selection until a `SetMotion(1)`/`SetMotion(2)` or `SetIdleMotion` replaces it, and any rule
  that needs φ[`IdleMotion`] or `SetMotion(IdleMotion + 1)` behaves as described in §7.

---

## 7  Errors and malformed tables

Argument errors (`ArgumentOutOfRangeException` with parameter `"motion"` from `SetMotion`,
`"framesElapsed"` from `Step`, `"idleMotion"` from `SetIdleMotion`) are raised before any
state change, and the machine remains fully usable.

Every other failure leaves the machine in an unspecified state; callers must discard it. An
implementation must throw (the exception type is free unless stated) in each of these cases:

* a step lacks a position that the rules read (for example a terminator without *E*, a run
  without *E*, a first step of motion 1 or 2 without *F* at construction);
* a blend run is the last step of the motion (no successor to read τ from);
* a blend run with *I* ≠ 0 is the first step (no predecessor to read σ from);
* a −1 successor target is expanded while `IdleMotion` is neither 0 nor 1;
* `Step` reads an entry while `Keyframe` is outside 0 … *N*−1 (a single-entry list on its
  first step, an out-of-range loop target, or a negative wrapped cursor);
* the expansion produces an empty list: `InvalidOperationException` (§4.5);
* the idle return of §5.1 asks for a motion number outside the table:
  `ArgumentOutOfRangeException` with parameter `"motion"`, after `Blending` and `BlendTarget`
  have already been cleared.

---

## 8  Worked examples (invented values)

### 8.1  Plain run with a loop

Table: motion 1 = [(0, 3, 20, 22), (1, 0, 0x8000, 1)].
Expansion: *K* = [20, 21, 22, 0x8000], *T* = [0, 0, 0, 1]; `Looping` = true.

| Call      | Keyframe after | Frame | Blending |
|-----------|----------------|-------|----------|
| ctor      | 1              | 0     | false    |
| `Step(1)` | 2              | 21    | false    |
| `Step(1)` | 3 → jump → 1   | 22    | false    |
| `Step(1)` | 2              | 21    | false    |

Frame 20 (entry 0) is never shown because the loop target is 1.

### 8.2  Saturation and large steps

Motion 1 = [(0, 6, 30, 35), (1, 0, 0x8000, 0)] → *K* = [30 … 35, 0x8000], *N* = 7.
`Step(3)`: reads entry 1 (Frame 31), cursor 4. `Step(10)`: reads entry 4 (Frame 34),
cursor min(14, 6) = 6, a loop marker → cursor 0. `Step(1)`: reads entry 0 (Frame 30), cursor 1.

### 8.3  Action blending back to idle

Table: motion 1 = [(0, 4, 10, 13), (1, 0, 0x8000, 0)], motion 2 = [(0, 3, 50, 52),
(1, 0, 0x8000, 0)], motion 3 = [(0, 3, 100, 102), (1, 3, 0, 0), (2, 0, −1, 0)].
φ₀ = 10, φ₁ = 50. Constructed with `idleMotion = 0`, then `SetMotion(3, true)`
(`IdleMotion` stays 0):

* run 1 plain: 100, 101, 102;
* run 2 blend, *C* = 3: successor *F* = −1 → τ = φ₀ = 10, `ToIdle` = true; *I* = 1 ≠ 0 →
  σ = *E* of the previous step = 102; weights ⌊1·4096/4⌋ = 1024, 2048, 3072 → *K* = 0x8400,
  0x8800, 0x8C00;
* terminator: −1 & 0xFFFF = 0xFFFF.

*K* = [100, 101, 102, 33792, 34816, 35840, 65535].

| `Step(1)` # | Motion | Keyframe | Frame | Blend | Blending | Done | BlendTarget | BlendSource |
|-------------|--------|----------|-------|-------|----------|------|-------------|-------------|
| 1           | 3      | 2        | 101   | 0     | false    | no   | 0           | −1          |
| 2           | 3      | 3        | 102   | 0     | false    | no   | 0           | −1          |
| 3           | 3      | 4        | 10    | 1024  | true     | no   | 10          | 102         |
| 4           | 3      | 5        | 10    | 2048  | true     | no   | 10          | 102         |
| 5           | 3      | 6        | 10    | 3072  | true     | yes  | 10          | 102         |
| 6           | 1      | 1        | 10    | 3072  | false    | no   | 0           | −1          |
| 7           | 1      | 2        | 11    | 3072  | false    | no   | 0           | −1          |

Step 6 is the idle return: `SetMotion(1, false)` resets the motion state but keeps `Frame` and
`Blend`; frame 11 is published only on step 7. Had `SetIdleMotion(1)` been called before
`SetMotion(3, true)`, τ would have been φ₁ = 50 and the return would go to motion 2.

### 8.4  Sticky blend source

Motion 1 = [(0, 2, 5, 6), (0, 2, 0, 0), (2, 0, 0, 0)]: the blend run has *I* = 0 (σ = 0xFFFF)
and its successor (the terminator) has *F* = 0, so τ = 0. *K* = [5, 6, 0x9555, 0xAAAA, 0]
(⌊4096/3⌋ = 1365, ⌊8192/3⌋ = 2730). Steps: Frame 6 (plain); then a blend entry with *T* = 0 —
`BlendTarget` is already 0, so `BlendSource` stays −1 although the entry's source is 0xFFFF;
Frame 0, Blend 1365, then Blend 2730.

### 8.5  Bit 15 and masking

* A plain run (0, 3, −3, 2) produces *K* = −3, −2, −1 (*T* = 0 for each): all have bit 15
  set, so reading one of them publishes `Blending = true`, `Frame = 0`, `Blend` = *K* & 0x7FFF
  (0x7FFD, 0x7FFE, 0x7FFF respectively), and `BlendSource` stays −1 because `BlendTarget` was
  already 0.
* A terminator with *F* = 0x18000 becomes *K* = 0x8000 (a loop marker); with *F* = 0x2FFFF it
  becomes 0xFFFF (a finish marker).
* A plain run reaching exactly 0xFFFF (for example *F* = 0xFFFD, *C* = 4) finishes the motion
  when the cursor lands on that entry.

### 8.6  Cursor wraparound

With a non-looping 5-entry list and `Keyframe = 1`, `Step(2147483647)` computes
wrap(1 + 2147483647) = −2147483648; min with 4 keeps it negative, so the step fails (§7).
With `Keyframe = 0` (after a loop to entry 0), `Step(2147483647)` saturates normally to *N*−1.

---

## 9  Differential vectors

`tests/Dw3.Game.Tests/Vectors/battle-motion-machine.json` holds fabricated scenarios replayed
by `BattleMotionMachineVectorTests`. Each case gives `motions` (the table), `idle` (constructor
argument), `ops` (`"s:N"` = `Step(N)`, `"m:M:R"` = `SetMotion(M, R == 1)`, `"i:K"` =
`SetIdleMotion(K)`), and, for the constructor (index 0) and each operation:

* `outcomes`: `"ok"`; `"V:param"` = argument error of §7 (state unchanged, scenario
  continues); `"E:type"` = the scenario stops with exactly that exception type;
  `"E:*"` = the scenario stops with any exception;
* `snapshots`: `[Motion, IdleMotion, Keyframe, Frame, Blend, Blending, MotionDone, ToIdle,
  BlendTarget, BlendSource, Looping]` (booleans as 0/1), `null` for a stopping outcome;
* `keyframes`: the full `Keyframes` list after the constructor, then only when it changed
  (`null` otherwise).
