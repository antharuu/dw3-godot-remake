# Battle scene diagnostics

These diagnostics require a prepared local data pack and a graphical renderer. On a
headless machine use Xvfb. Keep all captures and source vectors outside the checkout.
Build the exact source tree before running its `run.sh`.

## Native action boundaries

Set `DW3_BATTLE_ACTION_PROBE=1`, `DW3_CAPTURE_DIR` and `DW3_RNG_SEED`.
`DW3_BATTLE_ACTION_ENCOUNTER` selects an unmodified encounter from the local pack.
Choose one that survives both attacks and allows both partners to survive the switch
and enemy responses. Without this option the probe uses the F8 encounter, which may
end too early to satisfy the round-trip contract. `DW3_BATTLE_STAGE` selects the arena
document in the same way as the other battle probes.

The probe navigates native keyboard input through the production input adapter and
fixed simulation loop. It captures Attack at the root, the immediate accepted-input
tick before any release tick, and the subsequent root command turn. It repeats this
for the next party member through the production switch menus. The log counts every
simulation tick, including navigation, separately from the diagnostic host clock.
A spent scheduled turn is required. Opening a submenu is not an action.
`DW3_BATTLE_ACTION_MENU_ONLY=1` deliberately opens and cancels Technique and must exit 3.

Stage proof follows the actions. `DW3_BATTLE_ACTION_ANIMATED_STAGE` must identify an
actually animated arena in the local registry. The active arena and that registry
arena use the same normal presentation scheduler. Initial pose publication and
lighting complete before measuring effective bone transforms and framebuffer pixels.
The measurement removes actors and hides the HUD, freezes the camera and compares
geometric channel values rather than timestamps. A static stage must remain static.
The animated registry stage must change both transforms and rendered pixels. The
stage proof never calls a pose override. Removing production stage stepping must fail.

## Focused hidden-parent Canvas contribution

Use the existing model probe with `DW3_BATTLE_MODEL_PROBE=1` and
`DW3_BATTLE_HIDDEN_PARENT_ONLY=1`. `DW3_BATTLE_SKELETON_VECTORS` points to a private JSON
object with `parent_hidden_child_visible_cases`. Its first element supplies integer
`fighter`, `frame`, `parent` and `child` fields. No source vector is distributed.

The source pose must establish a locally hidden parent and visible child. At zero
elapsed presentation time, the current Canvas renderer captures the child visible,
suppressed and restored. Only the child's local visibility changes. A nonempty
framebuffer difference and an identical restored control are required.
`DW3_BATTLE_HIDDEN_PARENT_REJECT=1` suppresses the child in both images and must exit 3.
This is contribution proof for the selected source vector, not a whole-model audit.

Success exits 0. Failed assertions exit 3. Neither probe claims source action timing,
PSX trajectories, raster parity or complete fighter framing.
