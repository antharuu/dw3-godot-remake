using System.Text.Json.Nodes;

namespace Dw3.Game.Tests;

/// <summary>
/// Compares fields of a replayed state with the expected values stored in one object of a vector
/// file, and records one readable line per mismatch instead of stopping at the first.
/// </summary>
internal sealed class VectorFieldCheck(JsonNode expected, string tag, List<string> errors)
{
    public void Expect<T>(string field, string key, T actual)
        where T : IEquatable<T>
    {
        var value = expected[key]!.GetValue<T>();
        if (!actual.Equals(value))
        {
            errors.Add($"{tag} {field}: got {actual} expected {value}");
        }
    }

    // Floats are compared bit for bit: the vectors pin the exact single-precision result.
    public void ExpectBits(string field, string key, float actual)
    {
        var value = expected[key]!.GetValue<float>();
        if (BitConverter.SingleToInt32Bits(actual) != BitConverter.SingleToInt32Bits(value))
        {
            errors.Add($"{tag} {field}: got {actual} expected {value}");
        }
    }
}
