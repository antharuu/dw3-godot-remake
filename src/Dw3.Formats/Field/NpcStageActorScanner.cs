using System.Buffers.Binary;
using Dw3.Formats.Mips;

namespace Dw3.Formats.Field;

/// <summary>Recovers a stage actor pointer list from setup code and validates every record.</summary>
public static class NpcStageActorScanner
{
    /// <summary>Byte offset of the actor pointer-list field in the field state record the stage setup fills.</summary>
    public const short ActorListFieldOffset = 0x4C;

    private const int MaximumActors = 256;

    /// <summary>Returns the validated actor-entry addresses of the stage, or an empty list when setup stores none.</summary>
    public static IReadOnlyList<uint> FindActorAddresses(
        ReadOnlySpan<byte> overlay,
        uint overlayLoadBase,
        uint fieldStateAddress) =>
        FindActorList(overlay, overlayLoadBase, fieldStateAddress)?.Actors ?? [];

    /// <summary>
    /// Finds the unique structurally valid actor pointer list stored by the stage setup code into the
    /// field state record. Returns null when the setup never stores an actor list.
    /// </summary>
    /// <exception cref="FormatException">Stores exist but none, or several, point to a valid list.</exception>
    public static NpcStageActorList? FindActorList(
        ReadOnlySpan<byte> overlay,
        uint overlayLoadBase,
        uint fieldStateAddress,
        short actorListFieldOffset = ActorListFieldOffset)
    {
        uint[] stored = [.. MipsStoreScanner.FindStores(overlay, fieldStateAddress, actorListFieldOffset)
            .Where(store => !store.Value.IsLoadedWord)
            .Select(store => store.Value.ConstantValue)];
        var candidates = new List<NpcStageActorList>();
        foreach (uint pointer in stored.Distinct())
        {
            if (TryReadActorList(overlay, pointer, overlayLoadBase, out IReadOnlyList<uint>? actors))
                candidates.Add(new NpcStageActorList(pointer, actors));
        }

        return candidates.Count switch
        {
            0 when stored.Length == 0 => null,
            0 => throw new FormatException("Stage setup stores an actor-list pointer, but no candidate is structurally valid."),
            1 => candidates[0],
            _ => throw new FormatException($"Stage setup has {candidates.Count} structurally valid actor-list pointers."),
        };
    }

    private static bool TryReadActorList(
        ReadOnlySpan<byte> overlay,
        uint pointer,
        uint overlayLoadBase,
        out IReadOnlyList<uint> actors)
    {
        actors = [];
        if (pointer < overlayLoadBase)
            return false;
        int offset;
        try { offset = checked((int)(pointer - overlayLoadBase)); }
        catch (OverflowException) { return false; }
        var result = new List<uint>();
        try
        {
            for (int index = 0; index <= MaximumActors; index++)
            {
                int entryOffset = checked(offset + index * 4);
                if (entryOffset + 4 > overlay.Length)
                    return false;
                uint actorAddress = BinaryPrimitives.ReadUInt32LittleEndian(overlay.Slice(entryOffset, 4));
                if (actorAddress == 0)
                {
                    actors = result;
                    return true;
                }
                NpcActorEntry actor = NpcOverlayReader.ReadActorEntry(overlay, actorAddress, overlayLoadBase);
                _ = NpcOverlayReader.ReadConditionList(overlay, actor.ConditionsPtr, overlayLoadBase);
                result.Add(actorAddress);
            }
        }
        catch (FormatException)
        {
            return false;
        }
        return false;
    }
}

/// <summary>A stage actor pointer list: its own address and the null-terminated actor-entry addresses it holds.</summary>
public sealed record NpcStageActorList(uint Address, IReadOnlyList<uint> Actors);
