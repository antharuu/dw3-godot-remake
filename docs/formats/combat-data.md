# Combat data tables

This note describes the fixed-layout battle records read from the disc.
The decoder is `Dw3.Formats.Battle.BattleDataDecoder`. All integer fields are
little-endian.

## Source files

| File | Content |
|---|---|
| `SLES_039.36` (boot executable) | Digimon records, technique records, item records |
| `SDIGIEDT.PRO` | Enemy battle table |
| Fight-report overlay (`STFGTREP.PRO`) | Evolution records, growth tables |

Record positions within each file are owned by the extractor step. The decoder
only validates and interprets byte structures at supplied offsets.

## Digimon record (stride 0x58)

One entry per playable partner creature. All fields are at fixed offsets from the
record start:

| Offset | Type | Meaning |
|---|---:|---|
| `+0x00` | `u16` | Creature identifier |
| `+0x02` | `u16[6]` | Battle statistics |
| `+0x0E` | `u16[7]` | Elemental resistances |
| `+0x1C` | `u16[7]` | Known technique slots |
| `+0x2A` | `u16` | Pair technique |
| `+0x2C` | `u8[5]` | Status resistances |
| `+0x31` | `u8[6]` | Technique levels |
| `+0x37` | `u8[5]` | Required knowledge levels |
| `+0x3C` | `u8` | Experience level |
| `+0x3D` | `u8` | Pair partner index |
| `+0x3E` | `u8` | Experience rate |
| `+0x3F` | `u8` | HP base |
| `+0x40` | `u8` | MP base |
| `+0x41` | `u8` | HP growth factor |
| `+0x42` | `u8` | MP growth factor |
| `+0x43` | `u8[6]` | Per-stat growth factors |
| `+0x49` | `u8[7]` | Per-resistance growth factors |
| `+0x50` | `u8[5]` | Blast-form indices |
| `+0x55` | `u8` | Name identifier |
| `+0x56` | `u8` | Creature family |

## Technique record (stride 0x12)

One entry per learnable technique:

| Offset | Type | Meaning |
|---|---:|---|
| `+0x00` | `u16` | MP cost |
| `+0x02` | `u16` | Base power |
| `+0x04` | `u8` | HUD icon index |
| `+0x05` | `u8` | Technique kind |
| `+0x06` | `u8` | Accuracy |
| `+0x07` | `u8` | Element |
| `+0x08` | `u8` | Element power |
| `+0x09` | `u8` | Family |
| `+0x0A` | `u8` | Status effect type |
| `+0x0B` | `u8` | Status effect chance |
| `+0x0C` | `u8` | Status effect power |
| `+0x0D` | `u8` | Script stage |
| `+0x0E` | `u8` | Script effect |
| `+0x0F` | `u8` | Script sound |
| `+0x10` | `u8` | Script index |
| `+0x11` | `u8` | Hit count |

## Enemy battle table (SDIGIEDT.PRO, stride 0x46)

A variable-length sequence of enemy configuration entries terminated by an entry
with `id == 0`. Each entry:

| Offset | Type | Meaning |
|---|---:|---|
| `+0x00` | `s16` | Enemy identifier (0 = sentinel) |
| `+0x02` | `s16` | Drop item index |
| `+0x04` | `s16` | Drop item chance |
| `+0x06` | `s16` | Name identifier |
| `+0x08` | `s16[3]` | Technique slots |
| `+0x0E` | `s16[5]` | Battle statistics |
| `+0x18` | `s16[12]` | Elemental resistances |
| `+0x30` | `u8` | Family |
| `+0x32` | `BattleAction[4]` | Decision actions |
| `+0x42` | `BattleAction` | Counter action |

Each `BattleAction` is 4 bytes: `u8 target`, `u8 condition`, `s16 conditionArgument`.

## Item record (stride 12)

| Offset | Type | Meaning |
|---|---:|---|
| `+0x00` | `u32` | Virtual address of the item effect record |
| `+0x04` | `u16` | Buy price |
| `+0x06` | `u16` | Sell price |
| `+0x08` | `u8` | Item kind |
| `+0x09` | `u8` | Item type |

Each item effect record is 4 bytes: `u8 flags`, `u8 kind`, `u16 amount`.

## Evolution record (stride 16)

Stored in the fight-report overlay:

| Offset | Type | Meaning |
|---|---:|---|
| `+0x00` | `s32` | Target Digimon index (1-based into the creature table) |
| `+0x04` | `s16` | Required partner 1 index |
| `+0x06` | `s16` | Required partner 1 level |
| `+0x08` | `s16` | Required partner 2 index |
| `+0x0A` | `s16` | Required partner 2 level |
| `+0x0C` | `s16` | Required stat slot |
| `+0x0E` | `s16` | Required stat value |

## Battle reward record (stride 12)

Stored consecutively for each encounter entry:

| Offset | Type | Meaning |
|---|---:|---|
| `+0x00` | `s32` | Digimon experience |
| `+0x04` | `s32` | Player experience |
| `+0x08` | `s32` | Money |

## Growth tables

The fight-report overlay stores growth thresholds and gains in a single
consecutive block. Fixed counts (EU disc):

| Section | Count | Element type |
|---|---|---|
| Level experience thresholds | 4 | `s32` |
| Vital point cut thresholds | 4 | `s32` |
| Vital point random values | 9 | `s32` |
| Stat gain table | 6 tiers × 9 values | `s32` |
| Resistance gain table | 8 | `s32` |

The decoder reads all sections in sequence from a caller-supplied offset. Any
buffer overrun raises `FormatException`.
