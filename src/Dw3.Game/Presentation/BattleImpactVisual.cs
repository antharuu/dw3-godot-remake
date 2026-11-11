using System.Text.Json;
using Godot;

namespace Dw3Production.Presentation;

/// <summary>Draws the decoded additive common impact through its authored sprite channels.</summary>
internal sealed partial class BattleImpactVisual : Node2D
{
    private readonly List<short[][]> _animations = [];
    private readonly Dictionary<(int Frame, int Row), (Texture2D Texture, Vector2 Offset)> _frames = [];
    private int _time;
    private bool _started;

    /// <summary>Initializes a new instance of the <see cref="BattleImpactVisual"/> class from extracted impact data.</summary>
    /// <param name="manifest">Validated manifest path.</param>
    public BattleImpactVisual(string manifest)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(manifest));
        var root = document.RootElement;
        if (root.GetProperty("schema").GetString() != "battle-impact-v1")
        {
            throw new FormatException("Unsupported impact manifest.");
        }

        foreach (var animation in root.GetProperty("animations").EnumerateArray())
        {
            _animations.Add(ReadAnimation(animation));
        }

        foreach (var frame in root.GetProperty("frames").EnumerateArray())
        {
            LoadFrame(manifest, frame);
        }

        if (_animations.Count == 0 || _animations.SelectMany(animation => animation).Any(sample => !_frames.ContainsKey((sample[0], sample[1]))))
        {
            throw new FormatException("Impact manifest has missing referenced frames.");
        }

        Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };
        TextureFilter = TextureFilterEnum.Nearest;
    }

    /// <summary>Gets a value indicating whether every authored sprite child has finished.</summary>
    public bool IsComplete => _animations.All(animation => _time >= animation.Length);

    /// <inheritdoc/>
    public override void _Draw()
    {
        foreach (var animation in _animations.Where(animation => _time < animation.Length))
        {
            short[] sample = animation[_time];
            var frame = _frames[(sample[0], sample[1])];
            DrawSetTransform(new Vector2(sample[2], sample[3]), sample[8] * Mathf.Tau / 4096f, new Vector2(sample[4], sample[5]) / 4096f);
            DrawTexture(frame.Texture, frame.Offset);
        }

        DrawSetTransform(Vector2.Zero);
    }

    /// <summary>Advances one source-time sample and removes completed retained geometry.</summary>
    public void Step()
    {
        _time += _started ? 1 : 0;
        _started = true;
        QueueRedraw();
    }

    /// <summary>Validates and expands the manifest samples before drawing.</summary>
    /// <param name="animation">One child animation.</param>
    /// <returns>Nine-channel source-time samples.</returns>
    private static short[][] ReadAnimation(JsonElement animation)
    {
        var samples = animation.EnumerateArray().Select(sample => sample.EnumerateArray().Select(value => value.GetInt16()).ToArray()).ToArray();
        if (samples.Length == 0 || samples.Any(sample => sample.Length != 9 || sample[6] != 0 || sample[7] != 0))
        {
            throw new FormatException("Invalid or unsupported impact animation channels.");
        }

        return samples;
    }

    /// <summary>Loads one confined decoded PNG and its original sheet pivot.</summary>
    /// <param name="manifest">Manifest location.</param>
    /// <param name="frame">Frame metadata.</param>
    private void LoadFrame(string manifest, JsonElement frame)
    {
        string name = frame.GetProperty("path").GetString() ?? string.Empty;
        if (name != "battle/impact/" + Path.GetFileName(name) || frame.GetProperty("abr").GetInt32() != 1)
        {
            throw new FormatException("Invalid impact path or unsupported blend mode.");
        }

        string path = Path.Combine(Path.GetDirectoryName(manifest) ?? throw new FormatException("Impact manifest has no directory."), Path.GetFileName(name));
        using var image = Image.LoadFromFile(path);
        if (image.IsEmpty())
        {
            throw new FormatException("Impact image is empty.");
        }

        var offset = new Vector2(frame.GetProperty("x").GetInt32(), frame.GetProperty("y").GetInt32());
        _frames.Add((frame.GetProperty("frame").GetInt32(), frame.GetProperty("row").GetInt32()), (ImageTexture.CreateFromImage(image), offset));
    }
}
