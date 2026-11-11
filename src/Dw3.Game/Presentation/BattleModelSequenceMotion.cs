namespace Dw3Production.Presentation;

/// <summary>Extension methods for applying action-sequence motion commands to <see cref="BattleModel"/> instances.
/// Lives outside <see cref="BattleRenderer"/> so the switch logic is counted in this class's budget.</summary>
internal static class BattleModelSequenceMotion
{
    /// <summary>Applies one motion or visibility subcommand from an action-sequence event to the model.</summary>
    /// <param name="model">The target fighter model.</param>
    /// <param name="subcommand">Script subcommand: 0 = motion, 1 = show, 2 = hide.</param>
    /// <param name="motion">Motion index; only used when <paramref name="subcommand"/> is 0.</param>
    public static void ApplySubcommand(this BattleModel model, int subcommand, int motion)
    {
        switch (subcommand)
        {
            case 0:
                if (motion > 0)
                {
                    int selected = motion == 1 ? (model.Motion?.IdleMotion ?? 0) + 1 : motion + 1;
                    model.SetMotion(selected, restart: motion != 1);
                }

                break;
            case 1:
                model.SetVisibleForProbe(true);
                break;
            case 2:
                model.SetVisibleForProbe(false);
                break;
            default:
                break;
        }
    }
}
