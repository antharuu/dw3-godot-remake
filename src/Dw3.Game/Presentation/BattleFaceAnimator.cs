using System.Text.Json;
using Godot;

namespace Dw3Production.Presentation;

/// <summary>
/// Animates a battle model's face by copying small rectangles between areas of its texture pages,
/// the equivalent of the PS1 VRAM-to-VRAM move. Every decoded tpage/CLUT keeps its own working
/// page, copied from an immutable source page, so palette variants keep their exact pixels.
/// </summary>
/// <remarks>
/// Parts 0 and 1 are the eyes: closed (frame 2) during motions 4..11, half closed (frame 1) during
/// motion 2, otherwise driven by a blink timer re-armed to 60..187 PAL frames. Other parts cycle
/// their three frames every 6 PAL frames.
/// </remarks>
internal sealed class BattleFaceAnimator
{
    private const int PageSize = 256;
    private const int FrameCount = 3;

    private readonly Dictionary<int, BattleFaceTexture> _textures;
    private readonly List<BattleFacePart> _parts = [];
    private Func<int> _random = () => Random.Shared.Next();
    private int _blinkTimer;
    private int _time;
    private int _eyes;
    private int _cycle;

    /// <summary>
    /// Initializes a new instance of the <see cref="BattleFaceAnimator"/> class.
    /// </summary>
    /// <param name="textures">Every texture page of the model by key; each one is patched.</param>
    public BattleFaceAnimator(Dictionary<int, BattleFaceTexture> textures)
    {
        _textures = textures;
    }

    /// <summary>Raised with the texture key and working image after each page is patched.</summary>
    public event Action<int, Image>? PageChanged;

    /// <summary>Gets the number of animated face rectangles; zero when the model has no face.</summary>
    public int PartCount => _parts.Count;

    /// <summary>Gets every working texture page by key.</summary>
    public IEnumerable<(int TextureKey, Image Image)> Pages => _textures.Select(pair => (pair.Key, pair.Value.Working));

    /// <summary>Gets the source-frame clock that selects the currently visible facial frames.</summary>
    public (int BlinkTimer, int Time) State => (_blinkTimer, _time);

    /// <summary>Gets the frames currently committed to eye and non-eye face parts.</summary>
    public (int Eyes, int Cycle) FrameState => (_eyes, _cycle);

    /// <summary>Gets the live GPU texture page synchronized whenever a face rectangle changes.</summary>
    /// <param name="textureKey">Decoded page key.</param>
    /// <returns>The mutable GPU texture used by the ordered rasterizer.</returns>
    /// <exception cref="InvalidOperationException">No texture page has the requested key.</exception>
    public Texture2D TextureFor(int textureKey) =>
        _textures.TryGetValue(textureKey, out var texture)
            ? texture.Texture
            : throw new InvalidOperationException($"battle face texture page missing: {textureKey}");

    /// <summary>Reads the optional <c>face</c> section; parts with a non-positive width are skipped.</summary>
    /// <param name="root">Model document root.</param>
    /// <exception cref="InvalidOperationException">A part does not have three frames or a rectangle leaves the 256x256 page.</exception>
    public void Configure(JsonElement root)
    {
        if (!root.TryGetProperty("face", out var face))
        {
            return;
        }

        foreach (var value in face.GetProperty("parts").EnumerateArray())
        {
            var x = value.GetProperty("x").GetInt32();
            var y = value.GetProperty("y").GetInt32();
            var w = value.GetProperty("w").GetInt32();
            var h = value.GetProperty("h").GetInt32();
            if (w <= 0)
            {
                continue;
            }

            var frames = value.GetProperty("frames").EnumerateArray()
                .Select(frame => new Vector2I(frame[0].GetInt32(), frame[1].GetInt32())).ToArray();
            if (frames.Length != FrameCount || !FitsPage(x, y, w, h)
                || frames.Any(frame => !FitsPage(frame.X, frame.Y, w, h)))
            {
                throw new InvalidOperationException("battle face rectangle invalid");
            }

            _parts.Add(new BattleFacePart(value.GetProperty("index").GetInt32(), x, y, w, h, frames));
        }
    }

    /// <summary>Advances the face clocks and patches every page with the current frames.</summary>
    /// <param name="framesElapsed">PAL frames since the previous update.</param>
    /// <param name="motion">The fighter's one-based active motion number.</param>
    public void Update(int framesElapsed, int motion)
    {
        if (_parts.Count == 0)
        {
            return;
        }

        _eyes = MotionEyeFrame(motion);
        if (_eyes == 0)
        {
            _eyes = AdvanceBlink(framesElapsed);
        }
        else
        {
            _blinkTimer = 0;
        }

        _time += framesElapsed;
        _cycle = (_time % 18) / 6;
        CopyCurrentParts();
    }

    /// <summary>Counts working texels inside the face rectangles that differ from the source pages.</summary>
    /// <returns>The count summed over every page.</returns>
    public int ChangedTexels()
    {
        var changed = 0;
        foreach (var part in _parts)
        {
            foreach (var texture in _textures.Values)
            {
                changed += CountChanged(texture, part);
            }
        }

        return changed;
    }

    /// <summary>Describes the face for a probe log line.</summary>
    /// <param name="fighterId">Fighter id printed in the line.</param>
    /// <returns>The part count and the first (eyes) rectangle.</returns>
    public string Summary(int fighterId) => _parts.Count == 0 ? $"fighter={fighterId} parts=0" :
        $"fighter={fighterId} parts={_parts.Count} eyes_rect=({_parts[0].X},{_parts[0].Y},{_parts[0].W},{_parts[0].H})";

    /// <summary>Forces the blink timer, for a deterministic probe.</summary>
    /// <param name="timer">Remaining PAL frames; the eye frame is derived from <c>timer / 2</c>.</param>
    public void SetBlinkTimer(int timer) => _blinkTimer = timer;

    /// <summary>Replaces the random source used to re-arm the blink timer, for a deterministic probe.</summary>
    /// <param name="random">Source of non-negative integers; only the low 7 bits are used.</param>
    public void SetRandom(Func<int> random) => _random = random;

    /// <summary>
    /// Copies the already displayed source face selection to this model's independent texture pages.
    /// The partner viewport owns separate images, so it must not derive a second frame from a clock
    /// that the main fighter has already advanced.
    /// </summary>
    /// <param name="source">Animator that owns the visible fighter's clocks.</param>
    public void SynchronizeFrom(BattleFaceAnimator source)
    {
        ArgumentNullException.ThrowIfNull(source);
        (_blinkTimer, _time, _eyes, _cycle) = (source._blinkTimer, source._time, source._eyes, source._cycle);
        CopyCurrentParts();
    }

    /// <summary>Synchronizes an already committed face selection while retaining the former call shape.</summary>
    /// <param name="source">Animator that owns the visible fighter's selection.</param>
    /// <param name="motion">Ignored because the source has already selected its visible frame.</param>
    public void SynchronizeFrom(BattleFaceAnimator source, int motion)
    {
        _ = motion;
        SynchronizeFrom(source);
    }

    /// <summary>Chooses the forced eye frame associated with a motion.</summary>
    /// <param name="motion">One-based active motion number.</param>
    /// <returns>Forced eye frame or zero when blinking controls the eyes.</returns>
    private static int MotionEyeFrame(int motion) => motion switch
    {
        >= 4 and <= 11 => 2,
        2 => 1,
        _ => 0,
    };

    /// <summary>Checks whether a rectangle lies within one texture page.</summary>
    /// <param name="x">Rectangle horizontal coordinate.</param>
    /// <param name="y">Rectangle vertical coordinate.</param>
    /// <param name="w">Rectangle width.</param>
    /// <param name="h">Rectangle height.</param>
    /// <returns>True when the rectangle fits the page.</returns>
    private static bool FitsPage(int x, int y, int w, int h) => x >= 0 && y >= 0 && x + w <= PageSize && y + h <= PageSize;

    /// <summary>Counts changed texels for one face part and texture page.</summary>
    /// <param name="texture">Texture page to compare.</param>
    /// <param name="part">Face rectangle to inspect.</param>
    /// <returns>Count of changed texels.</returns>
    private static int CountChanged(BattleFaceTexture texture, BattleFacePart part)
    {
        var changed = 0;
        for (var y = part.Y; y < part.Y + part.H; y++)
        {
            for (var x = part.X; x < part.X + part.W; x++)
            {
                if (texture.Source.GetPixel(x, y) != texture.Working.GetPixel(x, y))
                {
                    changed++;
                }
            }
        }

        return changed;
    }

    /// <summary>Advances the blink timer and selects its eye frame.</summary>
    /// <param name="framesElapsed">Elapsed PAL frames.</param>
    /// <returns>Eye frame selected by the timer.</returns>
    private int AdvanceBlink(int framesElapsed)
    {
        var eyes = (_blinkTimer >> 1) switch { 1 or 2 or 5 or 6 => 1, 3 or 4 => 2, _ => 0 };
        if (_blinkTimer >> 1 == 0)
        {
            _blinkTimer = (_random() & 0x7f) + 60;
        }

        _blinkTimer = Math.Max(0, _blinkTimer - framesElapsed);
        return eyes;
    }

    /// <summary>Copies one face-frame rectangle into every working texture page.</summary>
    /// <param name="part">Face part to patch.</param>
    /// <param name="frame">Frame index to copy.</param>
    private void CopyPart(BattleFacePart part, int frame)
    {
        var source = part.Frames[frame];
        foreach (var (key, texture) in _textures)
        {
            texture.Working.BlitRect(texture.Source, new Rect2I(source, new Vector2I(part.W, part.H)), new Vector2I(part.X, part.Y));
            texture.Texture.Update(texture.Working);
            PageChanged?.Invoke(key, texture.Working);
        }
    }

    /// <summary>Copies the frame selection already committed by the authoritative animator.</summary>
    private void CopyCurrentParts()
    {
        foreach (var part in _parts)
        {
            CopyPart(part, part.Index < 2 ? _eyes : _cycle);
        }
    }
}
