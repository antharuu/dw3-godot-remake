# Battle effect-model registry

`FIGHTSTG.PRO` contains the European battle effect-model registry at overlay offset
`0x1F8A0`. It is a sequence of twelve-byte rows and ends with an all-purpose row whose
little-endian 32-bit effect identifier is zero.

| Bytes | Meaning |
| --- | --- |
| `0..3` | little-endian effect identifier |
| `4..7` | model archive address |
| `8..11` | motion archive address |

An archive address is represented in the data pack as an engine-file number in its upper
sixteen bits and an archive entry in its lower sixteen bits. Repeated source rows are legal
only when the identifier and both addresses agree. The extractor rejects conflicting rows.

The extractor decodes the referenced shared battle model and motion archives with the normal
battle-model decoder. It writes the source archive coordinates, SHA-256 of each payload,
geometry, texture, bone and motion data under `battle/effects`, then indexes the result in
`battle_effect_models`. `dw3-battle-effect-model-v1` manifests are portable extraction
artifacts, not original game assets.

This layer deliberately does not define runtime attachment transforms, material behavior,
child lifetime or script playback. Those remain the effect consumer's responsibility.
