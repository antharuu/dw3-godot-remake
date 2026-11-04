# Battle raster framebuffer proof

Build `Dw3.sln` first. The offscreen `DW3_BATTLE_MODEL_PROBE=1` path checks the actual
production `BattleModelLayer` executor, including source-strip quad coverage, the
calibrated texture/modulation contract, and fighter-only and saber-bone-only masks
in the composed main framebuffer and the raw 100x60 PartnerView. The main-model
mask excludes the separate PartnerView rectangle so a working thumbnail cannot
conceal a missing main model. It holds the source
pose at frame 10 and pins the HUD animation and shot-selection roll for repeatability.
It does not change ordinary camera, actor placement or gameplay rules.

## Negative controls

These diagnostic environment variables deliberately make the corresponding model
probe assertion fail with exit 3:

- `DW3_BATTLE_PROOF_CLEAR_INDICES=1`: remove explicit source quad triangle indices.
  A 16x16 source strip must cover 256 pixels, while this control covers none.
- `DW3_BATTLE_PROOF_HIDE_TARGET=1`: remove the target fighter while keeping the
  stage, HUD and opposing fighter. Both fighter contribution masks must fail.
- `DW3_BATTLE_PROOF_WRONG_BLEND=1`: force only the selected real source command
  opaque. The real-sample oracle must fail, not merely produce a different image.

The always-run fabricated blend fixture uses the same Draw path, source-strip
triangles and GPU materials in both targets. It independently asserts alpha-zero
discard, opaque non-STP coverage under both ABR modes, coloured STP half/additive
blends, black STP retention, source clamp before half blend, and additive saturation.
The fixture also requires deliberately opaque blending to fail its STP samples.
No original disc is needed to construct these fixture inputs. Launching the full
model probe still needs a previously prepared user data pack.

## Real overlap discovery and replay

Add `DW3_BATTLE_STP_PROOF=1` to the model probe. Diagnostic actors are translated
so ABR-bearing source primitives overlap at an explicit target point. Their source
meshes, scales, UVs, lighting, pose, collector, source OT key/order and production
cameras are not replaced. The raw target receives a diagnostic opposing actor,
where ordinary PartnerView contains only the partner. The two configurations move
only these diagnostic actors and reverse a one-world-unit relative depth offset.

Discovery requires `DW3_BATTLE_STP_DISCOVERY=1` when no vector file is supplied.
Discovery writes projected streams, live texture pages, complete images and scene
transforms outside the repository. Discovery is explicitly not a sample proof.

For verification set `DW3_BATTLE_STP_VECTORS` to a private JSON array. Each vector
contains `configuration` (`additive` or `half`), `view` (`main` or `raw`), `name`,
`index`, interior pixel `x`/`y`, source `fighter`/`bone`/`vertexStart`, `triangle`
(original three corner indices), `texel` (raw four byte channels) and `opponent`.
Do not store real vectors or images in Git. Select coordinates independently from
the exported input geometry and alpha classes, not by fitting an expected output.

For each vector the probe captures the actual ordered primitive prefix, the same
prefix with the opposing fighter removed, the prefix plus the selected command,
and that final command with deliberately incorrect opaque blending. It requires
an actual opposing-fighter contribution at the selected pixel. The oracle derives
nearest UV, interpolated vertex colour and input texel independently of
`BattlePrimitiveCompositor.Composite`. It checks the source identity and raw texel
against the supplied vector before checking the framebuffer. Whole quads receive
one OT key before their source triangles are emitted.

## Arithmetic domain and limits

These assertions qualify the current 8-bit OpenGL compatibility framebuffer, not
PSX hardware parity. Foreground is component-wise `clamp(texelByte * vertexColor *
2, 0, 255)`. Transparent texels retain the observed prefix destination. Non-STP
texels overwrite it. STP ABR 0 is `(destination + foreground) / 2`. STP ABR 1 is
`min(255, destination + foreground)`. Final integer rounding and Canvas vertex
colour upload quantization are allowed at most two 8-bit units per channel.
Raw word diagnostics invert the lossless 5-to-8-bit channel expansion and A=128
marker from the prepared live page. They are not new reads from the disc.

The original 5-bit modulation, per-term half rounding, dithering, GTE projection,
edge coverage and hardware fixed-point interpolation remain outside these colour
assertions. Residual UV state when a mesh command supplies no new UV bytes remains
textured and is covered by the existing residual-UV model probe.

## Qualified cadence

`DW3_BATTLE_CADENCE_PROOF=1` adds 60 warm-up and 180 measured frames of production
Present plus GPU frame completion, with natural actor placement, held pose and no
screenshots or image readback during the measured window. `cadence.json` records
the driver, resolutions, individual intervals, CPU Present time, process managed
allocations and Present-thread allocations. This is not a full gameplay benchmark
or proof of 60 FPS. Report software-driver/resource context and carry discrepancies
to the integration/cadence gate. Never use the model probe's screenshot-stalled FPS
line as performance acceptance.
