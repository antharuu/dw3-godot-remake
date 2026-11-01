# Damage Calculator — Specification

**Scope.** The `DamageCalculator` static class and the supporting types
`BattleStats`, `TechData`, `BattleFighter`, `BattleConst`, `BattleStat`,
`FighterFlags`, and `Resist`.  A clean-room developer can reimplement this
module from this document without consulting any other source file.

---

## 1. Supporting types

### 1.1 `BattleStat` — stat indices

Integer constants used to index `BattleStats.Stats[5]`.

| Constant       | Value | Meaning                                   |
|----------------|-------|-------------------------------------------|
| `Attack`       | 0     | Physical damage                           |
| `Defense`      | 1     | Physical damage reduction                 |
| `Spirit`       | 2     | Magic damage and magic damage reduction   |
| `Wisdom`       | 3     | Magic accuracy, status infliction, heals  |
| `Speed`        | 4     | Physical accuracy, evasion, run-away math |

### 1.2 `BattleConst` — sentinel thresholds

| Constant            | Value | Meaning                                                      |
|---------------------|-------|--------------------------------------------------------------|
| `ElementFirst`      | 2     | Minimum valid element index (elements 0 and 1 are "none")    |
| `ElementCount`      | 7     | Number of elemental resist slots                             |
| `FamilyFirst`       | 2     | Minimum valid family index (0 and 1 are "none")              |
| `TechPhysical`      | 2     | `TechData.Icon` value for a physical technique               |
| `TechMagic`         | 3     | `TechData.Icon` value for a magic technique                  |
| `TechEffectFirst`   | 2     | Minimum valid effect index (0 and 1 are "none")              |
| `TechEffectCritical`| 11    | Effect index for the guaranteed-critical effect               |
| `BlockRunAway`      | 11    | Index in `BattleSetup.Blocks` that disables player run-away  |

### 1.3 `Resist` — resist-array indices

`BattleStats.Resist[12]`: indices 0–6 are elemental, 7–11 are status-immunity.

| Constant    | Index | Status            |
|-------------|-------|-------------------|
| `Poison`    | 7     | Poisoned           |
| `Paralysis` | 8     | Paralyzed          |
| `Confusion` | 9     | Confused           |
| `Sleep`     | 10    | Asleep             |
| `KnockOut`  | 11    | Knockout immunity  |

### 1.4 `FighterFlags` — status bitmask

Bit-flags stored in `BattleFighter.Flags`.

| Constant      | Value  |
|---------------|--------|
| `Poisoned`    | 0x01   |
| `Paralyzed`   | 0x02   |
| `Confused`    | 0x04   |
| `Asleep`      | 0x08   |
| `NoSwitch`    | 0x10   |
| `NoDigivolve` | 0x20   |

### 1.5 `BattleStats` — per-fighter resolved stats

```csharp
public sealed class BattleStats
{
    public int Level;
    public int[] Stats = new int[5];       // indexed by BattleStat.*
    public int[] Resist = new int[12];     // indexed by Resist.*
    public int Flags;
    public int Family;
    public int DamageBonus;    // in 64ths, added to each hit
    public int DamageCut;      // subtracted from each hit before the cap
    public int[] WeaponFamilies = new int[3];
    public int Element;        // accessory element (0 = none)
    public int ElementPower;
    public int TripleHit;      // non-zero → triple-hit mode active
    public int CriticalBonus;
    public int Counter;
    public int Accuracy;
    public int Evasion;
    public int RunAwayBonus;
    public int RunAwayGuard;   // non-zero → enemy run-away base chance halved
    public int StealBonus;
}
```

### 1.6 `TechData` — technique descriptor

```csharp
public sealed class TechData
{
    public int Mp;
    public int Power;
    public int Icon;           // BattleConst.TechPhysical or TechMagic
    public int Kind;           // 3 = heals the target
    public int Accuracy;
    public int Element;        // < BattleConst.ElementFirst → "no element"
    public int ElementPower;
    public int Family;         // < BattleConst.FamilyFirst  → "no family"
    public int Effect;         // < BattleConst.TechEffectFirst → "no effect"
    public int EffectChance;
    public int EffectPower;
    public int ScriptStage;
    public int ScriptEffect;
    public int ScriptSound;
    public int Script;         // 5 and 12 have special routing in the engine
    public int HitCount;
}
```

### 1.7 `BattleFighter` — per-fighter runtime state

```csharp
public sealed class BattleFighter
{
    public int  Id;
    public int  PrevId;
    public int  Turns;
    public int  MaxHp;
    public int  Hp;
    public int  MaxMp;
    public int  Mp;
    public int  Charge;
    public int[] Boosts = new int[4];
    public int  Item;
    public bool Temporary;
    public bool Special;    // grants ×2 critical-bonus when true
    public int  Flags;      // FighterFlags bitmask
    public int  Paralysis;  // counter used to scale critical chance
    public int  Sleep;      // counter used to scale critical chance
    public int  Confusion;  // counter used to scale critical chance
    public BattleStats Stats;
    public int  Strength;
    public int[]         Techs   = new int[3];
    public BattleTableAction[] Actions = [];
    public BattleTableAction   Counter;
}
```

---

## 2. `DamageCalculator` — public API

All arithmetic is **integer with C# truncation-towards-zero** (not floor division).
Specifically:

- `a / b` when both have the same sign: `floor(|a| / |b|)`.
- `a / b` when signs differ: `-(|a| / |b|)` (C# truncates towards zero).
- In practice every intermediate in this class is non-negative, so truncation
  equals floor division.

### 2.1 `OpposedElements` array

```csharp
public static readonly int[] OpposedElements = { 0, 0, 4, 2, 5, 3, 7, 8, 6 };
```

Maps element index → the element that is its direct opponent.
Indices 0 and 1 map to 0 (no opponent).  Valid battle elements are 2–8.

### 2.2 `GetElementBoost(value, element, boostElement, boostAmount) → int`

Returns the damage bonus (or penalty) from the current battle's element boost.

**Inputs**

| Parameter     | Type  | Description                                                |
|---------------|-------|------------------------------------------------------------|
| `value`       | `int` | Current damage before the boost                           |
| `element`     | `int` | The technique's element index                             |
| `boostElement`| `int` | The battle's active boost element (0 = none)              |
| `boostAmount` | `int` | Boost strength (arbitrary units)                          |

**Formula**

```
if boostElement < ElementFirst:
    return 0

if element == boostElement:
    return value * boostAmount / 128   (truncated)

if OpposedElements[element] == boostElement:
    return -(value * boostAmount / 256)  (truncated, result is negative)

return 0
```

**Edge cases**

- `element` in `{0, 1}`: `OpposedElements[0] = OpposedElements[1] = 0 < ElementFirst`,
  so the opposed branch is never triggered regardless of `boostElement`.
- `value = 0` → always returns 0.

**Invented examples**

| value | element | boostElement | boostAmount | result          |
|-------|---------|--------------|-------------|-----------------|
| 128   | 3       | 3            | 64          | 64 (match)      |
| 200   | 3       | 2            | 48          | −37 (opposed: OE[3]=2=boost) |
| 100   | 4       | 3            | 64          | 0 (unrelated)   |
| 100   | 0       | 3            | 64          | 0 (boostElement≥2, but element=0 → no match, OE[0]=0≠3) |
| 256   | 5       | 5            | 128         | 256 (match)     |

---

### 2.3 `GetTurnDelay(ownSpeed, otherSpeed) → int`

Returns the number of ticks until the current side's next turn.

**Inputs**

| Parameter    | Type  | Description                           |
|--------------|-------|---------------------------------------|
| `ownSpeed`   | `int` | Acting side's speed                   |
| `otherSpeed` | `int` | Opposing side's speed                 |

**Formula**

1. Clamp: `ownSpeed  = max(ownSpeed,  1)`.
2. Clamp: `otherSpeed = max(otherSpeed, 1)`.
3. `square = ownSpeed * otherSpeed`.
4. Integer square root via 10 Newton–Raphson iterations, initial guess 999:
   ```
   root = 999
   repeat 10 times:
       root = (root + square / root) / 2   (both divisions truncated)
   ```
5. `delay = 1000 * otherSpeed / root` (truncated).
6. Clamp: `delay = clamp(delay, 707, 1414)`.

**Edge cases**

- Equal speeds: all ten iterations converge to approximately `sqrt(speed²) = speed`,
  so `delay ≈ 1000 * speed / speed = 1000` (within ±1 of convergence noise).
- `ownSpeed >> otherSpeed` → delay approaches 707 (lower clamp).
- `otherSpeed >> ownSpeed` → delay approaches 1414 (upper clamp).
- Zero speeds are clamped to 1.

**Invented examples**

| ownSpeed | otherSpeed | result |
|----------|------------|--------|
| 100      | 100        | 1000   |
| 1        | 400        | 1414   |
| 400      | 1          | 707    |
| 0        | 50         | 1414   |
| 50       | 0          | 707    |

---

### 2.4 `TestRunAway(runAttempts, atk, def, blockRunAway, atkFlags, rngValue) → bool`

Returns `true` if the player's run-away attempt succeeds.

**Inputs**

| Parameter      | Type          | Description                                                |
|----------------|---------------|------------------------------------------------------------|
| `runAttempts`  | `int`         | Number of run-away attempts so far this battle             |
| `atk`          | `BattleStats` | Attacker (player) stats                                    |
| `def`          | `BattleStats` | Defender (enemy) stats                                     |
| `blockRunAway` | `bool`        | `true` → always fail                                       |
| `atkFlags`     | `int`         | `FighterFlags` bitmask for the attacker                    |
| `rngValue`     | `int`         | Raw value from the battle RNG table                        |

**Algorithm**

```
if blockRunAway:                              → return false
if atkFlags & (Asleep | NoSwitch) != 0:       → return false

chance = (runAttempts + 1) * 8
if atkFlags & Paralyzed != 0:
    chance = chance >> 1          (arithmetic right-shift, halves chance)

if atk.Level > def.Level:
    chance += atk.Level - def.Level

if atk.Stats[Speed] > def.Stats[Speed]:
    chance += (atk.Stats[Speed] - def.Stats[Speed]) / 10   (truncated)

chance += atk.RunAwayBonus

return (rngValue & 0x7F) < chance
```

**Edge cases**

- `blockRunAway = true` → always `false`, regardless of all other inputs.
- `Asleep` or `NoSwitch` set → always `false`.
- `Paralyzed` halves `chance` (before level/speed bonuses).
- `chance ≥ 128` → always succeeds for any `rngValue`.

**Invented examples**

| runAttempts | atkLevel | defLevel | atkSpeed | defSpeed | runAwayBonus | blockRunAway | atkFlags | rngValue | result |
|-------------|----------|----------|----------|----------|--------------|--------------|----------|----------|--------|
| 0           | 10       | 10       | 100      | 100      | 0            | false        | 0        | 7        | true   |
| 0           | 10       | 10       | 100      | 100      | 0            | false        | 0        | 8        | false  |
| 5           | 99       | 1        | 300      | 1        | 30           | true         | 0        | 0        | false  |
| 1           | 10       | 10       | 100      | 100      | 0            | false        | Paralyzed| 7        | false  |

*(chance at runAttempts=0 with no bonuses = 1×8 = 8; `rngValue & 0x7F = 7 < 8` → true)*

---

### 2.5 `TestEnemyRunAway(atk, def, atkFlags, rngValue) → bool`

Returns `true` if the enemy's run-away attempt succeeds.

**Inputs**

| Parameter  | Type          | Description                                     |
|------------|---------------|-------------------------------------------------|
| `atk`      | `BattleStats` | Attacker (enemy) stats                          |
| `def`      | `BattleStats` | Defender (player) stats                         |
| `atkFlags` | `int`         | `FighterFlags` bitmask for the attacker          |
| `rngValue` | `int`         | Raw RNG value                                   |

**Algorithm**

```
if atkFlags & Asleep != 0:    → return false

chance = def.RunAwayGuard != 0 ? 0x20 : 0x40
if atkFlags & Paralyzed != 0:
    chance = chance >> 1

chance += (atk.Stats[Speed] - def.Stats[Speed]) / 10   (truncated; may be negative)

return (rngValue & 0x7F) < chance
```

**Edge cases**

- `Asleep` set → always `false`.
- `def.RunAwayGuard != 0` → base chance is 0x20 (32) instead of 0x40 (64).
- If `def.Stats[Speed] > atk.Stats[Speed]`, the speed term is negative and
  **reduces** `chance`.  With C# truncating division, `(-9) / 10 = 0`, not −1.

---

### 2.6 `AdjustDamage(tech, user, target, value, boostElement, boostAmount, critRoll) → int`

Transforms a raw damage value by applying all modifiers in order.

**Inputs**

| Parameter     | Type          | Description                                                      |
|---------------|---------------|------------------------------------------------------------------|
| `tech`        | `TechData`    | Technique being used                                             |
| `user`        | `BattleStats` | Attacker stats                                                   |
| `target`      | `BattleStats` | Defender stats                                                   |
| `value`       | `int`         | Raw damage before any adjustment                                 |
| `boostElement`| `int`         | Battle's active boost element                                    |
| `boostAmount` | `int`         | Boost strength                                                   |
| `critRoll`    | `int`         | −1 = not critical; 0–63 = the random component (already masked) |

**Algorithm** (each step modifies `result` in place, starting from `value`)

```
result = value

// Step 1: element boost
result += GetElementBoost(result, tech.Element, boostElement, boostAmount)

// Step 2: family bonus (+50%)
if tech.Family >= FamilyFirst:
    if tech.Family == target.Family:
        result += result / 2
else:
    for i in 0..2:
        if user.WeaponFamilies[i] >= FamilyFirst
           and user.WeaponFamilies[i] == target.Family:
            result += result / 2
            break

// Step 3: charge bonus
if user.DamageBonus != 0:
    result += result * user.DamageBonus / 64

// Step 4: element vs resistance
if tech.Element >= ElementFirst:
    result += result * tech.ElementPower * 2 / target.Resist[tech.Element - ElementFirst]
else if (tech.Script < 11 or tech.Script > 12) and user.Element != 0:
    result += result * user.ElementPower * 2 / target.Resist[user.Element - ElementFirst]

// Step 5: triple-hit reduction (×0.4)
if tech.Effect < TechEffectFirst
   and user.TripleHit != 0
   and (tech.Script < 11 or tech.Script > 12):
    result = result * 4 / 10

// Step 6: critical hit bonus
if critRoll >= 0:
    result += result * (critRoll + 0x20) / 64

// Step 7: damage cut (flat reduction, floored to 0)
if target.DamageCut != 0:
    result -= target.DamageCut
    if result <= 0: result = 0

// Step 8: 5× cap
if result > value * 5:
    result = value * 5

// Step 9: hard cap at 9999
if result >= 10000:
    result = 9999

return result
```

**All divisions are truncated towards zero.**

**Element-vs-resist formula (step 4) note:**
The formula `result += result * P * 2 / R` means:
- If `R = 100` (neutral), the added term equals `result * 200 / 100 = 2 * result`,
  so the new `result = 3 * result`.
- If `R = 200` (strong resist), added term = `result * 200 / 200 = result`,
  so new `result = 2 * result`.
- If `R = 50` (weak), added term = `result * 200 / 50 = 4 * result`,
  so new `result = 5 * result`.  The 5× cap then limits this.

**Step 4 script guard:** the user-element branch only applies when
`tech.Script` is outside the range `[11, 12]` (inclusive).

**Invented examples**

| value | tech.Element | tech.ElementPower | target.Resist[elem] | user.DamageBonus | critRoll | target.DamageCut | result |
|-------|--------------|------------------|---------------------|------------------|----------|------------------|--------|
| 100   | 0 (none)     | —                | —                   | 0                | −1       | 0                | 100    |
| 100   | 3            | 100              | 100                 | 0                | −1       | 0                | 300    |
| 50    | 0            | 0                | —                   | 64               | −1       | 0                | 100    |
| 200   | 0            | 0                | —                   | 0                | 63       | 10               | (200*(63+32)/64) - 10, then cap |
| 5000  | 0            | 0                | —                   | 0                | −1       | 0                | 9999 if > 10000; capped by 5× value otherwise |

*(Row 3: 50 + 50*64/64 = 50 + 50 = 100.)*
*(Row 4: 200 + 200*(63+32)/64 = 200 + 200*95/64 = 200 + 296 = 496 − 10 = 486; 5× cap = 1000; not hit.)*

---

### 2.7 `ComputeDamage(tech, user, target, enemyAttacker, strength, boostElement, boostAmount, critRoll) → int`

Computes damage for a physical technique.

**Inputs** — same as `AdjustDamage`, plus:

| Parameter       | Type          | Description                                         |
|-----------------|---------------|-----------------------------------------------------|
| `enemyAttacker` | `bool`        | `true` if the attacker is an enemy                  |
| `strength`      | `int`         | Enemy strength in 16ths (irrelevant when `false`)   |

**Formula**

```
if enemyAttacker:
    value = tech.Power * strength / 16 * user.Stats[Attack] / target.Stats[Defense]
    // Order: ((tech.Power * strength) / 16) * user.Stats[Attack]) / target.Stats[Defense]
else:
    value = tech.Power * user.Stats[Attack] / target.Stats[Defense]

return AdjustDamage(tech, user, target, value, boostElement, boostAmount, critRoll)
```

**Note on operator precedence:** C# integer `/` is left-associative.  The
enemy formula evaluates strictly left-to-right with truncation after each `/`.

**Invented examples**

| techPower | userAttack | targetDefense | enemyAttacker | strength | critRoll | result |
|-----------|------------|---------------|---------------|----------|----------|--------|
| 50        | 100        | 50            | false         | —        | −1       | AdjustDamage(..., 100, 0, 0, −1) |
| 50        | 100        | 50            | true          | 16       | −1       | AdjustDamage(..., 100, 0, 0, −1) ← same for strength=16 |
| 50        | 100        | 50            | true          | 32       | −1       | AdjustDamage(..., 200, ...) |
| 100       | 200        | 1             | false         | —        | 63       | ≥ 9999 after AdjustDamage |

---

### 2.8 `ComputeMagicDamage(tech, user, target, enemyAttacker, strength, boostElement, boostAmount, critRoll) → int`

Computes damage for a magic technique.  The order of modifiers differs from
`ComputeDamage` / `AdjustDamage`.

**Formula**

```
basePower = enemyAttacker ? tech.Power * strength / 16 : tech.Power

// Spirit ratio
power = basePower * (user.Stats[Spirit] * 50 / target.Stats[Spirit] + 50) / 100

// Clamp to [basePower/2 .. basePower*2]
if power > basePower * 2: power = basePower * 2
if power < basePower / 2: power = basePower / 2

result = power

// Element boost
result += GetElementBoost(result, tech.Element, boostElement, boostAmount)

// Element vs resistance (three branches on resist value)
if tech.Element >= ElementFirst:
    resist = target.Resist[tech.Element - ElementFirst]
    if   resist < 100:   result = result * (400 - resist * 3) / 100
    elif resist >= 300:  result = result * (65  - resist / 20) / 100
    else:                result = result * (125 - resist / 4 ) / 100

// Family bonus
if tech.Family >= FamilyFirst and tech.Family == target.Family:
    result += result / 2

// Critical
if critRoll >= 0:
    result += result * (critRoll + 0x20) / 64

// Damage cut
if target.DamageCut != 0:
    result -= target.DamageCut
    if result <= 0: result = 0

// Cap: 5× basePower then 9999
if result > power * 5: result = power * 5
if result >= 10000:    result = 9999

return result
```

**Resist branches** (let `R = target.Resist[element - ElementFirst]`):

| Condition     | Multiplier                     | At R=50    | At R=100   | At R=200   | At R=300   |
|---------------|--------------------------------|------------|------------|------------|------------|
| `R < 100`     | `(400 − R×3) / 100`            | 2.5×       | —          | —          | —          |
| `100 ≤ R < 300`| `(125 − R/4) / 100`           | —          | 1.0×       | 0.75×      | —          |
| `R ≥ 300`     | `(65 − R/20) / 100`            | —          | —          | —          | 0.50×      |

**Note:** the 5× cap uses `power` (post-spirit-clamp value), not `basePower`.

---

### 2.9 `ComputeCounterDamage(tech, user, target, receivedDamage, boostElement, boostAmount, critRoll, special = false) → int`

Computes the counter-attack damage of an enemy.

```
result = special ? receivedDamage * tech.EffectPower / 32
                 : receivedDamage * tech.EffectPower / 64
return AdjustDamage(tech, user, target, result, boostElement, boostAmount, critRoll)
```

**Note:** `special = true` doubles the counter scaling (divides by 32 instead of 64).
`receivedDamage = 0` → `result = 0` → `AdjustDamage` returns 0.

---

### 2.10 `ComputeEnemyStats(entry, level, strength) → BattleStats`

Resolves a battle-table entry into combat-ready stats by scaling by `strength / 16`.

```
result = new BattleStats { Level = level, Family = entry.Family }
for j in 0..4:
    result.Stats[j] = entry.Stats[j] * strength / 16
for j in 0..11:
    result.Resist[j] = entry.Resist[j]
return result
```

All other fields of `result` (DamageBonus, DamageCut, WeaponFamilies, …)
remain at their default (zero / null).

**Edge cases**

- `strength = 16` → `result.Stats[j] = entry.Stats[j]` (no scaling error).
- `strength < 16` and `entry.Stats[j] < 16` → `result.Stats[j] = 0` (truncation).
- `Resist` is copied verbatim (no scaling).

---

### 2.11 `RollCritical(tech, atk, def, atkFighter, defFighter, rngValue) → bool`

Returns `true` if a physical attack is a critical hit.

**Inputs**

| Parameter    | Type            | Description                         |
|--------------|-----------------|-------------------------------------|
| `tech`       | `TechData`      | Technique                           |
| `atk`        | `BattleStats`   | Attacker stats                      |
| `def`        | `BattleStats`   | Defender stats                      |
| `atkFighter` | `BattleFighter` | Attacker runtime state (`Special`)  |
| `defFighter` | `BattleFighter` | Defender runtime state (status flags) |
| `rngValue`   | `int`           | Raw RNG value                       |

**Algorithm**

```
chance = 4

// Tech-effect branch
if tech.Effect >= TechEffectFirst:
    if tech.Effect == TechEffectCritical:
        chance += tech.EffectPower
else:   // accessory branch
    if atk.CriticalBonus != 0:
        chance = atkFighter.Special ? atk.CriticalBonus * 2 + 4
                                    : atk.CriticalBonus + 4

// Family match
if tech.Family >= FamilyFirst:
    if tech.Family == def.Family:
        chance += 0x3C   // +60
else:
    for i in 0..2:
        if atk.WeaponFamilies[i] >= FamilyFirst
           and atk.WeaponFamilies[i] == def.Family:
            chance += 0x10   // +16
            break

// Status bonuses
if defFighter.Flags & Paralyzed: chance += defFighter.Paralysis >> 3
if defFighter.Flags & Asleep:    chance += defFighter.Sleep     >> 3
if defFighter.Flags & Confused:  chance += defFighter.Confusion >> 1

return (rngValue & 0x7F) < chance
```

**Edge cases**

- When `tech.Effect >= TechEffectFirst` but is **not** `TechEffectCritical`, neither branch adds to `chance` (tech-effect branch enters but does nothing; accessory branch is skipped).
- `atkFighter.Special = true` doubles the accessory critical bonus.
- A family match on `tech.Family` and on any `WeaponFamilies` are mutually exclusive (the tech branch runs only when `tech.Family >= FamilyFirst`; the weapon branch only when `tech.Family < FamilyFirst`).

---

### 2.12 `RollMagicCritical(tech, def, defFighter, rngValue) → bool`

Returns `true` if a magic attack is a critical hit.

**Inputs**

| Parameter    | Type            | Description                                              |
|--------------|-----------------|----------------------------------------------------------|
| `tech`       | `TechData`      | Technique (must have `tech.Element >= ElementFirst`)     |
| `def`        | `BattleStats`   | Defender stats                                           |
| `defFighter` | `BattleFighter` | Defender runtime state (status flags)                    |
| `rngValue`   | `int`           | Raw RNG value                                            |

**Algorithm**

```
value = tech.ElementPower * 100 / def.Resist[tech.Element - ElementFirst]
if value > 0x40: value = 0x40   // cap at 64

chance = value + 4

if tech.Family >= FamilyFirst and tech.Family == def.Family:
    chance = value + 0x40   // overrides the +4 with +64

if defFighter.Flags & Paralyzed: chance += defFighter.Paralysis >> 3
if defFighter.Flags & Asleep:    chance += defFighter.Sleep     >> 3
if defFighter.Flags & Confused:  chance += defFighter.Confusion >> 1

return (rngValue & 0x7F) < chance
```

**Note:** the family match **overwrites** `chance` (it is `= value + 0x40`, not `+=`).

---

## 3. Self-review checklist

A developer without any knowledge of the game should be able to reimplement
`DamageCalculator` from this document.  Verification:

- [x] All public method signatures listed with types (§2.1–§2.12).
- [x] All supporting types documented with field names, types, and semantics (§1).
- [x] Arithmetic semantics (truncation direction) stated once and unambiguously (§2).
- [x] Operator evaluation order for the multi-division enemy-formula stated (§2.7).
- [x] Every branching condition spelled out (element, family, effect, script guards).
- [x] `OpposedElements` literal array given (§2.1).
- [x] All three magic-resist branches documented with formulas and examples (§2.8).
- [x] Edge cases: zero inputs, clamped values, always-false conditions, cap semantics.
- [x] All constant values from `BattleConst`, `BattleStat`, `FighterFlags`, `Resist` (§1).
- [x] `critRoll` semantics: −1 = no crit, 0–63 = partial RNG (already masked to 6 bits).
- [x] `AdjustDamage` five-times cap uses the *original* `value` (parameter), not
      the modified `result`.
- [x] `ComputeMagicDamage` five-times cap uses `power` (post-spirit-clamp), not
      `basePower`.
- [x] `RollMagicCritical` family match overwrites `chance` (not adds to it).
- [x] `TestEnemyRunAway` speed-difference can produce a negative addition (C# truncation).
- [x] `ComputeCounterDamage` `special` parameter doubles the scaling divisor.
