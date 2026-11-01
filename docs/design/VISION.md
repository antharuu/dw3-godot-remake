# Product vision

DW3 Remake is an unofficial, from-scratch recreation of Digimon World 2003. It aims to preserve the original game's rules and atmosphere while making deliberate improvements to presentation, controls, diagnostics, and workflow.

## Current scope

The playable project currently focuses on field exploration, dialogue presentation, and battle presentation and simulation. Field work includes zones, movement, collision, warps, character rendering, environmental animation, climbing, ledge drops, and diagnostics. Battle work includes one active fighter per side, command selection, enemy behavior, rewards, and ongoing presentation refinement.

The following areas remain incomplete: full story progression, save handling, service menus, quests, cinematics, audio, and broad content coverage.

## Principles

- Original rules are the reference for behavior.
- Improvements are intentional and documented.
- The game never bundles game content.
- A user-supplied disc is converted into a private local data pack.
- The game reads that pack, not the disc image.
- Unknown or unsupported input fails clearly rather than receiving an invented replacement.
- Gameplay simulation, decoding, persistence, and presentation remain separable.

## Community-ready direction

The public source tree is being prepared as a C# project with a standalone extractor and a Godot runtime. Contributors can work on synthetic tests, format handling, simulation, tools, and documentation without requiring game content in the repository.

See [ARCHITECTURE.md](ARCHITECTURE.md), [ASSET_PIPELINE.md](ASSET_PIPELINE.md), and [DISTRIBUTION.md](DISTRIBUTION.md) for the technical and provenance contracts.