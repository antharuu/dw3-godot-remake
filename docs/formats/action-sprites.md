# Prepared action sprites

The separate extractor supports `extract --disc <user.cue> --out <pack-root> --only battle-sprites`.
`verify-sprites <pack-root>` exercises the production prepared readers without opening a disc.
The full extraction pipeline also runs this domain. Neither command is a Godot renderer.

## Independent versioned contract

The additive `battle_sprites_v1` and `battle_sprite_scripts_v1` SQLite tables do not change the pack-wide schema version or invalidate unrelated domain consumers. A sprite consumer must load and validate its own domain. An empty or missing sprite index is not a source absence.

Each sprite row retains the script-visible identity, sheet index, packed sheet/texture/animation selectors, source sheet/animation/texture hashes, two 32-bit VRAM placement coordinates, source frame definitions, texture-part definitions, decoded texture halfwords, and nine-channel animation samples. Null animation entries retain their original slot and are not compacted. Durations are sample counts, not measured seconds or an assumed task cadence.

A decoded frame retains its frame/animated-palette identity, source header row, unmodified ABR, pivot, dimensions, composite PNG and hash. Individual part PNGs retain source placement order, pivots, texture rectangles, CLUT coordinates and all sixteen original colour words. A consumer should draw parts back-to-front rather than assuming the composite reproduces every PS1 blend. Alpha zero identifies colour word zero. Alpha 128 identifies STP texels when blending is enabled. ABR -1 is opaque, ABR 0 is a valid blend, and ABR 1 through 3 remain distinct source modes.

The source draw routine adds the part CLUT row, frame header row and animated row. It does not replace the header row. Texture words and source frame definitions remain available even for a frame whose palette is outside its loaded effect pages. No row-zero retry, palette substitution, clamping or transparent-image fallback is allowed.

Each part records its `paletteSource` and the `vram` column and row the GPU actually reads. The address follows the source packing: sheet placement plus the summed rows, packed as `(y << 6) | ((x >> 4) & 0x3F)` in sixteen bits, of which the GPU keeps nine row bits. `effect-page` means the CLUT lies in the sheet's own loaded pages. `cleared-vram` is accepted only inside the 64-column, 256-row rectangle at VRAM (448, 0). The boot and country-select full clears zero it, the movie player clears all VRAM when it stops, and no loader, transfer, fill or drawing area of any game mode writes it afterwards. Its sixteen words are therefore zero and every texel of such a part is transparent, exactly as the GPU draws it. Any other out-of-page address still produces an `unsupportedFrames` record.

## Availability and dependencies

- `available` means every referenced frame/palette image was decoded and passed the production reader.
- `source-absent` requires a bounded zero archive entry with its entry index and archive digest.
- `unsupported` remains a failure to provide a self-contained renderable asset. It is never counted as available or as source absence. Where decoding reached the image boundary, the payload keeps all source definitions, texture words, animations, successful frames, and explicit `unsupportedFrames` records.
- A required identity missing from the prepared index is a corrupt/incomplete pack, not a qualified source absence.

An out-of-page CLUT can address other source VRAM after the GPU's address packing. Outside the qualified never-written rectangle this requires the corresponding resident or dynamic VRAM provider to be qualified separately. The extractor must not invent those palette words from an unrelated capture. Source palette addressing and its unresolved identity are retained so the limitation remains reproducible.

Source script rows retain fighter, selector, archive identity, original command ordinals, sprite commands, fade mode/duration signatures and technique-to-sprite selectors. Technique selectors, fighter identities, sheet indices, animation slots, and texture-part indices are separate namespaces. The runtime still needs to resolve the script's technique-dependent selector and obey the original event schedule.

`SpriteEffectDomainReader` validates identity chains, the `battle-sprites-v2` payload version, image hashes, part indices, texture page dimensions, palette widths, palette sources and animation references. It refuses an unknown palette source and a `cleared-vram` part whose address leaves the qualified rectangle or whose words are not all zero. `SpriteEffectScriptReader` validates source identity and fade signatures. Unsupported rows remain visible to the caller. No sprite/fade result upgrades `fullActionRendered`.

## Runtime consumer

`BattleDataPackLoader.LoadSpriteCatalog` validates the domain once through `SpriteEffectDomainReader`
and parses available rows into runtime sheets on demand. The battle renderer registers
`BattleSpriteEffectPlayback` on the production sequence consumer for both script commands:

- Sprite command mode 1 (load) is acknowledged once the identity resolves to an available sheet.
  Mode 0 starts one independent instance at the script position, even for an identity already
  running. A script keeps at most eight running effects, as the source does, and drops a ninth start.
  `source-absent` ends at once. `unsupported` and an identity missing from the index are unavailable,
  so the action fails recoverably instead of skipping the effect.
- An instance plays the archive's child animations up to the first empty entry (thirty at most).
  Each physical step advances every child one source sample, the first step showing sample zero.
  A sample whose scale has a zero is not drawn but keeps its time. The effect ends once every child
  reached its duration, and the script end waits for it.
- Each sample is drawn at the projected anchor plus its offset, through the drawer's YXZ rotation
  matrix and fixed-point scale. Screen anchors (depth `0x7FFF` front, `-1` back) are source screen
  positions, relative to the screen centre on the models layer, whose drawing offset is (160, 120),
  and to the top-left corner on the front layer. Effects 1000 to 1002 and 1007 draw on the front
  layer over the HUD, all others on the models layer under it. Sprites are clipped to the source
  screen at the 3x layout.
- Models-layer effects share the fighters' ordering table (4096 entries). The GTE stores a depth as
  SZ / 4. A fighter polygon's entry drops two more bits per corner and averages them (SZ / 16), a
  projected sprite anchor drops four (SZ / 64), a front screen marker uses entry 0 and a back marker
  entry 0xFFF. A sprite is therefore drawn in front of a fighter unless the fighter is more than four
  times nearer the camera than its anchor. The effect tasks queue after the fighters' model tasks, so
  in a shared entry the sprite is linked last and drawn before the polygons of that entry. The
  remake keeps its existing finer polygon sort and inserts each sprite before the first emitted
  polygon whose source entry is not farther than the sprite's.
- 16:9 policy (a remake choice, not a console behaviour): a screen-anchored frame whose unscaled
  parts span the whole 320-pixel source width is a full-screen backdrop. It is widened about the
  screen centre to the 1280-pixel viewport (factor 4/3, height unchanged). While an effect shows such a
  backdrop, all its parts are clipped to the viewport instead of the source screen, so neither the stage
  nor a cut line appears in the lateral margins. Every sprite keeps its source position and size; only
  the backdrop is widened, and effects without a backdrop keep the source-screen clip.
- Parts are drawn in placement order, placement zero first: the drawer queues them last to first
  and the ordering table draws the last queued first. Within one effect the last child is at the back,
  and among effects the farthest entry, then the latest created, is drawn first. An unblended
  (ABR -1) frame draws every nonzero texel opaque. A blended frame draws each part twice in place,
  its non-STP texels opaque, then its STP texels with the ABR equation (0 half plus half, 1 add,
  2 subtract, 3 add a quarter). `cleared-vram` layers are drawn like any other part and are fully transparent.
- Fade command 0 starts a white flash, an additive full-screen quad whose level rises by
  `255 / frames` per frame to 255 and holds. Command 1 makes the latest flash fall by `255 / frames`
  per frame from its current level until it is removed. The script does not wait for a flash.
  A zero duration is unavailable rather than a division the source would fault on.
- Completion and cancellation hide and free every effect child and the flash.

The offscreen proof runs with `DW3_BATTLE_ACTION_PROBE=1`, `DW3_BATTLE_SPRITE_EFFECTS=1`, an explicit
prepared `DW3_DATA_DIR` and optionally `DW3_SPRITE_CASES` (`technique:fighter`, an `e` prefix for a
natural enemy owner). It plays each case normally and cancelled through the production runner and
renderer, captures every sprite and flash frame with and without every sprite (the group contribution),
then hides each live instance alone in the same held frame: only that difference is credited to its
identity, and a sample whose parts all read the never-written VRAM palette must contribute nothing.
It also observes the action's result message (see [ACTION_PLAYBACK.md](../ACTION_PLAYBACK.md)),
compares the settled HUD numbers with those before the action and requires every child to be destroyed.

`DW3_BATTLE_SPRITE_COMPOSITION=1` runs the held composition proof: one models-layer effect anchored far
behind the fighter nearest the camera, then at its depth on the same camera ray and pose, a front effect
over the HUD and the full-screen backdrop 1020 on the 16:9 viewport.

No sprite or fade result sets `fullActionRendered`.
