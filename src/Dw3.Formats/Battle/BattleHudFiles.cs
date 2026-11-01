namespace Dw3.Formats.Battle;

/// <summary>
/// Disc file ids of the battle HUD, result-screen and menu sprite assets (EU disc). The ids index
/// the boot executable's file-sector table.
/// <list type="bullet">
///   <item>Sprite sheet (file 0x455, WFIGHTMN): SpriteDrawer parts + frame records.</item>
///   <item>Texture (file 0x456): cursor bar TIM and RLEN-compressed 4-bit HUD page.</item>
///   <item>Report sprite sheet (file 0x79F) and texture (file 0x7A0): STFGTREP report screen.</item>
///   <item>Menu sprite sheet (file 0x286, CMFONTCS) and its texture archive (file 0x287, CMFONTTM).</item>
/// </list>
/// </summary>
public static class BattleHudFiles
{
    // Engine file ids (EU disc).
    public const int HudSheetFileId = 0x455;   // FILE_BATTLE_MENU (WFIGHTMN)
    public const int HudImagesFileId = 0x456;  // FILE_BATTLE_IMAGES (cursor + texture)
    public const int ReportSheetFileId = 0x79F;   // STFGTREP sprites archive (entry 0 = sheet)
    public const int ReportTextureFileId = 0x7A0; // STFGTREP texture archive
    public const int MenuSheetFileId = 0x286;     // FILE_MENU_SPRITES (CMFONTCS)
    public const int MenuTextureFileId = 0x287;   // FILE_FONT (CMFONTTM)
}
