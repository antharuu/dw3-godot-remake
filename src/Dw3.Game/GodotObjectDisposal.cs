using Godot;

namespace Dw3Production;

/// <summary>
/// Helpers for deterministically freeing Godot <see cref="GodotObject"/>-derived resources
/// (RefCounted objects such as textures, style boxes, and images) before engine teardown.
/// </summary>
/// <remarks>
/// Godot's C# interop logs "Leaked unsafe reference" and aborts (exit 134) when a managed
/// wrapper is finalized after its native object has already been freed.  Calling these helpers
/// from <c>_ExitTree()</c> or probe cleanup ensures every wrapper is explicitly disposed while
/// the engine is still running.
/// </remarks>
internal static class GodotObjectDisposal
{
    /// <summary>
    /// Disposes every value in <paramref name="textures"/> and clears the dictionary.
    /// Does nothing when <paramref name="textures"/> is <see langword="null"/>.
    /// </summary>
    /// <param name="textures">Dictionary whose values must be freed; may be null.</param>
    public static void DisposeValues(Dictionary<int, Texture2D>? textures)
    {
        if (textures is null)
        {
            return;
        }

        foreach (var texture in textures.Values)
        {
            texture.Dispose();
        }
    }
}
