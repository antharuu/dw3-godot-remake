# Field terrain packets

This note describes the packet decoder in `Dw3.Formats.Field.ZonePacketDecoder`.
It documents structure, not extracted assets. All integer fields below are
little-endian.

## Disc member and top-level layout

One field stage supplies a terrain packet member. Its first 2048-byte sector is
the header. It is followed by one fixed-size packet slot for every cell in the
tile grid.

| File offset | Type | Meaning |
|---|---:|---|
| `+0x00` | `i32` | Number of present tiles |
| `+0x04` | `i32` | Tile columns |
| `+0x08` | `i32` | Tile rows |
| `+0x0C` | `i32` | Bytes reserved by every tile packet slot |
| `+0x10` | `u16[tileColumns * tileRows]` | Per-tile size table, zero for an absent tile |

The packet slot size must be a multiple of 2048. The member length must equal
`2048 + tileColumns * tileRows * packetBytes`. Each tile is 128 by 128 pixels.
A packet exists at `2048 + tileIndex * packetBytes`, with `tileIndex` increasing
left-to-right then top-to-bottom.

## Decoded tile packet

A non-empty raw packet may be RLEN-compressed. After optional decompression,
its first word is the offset of an indexed 8-bit TIM image. Starting at `+0x04`,
it has three ordered sprite lists:

| Field | Type | Meaning |
|---|---:|---|
| list header | `i32` | Sprite count for one depth plane |
| sprite | `i16 x, y, u, v, width, height` | Destination in the tile and source rectangle in the TIM |

There are exactly three lists, in depth order `0`, `1`, then `2`. The decoder
crops each source rectangle and composites the list in its recorded order onto
a 128 by 128 RGBA plane. It also composites all planes into the stage image.
Transparent source texels do not overwrite prior pixels.

## Validation boundary

The decoder rejects a short member, zero or negative dimensions, an unaligned
packet size, a header whose tile count disagrees with the size table, a member
whose length does not match its grid, non-8-bit terrain TIM data, and any sprite
rectangle or count that leaves the decoded packet. These checks are intentional:
a decoder must not guess a malformed layout.
