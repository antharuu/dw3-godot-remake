using Dw3Production.Conversion;

namespace Dw3Production.Presentation;

/// <summary>
/// One started sprite effect: the source effect task and its child animations, each advancing one
/// source-time sample per physical step and drawing while its scales are nonzero, until every child ended.
/// </summary>
internal sealed class BattleSpriteEffectInstance
{
    private readonly int[] _times;
    private bool _started;

    /// <summary>Initializes a new instance of the <see cref="BattleSpriteEffectInstance"/> class.</summary>
    /// <param name="serial">Creation order within the playback, unique per instance.</param>
    /// <param name="sheet">Prepared sheet.</param>
    /// <param name="anchor">Script position.</param>
    public BattleSpriteEffectInstance(long serial, BattleSpriteSheet sheet, BattleSpriteAnchor anchor)
    {
        Serial = serial;
        Sheet = sheet;
        Anchor = anchor;
        _times = new int[sheet.Animations.Count];
    }

    /// <summary>Gets the creation order, distinct for identical effects started more than once.</summary>
    public long Serial { get; }

    /// <summary>Gets the script-visible effect identity.</summary>
    public int Id => Sheet.Id;

    /// <summary>Gets the prepared sheet.</summary>
    public BattleSpriteSheet Sheet { get; }

    /// <summary>Gets the script position.</summary>
    public BattleSpriteAnchor Anchor { get; }

    /// <summary>Gets the source layer: front for effects 1000 to 1002 and 1007, the models layer otherwise.</summary>
    public BattleSpriteLayer Layer => Id is (>= 1000 and < 1003) or 1007 ? BattleSpriteLayer.Front : BattleSpriteLayer.Models;

    /// <summary>Gets a value indicating whether every child animation reached its duration.</summary>
    public bool IsComplete => _times.Select((time, i) => time >= Sheet.Animations[i].Count).All(done => done);

    /// <summary>Gets the current sample index of each child animation.</summary>
    public IReadOnlyList<int> Times => _times;

    /// <summary>Advances every unfinished child by one source sample. The first step displays sample zero.</summary>
    public void Step()
    {
        if (!_started)
        {
            _started = true;
            return;
        }

        for (int i = 0; i < _times.Length; i++)
        {
            _times[i] = Math.Min(_times[i] + 1, Sheet.Animations[i].Count);
        }
    }

    /// <summary>
    /// Lists the current drawn samples in source draw order. Every child adds its primitives to the
    /// same ordering-table entry, which draws the last added first, so the last child is at the back.
    /// </summary>
    /// <returns>Child index and sample, back to front, skipping finished children and zero scales.</returns>
    public IEnumerable<(int Animation, BattleSpriteSample Sample)> DrawnSamples()
    {
        for (int i = _times.Length - 1; i >= 0; i--)
        {
            IReadOnlyList<BattleSpriteSample> animation = Sheet.Animations[i];
            if (_times[i] < animation.Count && animation[_times[i]].IsDrawn)
            {
                yield return (i, animation[_times[i]]);
            }
        }
    }
}
