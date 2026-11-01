namespace Dw3.Formats.Dialogue;

/// <summary>Contains the decoded panel texture and its most frequent opaque body colour.</summary>
public sealed record TalkBoxPanelImage(int Width, int Height, int Red, int Green, int Blue, int TexturePage, int Clut, byte[] Rgba);
