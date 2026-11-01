# Battle Enemy AI — Specification

**Scope.** The static class `BattleEnemyAi` and the types it owns: `EnemyActionKind`,
`EnemyDecision` and `EnemyAiContext`. It decides what the active enemy does on its turn,
whether it counter-attacks, and which enemy slot a switch brings in. It reads (but does not
own) `BattleFighter`, `BattleTableAction`, `FighterFlags` and `BattleRng`; the parts of those
types it uses are restated in section 1 so this document is self-sufficient.

The module is pure: no engine types, no I/O, no hidden state. All randomness comes from an
injected `BattleRng`. The only side effect is drawing values from that random source, and the
**number** of values drawn is part of the contract (callers share one random stream).

All arithmetic is on 32-bit signed integers (`int`) with two's-complement wrap-around on
overflow (no overflow check). Division `/` truncates toward zero. The remainder `%` is the
truncated remainder: its sign follows the dividend (`-5 % 128 = -5`). `&` is bitwise AND.

Differential vectors: `tests/Dw3.Game.Tests/Vectors/battle-enemy-ai.json` (592 fabricated
cases, replayed by `BattleEnemyAiVectorTests`). Each case records the inputs, the scripted
random sequence `rng`, and `expected` = result + `rngCalls` (values drawn), or `throws` = the
exception type name.

---

## 1. Types read by the module (not owned)

### 1.1 `BattleRng`

```csharp
public interface BattleRng { int Next(); }
```

Returns the next raw random value. The module never assumes a range: any `int`, including
negative values, must be handled with the operators of the preamble.

### 1.2 `BattleTableAction`

```csharp
public readonly record struct BattleTableAction(int Target, int Condition, int ConditionArg);
```

One row of an enemy's behaviour table: `Target` is a *target kind* (section 3),
`Condition` a *condition code* (section 2) and `ConditionArg` its argument. The default value
is `(0, 0, 0)`.

### 1.3 `BattleFighter` (fields used)

| Field | Type | Meaning for this module |
|---|---|---|
| `Id` | `int` | Digimon id. `0` means an empty/unused fighter. |
| `Hp`, `MaxHp` | `int` | Current and maximum hit points. |
| `Mp` | `int` | Current magic points. |
| `Charge` | `int` | Pending charge bonus; non-zero means "charged". |
| `Turns` | `int` | Number of turns the enemy has taken. |
| `Boosts` | `int[]` | Stat modifiers; indices 0, 1, 2 are read. Negative = lowered. |
| `Flags` | `int` | Status bit set (`FighterFlags`). |
| `Techs` | `int[]` | The enemy's three technique ids, indices 0, 1, 2. |
| `Actions` | `BattleTableAction[]` | The enemy's action table (normally 4 rows). |
| `Counter` | `BattleTableAction` | The enemy's counter-attack row. |

### 1.4 `FighterFlags` (constant used)

`FighterFlags.Asleep = 8` (bit 3). Other bits are irrelevant here.

---

## 2. Public API

```csharp
public enum EnemyActionKind
{
    None, Attack, Technique1, Technique2, RunAway,
    SwitchTo0, SwitchTo1, SwitchTo2, SwitchToAny,
}
```

The declaration order above is part of the API (values 0..8); `SwitchTo0 + n` names slot `n`.

```csharp
public readonly record struct EnemyDecision(EnemyActionKind Kind, int TechId, int SwitchSlot)
{
    public static EnemyDecision None { get; }   // (None, 0, -1)
    public bool IsAttack    { get; }            // Kind == Attack
    public bool IsTechnique { get; }            // Kind is Technique1 or Technique2
    public bool IsSwitch    { get; }            // Kind is SwitchTo0, SwitchTo1, SwitchTo2 or SwitchToAny
    public bool IsRunAway   { get; }            // Kind == RunAway
}
```

Value equality (record struct). `TechId` is the technique id for an attack/technique and `0`
otherwise. `SwitchSlot` is `0`, `1` or `2` for `SwitchTo0..2` and `-1` otherwise. `None` is
also returned for an empty table or an unknown target kind. (`EnemyDecision.None` may be a
static property or expression-bodied member; callers only read it.)

```csharp
public sealed class EnemyAiContext
{
    public BattleFighter Enemy = new();                       // the acting enemy
    public BattleFighter? ActivePlayer;                       // the player's active fighter, may be null
    public IReadOnlyList<BattleFighter?> Enemies = [];        // the enemy roster, null = empty slot
    public int ActiveEnemy;                                   // roster index of the acting enemy
    public int BoostElement;                                  // the battle's current element boost
    public int BattleId;                                      // id of the battle
    public int EncounterKind;                                 // kind of the encounter
    public IReadOnlyCollection<int> StarterDigimonIds = [];   // ids counting as "starter" Digimon
}
```

Public mutable fields (not properties); callers use object initialisers.

```csharp
public static class BattleEnemyAi
{
    public const int BattleTableFallback = 3;
    public static bool TestEnemyCondition(int condition, int arg, EnemyAiContext ctx, BattleRng rng);
    public static EnemyDecision GetEnemyAction(BattleFighter enemy, int kind);
    public static EnemyDecision PickAction(BattleFighter enemy, EnemyAiContext ctx, BattleRng rng);
    public static EnemyDecision PickCounter(BattleFighter enemy, EnemyAiContext ctx, BattleRng rng);
    public static int ResolveSwitch(EnemyDecision decision, IReadOnlyList<BattleFighter?> enemies,
        int activeEnemy, BattleRng rng);
}
```

`BattleTableFallback` is the index of the fallback row of an action table.

---

## 3. `GetEnemyAction(enemy, kind)` — target kind to decision

Pure, never draws a random value.

| `kind` | Result |
|---|---|
| 1 | `(Attack, enemy.Techs[0], -1)` |
| 2 | `(Technique1, enemy.Techs[1], -1)` |
| 3 | `(Technique2, enemy.Techs[2], -1)` |
| 4 | `(RunAway, 0, -1)` |
| 5 | `(SwitchTo0, 0, 0)` |
| 6 | `(SwitchTo1, 0, 1)` |
| 7 | `(SwitchTo2, 0, 2)` |
| 8 | `(SwitchToAny, 0, -1)` |
| anything else (0, negative, ≥ 9) | `EnemyDecision.None` |

Edge case: kinds 1–3 index `Techs` directly; a `Techs` array too short throws
`IndexOutOfRangeException` (e.g. `Techs = [301, 302]`, kind 3). Kinds 4–8 and unknown kinds never
touch `Techs`.

Example: `Techs = [201, 202, 203]`, kind 2 → `(Technique1, 202, -1)`; kind 9 → `(None, 0, -1)`.

---

## 4. `TestEnemyCondition(condition, arg, ctx, rng)` — condition codes

Returns whether the condition holds. Draws **exactly one** random value for condition 1 and
**none** for every other code. `E = ctx.Enemy`, `P = ctx.ActivePlayer`.

### 4.1 HP share

Conditions 2, 3, 6 and 7 compare HP with a share of maximum HP, where `arg` is a fraction in
128ths:

    share(maxHp, arg) = ⌊maxHp / 100⌋ × ⌊(arg × 100) / 128⌋

(both divisions truncate toward zero, each computed before the product; 32-bit wrap applies).
The order matters: it is **not** `maxHp × arg / 128`.

Worked values (invented):

| maxHp | arg | ⌊maxHp/100⌋ | ⌊arg·100/128⌋ | share |
|---|---|---|---|---|
| 250 | 64 | 2 | 50 | 100 |
| 99 | 64 | 0 | 50 | 0 |
| 5000 | 1 | 50 | 0 | 0 |
| 5000 | 255 | 50 | 199 | 9950 |
| 1000 | 32 | 10 | 25 | 250 |

A share can exceed `maxHp` when `arg > 128`, and is 0 when `maxHp < 100` or `arg < 2`.

### 4.2 Condition table

| Code | Holds when |
|---|---|
| 0 | always |
| 1 | `r % 128 < arg`, with `r = rng.Next()` (one draw) |
| 2 | `E.Hp < share(E.MaxHp, arg)` |
| 3 | `E.Hp ≥ share(E.MaxHp, arg)` |
| 4 | `E.Mp < arg` |
| 5 | `E.Mp ≥ arg` |
| 6 | `P ≠ null` and `P.Hp < share(P.MaxHp, arg)` |
| 7 | `P ≠ null` and `P.Hp ≥ share(P.MaxHp, arg)` |
| 8 | `P ≠ null` and `ctx.StarterDigimonIds` contains `P.Id` (`arg` ignored) |
| 9 | `P ≠ null` and `(P.Flags & 8) ≠ 0` (asleep; `arg` ignored) |
| 10 | *another enemy alive* (4.3) |
| 11 | `ctx.BoostElement = arg` |
| 12 | `ctx.BattleId = arg` |
| 13 | `ctx.EncounterKind = arg` |
| 14 | `E.Charge ≠ 0` (`arg` ignored) |
| 15 | `E.Boosts[0] < 0` |
| 16 | `E.Boosts[1] < 0` |
| 17 | `E.Boosts[2] < 0` |
| 18 | `arg ≠ 0` and `E.Turns % arg = 0` |
| any other value (negative, ≥ 19) | always |

Notes:
- Condition 1 with a negative draw: `-5 % 128 = -5`, so with `arg = 0` it holds (`-5 < 0`).
  With non-negative draws, `arg ≤ 0` never holds and `arg ≥ 128` always holds.
  Examples: `arg = 64`, draw 127 → `127 < 64` false; draw 128 → `0 < 64` true.
- Conditions 15–17 index `Boosts` directly; a too-short array throws `IndexOutOfRangeException`.
- Condition 18: `arg = 0` is false (no division). Negative values use the truncated remainder:
  `Turns = -4, arg = -2` → `-4 % -2 = 0` → true; `Turns = 0`, any `arg ≠ 0` → true.

### 4.3 Condition 10 — another enemy alive

Scan roster indices `j = 0, 1, 2`, stopping early at `ctx.Enemies.Count` (indices ≥ 3 are never
read). Skip `j = ctx.ActiveEnemy`. An entry qualifies when it is non-null, its `Id ≠ 0` and its
`Hp ≠ 0` (**not** `Hp > 0`: a negative HP counts as alive here). With `arg = 0` the condition
holds if any entry qualifies; otherwise it holds if a qualifying entry has `Id = arg`.

`ActiveEnemy` outside 0..2 skips nothing. Examples: roster `[(10,5), (11,-3)]`, active 0,
arg 0 → true. Roster of four with only the fourth alive → false.

### 4.4 Example

`E = { Hp: 99, MaxHp: 250 }`, condition 2, arg 64: share = 100, `99 < 100` → true, zero draws.
Same with `Hp = 100` → false.

---

## 5. `PickAction(enemy, ctx, rng)` — the turn decision

`enemy` supplies the table (`enemy.Actions`) and the techniques; `ctx` supplies everything the
conditions read (callers pass `ctx.Enemy == enemy`; the conditions read `ctx.Enemy`, not the
`enemy` argument).

Let `n = enemy.Actions.Length`.

1. If `n = 0`: return `EnemyDecision.None` (no draws).
2. Let `last = min(n − 1, 3)`. Rows with index `> 3` are ignored.
3. For `i = 0, 1, …, last − 1` in increasing order: test row `i`'s condition with its argument
   (section 4). At the first row that holds, return `GetEnemyAction(enemy, row.Target)`.
   Evaluation stops there: later rows are not tested and draw nothing.
4. Otherwise return `GetEnemyAction(enemy, Actions[last].Target)`. The condition of row
   `last` (the fallback) is **never** tested and never draws.

So a one-row table is an unconditional action, and with the usual four rows, rows 0–2 are
conditional and row 3 is the fallback.

A holding row whose target kind is unknown returns `None`; the scan does not continue.

Examples (`Techs = [101, 102, 103]`, `rng` scripted):
- Rows `[(4, 1, 0), (3, 0, 0)]`, draws `[10]`: row 0 `10 % 128 < 0` false → fallback kind 3 →
  `(Technique2, 103, -1)`, 1 draw.
- Rows `[(2,1,10), (3,1,10), (4,1,10), (1,0,0)]`, draws `[50, 5, 99]`: row 0 `50 < 10` false,
  row 1 `5 < 10` true → `(Technique2, 103, -1)`, 2 draws (the third value is not drawn).
- Rows `[(2,1,0), (3,1,0), (4,1,0), (1,1,0)]`, draws `[0,0,0]`: three failures, fallback kind 1
  without testing its condition → `(Attack, 101, -1)`, 3 draws.

---

## 6. `PickCounter(enemy, ctx, rng)` — counter-attack

Let `c = enemy.Counter`.

1. If `c.Condition = 0`: return `None` (no draws). Code 0 means "no counter" here, even though
   condition 0 is "always" elsewhere.
2. If `TestEnemyCondition(c.Condition, c.ConditionArg, ctx, rng)` is false: return `None`.
3. `d = GetEnemyAction(enemy, c.Target)`. Return `d` if it is an attack or a technique
   (target kinds 1, 2, 3); otherwise return `None` (run-away, switches and unknown kinds never
   counter).

Draws: one value only when `c.Condition = 1`. Examples: counter `(1, 1, 128)`, draw 3 →
`(Attack, 101, -1)`; counter `(4, 14, 0)` with `Charge = 1` → `None`; counter `(2, 25, 0)` →
unknown code holds → `(Technique1, 102, -1)`.

---

## 7. `ResolveSwitch(decision, enemies, activeEnemy, rng)` — switch target

Returns the roster index to bring in, or `-1` when there is no valid target. A slot is
*alive* when the entry is non-null, `Id ≠ 0` and `Hp > 0` (strictly positive, unlike 4.3).

**Fixed slot** (`s = decision.SwitchSlot ≥ 0`), no draws:
1. If `s = activeEnemy` → `-1` (checked first, the roster is not read).
2. Read `enemies[s]` (throws `ArgumentOutOfRangeException` when `s ≥ enemies.Count` and the
   list is an array seen through `IReadOnlyList`; the exception from the list indexer
   propagates unchanged).
3. Return `s` if alive, else `-1`.

**Any slot** (`s < 0`, i.e. `SwitchToAny`, but also `None`, attacks or any decision with a
negative slot; `Kind` is not inspected):
1. Collect candidates: indices `i = 0, 1, 2` with `i < enemies.Count`, `i ≠ activeEnemy`, alive,
   in increasing order.
2. 0 candidates → `-1`, no draw. 1 candidate → it, no draw.
3. 2 or more → draw `r = rng.Next()` once and return `candidates[r & 1]`. With an
   `activeEnemy` outside 0..2 there can be three candidates; only the first two can be picked.

Examples (roster of three alive):
- `SwitchTo1`, active 1 → `-1`; active 0 → `1`.
- Any, active 1, draw `-1` → candidates `[0, 2]`, `-1 & 1 = 1` → `2`.
- Any, active `-1`, draw 2 → candidates `[0, 1, 2]`, `2 & 1 = 0` → `0`.
- `SwitchTo1` with `enemies[1] = (11, -2)` → `-1`.

---

## 8. Error behaviour summary

| Situation | Behaviour |
|---|---|
| `Techs` shorter than needed (kinds 1–3) | `IndexOutOfRangeException` |
| `Boosts` shorter than needed (codes 15–17) | `IndexOutOfRangeException` |
| Fixed switch slot ≥ roster size | list indexer's exception (`ArgumentOutOfRangeException` for arrays) |
| Unknown condition code | holds |
| Unknown target kind | `None` |
| Empty action table | `None` |
| Null active player (codes 6–9) | condition false |

No other input throws: null roster entries, short rosters and out-of-range `ActiveEnemy` are
handled as described above.

---

## 9. Self-review checklist (performed for this document)

| Check | Status |
|---|---|
| Every public type, member and constant listed with signature (2) | yes |
| Every field read from external types restated (1) | yes |
| Every condition code 0–18 and the default case defined (4.2) | yes |
| HP share formula with rounding order and worked numbers (4.1) | yes |
| Random draw count for every entry point (4, 5, 6, 7) | yes |
| Short-circuit order of action rows and untested fallback (5) | yes |
| Table shorter/longer than four rows (5) | yes |
| Counter "condition 0 = none" exception to the table (6) | yes |
| Alive rule difference `Hp ≠ 0` (4.3) vs `Hp > 0` (7) | yes |
| Negative random values and negative operands (preamble, 4.2, 7) | yes |
| All exceptions and null handling (8) | yes |
| Examples checked against the vectors file | yes |
