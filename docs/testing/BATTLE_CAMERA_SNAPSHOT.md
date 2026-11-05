# Qualified camera snapshots

`DW3_BATTLE_CAMERA_PROBE=1` now requires `DW3_BATTLE_CAMERA_SNAPSHOT` to name a private JSON manifest. It no longer combines an old screenshot, an unrelated software-projected pivot and a newly selected command-camera shot.

The manifest and all referenced images stay outside the repository. The game still loads its prepared data pack. This diagnostic never reads a disc or invokes an extractor.

## Qualification contract

Schema 1 contains:

- `qualified`, natural `camp` (`player` or `enemy`), `fighter`, `pose_frame`, settled `fade` and a 320 by 240 `raster`
- source-world `root` and independently recorded `projected_root`
- `camera` with live `position`, `reference`, `translation`, packed-source `rotation` expanded to three integer angles, `roll` and `projection`
- `draw_view` with the actual GTE nine-component `rotation`, three-component `translation` and `projection`
- `images` roles `full`, `repeat`, `without_main`, `restored`, each containing a private `path` and SHA-256 digest
- `qualification` counters `control_changed`, `restore_changed`, `partner_changed`, `hud_changed`, positive `main_changed`, `held_camera_and_pose`, zero `changed_outside_actor_hulls` and zero `nonmain_gte_changed`
- source-world `bones` matrices, each nine fixed-point rotation components followed by three translations, plus `bone_visibility`

A producer must establish the qualification counters from real captures and actual draw observations. Bound every changed pixel independently by the main actor's GTE-projected geometry. Compare the non-main GTE input/output sets across the intervention, including PartnerView. Record composite HUD/PartnerView ROI overlap separately: an actor can occlude those pixels without mutating the underlying layer. Do not call such overlap a zero pixel difference. In particular, an actor ID alone does not establish pixel isolation. State must remain held while the full scene redraws, with an identical untoggled control and restoration. The source image, draw transform and camera record must belong to that held state. Keep frame identifiers and the producer's raw observations in the private manifest and evidence bundle.

The consumer verifies required fields, image identities, source qualification, the independent draw/root projection, the actual live model root, visible bone origins, all three basis endpoints and visibility. Missing or invalid input exits 3. A passing pivot is not a passing pose.

## Explicit camera conversion

Live source camera vectors enter the existing authored-camera API through `C = diag(1,-1,-1)`. Rotation conjugation is `C R C`. The currently qualified camera subset has zero X rotation, Z rotation and roll. Its Y angle is negated. Unsupported rotations fail instead of being guessed.

The expected root is projected independently using the recorded draw matrix, including its fixed-point rounding. It is not recomputed with the production look-at implementation. The probe holds the camera and pose through the captured production frame and repeats that frame before toggling the main actor.

Set `DW3_BATTLE_CAMERA_NEGATIVE=1` to deliberately use a different Y rotation. The saved comparison must visibly differ and the root comparison must exit 3. This switch is diagnostic only and does not alter ordinary menu behavior.

## Limits

The probe does not repair or waive source/model pose mismatches. The earlier menu-shot timeline check remains behind successful snapshot qualification. It does not prove attack, technique, KO, switch or end trajectories. Natural-camp small, large and asymmetric cases each need their own qualified source inputs. Neither a build nor a subpixel root residual establishes full visual parity.
