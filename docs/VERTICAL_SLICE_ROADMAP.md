# Exploration roadmap

## Scope

The exploration work recreates field navigation before broader story and service systems. It covers authored zones, character and environmental animation, collision, warp transitions, climbing, ledge drops, and the F3 diagnostic overlay.

The public runtime receives all required field data through the private data pack. It does not read a disc image while a game session is running.

## Field requirements

- Decode layers, map objects, floor data, trigger data, exits, and visible actors from the supported disc.
- Preserve source-derived positions, ordering, animation timing, and collision data where supported.
- Keep occupancy separate from map collision.
- Validate destination resources before committing a warp.
- Reject unavailable or unsupported records explicitly.
- Use the same simulation data for collision, warps, and F3 diagnostics.

## Transition behavior

A warp validates its trigger and destination, locks input while loading, commits the destination once, releases input, and requires leaving the trigger before it can fire again. A missing destination preserves the current valid scene and reports an actionable resource error.

## Presentation and controls

The default field presentation targets 16:9 without stretching source pixels or inventing map scenery. Simulation timing is independent of rendering frequency. Keyboard, mouse, and native gamepad input use shared semantic actions. Focus loss and device changes clear held input safely.

## Validation

Field changes require synthetic unit coverage plus targeted headless checks when they affect rendering or runtime behavior. A visual capture proves pixels only. Collision, warps, timing, and state changes require simulation assertions or traces.

## Out of scope

This roadmap does not by itself complete story progression, save handling, service menus, quests, cinematics, audio, or every game region. Those systems are added only when their data and runtime contracts are ready.

See [design/ARCHITECTURE.md](design/ARCHITECTURE.md), [design/ASSET_PIPELINE.md](design/ASSET_PIPELINE.md), and [design/VALIDATION.md](design/VALIDATION.md).