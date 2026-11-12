# Battle action audio checkpoint

This is a physical-audio checkpoint, not completion of B9-061 or battle sound parity.

## Production route

`GameHost` passes its active pack directory to `BattleRenderer`. The renderer owns one
`BattleAudioRuntime`, registers it on its real `BattleRendererSequencePlayback`, and
retains the other asset-family consumer chain. The runtime lazily loads
`battle-action-sounds-v1` and `battle-audio-vab-v4` from the prepared pack. It never
reads a disc or references the Formats project.

The extractor reads the action-index table from the supported battle overlay and
writes the encoded identifiers as private pack data. Key-on identifiers are resolved
separately into bank, physical program slot, tone slot and note. VAB program slots
can be sparse. Tone rows are compact, and priority zero is a valid tone, not an
absence marker. The old v3 first-tone view is retained for compatibility diagnostics,
not used for production playback. Old audio packs require preparation again.

The Godot backend loads mono PCM16 WAVs, applies decoded static pan, program/bank/tone
gain, note/centre/fine pitch, and the decoded loop marker. `AudioStreamPlayer` feeds
the real engine mixer. Timed notes receive key-off after positive 60 Hz source units.
Zero-duration notes finish naturally or loop until interrupted. Script End does not
arbitrarily stop a trailing note. Battle hide, cancellation and disposal stop every
physical channel, including trailing notes, without constructing a replacement backend.

Pending acknowledgements use request reference identity. Separate equal-value sound
commands start separate voices. Sound substitution commands use the frozen technique
sound and frozen hit markers, without another rules or RNG evaluation.

## Offscreen evidence entry points

Always run the probes with `--headless --audio-driver Dummy`. They refuse any other
audio driver. They do not change devices, volumes or system routing.

- `BattleAudioProbe.tscn` uses the production pack/provider and renderer registration,
  records the actual engine master bus with `AudioEffectRecord`, and repeats playback
  after hide using the same renderer and backend. Set `DW3_DATA_DIR` to a prepared pack
  and `DW3_AUDIO_PROOF` to a private output directory. This is a renderer lifecycle
  test, not a complete GameHost two-battle gameplay acceptance.
- `BattleAudioBackendProbe.tscn` measures physical mono-to-stereo pan, gain, octave
  pitch, a loop marker, natural completion and positive-duration key-off. Set
  `DW3_AUDIO_SIGNAL` to a fabricated one-second 440 Hz mono PCM16 WAV and
  `DW3_AUDIO_PROOF` to a private output directory. Its output is engine-mixed PCM,
  not a copy of its input and not output fabricated by a test backend.

Unit tests cover request identity, cancellation after ordinary End, source-tick
lifetime, channel bounds, priority preemption, PCM pan and frozen substitutions.
Extractor tests exercise the production writer followed by the real action loader
with fabricated disc data, including an action index above the VAB program count.

## Real-action regression probe

`DW3_ACTION_AUDIO_RECORD=1` on the physical basic-attack probe retains the actual
renderer audio registration instead of the explicitly synchronization-only sound
acknowledgements. It requires the Dummy driver and records each action's master
bus into a private WAV. A silent recording fails rather than declaring playback.
`DW3_DATA_DIR` names the parent of the pack's `data` directory.

The first execution with the synchronization control produced peak zero and failed.
With that control removed, the real player attack currently fails at its first
sound command, index 10. The extracted catalog resolves it to a sequence identifier,
not a key-on. The enemy's basic attack and the target's hit/KO reaction also include
sequence identifiers. Thus the prior single key-on proof cannot establish complete
action or KO audio. These non-music sequence cues require a scoped technical
resolution of the existing sequence-playback ownership before this card can close.
No sequence was replaced by a key-on or acknowledged as rendered.

Frozen sound markers now distinguish terminal KO from ordinary landed hits and
preserve the special two-slot magic layout, including early lethal single-hit
resolution. Tests drive both substitution opcodes from those frozen results.

## Still required before B9-061 acceptance

- Qualify and exercise the actual change and KO audio producers, including reaction
  scripts and their timing, rather than replaying a manually chosen catalog entry.
- Complete frozen sound-marker qualification for special multi-hit/KO combinations.
- Qualify priority ties and allocation against the original envelope-sensitive voice
  allocator. The current bounded pool tests establish priority ordering and cleanup,
  not exact SPU envelope arbitration or ADSR parity.
- Exercise the complete GameHost exit, second combat and cancellation path, with
  recordings for attack, technique, change and KO. Renderer hide/reuse alone does not
  establish that acceptance criterion.
- SEP/music, including the special music/stop indices, remains owned by its separate
  playback work. No action index is silently reinterpreted as a VAB program or replaced
  with a fallback sample.

Container rate, PCM energy and successful engine playback do not establish an
original-hardware 1:1 mix, envelope, interpolation or PAL task/draw timing.
