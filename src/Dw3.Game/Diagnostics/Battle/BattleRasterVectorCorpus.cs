using System.Text.Json;
using Dw3Production.Diagnostics.Field;

namespace Dw3Production.Diagnostics.Battle;

/// <summary>Prevents an empty or incomplete real-vector set from masquerading as an overlap proof.</summary>
internal static class BattleRasterVectorCorpus
{
    /// <summary>Requires the observed additive and half fighters plus opaque controls in both views.</summary>
    /// <param name="vectors">Private externally selected input vectors.</param>
    public static void Validate(JsonElement vectors)
    {
        foreach (var vector in vectors.EnumerateArray())
        {
            ProbeRequirement.Require(vector.GetProperty("configuration").GetString() is "additive" or "half", "unknown real-vector configuration");
        }

        var signatures = vectors.EnumerateArray().Select(Signature).ToHashSet(StringComparer.Ordinal);
        foreach (var view in new[] { "main", "raw" })
        {
            ProbeRequirement.Require(signatures.Contains($"{view}:374:1:128"), $"real corpus lacks additive374 in {view}");
            ProbeRequirement.Require(signatures.Contains($"{view}:51:0:128"), $"real corpus lacks half51 in {view}");
            ProbeRequirement.Require(signatures.Contains($"{view}:374:-1:255"), $"real corpus lacks opaque374 control in {view}");
        }

        ProbeRequirement.Require(signatures.Contains("main:51:0:0"), "real corpus lacks observed transparent51 control");
    }

    /// <summary>Builds a required-coverage signature from input identity, mode and raw alpha classification.</summary>
    /// <param name="vector">One externally selected coordinate and source identity.</param>
    /// <returns>View, source fighter, runtime ABR and alpha classification.</returns>
    private static string Signature(JsonElement vector) =>
        $"{vector.GetProperty("view").GetString()}:{vector.GetProperty("fighter").GetInt32()}:{vector.GetProperty("runtimeAbr").GetInt32()}:{vector.GetProperty("texel")[3].GetInt32()}";
}
