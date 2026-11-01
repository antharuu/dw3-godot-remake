# Battle Growth — Specification

**Scope.** The `GrowthCalculator` static class and its supporting types
`BattleReward`, `GrowthTables`, `PartnerGrowthState`, and the `BattleRng`
interface.  A clean-room developer can reimplement this module from this
document without consulting any other source file.

---

## 1. Supporting types

### 1.1 `BattleRng` — injected random source

```csharp
public interface BattleRng
{
    /// <summary>Returns the next raw random value for a battle decision.</summary>
    int Next();
}
```

All randomness in the growth calculations is sourced through this interface so
the module is fully deterministic and unit-testable.  The caller supplies a
concrete implementation; tests typically supply a fixed-sequence stub.

### 1.2 `BattleReward` — battle reward record

```csharp
public readonly record struct BattleReward(int DigimonExp, int Exp, int Money);
```

A battle's outcome awards three integer quantities:

| Field         | Meaning                                                           |
|---------------|-------------------------------------------------------------------|
| `DigimonExp`  | Shared digimon experience (split among all Digimon used)          |
| `Exp`         | Partner experience (awarded to partners that participated)        |
| `Money`       | Gold reward (awarded in full to all partners)                     |

### 1.3 `GrowthTables` — growth lookup arrays

```csharp
public sealed class GrowthTables
{
    public int[] LevelExp    = new int[4];      // exp base per tier
    public int[] VitalCuts   = new int[4];      // HP/MP growth cut per tier
    public int[] VitalRandom = new int[9];      // HP/MP random swing table
    public int[][] StatGains = new int[6][];    // [tier][column] stat-gain table
    public int[] ResistGains = new int[8];      // resistance-gain table
}
```

Dimensions:

| Field         | Length          | Indexing                                              |
|---------------|-----------------|-------------------------------------------------------|
| `LevelExp`    | 4               | indexed by level tier (see §2.1 tier rules)           |
| `VitalCuts`   | 4               | indexed by level tier (see §2.1 tier rules)           |
| `VitalRandom` | 9               | `rng.Next() % 9` picks the index                     |
| `StatGains`   | 6 rows × 9 cols | row = stat tier; col = `growthParam + rng.Next() % 5` |
| `ResistGains` | 8               | `growthParam + rng.Next() % 4` picks the index       |

Each `StatGains[i]` row must be allocated as `new int[9]` before use.

### 1.4 `PartnerGrowthState` — partner's persistent growth record

```csharp
public sealed class PartnerGrowthState
{
    public int Level = 1;
    public int Exp;
    public int Tp;
    public int MaxHp;
    public int MaxMp;
    public int[] BattleStats  = new int[6];    // strength, defense, spirit, wisdom, speed, charisma
    public int[] Resistances  = new int[7];    // seven elemental resistances
}
```

| Field         | Semantics                                                        |
|---------------|------------------------------------------------------------------|
| `Level`       | Current partner level (1–99)                                     |
| `Exp`         | Cumulative experience points (0–999 999)                         |
| `Tp`          | Technique points (0–99)                                          |
| `MaxHp`       | Maximum HP (0–9 999)                                             |
| `MaxMp`       | Maximum MP (0–9 999)                                             |
| `BattleStats` | Six stats in fixed order: strength[0], defense[1], spirit[2], wisdom[3], speed[4], charisma[5]. Each 0–999. |
| `Resistances` | Seven elemental resistance values, each 0–999                    |

---

## 2. `GrowthCalculator` — public API

All arithmetic is **integer with C# truncation-towards-zero**.

### 2.1 `RaiseStats` — apply one level of stat growth

```csharp
public static void RaiseStats(
    GrowthTables tables,
    PartnerGrowthState state,
    int hpGrowth,
    int mpGrowth,
    IReadOnlyList<int> statGrowth,     // length 6
    IReadOnlyList<int> resistGrowth,   // length 7
    int level,
    BattleRng rng)
```

Applies one level's worth of stat growth to `state` in place.  The caller
passes the growth parameters for the partner's species; `tables` holds the
global lookup arrays.  The method does **not** modify `state.Level`, `state.Exp`,
or `state.Tp`.

#### 2.1.1 Level tiers

Two independent tier mappings are used:

**Vital tier** (used for HP and MP):

| Condition        | Tier |
|------------------|------|
| `level < 5`      | 0    |
| `5 ≤ level < 20` | 1    |
| `20 ≤ level < 40`| 2    |
| `level ≥ 40`     | 3    |

**Stat tier** (used for the six battle stats):

| Condition        | Tier |
|------------------|------|
| `level < 5`      | 0    |
| `5 ≤ level < 20` | 1    |
| `20 ≤ level < 40`| 2    |
| `40 ≤ level < 60`| 3    |
| `60 ≤ level < 80`| 4    |
| `level ≥ 80`     | 5    |

#### 2.1.2 HP growth formula

```
vitalTier = vital_tier(level)            // see §2.1.1
rng0      = rng.Next() % 9              // first RNG call
gain      = hpGrowth - tables.VitalCuts[vitalTier] + tables.VitalRandom[rng0]
state.MaxHp += gain
if state.MaxHp >= 10000: state.MaxHp = 9999
```

#### 2.1.3 MP growth formula

```
rng1      = rng.Next() % 9              // second RNG call
gain      = mpGrowth - tables.VitalCuts[vitalTier] + tables.VitalRandom[rng1]
state.MaxMp += gain
if state.MaxMp >= 10000: state.MaxMp = 9999
```

#### 2.1.4 Battle-stat growth formula (repeated 6 times, i = 0..5)

```
statTier  = stat_tier(level)            // see §2.1.1
growth    = statGrowth[i]
rng_i     = rng.Next() % 5             // one RNG call per stat
statGain  = tables.StatGains[statTier][growth + rng_i]
state.BattleStats[i] += statGain
if state.BattleStats[i] >= 1000: state.BattleStats[i] = 999
```

The RNG calls are issued in order i = 0, 1, 2, 3, 4, 5.

#### 2.1.5 Resistance growth formula (conditional on level)

**Only applied when `level < 41`**, repeated 7 times (i = 0..6):

```
growth      = resistGrowth[i]
rng_r       = rng.Next() % 4           // one RNG call per resistance
resistGain  = tables.ResistGains[growth + rng_r]
state.Resistances[i] += resistGain
if state.Resistances[i] >= 1000: state.Resistances[i] = 999
```

The RNG calls are issued in order i = 0, 1, 2, 3, 4, 5, 6.

**When `level ≥ 41`**, `state.Resistances` is not modified and no RNG calls are
made for resistances.

#### 2.1.6 RNG call order summary

| Call # | Purpose                    | Modulus |
|--------|----------------------------|---------|
| 1      | HP random swing             | 9       |
| 2      | MP random swing             | 9       |
| 3      | BattleStats[0] offset       | 5       |
| 4      | BattleStats[1] offset       | 5       |
| 5      | BattleStats[2] offset       | 5       |
| 6      | BattleStats[3] offset       | 5       |
| 7      | BattleStats[4] offset       | 5       |
| 8      | BattleStats[5] offset       | 5       |
| 9–15   | Resistances[0..6] offset (only if level < 41) | 4 each |

Total: 8 calls when `level ≥ 41`, 15 calls when `level < 41`.

#### 2.1.7 Edge cases and saturation rules

- Gains may be negative (if `hpGrowth - tables.VitalCuts[tier]` is negative and
  the random swing does not compensate).  Negative additions to `MaxHp` / `MaxMp`
  are applied without a lower bound in this method.
- `MaxHp` and `MaxMp` are capped at 9 999 (condition `>= 10000`).
- `BattleStats[i]` is capped at 999 (condition `>= 1000`).
- `Resistances[i]` is capped at 999 (condition `>= 1000`).
- The stat gain index `growth + rng.Next() % 5` must stay within bounds; callers
  are responsible for valid `statGrowth[i]` values (0–4 keeps the index in 0–8).
- The resist gain index `growth + rng.Next() % 4` must stay within bounds; callers
  are responsible for valid `resistGrowth[i]` values (0–4 keeps the index in 0–7).

#### 2.1.8 Invented examples

Assume:
- `tables.VitalCuts  = [10, 15, 20, 25]`
- `tables.VitalRandom = [0, 2, 4, 6, 8, 10, 12, 14, 16]`
- `tables.StatGains[0] = [1, 2, 3, 4, 5, 6, 7, 8, 9]`
- `tables.ResistGains  = [0, 1, 2, 3, 4, 5, 6, 7]`
- Initial state: `MaxHp=100, MaxMp=50, BattleStats=[10,10,10,10,10,10], Resistances=[100,100,100,100,100,100,100]`
- `hpGrowth=30, mpGrowth=20, statGrowth=[2,2,2,2,2,2], resistGrowth=[2,2,2,2,2,2,2]`
- Level=1 → vitalTier=0, statTier=0

If the RNG returns `[3,5, 0,1,2,3,4,0, 0,1,2,0,1,2,0]` (indices before modulus):

- HP: gain = 30 − 10 + VitalRandom[3%9] = 30 − 10 + 6 = 26 → MaxHp=126
- MP: gain = 20 − 10 + VitalRandom[5%9] = 20 − 10 + 10 = 20 → MaxMp=70
- BattleStats[0]: StatGains[0][2 + 0%5] = StatGains[0][2] = 3 → 13
- BattleStats[1]: StatGains[0][2 + 1%5] = StatGains[0][3] = 4 → 14
- ...and so on for each stat.
- Resistances[0]: ResistGains[2 + 0%4] = ResistGains[2] = 2 → 102

Saturation example: if MaxHp=9990 and gain=15 → 9990+15=10005 ≥ 10000 → MaxHp=9999.

---

### 2.2 `AddExp` — add experience and level up

```csharp
public static bool AddExp(
    GrowthTables tables,
    PartnerGrowthState state,
    int expRate,
    int hpGrowth,
    int mpGrowth,
    IReadOnlyList<int> statGrowth,
    IReadOnlyList<int> resistGrowth,
    int exp,
    BattleRng rng)
```

Returns `true` if the partner gained at least one level.

#### 2.2.1 Exp accumulation

```
state.Exp += exp
if state.Exp > 999999: state.Exp = 999999
```

#### 2.2.2 Level-up loop

The loop starts from `level = state.Level + 1` and continues while the exp
threshold for `level` is below `state.Exp`.

**Exp threshold formula** for a given `level` (the amount of cumulative exp that
triggers the level-up to `level`):

```
tier = level < 5  ? 0
     : level < 20 ? 1
     : level < 40 ? 2
     : level < 100? 3
     : (exit loop)

threshold = (level³ + level×5 − 6) × expRate / 10 + tables.LevelExp[tier]
```

All arithmetic is integer with C# truncation-towards-zero.

If `threshold < state.Exp`, the partner levels up to `level`:

```
state.Level++
if state.Level < 100:
    RaiseStats(tables, state, hpGrowth, mpGrowth, statGrowth, resistGrowth,
               state.Level, rng)
else:
    state.Level = 99
state.Tp += 5
if state.Tp >= 100: state.Tp = 99
leveled = true
```

The variable `level` is then incremented by 1 and the loop repeats.

**Loop termination:** the loop terminates when `threshold ≥ state.Exp`, or when
`level ≥ 100` (the tier lookup yields no tier and the loop exits).

**Note:** `RaiseStats` is called with the **new** `state.Level` (after the
increment), not with `level`.

**Note on level cap:** `state.Level` is capped at 99.  When the level reaches
100, it is immediately set to 99 and `RaiseStats` is **not** called for that
final tick (the `if state.Level < 100` guard prevents it).  In practice, since
`level ≥ 100` terminates the loop, `state.Level` cannot exceed 99.

**Note on TP cap:** `state.Tp` is capped at 99 (condition `>= 100`).

#### 2.2.3 Edge cases

- `exp = 0`: `state.Exp` unchanged; no level-up if already below the next
  threshold; returns `false`.
- Multiple level-ups in one call: the loop repeats until the threshold is not
  crossed.
- `state.Level = 99` on entry: `level = 100`, the loop exits immediately
  (level tier not found); returns `false`.
- `expRate = 0`: threshold = `tables.LevelExp[tier]`; only the base-table offset
  matters.

#### 2.2.4 Invented examples

Assume `tables.LevelExp = [0, 0, 0, 0]`, `expRate = 10`:

Level-up threshold from level 1 to level 2 (`level = 2`, tier = 0):
```
threshold = (8 + 10 − 6) × 10 / 10 + 0 = 12
```
Adding 12 exp when `state.Exp = 0` → `state.Exp = 12`, which is NOT `< 12`,
so no level-up.
Adding 13 exp when `state.Exp = 0` → `state.Exp = 13 > 12` → level up.

Level-up threshold from level 1 to level 3 (`level = 3`, tier = 0):
```
threshold = (27 + 15 − 6) × 10 / 10 + 0 = 36
```
If after the level-2 tick `state.Exp = 40`: `40 > 36` → another level-up.

---

### 2.3 `GetDigimonExp` — compute one Digimon's share of battle exp

```csharp
public static int GetDigimonExp(
    int level,
    int digimonExpLevel,
    int entryLevel,
    int exp,
    int used)
```

Returns the number of exp points one Digimon receives.

**Inputs:**

| Parameter        | Meaning                                              |
|------------------|------------------------------------------------------|
| `level`          | Partner's current level                              |
| `digimonExpLevel`| The Digimon's "exp level" threshold                  |
| `entryLevel`     | The Digimon's current entry level                    |
| `exp`            | Raw digimonExp from the battle reward                |
| `used`           | Number of Digimon used in the battle (0 or more)     |

#### 2.3.1 Base amount formula

```
tenfold = exp × 10

n = level < 51 ? tenfold / level          (truncated)
               : exp / 5                  (truncated)
```

#### 2.3.2 Share formula

```
share = used == 0 or used == 1 ? n
      : used == 2              ? n × 6 / 10     (truncated)
      :                          n / used        (truncated)
```

#### 2.3.3 Clamping

```
if share <= 0:
    share = 1
else if entryLevel < digimonExpLevel:
    if share > 10: share = 10
else:
    if share > 50: share = 50
```

The lower bound of 1 is applied first.  Once `share ≥ 1`, the upper cap
is conditional on whether `entryLevel < digimonExpLevel`.

#### 2.3.4 Edge cases

- `used = 0`: treated the same as `used = 1` (full share).
- `level = 0` (or any value < 1): `n = tenfold / 0` → division-by-zero; callers
  must supply `level ≥ 1`.
- `level = 50`: uses the `< 51` branch (full tenfold / level path).
- `level = 51`: uses the `exp / 5` path.
- `exp = 0`: `tenfold = 0`, `n = 0`, `share = 0 ≤ 0` → clamped to 1.
- `used ≥ 4`: integer share = `n / used` (truncated), then clamped to [1, cap].

#### 2.3.5 Invented examples

| level | digimonExpLevel | entryLevel | exp | used | result |
|-------|-----------------|------------|-----|------|--------|
| 10    | 5               | 3          | 10  | 1    | 10 (n=100/10=10; entryLevel<expLevel → cap 10) |
| 10    | 5               | 5          | 10  | 1    | 10 (n=10; entryLevel≥expLevel → cap 50; 10≤50) |
| 10    | 5               | 3          | 50  | 1    | 10 (n=500/10=50 > 10 → capped to 10) |
| 10    | 5               | 5          | 500 | 1    | 50 (n=5000/10=500 > 50 → capped to 50) |
| 60    | 5               | 5          | 30  | 1    | 6 (n=30/5=6; ≤50) |
| 10    | 5               | 5          | 0   | 1    | 1 (n=0; clamped to 1) |
| 10    | 5               | 5          | 20  | 2    | 12 (n=20; share=20×6/10=12; ≤50) |
| 10    | 5               | 5          | 20  | 4    | 5 (n=20; share=20/4=5; ≤50) |

---

### 2.4 `AddDigimonExp` — accumulate Digimon experience and level up

```csharp
public static bool AddDigimonExp(
    int expLevel,
    ref int entryLevel,
    ref int entryExp,
    int exp)
```

`entryLevel` and `entryExp` are passed by reference and mutated in place.
Returns `true` if `entryLevel` increased by at least 1.

#### 2.4.1 Exp accumulation

```
entryExp += exp
if entryExp > 9999999: entryExp = 9999999
```

#### 2.4.2 Early exit

```
if entryLevel >= 99: return false
```

This check is performed **after** the exp accumulation.

#### 2.4.3 Level-up loop

The loop iterates while a level-up condition is met:

**Cumulative exp threshold formula** for the current `entryLevel`:

```
need = expLevel >= entryLevel + 1
       ? entryLevel × 10
       : (expLevel − 1) × 10 + (entryLevel − (expLevel − 1)) × 50
```

Simplified:
- If `entryLevel < expLevel` (i.e., `expLevel > entryLevel`):
  `need = entryLevel × 10`
- Else:
  `need = (expLevel − 1) × 10 + (entryLevel − expLevel + 1) × 50`

Each level below `expLevel` costs **10** cumulative exp to reach; each level at
or above `expLevel` costs an additional **50** cumulative exp per level.

**Level-up condition:**

```
if entryExp >= need:
    leveled = true
    entryLevel++
    continue loop if entryLevel < 99
else:
    exit loop
```

The loop exits when `entryExp < need` or `entryLevel = 99`.

#### 2.4.4 Edge cases

- `entryLevel = 0`, `entryExp ≥ 0`: `need = 0 × 10 = 0`; the condition
  `entryExp ≥ 0` is always true for non-negative exp, so level 0 always levels
  up to at least 1 after any call that adds a non-negative amount (and even with
  `exp = 0` since `entryExp ≥ 0`).
- `entryLevel = 99` on entry (before exp accumulation): function still adds exp
  to `entryExp`, but returns `false` immediately.
- `exp = 0`: exp accumulation adds 0; level-up only occurs if `entryExp` already
  crossed a threshold from a prior call.
- Multiple level-ups: the loop continues as long as the threshold for the new
  level is also crossed.

#### 2.4.5 Cumulative threshold table (example with `expLevel = 5`)

| entryLevel | Condition             | need  |
|------------|-----------------------|-------|
| 0          | 5 ≥ 1 (< expLevel)   | 0     |
| 1          | 5 ≥ 2 (< expLevel)   | 10    |
| 2          | 5 ≥ 3 (< expLevel)   | 20    |
| 3          | 5 ≥ 4 (< expLevel)   | 30    |
| 4          | 5 ≥ 5 (< expLevel)   | 40    |
| 5          | 5 < 6 (≥ expLevel)   | 4×10 + 1×50 = 90  |
| 6          | 5 < 7 (≥ expLevel)   | 4×10 + 2×50 = 140 |
| 7          | 5 < 8 (≥ expLevel)   | 4×10 + 3×50 = 190 |

#### 2.4.6 Invented examples

| expLevel | entryLevel (in) | entryExp (in) | exp | entryLevel (out) | entryExp (out) | returns |
|----------|-----------------|---------------|-----|------------------|----------------|---------|
| 5        | 1               | 0             | 10  | 2                | 10             | true    |
| 5        | 1               | 0             | 9   | 1                | 9              | false   |
| 5        | 4               | 38            | 2   | 5                | 40             | true    |
| 5        | 5               | 88            | 2   | 6                | 90             | true    |
| 5        | 99              | 0             | 50  | 99               | 50             | false   |
| 5        | 1               | 0             | 200 | multiple levels  | 200            | true    |

---

### 2.5 `ExpShare` — split experience among fighting partners

```csharp
public static int ExpShare(int rewardExp, int fought)
```

Returns the exp amount one partner receives, based on how many partners fought.

**Formula:**

```
fought == 2 ? rewardExp × 6 / 10   (truncated)
fought == 3 ? rewardExp / 3        (truncated)
_           ? rewardExp             (fought = 1 or any other value)
```

**Edge cases:**

- `fought = 0` or `fought ≥ 4`: returns `rewardExp` unchanged (the "else" branch).
- `fought = 2`: result is truncated towards zero (e.g. `10 × 6 / 10 = 6`; `11 × 6 / 10 = 6`).
- `fought = 3`: result is truncated (e.g. `10 / 3 = 3`).

**Invented examples:**

| rewardExp | fought | result |
|-----------|--------|--------|
| 100       | 1      | 100    |
| 100       | 2      | 60     |
| 100       | 3      | 33     |
| 11        | 2      | 6      |
| 11        | 3      | 3      |
| 50        | 4      | 50     |
| 0         | 1      | 0      |

---

## 3. Self-review checklist

A developer without any knowledge of the game should be able to reimplement
`GrowthCalculator` and its supporting types from this document.  Verification:

- [x] All public type signatures documented with field names, types, and semantics (§1).
- [x] `BattleRng` interface documented with its single method (§1.1).
- [x] `GrowthTables` array dimensions and indexing semantics documented (§1.3).
- [x] `PartnerGrowthState` field ranges and semantics documented (§1.4).
- [x] All public method signatures with parameter types and names (§2.1–§2.5).
- [x] Arithmetic semantics (truncation-towards-zero) stated once and unambiguously (§2).
- [x] `RaiseStats` vital-tier and stat-tier mappings both documented (§2.1.1).
- [x] `RaiseStats` exact RNG call order and moduli documented (§2.1.6).
- [x] `RaiseStats` resistance skip condition (`level ≥ 41`) explicit (§2.1.5).
- [x] `RaiseStats` saturation bounds documented for MaxHp, MaxMp, BattleStats, Resistances (§2.1.7).
- [x] `AddExp` uses `level = state.Level + 1` as loop variable (§2.2.2).
- [x] `AddExp` calls `RaiseStats` with the **post-increment** `state.Level` (§2.2.2 note).
- [x] `AddExp` level-100 special case (set to 99, no RaiseStats) documented (§2.2.2 note).
- [x] `AddExp` threshold formula `(L³ + L×5 − 6) × expRate / 10 + LevelExp[tier]` exact (§2.2.2).
- [x] `AddExp` tier boundaries for the threshold (100 terminates the loop) documented (§2.2.2).
- [x] `AddExp` Tp += 5 with cap 99 documented (§2.2.2).
- [x] `GetDigimonExp` both branches for `level < 51` and `≥ 51` documented (§2.3.1).
- [x] `GetDigimonExp` share formula for used ∈ {0,1}, {2}, {≥3} documented (§2.3.2).
- [x] `GetDigimonExp` lower-bound and upper-bound clamping logic documented (§2.3.3).
- [x] `AddDigimonExp` cumulative threshold model (not marginal per-level cost) documented (§2.4.3).
- [x] `AddDigimonExp` early-exit for `entryLevel ≥ 99` is **after** exp accumulation (§2.4.2).
- [x] `AddDigimonExp` loop-exit when entryLevel = 99 documented (§2.4.3).
- [x] `AddDigimonExp` level-0 always-levels-up edge case documented (§2.4.4).
- [x] `ExpShare` all three branches with exact formulas and truncation documented (§2.5).
- [x] Invented examples provided for every method with concrete computed values (§2.1.8, §2.2.4, §2.3.5, §2.4.6, §2.5).
