# Dialogue overlays and MIPS analysis

This note describes how field dialogue data is recovered from disc overlays
without any external decompilation. The relevant decoders are
`Dw3.Formats.Dialogue.StageDialogueScanner`, `Dw3.Formats.Dialogue.RomAnalysis`,
`Dw3.Formats.Dialogue.FieldTalkReader`, `Dw3.Formats.Mips.MipsInstruction`, and
`Dw3.Formats.Mips.MipsStoreScanner`. All integer fields are little-endian.

## Overview of the recovery pipeline

Each stage overlay (`WSTAGnnn`) is a MIPS R3000 binary loaded at a known base
address. The dialogue pipeline reads from the disc:

1. Scan the overlay's code for two `sw` (store-word) instructions that write into
   the field-state record.
2. From the first store, recover the text-file selector (a language-relative
   offset that identifies the stage's `FSTALKNN.BIN`).
3. From the second store, recover the actor-list pointer, then traverse the list
   of actor records and their FieldTalk tables.

The analysis never falls back to a default or guesses: a stage that cannot be
decoded unambiguously raises `StageAnalysisException`.

## MIPS instruction subset decoded

The R3000 instruction words are decoded by `MipsInstruction`. Only the operations
required for overlay analysis are interpreted; everything else is `Other`.

| Opcode / Function | Operation | Effect |
|---|---|---|
| op `0x0F` | `lui rt, imm` | Loads upper 16 bits |
| op `0x09` | `addiu rt, rs, imm` | Adds sign-extended immediate |
| op `0x0D` | `ori rt, rs, imm` | ORs zero-extended immediate |
| op `0x23` | `lw rt, disp(rs)` | Loads 32-bit word |
| op `0x2B` | `sw rt, disp(rs)` | Stores 32-bit word |
| op `0x02` | `j target` | Jumps within 256 MiB segment |
| op `0x03` | `jal target` | Calls within 256 MiB segment |
| fn `0x08` | `jr rs` | Register jump (return when `rs = $ra`) |
| fn `0x09` | `jalr rd, rs` | Register call |
| fn `0x21` | `addu rd, rs, rt` | 32-bit addition |
| fn `0x25` | `or rd, rs, rt` | Bitwise OR (register copy idiom) |

Instructions with a delay slot (`j`, `jal`, `jr`, `jalr`, branches) are
flagged; the following word executes before the jump takes effect.

## Register tracking and store scanning

`MipsRegisterTracker` follows the value flow of the 32 general-purpose registers
across a linear instruction window. It tracks:
- `lui` / `addiu` / `ori` pairs that load a constant or a language-global-relative
  value.
- `lw` reads from a known address, establishing that a register holds a
  memory-loaded word.

`MipsStoreScanner.FindStores` scans all instructions in the overlay for `sw`
instructions that write to a specific field of a specific base address. It
re-runs the tracker from the beginning of the overlay for each store instruction
found, resolving what value the storing register holds at that point.

A `MipsValue` carries:

| Kind | Meaning |
|---|---|
| `IsConstant` | A compile-time constant |
| `IsLoadedWord` | A word loaded from a known RAM address |
| `IsLoadedWordPlusAddend` | A loaded word with a known compile-time offset |

## Text-file selector recovery

The setup routine of each overlay loads the language global (a RAM address
defined in `DialogueRomLayout`), adds a stage-specific immediate, and stores the
sum into the field-state record at offset `+TextFileFieldOffset`. The scanner
accepts the selector only when all stores to that field agree on one addend.
Zero matching stores, or stores with conflicting addends, raise
`MipsDecodeException`.

## Actor-list pointer recovery

The setup routine stores a materialized overlay pointer into the field-state
record at offset `+ActorListFieldOffset`. The scanner accepts a pointer only when
exactly one stored value leads to a structurally valid null-terminated actor
pointer list. A list is validated by reading each `u32` address in turn and
confirming it decodes as a valid actor record within the overlay.

## Stage text file table

`StageTextFileResolver` maps a text selector to one `FSTALKNN.BIN` disc path.
The disc holds multiple language copies of each text file (e.g., `FRA/`, `GER/`).
The resolver uses the per-file language-copy count from `DialogueRomLayout` plus
the target country folder to compute the correct path. An unknown selector raises
`FormatException`.

## FieldTalk table format

Each actor record includes a pointer to its FieldTalk table. The table is a
sequence of 12-byte records ended by an all-zero record:

| Offset | Type | Meaning |
|---|---:|---|
| `+0x00` | `u32` | Pointer to conditions list, or 0 when unconditional |
| `+0x04` | `u32` | Pointer to actions list, or 0 when none |
| `+0x08` | `s32` | Text entry reference in the stage text file |

A conditions or actions pointer addresses `u16 code, u16 value` pairs terminated
by code `0xFFFF`. Both the pointer and the pair count are bounded against the
overlay. The maximum table length is 64 records; a missing sentinel raises
`FormatException`.

## Progress condition tables

The game executable (`SLES_039.36`) stores two tables used when evaluating gating
conditions during extraction:

- **Progress range table**: `ProgressRangeCount` pairs of `u16` values at a
  fixed virtual address. Each pair defines the inclusive bounds of one condition
  group.
- **Special conditions table**: a compact structure preceding the range table
  at a known virtual address.

Both are decoded by `ProgressConditionDecoder`. Failure aborts the disc-wide
analysis.
