# Battle HUD and victory report sprites

This note covers the SpriteDrawer sprite sheet format used by the battle HUD
and victory report screen. The decoder is `Dw3.Formats.Battle.BattleHudDecoder`
and `Dw3.Formats.Battle.BattleReportDecoder`. All integer fields are
little-endian.

## Source files (EU disc)

| File ID | Disc path / name | Content |
|---|---|---|
| `0x455` | `WFIGHTMN` | Battle HUD sprite sheet |
| `0x456` | battle images | Cursor bar TIM (entry 0) and HUD texture page (entry 1) |
| `0x286` | `CMFONTCS` | Menu sprite sheet (arrow frame reused by report) |
| `0x287` | `CMFONTTM` | Menu texture archive |
| `0x79F` | `STFGTREP` | Victory report sprite sheet |
| `0x7A0` | STFGTREP textures | Multi-entry model-archive of RLEN TIM pages |

## SpriteDrawer sheet layout

Each sprite sheet file starts with a 4-byte skip word. The sheet body begins at
byte 4. Its header:

| Offset in body | Type | Meaning |
|---|---:|---|
| `+0x00` | `u32` | `partsOffset`: offset (from body start) of the parts table |
| `+0x04` | `u32` | `idsOffset`: offset of the frame-id list |
| `+0x08` | `u32[frameCount]` | Frame offsets (one per frame, relative to body start), starting at `+0x08` |

### Parts table

Starts at `partsOffset` in the body, runs to `idsOffset`. Each entry is 7 × `s16`
(14 bytes). If the table length in bytes is not a multiple of 14, a 2-byte
alignment pad is subtracted first.

| Field | Type | Meaning |
|---|---:|---|
| `U` | `s16` | Texel column in the texture page |
| `V` | `s16` | Texel row in the texture page |
| `Width` | `s16` | Width in texels |
| `Height` | `s16` | Height in texels |
| `ClutX` | `s16` | CLUT X in halfword units within the texture page |
| `ClutY` | `s16` | Base CLUT row |
| `Mode` | `s16` | Colour mode: 0 = 4-bit (the only valid value) |

### Frame-id list

Starts at `idsOffset`, a sequence of `u8` values terminated by `0xFF`. The
number of entries equals the number of frame offsets in the header.

### Frame record

Each frame offset points to a frame record in the body:

| Offset | Type | Meaning |
|---|---:|---|
| `+0x00` | `s16` | Part count (1..64) |
| `+0x02` | `s16` | Frame CLUT row offset (added to per-part `ClutY`) |
| `+0x04` | `s16` | ABR (semi-transparency mode; non-zero enables it) |
| `+0x06` | `s16[3 × count]` | Part records: `partIndex`, `dx`, `dy` per part |

SpriteDrawer draws parts in reverse order (last part is drawn deepest).

### CLUT lookup

For a part: `page = ClutX / 64`, `word = ClutX % 64`. The palette is 16
consecutive halfwords starting at `word` in row `ClutY + frameRow +
clutRowOverride` of `pages[page]`.

### 4-bit pixel read

Each texel `t` within a row of halfwords:
- halfword index = `t / 4`
- nibble shift = `(t % 4) * 4`
- palette index = `(halfword >> shift) & 0xF`

Palette entry 0 is transparent. For semi-transparent parts (`abr != 0`), entries
with bit 15 set render at alpha 128; otherwise alpha 255. Channel expansion:
`5-bit value × 255 ÷ 31`.

## Texture sources

### HUD texture page (file 0x456)

The outer archive has two entries (at offsets `u32[0]` and `u32[1]`). Entry 0 is
the cursor bar TIM. Entry 1 is a one-entry inner archive whose sole entry is an
RLEN-compressed 4-bit TIM page (64 halfwords × 256 rows = 256 × 256 texels).

### Cursor bar (file 0x456, entry 0)

A direct-16 TIM (magic `0x10`, no CLUT block). The image block starts at byte 8.
The strip is 156 × 12 pixels, holding 12 animation frames of 12 × 12 each, plus
one "off" frame. Channel `0` (zero halfword) is transparent.

### Report texture (file 0x7A0)

A 4-byte leading skip word (value = 4), then an offset-table archive. Each
present entry is an RLEN-compressed or raw 4-bit TIM page uploaded at VRAM
position `(0x280, 0)`.

### Menu texture (file 0x287, CMFONTTM)

4-byte skip word, then an offset table (u32 values relative to byte 4, terminated
by zero), each pointing to an RLEN-compressed 4-bit TIM page. Same format as the
font atlas.

## RLEN decompression

RLEN magic: bytes `R`, `L`, `E`, `N` at offset 0. Offset 4: `u32` uncompressed
size. Stream starts at byte 8:

| Command byte | Action |
|---|---|
| `0x00` | End of stream |
| `0x80` (count = 0) | Zero-count repeat: reads and discards value byte |
| `0x80`..`0xFF` (count > 0) | Repeat: `(cmd & 0x7F)` copies of the next byte |
| `0x01`..`0x7F` | Literal: copy the next `cmd` bytes verbatim |

The decompressed output must equal exactly the declared size.

## Victory report overlay (STFGTREP.PRO)

The report overlay (`AAA/PRO/STFGTREP.PRO`) loads at `0x80082CB0`. Its
`STFGTREP_animations` table at `0x8008905C` encodes 8 partner × 7 frame-slot
entries (`s32`), terminated with −1. The extractor reads this table to supply
animation sequences to the game. The report renderer also borrows the menu arrow
(frame id 10 from `CMFONTCS`, 5 CLUT rows) to draw navigation indicators.
