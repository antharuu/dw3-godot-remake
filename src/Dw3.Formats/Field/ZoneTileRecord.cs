namespace Dw3.Formats.Field;

/// <summary>Metadata for one tile in the zone terrain packet.</summary>
/// <param name="Tile">Zero-based tile index within the tile grid.</param>
/// <param name="Present">True when the tile has pixel data; false for absent (empty) tiles.</param>
/// <param name="Rlen">True when the raw packet was RLEN-compressed.</param>
/// <param name="DecodedBytes">Byte length of the decoded (decompressed) packet, or 0 for absent tiles.</param>
/// <param name="TimOffset">Byte offset of the TIM texture within the decoded packet, or 0 for absent tiles.</param>
/// <param name="TimWidth">TIM texture pixel width, or 0 for absent tiles.</param>
/// <param name="TimHeight">TIM texture pixel height, or 0 for absent tiles.</param>
/// <param name="Sprites">Ordered sprite records across all depth layers, or null for absent tiles.</param>
public sealed record ZoneTileRecord(int Tile, bool Present, bool Rlen = false, int DecodedBytes = 0, int TimOffset = 0, int TimWidth = 0, int TimHeight = 0, IReadOnlyList<ZoneSpriteRecord>? Sprites = null);
