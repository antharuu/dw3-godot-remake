# Prepared action sprites

The separate extractor supports `extract --disc <user.cue> --out <pack-root> --only battle-sprites`.
`verify-sprites <pack-root>` exercises the production prepared readers without opening a disc.
The full extraction pipeline also runs this domain. Neither command is a Godot renderer.

## Independent versioned contract

The additive `battle_sprites_v1` and `battle_sprite_scripts_v1` SQLite tables do not change the pack-wide schema version or invalidate unrelated domain consumers. A sprite consumer must load and validate its own domain. An empty or missing sprite index is not a source absence.

Each sprite row retains the script-visible identity, sheet index, packed sheet/texture/animation selectors, source sheet/animation/texture hashes, two 32-bit VRAM placement coordinates, source frame definitions, texture-part definitions, decoded texture halfwords, and nine-channel animation samples. Null animation entries retain their original slot and are not compacted. Durations are sample counts, not measured seconds or an assumed task cadence.

A decoded frame retains its frame/animated-palette identity, source header row, unmodified ABR, pivot, dimensions, composite PNG and hash. Individual part PNGs retain source placement order, pivots, texture rectangles, CLUT coordinates and all sixteen original colour words. A consumer should draw parts back-to-front rather than assuming the composite reproduces every PS1 blend. Alpha zero identifies colour word zero. Alpha 128 identifies STP texels when blending is enabled. ABR -1 is opaque, ABR 0 is a valid blend, and ABR 1 through 3 remain distinct source modes.

The source draw routine adds the part CLUT row, frame header row and animated row. It does not replace the header row. Texture words and source frame definitions remain available even for a frame whose palette is outside its loaded effect pages. No row-zero retry, palette substitution, clamping or transparent-image fallback is allowed.

## Availability and dependencies

- `available` means every referenced frame/palette image was decoded and passed the production reader.
- `source-absent` requires a bounded zero archive entry with its entry index and archive digest.
- `unsupported` remains a failure to provide a self-contained renderable asset. It is never counted as available or as source absence. Where decoding reached the image boundary, the payload keeps all source definitions, texture words, animations, successful frames, and explicit `unsupportedFrames` records.
- A required identity missing from the prepared index is a corrupt/incomplete pack, not a qualified source absence.

An out-of-page CLUT can address other source VRAM after the GPU's address packing. This requires the corresponding resident or dynamic VRAM provider to be qualified separately. The extractor must not invent those palette words from an unrelated capture. Source palette addressing and its unresolved identity are retained so the limitation remains reproducible.

Source script rows retain fighter, selector, archive identity, original command ordinals, sprite commands, fade mode/duration signatures and technique-to-sprite selectors. Technique selectors, fighter identities, sheet indices, animation slots, and texture-part indices are separate namespaces. The runtime still needs to resolve the script's technique-dependent selector and obey the original event schedule.

`SpriteEffectDomainReader` validates identity chains, version, image hashes, part indices, texture page dimensions, palette widths and animation references. `SpriteEffectScriptReader` validates source identity and fade signatures. Unsupported rows remain visible to the caller. No sprite/fade result upgrades `fullActionRendered`.
