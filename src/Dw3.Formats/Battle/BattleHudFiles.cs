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
    /// <summary>Disc file id of the main battle HUD sprite sheet (WFIGHTMN); contains SpriteDrawer frame records and the cursor bar.</summary>
    public const int HudSheetFileId = 0x455;   // FILE_BATTLE_MENU (WFIGHTMN)

    /// <summary>Disc file id of the HUD texture archive; holds the cursor-bar TIM and the RLEN-compressed 4-bit HUD texture page.</summary>
    public const int HudImagesFileId = 0x456;  // FILE_BATTLE_IMAGES (cursor + texture)

    /// <summary>Disc file id of the STFGTREP result-screen sprite sheet (entry 0 = frame records).</summary>
    public const int ReportSheetFileId = 0x79F;   // STFGTREP sprites archive (entry 0 = sheet)

    /// <summary>Disc file id of the STFGTREP result-screen texture archive.</summary>
    public const int ReportTextureFileId = 0x7A0; // STFGTREP texture archive

    /// <summary>Disc file id of the menu sprite sheet (CMFONTCS); the arrow frame (id 10) is borrowed by the battle report.</summary>
    public const int MenuSheetFileId = 0x286;     // FILE_MENU_SPRITES (CMFONTCS)

    /// <summary>Disc file id of the menu texture archive (CMFONTTM); provides the arrow palette used in the battle report.</summary>
    public const int MenuTextureFileId = 0x287;   // FILE_FONT (CMFONTTM)
}
