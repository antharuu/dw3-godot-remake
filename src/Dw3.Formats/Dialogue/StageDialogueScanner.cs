using Dw3.Formats.Field;
using Dw3.Formats.Mips;

namespace Dw3.Formats.Dialogue;

/// <summary>What a stage overlay's setup routine stores into the field state record for dialogue.</summary>
/// <param name="TextSelector">Immediate the setup adds to the language global to pick the stage talk file.</param>
/// <param name="ActorList">The validated actor pointer list, or null for a stage whose setup stores none.</param>
public sealed record StageDialogueSetup(int TextSelector, NpcStageActorList? ActorList);

/// <summary>
/// Recovers the dialogue setup of a WSTAG overlay from its code.
/// </summary>
/// <remarks>
/// The setup routine loads the language global, adds a stage-specific immediate and stores the sum
/// into the text-file member of the field state record. It also stores a materialized overlay pointer
/// into the actor-list member. The text selector is accepted only when every such store agrees on one
/// immediate. The actor list is accepted only when exactly one stored pointer leads to a structurally
/// valid null-terminated actor list.
/// </remarks>
public static class StageDialogueScanner
{
    /// <exception cref="MipsDecodeException">No text-file store, or stores with different selectors.</exception>
    /// <exception cref="FormatException">The actor-list store is ambiguous or points to no valid list.</exception>
    public static StageDialogueSetup Scan(ReadOnlySpan<byte> overlay, DialogueRomLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        int[] selectors = MipsStoreScanner
            .FindStores(overlay, layout.FieldStateAddress, layout.TextFileFieldOffset, [layout.LanguageAddress])
            .Where(store => store.Value.IsLoadedWord && store.Value.LoadAddress == layout.LanguageAddress)
            .Select(store => store.Value.Addend)
            .Distinct()
            .ToArray();
        if (selectors.Length != 1)
        {
            string found = selectors.Length == 0 ? "none" : string.Join(", ", selectors.Select(value => $"0x{value:X}"));
            throw new MipsDecodeException(
                $"Setup code must store exactly one language-relative text file into field state +0x{layout.TextFileFieldOffset:X}; found {found}.");
        }

        NpcStageActorList? actors = NpcStageActorScanner.FindActorList(
            overlay, layout.StageOverlayLoadAddress, layout.FieldStateAddress, layout.ActorListFieldOffset);
        return new StageDialogueSetup(selectors[0], actors);
    }
}
