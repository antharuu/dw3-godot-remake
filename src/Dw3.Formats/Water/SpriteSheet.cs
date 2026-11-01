namespace Dw3.Formats.Water;

/// <summary>Holds one decoded TMPK sprite sheet with parts and frame byte ranges keyed by id.</summary>
public sealed record SpriteSheet(IReadOnlyList<SpritePart> Parts, IReadOnlyDictionary<int, ReadOnlyMemory<byte>> Frames);
