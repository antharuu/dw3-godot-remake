# Battle action audio

## Production boundary

`GameHost` passes the active prepared-pack directory to `BattleRenderer`. Its single
`BattleAudioRuntime` registers on the production sequence playback, preserves the
other asset-family consumers, and retains the same physical backend across battles.
The game reads prepared JSON and PCM WAV files only, never an ISO or MP archive.

Preparation writes the complete VAB tone catalog, PCM samples, action-index catalog,
`battle-audio-sequences-v1` events and `battle-entrance-audio-v1`. Both SEP members of
the common archive are prepared, not just its first sixteen tracks. Old audio packs
must be prepared again to supply the sequence and entrance projections.

Encoded identifiers are not VAB program indices. Key-ons resolve their explicit
bank, sparse physical program, tone and note. Sequence cues select their authored
SEP slot. The supported common bank has two consecutive sixteen-slot groups. A
slot above fifteen addresses the following group, retaining the original contiguous
slot identity rather than truncating it or substituting a sample. Prepared rows keep
both the archive-group and local track numbers. MIDI notes select all declared VAB
tones whose key ranges contain the note, with the original program and velocity.

The shared cue consumer supports program changes, note-on, zero-velocity note-off,
explicit note-off, tempo changes and end-of-track. Unsupported required channel or
system events fail explicitly before scheduling. The music and reverb controls are
described below. No unknown event is silently acknowledged, and no sequence is
replaced with a WAV.

Sample paths, PCM format and loop bounds are prepared before a sequence is
acknowledged. Validated mono streams are cached by the renderer-owned backend and
copied for each physical voice's stereo pan. Request reference identity distinguishes
a Pending retry from a separate same-sound occurrence. Ordinary script End releases
acknowledgement identities without cutting trailing audio. Cancellation and battle
exit stop both scheduled events and physical channels, including release tails.

## Physical voices and timing

`AudioStreamPlayer` feeds the engine mixer with decoded gain, pan, note/centre/fine
pitch, loop enable and loop start. Positive key-on deadlines begin release once.
Natural sample completion retires one-shots. An envelope reaching silence retires
an otherwise looping note. Cancellation and priority preemption are hard stops.

Decoded ADSR1/ADSR2 feed an integer 44.1 kHz envelope accumulator. Physical gain and
allocation observations are updated on the presentation/source-clock boundary.
Allocation uses a free channel first, then lower priority, lower current envelope
at equal priority, and oldest allocation at an equal envelope. A lower-priority
request cannot steal a protected channel. The pool has at most 24 physical voices.
The documented SPU rate contract is described at
[psx-spx](https://psx-spx.consoledev.net/soundprocessingunitspu/).

The entrance cue is extracted from the supported overlay's actual direct sound
call operands, not appended as an arbitrary action-catalog index. Initial entrance
and a confirmed normal partner change arm it after model readiness. The authored
camera lead-in is sixty nominal 60 Hz units. The production incoming-model path
fires it once and teardown disarms an unstarted entrance.

Audio pumps settle the elapsed interval before starting a newly prepared occurrence.
Otherwise synchronous loading before a request can be counted as time elapsed after
its start, causing a short sequence to key off before its first mixer block. An
isolated physical RED reproduced this silence and the corrected probe records the
sequence alone, without an entrance or another action masking it.

## Battle music, victory track and reverb

`GameHost` starts the encounter's source music identity when a battle opens and the
victory track `6004001E` once from the production outcome resolution. Both carry an
authored endless loop and use `BattleMusicPlayback`: the track plays to its loop end,
then jumps back to its loop start for ever. Battle exit and cancellation stop both.

SEP NRPN controllers follow libsnd semantics, scoped to the sequence rather than a
MIDI channel. CC99 20 marks the loop start, whose repeat count is the next data entry
(127 is endless), and CC99 30 marks the loop end. CC99 0 to 16 selects a tone (16 for
all), CC98 selects an attribute and CC6 applies it. Attribute 15 is the global reverb
type and 16 its depth. Any other attribute, a finite loop count, echo and delay
presets or an incomplete selection is refused, never ignored. Channel pitch bends start
at the MIDI centre and keep their state across a loop jump, as libsnd does.

Reverb is SPU-global. A voice uses it when its VAB tone mode has bit 4, which libsnd
copies into the SPU reverb-voice mask at key-on. The prepared manifest
`battle-audio-vab-v6` carries that mode byte, so older packs must be prepared again.
The backend routes such voices through a private bus with a native reverb feeding the
master bus. The initial state is the game's start-up state, Studio B with depth zero.
Depth maps linearly to the wet level, as libsnd scales 0-127 linearly to the SPU depth
register, and type 0 disables the effect. The global state survives battle exit so a
tail drains naturally.

The native reverb is an approximation, not the SPU reverb program. Its room size
follows the documented SPU work-area size of each preset relative to Space Echo. SPU
filter coefficients, the 22.05 kHz reverb rate, exact tail shape and calibrated gain
are not reproduced or claimed.

## Offscreen evidence entry points

All audio probes require `--audio-driver Dummy`. They do not change host devices,
volume or routing. `DW3_DATA_DIR` is the parent of the prepared pack's `data` folder.
Recordings and extracted resources belong outside the repository.

- `BattleAudioProbe.tscn`, with `DW3_AUDIO_PROOF`, exercises the real provider, script,
  renderer registration and mixer. `DW3_AUDIO_FIGHTER` and `DW3_AUDIO_SELECTOR` select
  an existing script, defaulting to 26/5. The first actual sound command is played
  twice on the same backend. Nonempty, all-zero post-hide recordings are required.
  The action, enemy and reaction scripts 383/5, 51/5 and 51/3 qualify the three
  previously unavailable short-sequence cues independently.
- `BattleAudioBackendProbe.tscn`, with `DW3_AUDIO_SIGNAL` and `DW3_AUDIO_PROOF`, measures
  natural completion, octave/half gain/left pan, right pan/loop marker and timed
  key-off using a nonzero fabricated PCM signal. A fifth case fills all 24 real
  channels, requires eviction of the quieter non-oldest equal-priority envelope,
  rejects lower-priority interruption and saves the engine mix.
- `DW3_ACTION_AUDIO_RECORD=1` on the physical basic-attack probe retains production
  audio rather than its synchronization-only control. Player/enemy attacks, misses,
  repetition, KO with a reserve, final victory and final defeat save actual mixes.
- The physical technique probe accepts the same recording option and retains its
  production consumer. The normal 388/123 case records technique, enemy reply and
  repeated technique without replaying resolution RNG.
- `DW3_BATTLE_INPUT_PROBE=1 DW3_BATTLE_AUDIO_LIFECYCLE=1
  DW3_ACTION_AUDIO_RECORD=1` records actual GameHost entrance, a normal change through
  all three input-driven menu pages, host exit, a second combat on the same renderer
  and backend, and cancellation of the retained production action runner while a
  physical voice is active. It requires nonzero useful recordings and nonempty
  silent recordings after exit, cancellation and final exit. The probe invokes the
  production host exit entry directly, not a renderer-hide replacement.

- `BattleReverbProbe.tscn`, with `DW3_AUDIO_PROOF`, drives a fabricated 50 ms tone and
  authored reverb commands through the production sequence consumer and backend. A dry
  voice, depth zero and type off must leave no tail, depths 56 and 127 growing tails,
  a live depth change must silence a ringing tail and the final mix must be silent.
- `DW3_BATTLE_INPUT_PROBE=1 DW3_BATTLE_AUDIO_MUSIC=1 DW3_ACTION_AUDIO_RECORD=1` records
  the F8 encounter music through two authored loop boundaries with a post-boundary
  note and its reverb, the production victory exactly once, field return, re-entry and
  a cancelled second battle, with silent mixes after each drained tail.

Synthetic unit tests cover writer-to-loader identity, MIDI running status and tempo,
malformed events, entrance deadline/cancellation, distinct occurrences, Pending
retries, source-tick duration, natural completion, envelope-sensitive priority ties,
all-ones release rate and cleanup after ordinary End. No disc is needed by tests.

## Qualification limits

The evidence establishes non-music action-cue consumption and the measured lifecycle,
not original-hardware mix parity or human acceptance of combat fidelity. Godot performs the
sample interpolation and mixing. ADSR gain application is presentation-boundary
quantized rather than an emulation of every SPU output sample. SPU reverb parity, hardware
interpolation, reserved-channel policies from unrelated modes, complete MIDI music
control and exact PAL task/draw scheduling are not inferred from these recordings.
The existing visual entrance/flash choreography has its own fidelity boundary.
