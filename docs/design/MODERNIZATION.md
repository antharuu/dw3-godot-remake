# Modernization goals

DW3 Remake keeps the original game as a behavioral reference while making selected quality-of-life improvements explicit.

## Presentation

The primary presentation target is 16:9. A 4:3 option remains a future compatibility goal. Field art keeps its proportions. Narrow maps use a documented camera and margin policy instead of stretched art or invented scenery.

## Simulation and rendering

Gameplay is designed for a fixed simulation cadence independent of rendering frequency. Animation sampling and presentation interpolation must not change collision, movement, event ordering, or battle results. A renderer stall must not create unbounded catch-up behavior.

## Diagnostics

The F3 overlay is a runtime diagnostic view. It draws collision, occupancy, trigger, and transition information from the same simulation data used by gameplay. It must not alter movement, warps, or state.

Logs report lifecycle events and actionable failures without per-frame spam. Repeated messages are bounded while preserving the first occurrence and its recurrence count.

## Input

Keyboard, mouse, and native gamepad support share semantic input actions. Held movement, one-shot interactions, focus changes, and device disconnection are handled separately so input state cannot stick or replay after a transition.

## Fast-forward

Dialogue and cinematic acceleration is isolated from exploration and battle simulation. It may shorten presentation waits only when all required state changes still occur exactly once. Its precise interaction policy remains subject to implementation and testing.

## Fidelity rule

A modernization must state what it changes and why. It must not be presented as an original behavior unless that behavior is independently verified. Missing data is reported as missing, not hidden by a visual or gameplay substitute.