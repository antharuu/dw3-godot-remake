# Native Technique and Digivolution list proof

Build the analyzer-enabled solution, then run the Mono Godot scene with an explicit prepared
pack and a private capture directory:

```sh
dotnet build Dw3.sln --nologo
DW3_DATA_DIR=/path/to/prepared-pack DW3_CAPTURE_DIR=/path/to/private-evidence \
  xvfb-run -a godot --path src/Dw3.Game --rendering-method gl_compatibility \
  --audio-driver Dummy res://Diagnostics/Battle/NativeListMenuProof.tscn
```

On guarded hosts, use the project heavy lane, the PATH-protected dotnet command and the
protected runner for Godot. Remove inherited Wayland environment variables before Xvfb.
Do not use Godot's dummy headless renderer for pixel evidence. No physical input, desktop
window, field startup, disc image or runtime extraction is involved.

## Data and consumer boundary

`NativeListData` loads fighter, technique, item and text records from the validated pack.
`SessionBattleCatalogFactory` configures a real session with an explicitly acquired base
and four acquired forms, of which three are equipped. The natural technique case uses
that factory's unmodified current-form catalogue. A second, explicitly controlled stress
case combines seven unmodified decoded techniques from real form skill records, in record
order, to exercise six rows plus a one-row last page. This is not a claim that the normal
debug starter naturally has seven learned skills. Empty and zero-MP cases are explicit
fixture states.

`NativeMenuSession` supplies these data to the existing native host fixture. It does not
supply callbacks or dispatch. Constructed key events enter `GameHost._Input`, the real
`InputAdapter`, `TickBattleMenu`, `BattleMenuDriver`, production coordinator openers and
production command callbacks. Assertions observe the actual battle flow, active identity,
every fighter's HP/MP, item quantities, next actor, phase, outcome and random draw count.
No player command is submitted through a diagnostic call to `Flow.Choose`.

## Rendering and assertions

A real offscreen `SubViewport` contains `BattleHudNode`, bound to prepared fonts, frames
and icons. `BattleViewBuilder` projects the same host menu and flow. Captures are read
only after `RenderingServer.FramePostDraw`, not merely a process tick.

An independent transparent target submits expected catalogue tokens through the common
PSX text painter. Every opaque expected glyph texel must be present with its exact colour
in the production HUD readback. The mask must also contain visible texels. This checks
all populated technique/form names, grey/current names, refusal description, last-page
description, selected MP cost, active MP and source-derived skill icons. The catalogue to
row trace additionally checks name identity, MP cost and icon identifier against the
unmodified technique record. It is not an independent implementation of the font rasterizer.
Visual reading of retained captures remains a separate check.

Cases cover natural technique execution, paged technique execution and a unique MP debit,
real target damage, held/repeated confirmation, cancellation and normal reentry, zero-MP
and prohibited technique refusal, both empty panes, current-form refusal, all three equipped
slots, transformation from the last slot, current-row refresh on reentry, prohibited
Digivolution and cancellation without resource or RNG cost. Unequipped acquired forms stay
in acquired state and are not silently truncated into equipped slots. Normal Digivolution
has the base plus at most three equipped slots, not an invented paged inventory of every
acquired form. It has no MP cost or technique-description window.

Fixture sequence completion and optional scheduler-owned enemy progression are explicit
lifecycle operations for reaching another player turn. This proof qualifies list rendering
and activation, not physical transformation playback, battle camera, audio, scene composition
or human acceptance of visual fidelity. The existing root proof separately covers controller
overlap, focus and phase gating. Standard CI compiles the native scenes but does not execute
Godot rendering.

## Reproduced preparation defect and controls

Reading the initial real captures exposed truncated small MP/HP digits and page hints.
Preparation rendered every style with the dialogue CLUT. Small-font pixels using indices
outside that palette's nonzero entries became transparent. `FontStyleAtlasRenderer` now
reads the origin from the selected style's decoded glyph metrics before selecting one of
eight palette rows. Pack schema 22 requires the corrected images, so schema 21 packs must
be prepared again. No font bytes or game images are stored in this repository.

Synthetic format tests check the style CLUT, palette bounds and malformed data. A synthetic
preparation test invokes the same production atlas writer and checks all three styles and
eight rows against independently constructed expected PNG bytes. Restoring the former
shared-palette line made that test fail on the PNG byte comparison. Restoring the fix
passed it. Separately, erasing the production technique-name submission made the native
scene fail on missing glyph pixels while the pane and rows still existed. Both controls
were reverted before the final build.

An empty technique pane no longer submits a nonexistent selection or emits a misleading
unsourced-description warning. It retains the real empty pane and cancellation path.
