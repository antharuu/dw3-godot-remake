using System.Text.Json;

namespace Dw3.Data.Battle;

/// <summary>A validated sprite-domain row, distinct from an absent or unsupported source.</summary>
/// <param name="Id">Script-visible sprite identity.</param>
/// <param name="Status">Available, unsupported, or explicitly qualified source-absent.</param>
/// <param name="Payload">Versioned image, palette, provenance and animation contract.</param>
public sealed record PreparedSpriteEffect(int Id, string Status, JsonElement Payload);
