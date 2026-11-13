using System.Text.Json;
using Dw3.Data;
using Dw3.Data.Battle;

namespace Dw3.Extractor;

/// <summary>Exercises the production prepared-domain reader without opening the source disc.</summary>
internal static class SpriteEffectValidationCommand
{
    /// <summary>Validates an extracted sprite domain and reports exact identity/status sets.</summary>
    /// <param name="arguments">Command name followed by the prepared pack root.</param>
    /// <param name="output">Machine-readable per-effect qualification.</param>
    /// <param name="error">Validation failures.</param>
    /// <returns>Zero for a valid index, two for usage, one for malformed or missing data.</returns>
    internal static int Run(IReadOnlyList<string> arguments, TextWriter output, TextWriter error)
    {
        if (arguments.Count != 2)
        {
            error.WriteLine("Usage: Dw3.Extractor verify-sprites <prepared-pack-root>");
            return 2;
        }

        try
        {
            var index = SpriteEffectDomainReader.Load(new DataPackReader(arguments[1]));
            var scripts = SpriteEffectScriptReader.Load(new DataPackReader(arguments[1]));
            output.WriteLine(JsonSerializer.Serialize(new { sprites = index.Values, scripts }));
            return 0;
        }
        catch (Exception ex) when (ex is DataPackException or InvalidDataException or IOException or JsonException or KeyNotFoundException or InvalidOperationException or Microsoft.Data.Sqlite.SqliteException)
        {
            error.WriteLine($"Sprite validation error: {ex.Message}");
            return 1;
        }
    }
}
