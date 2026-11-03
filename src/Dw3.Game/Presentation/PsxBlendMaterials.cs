using Godot;

namespace Dw3Production.Presentation;

/// <summary>
/// Creates and caches one canvas material per PS1 semi-transparency (ABR) mode, so every sprite drawn
/// with the same mode shares one shader. The materials live until <see cref="Dispose"/>.
/// </summary>
internal sealed class PsxBlendMaterials : IDisposable
{
    private readonly Dictionary<int, ShaderMaterial> _materials = [];

    /// <summary>Returns the cached material of an ABR mode, creating it on first use.</summary>
    /// <param name="abr">
    /// PS1 semi-transparency mode: 0 = half background plus half foreground, 1 = background plus
    /// foreground, 2 = background minus foreground, 3 = background plus a quarter of the foreground;
    /// any other value (the sprites use -1) draws the texture without blending.
    /// </param>
    /// <returns>The material for <paramref name="abr"/>; the same instance on every call with the same mode.</returns>
    public ShaderMaterial Get(int abr)
    {
        if (_materials.TryGetValue(abr, out var material))
        {
            return material;
        }

        var code = abr switch
        {
            0 => "shader_type canvas_item; void fragment(){ COLOR = texture(TEXTURE, UV); COLOR.a *= 0.5; }",
            1 => "shader_type canvas_item; render_mode blend_add; void fragment(){ COLOR = texture(TEXTURE, UV); }",
            2 => "shader_type canvas_item; render_mode blend_sub; void fragment(){ COLOR = texture(TEXTURE, UV); }",
            3 => "shader_type canvas_item; render_mode blend_add; void fragment(){ COLOR = texture(TEXTURE, UV); COLOR.a *= 0.25; }",
            _ => "shader_type canvas_item; void fragment(){ COLOR = texture(TEXTURE, UV); }",
        };
        material = new ShaderMaterial { Shader = new Shader { Code = code } };
        _materials.Add(abr, material);
        return material;
    }

    /// <summary>Releases every material created so far; later <see cref="Get"/> calls create new ones.</summary>
    public void Dispose()
    {
        foreach (var material in _materials.Values)
        {
            material.Dispose();
        }

        _materials.Clear();
    }
}
