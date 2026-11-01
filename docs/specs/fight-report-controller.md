# Fight Report Controller — Specification

**Scope.** The `FightReportController` class and its supporting input/output records:
`FightReportPartner`, `FightReportInput`, `FightReportSlotView`, `FightReportPartnerView`,
`FightReportMessage`, and `FightReportView`. A clean-room developer can reimplement this
module from this document without consulting any other source file.

---

## 1. Supporting types

### 1.1 `FightReportPartner` — per-partner battle result

```csharp
public sealed record FightReportPartner(
    int PartyIndex,
    int PartnerId,
    int FighterId,
    int LevelBefore,
    int ExpBefore,
    int ExpGained,
    int LevelAfter,
    IReadOnlyList<int> SlotDigimonIds);
```

| Field | Meaning |
|-------|---------|
| `PartyIndex` | Zero-based index of this partner in the party |
| `PartnerId` | Identifier used as message substitution for the level-up notice |
| `FighterId` | Fighter identifier passed through to the partner view unchanged |
| `LevelBefore` | Partner level before exp was applied |
| `ExpBefore` | Cumulative partner exp before this battle |
| `ExpGained` | Exp earned this battle; **0 means this partner is skipped** in the report sequence |
| `LevelAfter` | Partner level after exp was applied |
| `SlotDigimonIds` | Up to 3 Digimon IDs in this partner's active slots (may be empty) |

### 1.2 `FightReportInput` — complete battle reward input

```csharp
public sealed record FightReportInput(
    IReadOnlyList<FightReportPartner> Partners,
    int Money,
    int Item,
    IReadOnlyDictionary<int, int>? MessageTokenCounts = null);
```

| Field | Meaning |
|-------|---------|
| `Partners` | Ordered list of partner results (at least one) |
| `Money` | Gold reward shown in the money message (always shown) |
| `Item` | Item reward ID; **0 means no item message is shown** |
| `MessageTokenCounts` | Optional map from message entry index to token count. Missing or null → every message has 1 token. The token count used is `max(1, MessageTokenCounts[entry] ?? 1)` |

### 1.3 `FightReportSlotView` — one Digimon slot in the view

```csharp
public sealed record FightReportSlotView(
    int DigimonId,
    bool Visible,
    bool ExpApplied,
    int BlinkFrame);
```

The partner view always exposes exactly **3** slot views regardless of how many Digimon the
partner has.  Slot `i` (0, 1, 2):
- `DigimonId` = `SlotDigimonIds[i]` if `i < SlotDigimonIds.Count`, else `-1`
- `Visible` = `i < SlotDigimonIds.Count`
- `ExpApplied` = always `false`
- `BlinkFrame` = always `0`

### 1.4 `FightReportPartnerView` — one partner's visible state

```csharp
public sealed record FightReportPartnerView(
    int PartyIndex,
    int PartnerId,
    int FighterId,
    int Fade,
    bool Selected,
    int CursorFrame,
    int LevelBlinkFrame,
    int ShownExp,
    int AnimationFrame,
    IReadOnlyList<FightReportSlotView> Slots);
```

| Field | Meaning |
|-------|---------|
| `Fade` | Panel opacity, `0` = invisible, `4096` = fully visible (see §4) |
| `Selected` | True while this partner is highlighted during the EXP sequence |
| `CursorFrame` | Selection cursor animation frame, 0–3 (see §6.3) |
| `LevelBlinkFrame` | Level-blink counter, 0–10 (see §6.3) |
| `ShownExp` | Animated EXP display value (see §7) |
| `AnimationFrame` | Character animation frame counter (see §6.3) |
| `Slots` | Always 3 slots (see §1.3) |

### 1.5 `FightReportMessage` — current on-screen message state

```csharp
public sealed record FightReportMessage(
    int Entry,
    IReadOnlyList<int> Substitutions,
    int TokensRevealed,
    int TokenCount,
    bool ArrowVisible,
    int ArrowFrame);
```

| Field | Meaning |
|-------|---------|
| `Entry` | Message table index (see §5.3 for the values used) |
| `Substitutions` | Integer substitution values for the message template |
| `TokensRevealed` | How many tokens of the message have been revealed so far |
| `TokenCount` | Total number of tokens in the message |
| `ArrowVisible` | True when all tokens are visible and the player has not yet pressed Cross |
| `ArrowFrame` | Arrow blink frame: `(time / 5) % 5` where `time` is the frame counter |

### 1.6 `FightReportView` — complete frame snapshot

```csharp
public sealed record FightReportView(
    int HeaderFade,
    int FooterFade,
    int BgScroll,
    IReadOnlyList<FightReportPartnerView> Partners,
    FightReportMessage? Message,
    int ExitFade,
    bool Done);
```

| Field | Meaning |
|-------|---------|
| `HeaderFade` | EXP header panel opacity, 0–4096 |
| `FooterFade` | Footer panel opacity, 0–4096 |
| `BgScroll` | Background tile scroll offset, 0–95 |
| `Partners` | One view per partner, in input order |
| `Message` | Current message, or `null` if no message is active |
| `ExitFade` | Exit black-out opacity, 0–4096 |
| `Done` | True once the exit fade-out completes; further `Step` calls are no-ops |

---

## 2. `FightReportController` — public API

```csharp
public sealed class FightReportController
{
    public FightReportController(FightReportInput input);

    public FightReportView View { get; }
    public IReadOnlyList<string> Logs { get; }

    public static int RollStep(int targetExp);
    public void Step(bool crossPressedEdge);
}
```

- **Constructor**: validates `input` and `input.Partners` are non-null (throws
  `ArgumentNullException` otherwise).  Builds the initial `View` from the default state
  (all fades at 0, all partners at `ShownExp = ExpBefore`).
- **`View`**: current frame snapshot, rebuilt at the end of each `Step` call.
- **`Logs`**: immutable-at-time-of-read log entries emitted during the simulation.  Used
  by callers to detect partially-supported features.
- **`RollStep`**: static, see §3.
- **`Step`**: advances by one PAL frame, see §5.  A no-op when `View.Done` is `true`.

---

## 3. `RollStep` — decimal-wheel EXP increment

```csharp
public static int RollStep(int targetExp)
```

Returns the per-frame increment for the animated EXP counter.

Let `n = max(0, targetExp)`.  Let `d` be the **number of decimal digits** of `n`:
- `d = 0` when `n = 0` (zero has no digits in this convention).
- Otherwise `d` equals the total count of decimal digits in `n`, including leading
  significant digits (e.g. `d(4)=1`, `d(99)=2`, `d(100)=3`, `d(250)=3`).

The return value is the **repunit** of length `max(1, d)`:

```
repunit(k) = (10^k − 1) / 9   =   1, 11, 111, 1111, …
```

Examples:

| `targetExp` | `n` | `d` | `RollStep` |
|-------------|-----|-----|------------|
| −5          | 0   | 0   | 1          |
| 0           | 0   | 0   | 1          |
| 1           | 1   | 1   | 1          |
| 4           | 4   | 1   | 1          |
| 9           | 9   | 1   | 1          |
| 10          | 10  | 2   | 11         |
| 50          | 50  | 2   | 11         |
| 99          | 99  | 2   | 11         |
| 100         | 100 | 3   | 111        |
| 250         | 250 | 3   | 111        |
| 999         | 999 | 3   | 111        |
| 1000        | 1000| 4   | 1111       |
| 999 999     | …   | 6   | 111 111    |

> **Edge case:** `targetExp ≤ 0` → always returns 1.

---

## 4. Fade mechanism

All fades use a fixed-point integer with **K = 4096 (= 0x1000)** representing "fully visible".

### 4.1 Starting a fade

`StartFade(fadeIn, duration)`:

- **Fade in** (`fadeIn = true`):
  - `Level = 0`
  - `Step = ⌊K / duration⌋`  (integer division, C# truncation towards zero)
- **Fade out** (`fadeIn = false`):
  - `Level = K`
  - `Step = −2 × ⌊K / duration⌋`

For the **default duration of 10**:
- Fade-in step: `⌊4096 / 10⌋ = 409`
- Fade-out step: `−818`

For the **exit fade duration of 30**:
- Fade-in step: `⌊4096 / 30⌋ = 136`

### 4.2 Advancing a fade

`UpdateFade()` is called once per PAL frame per active fade:

```
if not Active:
    return true          // already done

Level += Step
if Step > 0 and Level > K:
    Level = K; Active = false; return true
if Step < 0 and Level < 0:
    Level = 0; Active = false; return true
return false
```

### 4.3 Timing examples (duration = 10)

**Fade-in**: Step = 409.  After 10 `UpdateFade` calls Level = 4090, still active.  After
11 calls Level = 4499 > 4096 → clamped to 4096, done.  **Completes on the 11th call.**

**Fade-out**: Step = −818.  Starting at 4096:
- Call 1: 3278. Call 2: 2460. Call 3: 1642. Call 4: 824. Call 5: 6. Call 6: −812 → 0, done.
**Completes on the 6th call.**

**Exit fade-in** (duration = 30): Step = 136.  After 30 calls: 4080, active.  After 31
calls: 4216 > 4096 → done.  **Completes on the 31st call.**

### 4.4 Initial state

All fade objects start with `Level = 0`, `Step = 0`, `Active = false`.  An inactive fade's
`UpdateFade` returns `true` immediately (treated as already complete).

---

## 5. `Step` — frame advance

Each `Step(crossPressedEdge)` call:

1. Increment internal frame counter `_time` (starts at 0; after the first `Step`, `_time = 1`).
2. **Tick background** (§5.1).
3. **Tick message** (§5.2).
4. **Tick each partner** in input order (§6).
5. **Tick report state machine** (§5.3).
6. Rebuild `View` from all internal state.

`crossPressedEdge` must be `true` for exactly **one** frame at a button-press event
(rising edge); holding the button does not count as repeated presses.

### 5.1 Background scroll

There is a one-bit skip toggle (initially `false`).  Each `Step`:

```
if skip:
    bgScroll = (bgScroll + 1) % 96
    skip = false
else:
    skip = true
```

After **N** steps: `bgScroll = ⌊N / 2⌋ mod 96`.

### 5.2 Message tick

Called **before** partner and report ticks.  If no message is active, or the current
message is already finished, the tick does nothing.

**When the message is active and waiting for Cross (all tokens revealed):**
- If `cross`: mark finished.
- Else: do nothing.

**When the message is active and tokens are still being revealed:**
- If `cross`: set `Revealed = TokenCount`; mark waiting.  Return.
- Increment an internal reveal timer.
- If timer **exceeds 6** (i.e., becomes 7): reset timer to 0; increment `Revealed`.
- If `Revealed ≥ TokenCount`: mark waiting.

Summary: **one new token is revealed every 7 frames** unless Cross is pressed.

**Token count** for a new message with entry index `e`:
```
tokenCount = max(1, MessageTokenCounts[e] ?? 1)
```

### 5.3 Report state machine

The report progresses through the following phases in strict order.  Each phase occupies
one or more PAL frames.  The report tick is called **after** all partner ticks in each frame.

#### Phase sequence

```
Initial
  └─ (1 frame) → ShowPanels
      └─ (1 frame, sets all partners to ShowStart) → WaitPanels
          └─ (wait until all partners Idle) → FooterIn
              └─ (wait for footer fade-in to complete) → SelectPartner
                  └─ SelectNextPartner() ─┐
                                          ├─ [partner found] → HeaderIn
                                          │     └─ (wait for header fade-in) → ExpMessage
                                          │           (message entry 2, sub=[partner.ExpGained])
                                          │         └─ (wait for message done) → RaisePartner
                                          │               (partner set to SlotExp phase)
                                          │             └─ (wait for partner Idle again)
                                          │                   → HeaderOut (start header fade-out, advance to next partner)
                                          │                       └─ (wait for header fade-out) → SelectPartner ─┘ (loop)
                                          └─ [no more partners with ExpGained > 0] → Money
                                                (message entry 10, sub=[Money])
                                              └─ (wait for message done) → Item
                                                  ├─ [Item = 0] → StartExit
                                                  └─ [Item ≠ 0] → ItemMessage
                                                        (message entry 11, sub=[Item])
                                                      └─ (wait for message done) → StartExit
                                                          └─ StartExit: start exit fade-in (dur=30),
                                                                        start footer fade-out (dur=10),
                                                                        set all partners to HideStart
                                                              → ExitPanels
                                                                  └─ (wait for footer done AND all partners Hidden)
                                                                      → ExitFade
                                                                          └─ (wait for exit fade-in complete)
                                                                              → Done = true
```

#### SelectNextPartner detail

Partners are iterated by an internal index starting at 0, incremented each time a partner
is fully processed.  `SelectNextPartner` skips partners with `ExpGained = 0`.  If the
index reaches or exceeds the partner count, the report advances to the **Money** phase.

#### Message entries used

| Entry | Substitutions | Trigger |
|-------|---------------|---------|
| 2     | `[partner.ExpGained]` | EXP award for a partner |
| 7     | `[partner.PartnerId, partner.LevelAfter]` | Partner level-up (§6.2) |
| 10    | `[input.Money]` | Money reward |
| 11    | `[input.Item]` | Item reward (only when `Item ≠ 0`) |

#### Fade directions

| Fade | Event | Direction | Duration |
|------|-------|-----------|---------|
| Footer | WaitPanels complete | In | 10 |
| Header | Partner selected | In | 10 |
| Header | Partner fully processed | Out | 10 |
| Exit | StartExit | In | 30 |
| Footer | StartExit | Out | 10 |
| Partner.Fade | ShowStart | In | 10 |
| Partner.Fade | HideStart | Out | 10 |

---

## 6. Partner state machine

Each partner has its own substate that advances independently.  The partner tick runs
**before** the report tick each frame.

### 6.1 Show and hide sequence

The report sets a partner to **ShowStart** when all panels are activated.  The report sets
all partners to **HideStart** when the exit begins.

```
ShowStart → (StartFade partner.Fade in, dur=10) → ShowFade
ShowFade  → (UpdateFade each frame until done) → Idle

HideStart → (StartFade partner.Fade out, dur=10) → HideFade
HideFade  → (UpdateFade each frame until done) → Hidden
```

### 6.2 EXP and level-up sequence

The report sets a partner to **SlotExp** to begin the EXP processing phase.

**Slot loop** (3 iterations, one per frame each):
```
SlotExp  → (log "report-digimon-exp-unwired" once) → SlotLevel
SlotLevel → NextSlot
NextSlot  → slot++; if slot < 3: back to SlotExp; else: PartnerLevel
```

The log string `"report-digimon-exp-unwired"` is appended to `Logs` exactly once for the
entire controller lifetime, on the first `SlotExp` entry.

**Level check:**
```
PartnerLevel:
    if LevelAfter > LevelBefore:
        LevelBlink = true
        begin message 7 ([PartnerId, LevelAfter])
        → LevelMessage
    else:
        → NewDigimon
LevelMessage: (wait for message done, clear message) → NewDigimon
NewDigimon → WaitBlinks
WaitBlinks: if (not LevelBlink) or (LevelBlinkFrame ≥ 10):
                Selected = false → Idle
```

### 6.3 Per-frame partner animations

These run at the **start** of each partner tick (before the substate switch), every frame:

```
if partner.Selected and (_time % 5 == 0):
    CursorFrame = (CursorFrame + 1) % 4

if LevelBlink and (_time % 3 == 0):
    LevelBlinkFrame = min(10, LevelBlinkFrame + 1)

if partner.Fade.Level ≠ 0 and (_time % 13 == 0):
    AnimationFrame++
```

`_time` is the frame counter (1 after the first `Step`).

### 6.4 EXP roll (`RollExp`)

Called every frame when the partner's substate is **≥ SlotExp and not Hidden**.

Let `targetExp = min(999999, partner.ExpBefore + partner.ExpGained)`.

Each `RollExp` call:

```
rollTime++
if rollTime ≥ 10:
    shownExp = targetExp
    return

step = RollStep(targetExp)
shownExp += step
if shownExp > step × 9:
    shownExp -= (step × 9 + 1)
```

`shownExp` starts at `ExpBefore`.  After at most 10 `RollExp` calls, it equals `targetExp`.
Intermediate values cycle modulo `(step × 9 + 1)`.

> **Example** (`targetExp = 4`, step = 1): after 10 calls `shownExp` is forced to 4.
> Intermediate values cycle 1 → 2 → 3 → 4 → 5 → 6 → 7 → 8 → 9 → (10 calls reached: 4).

> **Example** (`targetExp = 250`, step = 111): intermediate cycle is 111 → 222 → 333 →
> 444 → 555 → 666 → 777 → 888 → 999 → 0 (1000 > 999 → 1000-999-1=0) → (10 calls: 250).

---

## 7. View construction

`View` is rebuilt at the end of every `Step` call:

- `HeaderFade` = header fade `Level`
- `FooterFade` = footer fade `Level`
- `BgScroll` = background scroll counter
- `Partners` = one `FightReportPartnerView` per partner (order preserved):
  - `Fade` = partner fade `Level`
  - `Selected`, `CursorFrame`, `LevelBlinkFrame`, `ShownExp`, `AnimationFrame` from partner state
  - `Slots`: 3 entries as described in §1.3
- `Message`:
  - `null` if no message is active
  - Otherwise: `Entry`, `Substitutions`, `TokensRevealed = Revealed`, `TokenCount`
  - `ArrowVisible = Waiting and not Finished`
  - `ArrowFrame = (_time / 5) % 5`
- `ExitFade` = exit fade `Level`
- `Done` = internal done flag

---

## 8. Edge cases and constraints

- **`ExpGained = 0`**: the partner is silently skipped; its EXP, level-up, and slot phases
  never run.  The partner still shows/hides with the panel.
- **`Item = 0`**: no item message is shown; `StartExit` begins immediately after the money
  message.
- **`LevelAfter = LevelBefore`**: no level-up message; the partner proceeds directly to
  `NewDigimon → WaitBlinks → Idle` after the slot loop.
- **`LevelAfter > LevelBefore`**: the level-up message (entry 7) is shown; `LevelBlink`
  becomes `true`; `LevelBlinkFrame` advances at the rate of §6.3.
- **Multiple partners**: processed one at a time in input order.  All panels show/hide
  together; the EXP sequence is per-partner.
- **All partners have `ExpGained = 0`**: `SelectNextPartner` advances directly to Money
  without any header fade or EXP message.
- **Done is permanent**: once `Done = true`, `Step` calls do nothing and `View` is
  unchanged.
- **`MessageTokenCounts` absent**: all messages use 1 token each.
- **`MessageTokenCounts` maps entry → count**: if count < 1, `max(1, count) = 1` is used.

---

## 9. Invented examples

### 9.1 BgScroll after N steps

| N steps | bgScroll |
|---------|----------|
| 0       | 0        |
| 1       | 0        |
| 2       | 1        |
| 3       | 1        |
| 4       | 2        |
| 192     | 0 (wraps)|
| 193     | 0        |
| 194     | 1        |

### 9.2 Single partner, ExpGained = 4, no level up, Money = 50, Item = 0, cross every 20 frames

Key view states (cross pressed at frames 20, 40, 60, 80, 100, 120, ...):

| Frame | Notable event |
|-------|---------------|
| 2     | Partners set ShowStart |
| 14    | Partners Idle; footer fade starts |
| 25    | Footer fully visible; SelectPartner called |
| 26    | Header fade-in starts; partner.Selected = true |
| 37    | Header fully visible; EXP message (entry 2) starts |
| 40    | Cross: message revealed and waiting |
| 60    | Cross: message finished; partner→SlotExp; RaisePartner starts |
| 70    | partner.ShownExp = 4 (TargetExp reached after 10 RollExp calls) |
| 72    | Partner Idle; header fade-out starts |
| 78    | Header fully faded; SelectPartner called |
| 79    | All partners processed; Money phase; message 10 starts |
| 87    | Money message token revealed and waiting |
| 100   | Cross: money message finished; Item = 0 → StartExit |
| 101   | Exit fade-in (dur=30) and footer fade-out start; partners→HideStart |
| 107   | Footer and partner fades fully out; ExitFade phase starts |
| 138   | ExitFade complete; Done = true |

### 9.3 Fade level values at selected frames

Footer fade-in (duration 10), called first at frame 15:

| Frame | FooterFade |
|-------|------------|
| 14    | 0          |
| 15    | 409        |
| 16    | 818        |
| 24    | 4090       |
| 25    | 4096 (clamped, done) |

---

## 10. Self-review checklist

A developer without any game knowledge should be able to reimplement `FightReportController`
from this document alone.  Verification:

- [x] All 6 public record types documented with field names, types, and semantics (§1).
- [x] `FightReportSlotView` always has 3 entries; DigimonId = −1 for missing slots (§1.3).
- [x] `FightReportMessage.ArrowFrame` formula `(_time/5)%5` specified (§1.5).
- [x] `MessageTokenCounts` default and `max(1,…)` clamp documented (§1.2, §5.2).
- [x] `FightReportController` constructor preconditions (null checks) documented (§2).
- [x] `RollStep` formula (decimal digit count → repunit) with table and edge cases (§3).
- [x] Fade fixed-point constant K=4096, StartFade formulas, UpdateFade algorithm (§4).
- [x] Fade timing: fade-in 11 calls, fade-out 6 calls, exit 31 calls (§4.3).
- [x] All initial fade states at Level=0, inactive (§4.4).
- [x] `Step` call order: time++, background, message, partners, report, view (§5).
- [x] Background scroll formula `⌊N/2⌋ mod 96` with initial=0 (§5.1).
- [x] Message reveal rate (one token per 7 frames; Cross skips) documented (§5.2).
- [x] Report phase sequence including all transitions (§5.3).
- [x] All 4 message entries with their substitution lists (§5.3).
- [x] All fade directions and durations tabulated (§5.3).
- [x] Partner show/hide phases and their fade behavior (§6.1).
- [x] Slot loop (3 iterations, one per frame) and log-once behavior (§6.2).
- [x] Level-up check (LevelAfter > LevelBefore) and LevelBlink mechanism (§6.2).
- [x] Three per-frame animation rules with exact moduli (§6.3).
- [x] `RollExp` formula: rollTime ceiling, step source, wrap-around (§6.4).
- [x] `ShownExp` starts at `ExpBefore`; `targetExp = min(999999, ExpBefore+ExpGained)` (§6.4).
- [x] Substate range for RollExp call: `≥ SlotExp` and `≠ Hidden` (§6.4).
- [x] View fields fully specified, including `ArrowVisible = Waiting and not Finished` (§7).
- [x] All edge cases: ExpGained=0, Item=0, no level-up, multi-partner, Done is permanent (§8).
- [x] Invented numeric examples provided for BgScroll, single-partner run, fade levels (§9).
