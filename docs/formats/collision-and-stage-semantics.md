# Field collision and stage semantics

Field movement data is decoded by `TmpkMapDecoder`, `StageSlotDecoder`, and
`StageSlotLocator`. These are separate structures: TMPK maps describe cells,
while overlay slot records describe semantic triggers such as exits and movement
features. All multi-byte values are little-endian.

## TMPK cell maps

A stage TMPK archive starts with an array of `u32` entry offsets. A selected
entry begins with six relative `u32` component offsets:

| Component | Purpose |
|---:|---|
| 0 | Tile grid: `u8 tilesWide`, `u8 tilesHigh`, then one tile index per grid position |
| 1 | 64-pixel indirection table |
| 2 | 32-pixel indirection table |
| 3 | 16-pixel indirection table |
| 4 | 8-pixel indirection table |
| 5 | Final 8 by 8 cell blocks |

The expanded map is `tilesWide * 128` by `tilesHigh * 128` bytes. To resolve a
pixel, the decoder takes its 128-pixel tile index, then follows the 64, 32, 16,
and 8-pixel tables before selecting a byte from the final 8 by 8 block. The
result is a raw cell value. Its meaning belongs to the game rule that consumes
the map, not to the generic TMPK decoder.

Every entry offset and component range is bounded against its own entry. Zero
entry offsets, overlapping components, zero dimensions, and truncated lookup
tables are rejected.

## Stage-slot table

A field overlay can expose an optional slot table through setup instructions.
The locator accepts only an unambiguous `lui` followed by `addiu` or `ori`, then
a store to descriptor member `+0x14`. The resulting RAM pointer must refer back
inside that overlay. This avoids treating arbitrary record-shaped bytes as
triggers.

A slot record is 24 bytes:

| Record offset | Type | Meaning |
|---|---:|---|
| `+0x00` | `u16` | Condition field retained by the decoder |
| `+0x02..+0x07` | bytes | Remaining condition data |
| `+0x08` | `u16` | Slot type, `0` terminates the table |
| `+0x0A` | `u16` | Slot argument |
| `+0x0C` | `u16` | Map X |
| `+0x0E` | `u16` | Map Y |
| `+0x10` | `u16` | Direction |
| `+0x12..+0x17` | bytes | Additional slot fields preserved only where needed by a domain |

The decoder includes the type-zero terminator record in its result. It rejects
unknown slot types, a missing terminator, truncation, or more than its bounded
record limit. The extractor maps supported slot types to pack semantics such as
exits while retaining the raw slot data for diagnostics.

## Overlay coordinates

Field overlays use an executable load address. A RAM pointer into an overlay is
converted to a file offset by subtracting that load address and then range
checking the structure. This coordinate conversion is required before reading
actor, object, or slot tables from the raw overlay bytes.
