# Format notes index

One file per format, written from the C# decoder code. Each note documents
structure and offsets, not extracted asset values. All integer fields are
little-endian unless otherwise stated.

## Field and exploration

| File | Covers |
|---|---|
| [terrain-packets.md](terrain-packets.md) | `ZonePacketDecoder`: tile-grid header, RLEN-compressed TIM packets, sprite lists |
| [collision-and-stage-semantics.md](collision-and-stage-semantics.md) | `TmpkMapDecoder`, `StageSlotDecoder`, `StageSlotLocator`: cell maps, semantic triggers, warp records |
| [water-and-map-objects.md](water-and-map-objects.md) | `StageDescriptorReader`, `StageTileDecoder`, `SpriteSheetDecoder`, `ObjectTextureArchiveDecoder`: map objects and sprite frames |
| [npcs.md](npcs.md) | `NpcStageActorScanner`, `NpcOverlayReader`, `NpcDiscTableDecoder`, `ActionArchiveDecoder`: actor records, conditions, character animation archives |

## Text and dialogue

| File | Covers |
|---|---|
| [text-and-font.md](text-and-font.md) | `TextTableDecoder`: FSTALKNN.BIN text tables and token encoding; `FontAtlasDecoder`: CMFONTTM.BIN archive and 4-bit TIM pages with embedded CLUT; `FontStyleDecoder`: per-style glyph and icon metrics from the executable |
| [dialogues-and-mips.md](dialogues-and-mips.md) | `StageDialogueScanner`, `RomAnalysis`, `FieldTalkReader`, `MipsInstruction`, `MipsStoreScanner`: MIPS R3000 overlay analysis, text-file selector recovery, FieldTalk table format |

## Battle

| File | Covers |
|---|---|
| [combat-data.md](combat-data.md) | `BattleDataDecoder`: Digimon records, technique records, enemy battle table (SDIGIEDT.PRO), item records, evolution records, reward records, growth tables |
| [battle-models.md](battle-models.md) | `ModelArchive`, `BattleModelDecoder`, `BattleMeshDecoder`, `BattleModelAssets`: offset-table archive, bone hierarchy descriptor, mesh vertex/normal/command streams, 4-bit TIM textures, key tracks and motions |
| [hud-and-report.md](hud-and-report.md) | `BattleHudDecoder`, `BattleReportDecoder`: SpriteDrawer sheet layout (WFIGHTMN, CMFONTCS, STFGTREP), 4-bit texture pages, cursor bar, RLEN decompression, victory report overlay |
