# Text tables and font atlas

This note covers the EU French text encoding, the font atlas, and the per-style
metrics read from the game executable. The relevant decoders are
`Dw3.Formats.Text.TextTableDecoder`, `Dw3.Formats.Text.FontAtlasDecoder`, and
`Dw3.Formats.Text.FontStyleDecoder`. All integer fields are little-endian.

## Text table files (FSTALKNN.BIN)

Each stage's spoken lines are stored in one `FSTALKNN.BIN` file inside the
language subfolder (`AAA/DAT/COUNTRY/FRA/` for the EU French copy). The file
begins with a count and an offset table:

| Offset | Type | Meaning |
|---|---:|---|
| `+0x00` | `s32` | Entry count |
| `+0x04` | `u32[count]` | Byte offset of each null-terminated string, from file start |

Offsets must be monotonically non-decreasing, and each must fall within the
string payload (past the header). The decoder bounds-checks every offset and
rejects a missing null terminator.

### Token encoding (EU French, non-SJIS)

Each string is a sequence of tokens decoded as follows:

| Lead byte | Meaning |
|---|---|
| `0x00` | String terminator (not a token) |
| `0x01 {index}` | Inline icon, 2 bytes total |
| `0x02 {cmd} {args…}` | Control code; argument count from `FONT_CODE_LENGTHS[cmd]` |
| `0x03` | Reserved — rejected as a `FormatException` |
| `0x04`..`0xFF` | Glyph code |

`FONT_CODE_LENGTHS` (total bytes consumed including `0x02` and `cmd`):

| Command | Total bytes |
|---|---|
| 0 | 1 |
| 1 | 2 |
| 2 | 3 |
| 3 | 2 |
| 4 | 5 |
| 5 | 3 |
| 6 | 3 |
| 7 | 2 |
| 8 | 3 |
| 9 | 2 |

A command index ≥ 10 is unknown and always rejected.

## Font atlas (CMFONTTM.BIN)

The font atlas is stored in `AAA/DAT/COUNTRY/CMFONTTM.BIN`. It is a FILE\_CACHE
archive:

| Offset | Type | Meaning |
|---|---:|---|
| `+0x00` | `u32` | FILE\_CACHE count header (skipped) |
| `+0x04` | `u32…` | Offset table: u32 values relative to `+0x04`, zero-terminated |
| each entry | RLEN | RLEN-compressed 4-bit TIM page |

The archive must contain at least two pages. Page 0 carries the in-page CLUT.

### TIM page format

Each decompressed entry is a 4-bit TIM without a CLUT block in the standard
header (magic `0x10`, flags `0x02`). The image block begins at byte 8:

| Block offset | Type | Meaning |
|---|---:|---|
| `+0x00` | `u32` | Image block length |
| `+0x04` | `u16` | X position (unused) |
| `+0x06` | `u16` | Y position (unused) |
| `+0x08` | `u16` | Width in halfwords (`words`); pixel width = `words × 4` |
| `+0x0A` | `u16` | Height in pixels |
| `+0x0C` | `u8[words × height × 2]` | 4-bit indexed pixel data |

Pixel packing: each byte holds two pixels, low nibble first (pixel 0 = `byte &
0xF`, pixel 1 = `byte >> 4`).

### Embedded CLUT

Page 0 holds eight 16-entry palettes at pixel column 48, rows 232–239. Each
entry is a 15-bit BGR halfword; word 0 maps to fully transparent RGBA
`(0, 0, 0, 0)`; all others are fully opaque. Channel expansion: `5-bit value ×
255 ÷ 31`.

## Font style metrics (SLES\_039.36)

Three active text styles are described by a `TextStyle[4]` table in the game
executable (`SLES_039.36`). The table starts at SLES file offset `0x3E574`
(virtual address `0x8004DD74`). Index 0 is unused; indices 1–3 hold the active
styles. Each descriptor is 24 bytes:

| Offset | Type | Meaning |
|---|---:|---|
| `+0x00` | `u8` | Blend mode |
| `+0x01` | `s8` | Line height (disc-verified: 14, 11, 9 for styles 1–3) |
| `+0x02` | `u16` | Padding |
| `+0x04` | `u32` | Virtual address of `Glyph[glyph_count]` |
| `+0x08` | `u32` | Virtual address of `Glyph[icon_count]` |
| `+0x0C` | `u32` | Virtual address of glyph map |
| `+0x10` | `u32` | Virtual address of icon map |
| `+0x14` | `s16` | Declared glyph count (EU: 234, but only 230 are mapped to codes 4–233) |
| `+0x16` | `s16` | Icon count (EU: 114) |

Virtual address to file offset: `0x800 + (va − 0x80010000)`.

Each on-disc `Glyph` record is 11 bytes:

| Offset | Type | Meaning |
|---|---:|---|
| `+0x00` | `u8` | Atlas page index |
| `+0x01` | `u8` | U (texel column in page) |
| `+0x02` | `u8` | V (texel row in page) |
| `+0x03` | `u8` | CLUT column within texture page |
| `+0x04` | `u8` | CLUT row within texture page |
| `+0x05` | `u8` | Width in texels |
| `+0x06` | `u8` | Height in texels |
| `+0x07` | `s8` | Horizontal draw offset dx |
| `+0x08` | `s8` | Vertical draw offset dy |
| `+0x09` | `u8` | Advance width |
| `+0x0A` | `u8` | Unused, discarded |

The EU disc profile declares 230 active glyphs (codes 4–233) and 114 icons per
style. Mismatches are rejected as `FormatException`.
