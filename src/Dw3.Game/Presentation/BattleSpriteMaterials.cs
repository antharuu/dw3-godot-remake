using Godot;

namespace Dw3Production.Presentation;

/// <summary>
/// Canvas materials reproducing the PS1 sprite blend rules on prepared layers, whose alpha marks
/// colour word zero (0, transparent), STP texels of a blended frame (128) and other texels (255).
/// A blended frame draws each part twice in place: its opaque texels, then its STP texels with the ABR equation.
/// </summary>
internal sealed class BattleSpriteMaterials : IDisposable
{
    private const string Opaque = "shader_type canvas_item; render_mode blend_mix; void fragment(){ vec4 t=texture(TEXTURE,UV); if(t.a<0.25) discard; COLOR=vec4(t.rgb,1.0); }";
    private const string NonStp = "shader_type canvas_item; render_mode blend_mix; void fragment(){ vec4 t=texture(TEXTURE,UV); if(t.a<0.75) discard; COLOR=vec4(t.rgb,1.0); }";

    private readonly ShaderMaterial _opaque = Create(Opaque);
    private readonly ShaderMaterial _nonStp = Create(NonStp);
    private readonly ShaderMaterial[] _stp =
    [
        Create(StpShader("blend_mix", "1.0", "0.5")),
        Create(StpShader("blend_add", "1.0", "1.0")),
        Create(StpShader("blend_sub", "1.0", "1.0")),
        Create(StpShader("blend_add", "0.25", "1.0")),
    ];

    /// <summary>Gets the single pass of an unblended (ABR -1) frame: every nonzero texel opaque.</summary>
    public ShaderMaterial OpaqueFrame => _opaque;

    /// <summary>Gets the first pass of a blended frame: its non-STP texels, opaque.</summary>
    public ShaderMaterial NonStpPass => _nonStp;

    /// <summary>Returns the second pass of a blended frame: its STP texels with the source equation.</summary>
    /// <param name="abr">0 half background plus half sprite, 1 add, 2 subtract, 3 add a quarter.</param>
    /// <returns>The pass material.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The mode is not a PS1 ABR mode.</exception>
    public ShaderMaterial StpPass(int abr) => abr is >= 0 and <= 3 ? _stp[abr] : throw new ArgumentOutOfRangeException(nameof(abr), abr, "Unknown PS1 blend mode.");

    /// <summary>Releases the materials and their shaders.</summary>
    public void Dispose()
    {
        foreach (ShaderMaterial material in _stp.Append(_opaque).Append(_nonStp))
        {
            material.Shader?.Dispose();
            material.Dispose();
        }
    }

    /// <summary>Builds the STP pass of one ABR mode: only alpha-128 texels, with the mode's blend equation.</summary>
    /// <param name="blend">Godot blend render mode.</param>
    /// <param name="factor">Sprite colour factor.</param>
    /// <param name="alpha">Source alpha used by the blend equation.</param>
    /// <returns>Godot shader source.</returns>
    private static string StpShader(string blend, string factor, string alpha) =>
        "shader_type canvas_item; render_mode " + blend + "; void fragment(){ vec4 t=texture(TEXTURE,UV); if(abs(t.a-0.501961)>=0.01) discard; COLOR=vec4(t.rgb*" + factor + "," + alpha + "); }";

    /// <summary>Compiles one canvas shader into a material.</summary>
    /// <param name="code">Godot shader source.</param>
    /// <returns>The material.</returns>
    private static ShaderMaterial Create(string code) => new() { Shader = new Shader { Code = code } };
}
