# Battle scene math contract

## Purpose

`BattleSceneMath` is the engine-free numeric contract used by the battle presenter. It maps decoded integer records to Godot-space placement, camera and fade values without depending on scene nodes.

## Fixed units

- One Godot world unit is 256 decoded position units.
- Angles use a complete turn of 4096 units.
- Animation and fade interpolation uses a 12-bit weight where 4096 means the destination value.
- The primary battle viewport has half-height 120 in presentation pixels. Auxiliary 100 by 60 targets have half-height 30.

## Required operations

- `StageFadeStep(t)` requires `t > 0` and returns integer floor division `4096 / t`.
- `AdvanceStageFade(f, s, n)` advances by `max(n, 0)` whole frames and clamps the result to 4096.
- `LerpFixed(a, b, w)` returns `a + ((b - a) * w >> 12)`.
- `Ps1BoneBasis(r, s)` returns the row-vector matrix `S * Rx * Ry * Rz`, where `S = diag(s / 4096)` and each rotation angle is `r * tau / 4096`.
- `FighterPlacement(enemy, distance, height)` places camps on opposite Z sides around the fixed origin 0x1400, converts Y and Z to Godot space, and rotates only the player camp by pi radians.
- `CameraPosition(x, y, z)` converts the decoded camera coordinate directly by 256.
- `ComposeCameraPosition(view, reference)` selects the reference or position vector, transforms it in Y-X-Z rotation order, adds translation, and converts by 256.
- `VerticalFovDegrees(projection, halfHeight)` requires both inputs to be positive and returns `2 * atan(halfHeight / projection)` in degrees.
- `PartnerCameraViewIndex(idleMotion)` returns 6 for a zero idle motion and 7 otherwise.

## Render ordering contract

`RenderOrderingTable` converts projected depth values into painter keys. Its model layer uses a depth shift of 12, has 4096 buckets, accepts only non-degenerate clockwise projected polygons, sorts larger depth keys before smaller ones, and resolves equal keys by later registration first. A quad receives one depth key before it expands to `[0, 1, 2, 2, 1, 3]` triangles.

For already shifted depths `z0…zn`, the triangle key is `((z0 + z1 + z2) * 0x555) >> 12` and the quad key is `((z0 + z1 + z2 + z3) * 0x400) >> 12`.
