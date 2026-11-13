# DW3 Remake

An unofficial, from-scratch reimplementation of **Digimon World 2003** (PS1) in Godot 4 and C#.
The original rules are the reference; modern comforts (16:9, 60 FPS, gamepad, debug views) are opt-in improvements.

> **No game content is included.** You must supply your own disc image. The game files are read locally and
> converted into a private data pack on your machine. Nothing proprietary is ever committed or distributed.

## License

Source-available under the [PolyForm Noncommercial License 1.0.0](LICENSE.md): free for any non-commercial use,
modification and redistribution, as long as the `Required Notice` credit is kept. Commercial use is not permitted.
Third parties: [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

Digimon is a trademark of its respective owners. This is a non-commercial fan project, not affiliated with or
endorsed by Bandai Namco.

## Status

| Area | State |
|---|---|
| Exploration: Asuka City, 235 field stages, warps, climbing, cliff drops | Playable |
| Dialogue (French text, original PSX speech bubble) | Playable |
| Combat (1v1, six commands, enemy AI, rewards) | In progress |
| Save, quests, shops, cutscenes, audio | Not started, help wanted |

The combat loop is playable for development, but combat fidelity is still under active verification. Model pivot and scene-composition work, plus production framebuffer comparisons, remain pending. No 1:1 combat claim is made.

See [docs/ROADMAP.md](docs/ROADMAP.md).

## Quick start

Requirements: Godot 4.7.2 .NET, .NET 10 SDK, Git, your own **Digimon World 2003 (Europe)** BIN/CUE disc image.

```sh
git clone https://github.com/antharuu/dw3-remake.git
cd dw3-remake
dotnet build Dw3.sln
godot --path src/Dw3.Game
```

On first launch the game asks for your disc and runs the extractor (a few minutes).
The extracted data is stored in your user data folder and the disc is never needed again.

On Linux, `./run.sh` builds the game and extractor from the same checkout before launch.
It stops if either build fails. An overridden `DW3_EXTRACTOR` must report the same pack
schema as the runtime or extraction is refused before reading the disc. If your checkout
is behind, inspect `git status --short`, then use `git pull --ff-only` only if it can
update without overwriting your work. Git stops on conflicting untracked files. Preserve
those files rather than deleting them, then run `./run.sh` from the updated checkout.

Controls and debug keys: [docs/CONTROLS.md](docs/CONTROLS.md). **F3** shows collisions and warps.
Development, tests, probes, and F3 details: [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md).

## How it works

```
your disc -> Dw3.Extractor (separate process)
               validates the edition, decodes the disc formats (Dw3.Disc, Dw3.Formats)
            -> game data in your user data dir: dw3.sqlite + images/*.png
            -> the game (Dw3.Game, Godot) reads only that data, never the disc
```

Architecture overview: [docs/OVERVIEW.md](docs/OVERVIEW.md).
Format notes: [docs/formats/README.md](docs/formats/README.md).

## Contributing

Reverse engineers, C# and Godot developers, testers with a disc, translators: all welcome.
Start with [CONTRIBUTING.md](CONTRIBUTING.md) and the
[good first issues](https://github.com/antharuu/dw3-remake/labels/good%20first%20issue).

For a full picture of what is done and what is next, see the
[Roadmap](https://github.com/antharuu/dw3-remake/issues/19).
New contributors: the [Start here](https://github.com/antharuu/dw3-remake/discussions/20)
discussion post has setup tips and research notes.

## Credits and legal

Thanks to the Digimon World 2003 reverse-engineering community for format research.
The project does not use or require any decompilation: everything comes from your disc.
