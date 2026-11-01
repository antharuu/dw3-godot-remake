# Water and map objects

The water domain is a general stage-map-object decoder. It is implemented by
`StageDescriptorReader`, `StageTileDecoder`, `SpriteSheetDecoder`,
`ObjectTextureArchiveDecoder`, and `SpriteFrameRenderer`. Names in this note
describe file roles, not a requirement that every object is water.

## Sources and descriptor recovery

A stage uses three disc members: its overlay, a TMPK archive containing a sprite
sheet, and a MASK archive containing texture pages. The overlay setup code writes
two members of a field descriptor: the StageTile table pointer and the TMPK
archive entry index. `StageDescriptorReader` performs bounded constant
propagation over straight-line MIPS instructions and accepts a descriptor member
only when it is assigned one unambiguous constant.

## StageTile table

The recovered table is a sequence of 18-byte records in the overlay. Byte
`+0x02` (`margin`) terminates the table when zero.

| Offset | Type | Meaning |
|---|---:|---|
| `+0x00..+0x09` | `u8[10]` | Visible, archive selector, margin, depth, frame, cycle, cycle range, delay, palette row |
| `+0x0A` | `i16` | Map X |
| `+0x0C` | `i16` | Map Y |
| `+0x0E` | `i16` | Sort Y |
| `+0x10` | `i16` | Initial cycle timer |

Cycle `0` is static, `1` advances frame IDs, and `2` or `3` advances palette
rows. The decoder rejects unsupported visibility or cycle values, an inverted
cycle range, a first-record terminator, and a table that leaves the overlay.

## Sprite sheet and texture archive

The selected TMPK entry starts with a `u32` part-table offset and a `u32`
frame-ID-table offset. Parts are 14-byte records of seven signed 16-bit values:
texture rectangle, palette coordinates, and pixel mode. The frame-ID list ends
with `0xFF` or `0x88`; frame offsets associate the listed IDs with frame bytes.

The MASK archive begins with increasing `u32` entry offsets terminated by zero.
Each entry is optionally RLEN-packed TIM data. Its image block must decode to
one 64-word by 256-row VRAM page. Entry zero may also provide 256-colour palette
rows used by 8-bit parts.

A frame begins with `i16 partCount`, `i16 paletteRowShift`, and `i16 blendMode`,
then `partCount` triples of `i16 partIndex, x, y`. Parts are emitted in reverse
record order, leaving the first listed part on top. Colour word zero is
transparent. A non-zero colour word with its high bit set is emitted to the STP
layer, while other colour words are emitted to the opaque layer. The extractor
writes these layers as separate PNG files so the game can apply the recorded
blend mode without reopening the disc.
