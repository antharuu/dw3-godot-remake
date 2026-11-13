using Dw3Production.Simulation;
using BattleEnemy = Dw3Production.Simulation.BattleEnemy;

namespace Dw3Production.Diagnostics.Battle;

/// <summary>SYNTHETIC controlled preconditions for a native host session. They seed data only, never a result:
/// every command, effect, scheduler event and expiry still runs through the production host and flow.</summary>
/// <param name="Arrange">Mutation of the fabricated setup applied before fighters are initialized.</param>
/// <param name="RngScript">Values returned first by the host's battle stream, in order, before its constant tail.</param>
internal sealed record NativeMenuFixture(Action<BattleSetup>? Arrange = null, IReadOnlyList<int>? RngScript = null)
{
    /// <summary>Returns the fabricated active partner seed of a native setup.</summary>
    /// <param name="setup">Fabricated setup before fighter initialization.</param>
    /// <returns>The first partner seed.</returns>
    public static PartnerSeed Partner(BattleSetup setup) => setup.Partners[0] ?? throw new InvalidOperationException("Native fixture partner seed missing.");

    /// <summary>Returns the fabricated active enemy of a native setup.</summary>
    /// <param name="setup">Fabricated setup before fighter initialization.</param>
    /// <returns>The first enemy record.</returns>
    public static BattleEnemy Enemy(BattleSetup setup) => setup.Enemies[0] ?? throw new InvalidOperationException("Native fixture enemy missing.");
}
