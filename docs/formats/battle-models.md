# Battle model format (skeleton, meshes, textures, motions)

This note covers the offset-table archive and the model descriptor, mesh command
stream, texture, and animation formats used for battle fighters and fight-stage
geometry. The relevant decoders are `Dw3.Formats.Battle.ModelArchive`,
`Dw3.Formats.Battle.BattleModelDecoder`, `Dw3.Formats.Battle.BattleMeshDecoder`,
and `Dw3.Formats.Battle.BattleModelAssets`. All integer fields are little-endian.

## Offset-table archive (ModelArchive)

Many battle data files share one container format. Its layout:

- The file starts with a table of `u32` offsets.
- The first offset `F` also encodes the table size: entry count = `F / 4`. Thus
  `F` must be a non-zero multiple of 4 and must lie within the file.
- Entry `i`: if `offsets[i] != 0`, it starts at `offsets[i]` and runs to the
  next non-zero offset (or the end of file for the last entry). Zero offsets mark
  absent entries; their index remains valid so references by entry number work.
- Non-zero offsets must be strictly increasing and lie inside the data.

Absent entries are represented as `ArchiveRange(0, 0)`; present ones as
`ArchiveRange(start, end)`. Nested archives inside entries use relative offsets
and a supplied entry count.

## Model descriptor (BattleModelDecoder)

A fighter model file is an offset-table archive. The model descriptor is one of
its entries. Its layout:

| Offset | Type | Meaning |
|---|---:|---|
| `+0x00` | `u32` | Archive entry of the texture sub-archive |
| `+0x04` | `u32` | `lastBone` = bone count − 1 (valid range 1..63, giving 2..64 bones) |
| `+0x08` | `BoneRecord[lastBone]` | One record per non-root bone (bones 1..lastBone) |

Bone 0 is the implicit root with no parent, mesh, or key-track entry. Each
`BoneRecord` is 12 bytes:

| Offset | Type | Meaning |
|---|---:|---|
| `+0x00` | `u32` | Parent bone index (must name an earlier bone) |
| `+0x04` | `u32` | Archive entry of the bone's mesh |
| `+0x08` | `u32` | Archive entry of the bone's key-track data |

All three entry numbers must exist in the archive.

## Mesh entry (BattleMeshDecoder)

Each non-root bone has a mesh archive entry that is itself a nested archive of
six sub-entries referenced by relative offsets:

| Sub-entry | Status | Content |
|---|---|---|
| 0 | required | Vertex stream |
| 1 | optional | Normal stream |
| 2 | required | Command stream |
| 3, 4 | unused | Not read |
| 5 | required | Bounds (offset recorded for reference, not decoded) |

### Vertex and normal streams

Both streams begin with an `s16` count (1–255), followed by 4 bytes of padding,
followed by `count × { s16 x, s16 y, s16 z }` records. A count of 0 or > 255
raises `FormatException`.

### Command stream

The command stream encodes draw state and polygon assembly. Each byte is a
command nibble `(byte >> 4)` and a data nibble `(byte & 0xF)`:

| Command nibble | Meaning |
|---|---|
| `0x0` | End of stream |
| `0x1` | Set texture state: next 6 bytes are `u_base(u16), v_base(u8), clut_x(u8), clut_y(u8), tpage_mode(u8)` |
| `0x2` | Set colour slot `data_nibble` from next 3 bytes `R, G, B` |
| `0x3`..`0x7` | Vertex index literals: data nibble is a small index; used to build polygon index runs |
| `0x8` | Quad flag: the polygon uses four vertex indices |
| `0x9` | Textured: polygon carries its own UV payload |
| `0xA` | `Flag0xA`, kept verbatim |
| `0xB` | `Flag0xB`, kept verbatim |
| `0xC` | Lit: polygon has associated normal indices |
| `0xD` | Gouraud: per-vertex colour interpolation |
| `0xE` | Semi-transparency (ABR) mode, data nibble = ABR value |
| `0xF` | Emit polygon: assembles vertices, normals, UVs and flags accumulated so far |

Polygon emission validates that the index count matches the `Quad` flag (3 for
triangles, 4 for quads). Quads are split into two triangles: `(i0, i1, i2)` and
`(i2, i1, i3)`.

### UV payload

When a polygon is textured (`0x9`), it carries 2 bytes per vertex: `u8 u_rel,
u8 v_rel`. Final UVs are `u_rel + u_base` and `v_rel + v_base` from the current
texture state.

## Textures (BattleTextureDecoder)

The texture sub-archive is itself an offset-table archive. Each present entry is
one 4-bit TIM (optionally RLEN-compressed). The TIM format matches the standard
PS1 structure (magic `0x10`). A CLUT block is present when bit 3 of the flags
word is set.

4-bit pixel packing: each halfword encodes four pixels. Pixel at texel `t`:
nibble index = `(t % 4) * 4`; value = `(halfword >> nibble) & 0xF`.

## Key tracks and motions (BattleAnimationDecoder)

Each non-root bone has a key-track archive entry. Its content is an array of
`BattleBoneTracks` — one per animation — where each track encodes the bone's
transform over time.

A separate motion archive entry (in the same or a companion file) encodes
`BattleMotionStep` sequences. The first step of the first motion must start at
index 0. Fighter models provide a `BattleFighterMetadata` record that supplies
camera data, geometry offsets, and face animation parameters alongside the model.
Fight stages reuse the same model format without fighter metadata.
