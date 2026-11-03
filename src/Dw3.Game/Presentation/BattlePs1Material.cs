using Godot;

namespace Dw3Production.Presentation;

/// <summary>
/// Builds the unshaded shader materials that reproduce PS1 textured-primitive colour modulation
/// for battle models and stages.
/// </summary>
/// <remarks>
/// The PS1 modulates a sampled texel by vertex RGB/128, so a vertex value of 0x80 leaves the texel
/// unchanged and values up to 0xFF brighten it. Godot vertex colours are clamped to 0..1, so the
/// shader receives RGB/255 and multiplies by two after interpolation.
/// A spatial shader that writes ALPHA moves to Godot's transparent pass, which neither writes depth
/// nor sorts per polygon. Non-ABR primitives are opaque on the PS1 (semi-transparency needs the ABR
/// command bit, texel 0x0000 alone is skipped), so their shader only discards cut-out texels and
/// keeps the depth buffer. Only ABR surfaces write ALPHA.
/// </remarks>
internal static class BattlePs1Material
{
    /// <summary>Creates a material sampling <paramref name="texture"/> with nearest filtering.</summary>
    /// <param name="texture">Decoded 256x256 texture page.</param>
    /// <param name="alphaScissor">True for an opaque primitive (cut-out texels discarded), false for an ABR one.</param>
    /// <param name="cullBack">True to cull back faces, false to draw both sides.</param>
    /// <returns>The configured material.</returns>
    public static ShaderMaterial Create(Texture2D texture, bool alphaScissor, bool cullBack)
    {
        var material = new ShaderMaterial { Shader = CreateShader(cullBack, opaque: alphaScissor) };
        material.SetShaderParameter("tex", texture);
        material.SetShaderParameter("alpha_scissor", alphaScissor);
        return material;
    }

    /// <summary>Creates the shader for one opacity and culling combination.</summary>
    /// <param name="cullBack">True to cull back faces, false to draw both sides.</param>
    /// <param name="opaque">True for the depth-writing cut-out shader, false for the ALPHA-writing one.</param>
    /// <returns>A new shader instance.</returns>
    public static Shader CreateShader(bool cullBack, bool opaque)
    {
        string cull = cullBack ? "cull_back" : "cull_disabled";
        string code = opaque
            ? $@"shader_type spatial;
render_mode unshaded, depth_draw_opaque, {cull};
uniform sampler2D tex : source_color, filter_nearest;
uniform bool alpha_scissor = true;
void fragment() {{
    vec4 sampled = texture(tex, UV);
    if (sampled.a < 0.25) discard;
    ALBEDO = sampled.rgb * COLOR.rgb * 2.0;
}}"
            : $@"shader_type spatial;
render_mode unshaded, {cull};
uniform sampler2D tex : source_color, filter_nearest;
uniform bool alpha_scissor = false;
void fragment() {{
    vec4 sampled = texture(tex, UV);
    ALBEDO = sampled.rgb * COLOR.rgb * 2.0;
    ALPHA = sampled.a;
}}";
        return new Shader { Code = code };
    }

    /// <summary>Tells whether a material is drawn in Godot's opaque, depth-writing pass.</summary>
    /// <param name="material">A material created by <see cref="Create"/>.</param>
    /// <returns>True when its shader never writes ALPHA.</returns>
    public static bool IsOpaque(ShaderMaterial material) =>
        !material.Shader.Code.Contains("ALPHA =", StringComparison.Ordinal);
}
