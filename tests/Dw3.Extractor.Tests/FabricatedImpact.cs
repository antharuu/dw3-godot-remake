namespace Dw3.Extractor.Tests;

/// <summary>Supplies fabricated common contact archives for extraction tests without a disc.</summary>
internal static class FabricatedImpact
{
    /// <summary>Adds one static animation, its sprite sheet and a compressed texture page.</summary>
    /// <param name="disc">Synthetic disc listing.</param>
    /// <param name="executable">Executable whose engine file sectors are updated.</param>
    internal static void Add(FabricatedDisc disc, byte[] executable)
    {
        var animation = new byte[24];
        Fabricate.U16(animation, 4, 1);
        Fabricate.U16(animation, 6, 7);
        Fabricate.U16(animation, 14, 4096);
        Fabricate.U16(animation, 16, 4096);
        byte[][] entries = [.. Enumerable.Range(0, 39).Select(_ => new byte[4])];
        entries[2] = Fabricate.Archive([animation]);
        entries[38] = FabricatedSprites.Sheet([7])[4..];
        disc.Add("AAA/DAT/FIGHT/CONTACT.BIN", Fabricate.Archive(entries));
        disc.Add("AAA/DAT/FIGHT/CONTACTTM.BIN", Fabricate.Archive([FabricatedSprites.Rlen(FabricatedSprites.FourBitTim())]));
        Fabricate.FileSector(executable, 0x79D, disc.LbaOf("AAA/DAT/FIGHT/CONTACT.BIN"));
        Fabricate.FileSector(executable, 0x79E, disc.LbaOf("AAA/DAT/FIGHT/CONTACTTM.BIN"));
    }
}
