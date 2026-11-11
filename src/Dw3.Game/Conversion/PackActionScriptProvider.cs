using Dw3.Data.Battle;
using Dw3Production.Logging;

namespace Dw3Production.Conversion;

/// <summary>Loads action scripts from the validated user data pack.
/// Returns null on cache miss or unsupported script so the caller can skip the sequence gracefully.</summary>
public sealed class PackActionScriptProvider : IActionScriptProvider
{
    private readonly BattleDataPackLoader _loader;

    /// <summary>Initializes a new instance of the <see cref="PackActionScriptProvider"/> class.</summary>
    /// <param name="loader">Open data-pack loader bound to the user's cache.</param>
    public PackActionScriptProvider(BattleDataPackLoader loader)
    {
        ArgumentNullException.ThrowIfNull(loader);
        _loader = loader;
    }

    /// <inheritdoc/>
    public BattleActionScript? TryLoad(int fighterId, int scriptSelector)
    {
        if (scriptSelector <= 0)
        {
            return null;
        }

        string path;
        try
        {
            path = _loader.ActionScriptPath(fighterId, scriptSelector);
        }
        catch (CacheResourceException error)
        {
            QuietLog.Info($"action-script-unavailable fighter={fighterId} sel={scriptSelector}: {error.Message}");
            return null;
        }

        try
        {
            return BattleActionScriptJsonParser.ParseFile(path);
        }
        catch (Exception error) when (error is IOException or FormatException)
        {
            QuietLog.Info($"action-script-invalid fighter={fighterId} sel={scriptSelector}: {error.Message}");
            return null;
        }
    }
}
