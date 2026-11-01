# NPC placement and animation data

The NPC pipeline reads actor placement from field overlays and character assets
through disc-backed lookup tables. It is implemented by `NpcStageActorScanner`,
`NpcOverlayReader`, `NpcDiscTableDecoder`, `ActionArchiveDecoder`, and the NPC
extractor domain. All listed scalar fields are little-endian.

## Finding actor lists

Stage setup code stores a pointer to its actor list in a field-state member at
offset `+0x4C`. The scanner follows constant stores to that member, then accepts
only a unique pointer list whose entries all decode as valid actor records. The
list is a sequence of `u32` overlay RAM addresses terminated by zero. A setup
with no store has no actors. A store that yields no valid list, or multiple valid
lists, is rejected rather than guessed.

## Actor record and conditions

An actor record has 18 used bytes:

| Offset | Type | Meaning |
|---|---:|---|
| `+0x00` | `u32` | Condition-list pointer, zero for unconditional |
| `+0x04` | `u32` | Dialogue-table pointer, zero when absent |
| `+0x08` | `i16` | Character lookup ID |
| `+0x0A` | `i16` | Image slot |
| `+0x0C` | `i16` | Map X |
| `+0x0E` | `i16` | Map Y |
| `+0x10` | `i16` | Facing direction, constrained to `0..7` |

A non-zero condition pointer addresses `u16 code, u16 value` pairs. A code of
`0xFFFF` terminates the list. The decoder bounds both the pointer and the number
of pairs before returning the conditions.

The disc lookup table stores one packed `u32` per character ID: high 16 bits are
the source file number and low 16 bits are that archive's entry index. A zero
word means no character asset. A separate byte table supplies collision widths.
The extractor writes placement, conditions, resolved asset identity, decoded
frames, and animation sequences to the data pack.

## Character animation archive

The resolved character archive has independent data for frames, animation sets,
and sequences. The format decoder treats offsets and sequence terminators as
mandatory structure: it validates every archive range and distinguishes a normal
sequence end from a loop sentinel. The game consumes the extracted records and
PNG frames only. It does not inspect actor overlays or animation archives at
runtime.

## Snapshot visibility

Numeric condition codes are evaluated against numeric progress data during
extraction. The evaluator recognizes the implemented equality and inclusive-range
groups, fails closed for unavailable range references, and does not invent labels
for codes. This keeps the pack reproducible while leaving future condition groups
explicit work instead of silent assumptions.
