using Dw3Production.Conversion;
using Godot;

namespace Dw3Production.Presentation;

/// <summary>Manages the NPC sprite nodes on the field depth-4 layer, cycling their idle animations
/// one source tick at a time alongside the player.</summary>
internal sealed class FieldNpcLayer
{
    private readonly List<NpcRuntime> _npcSprites = [];
    private readonly FieldTextureCache _textures;

    /// <summary>Initializes a new instance of the <see cref="FieldNpcLayer"/> class backed by the given texture cache.</summary>
    /// <param name="textures">Shared field texture cache used to load NPC frames.</param>
    public FieldNpcLayer(FieldTextureCache textures)
    {
        _textures = textures;
    }

    /// <summary>Gets the number of NPC sprite nodes currently active (used by the test-only re-entry leak check).</summary>
    public int Count => _npcSprites.Count;

    /// <summary>Populates NPC sprites from the record list and attaches them to <paramref name="depthRoot"/>.</summary>
    /// <param name="npcs">Optional list of NPC records for the zone; null clears all sprites.</param>
    /// <param name="depthRoot">Parent node at depth 4 where NPC sprites are inserted.</param>
    public void LoadZone(IReadOnlyList<NpcRecord>? npcs, Node2D depthRoot)
    {
        _npcSprites.Clear();
        if (npcs is null)
        {
            return;
        }

        foreach (var npc in npcs)
        {
            var sprite = new Sprite2D { Centered = false };
            depthRoot.AddChild(sprite);
            var runtime = new NpcRuntime { Sprite = sprite, Record = npc, FrameCursor = 0, TicksRemaining = npc.IdleFrames[0].Duration };
            ApplyFrame(runtime);
            _npcSprites.Add(runtime);
        }
    }

    /// <summary>Returns the sort callbacks for depth-4 ordering: each NPC contributes its sprite node and ground Y.</summary>
    /// <returns>A sequence of (sprite node, ground-Y) pairs, one per active NPC.</returns>
    public IEnumerable<(Node Sprite, int Y)> Callbacks()
    {
        return _npcSprites.Select(npc => ((Node)npc.Sprite, npc.Record.Y));
    }

    /// <summary>Advances all active NPC idle animations by one source tick.</summary>
    public void Advance()
    {
        foreach (var npc in _npcSprites)
        {
            AdvanceNpc(npc);
        }
    }

    /// <summary>Advances a single NPC's idle animation, cycling frames when the per-frame duration expires.</summary>
    /// <param name="npc">Runtime state of the NPC to advance.</param>
    private void AdvanceNpc(NpcRuntime npc)
    {
        var frames = npc.Record.IdleFrames;
        if (frames.Count <= 1)
        {
            return; // a single source frame is a static pose, never cycled
        }

        if (--npc.TicksRemaining > 0)
        {
            return;
        }

        npc.FrameCursor = (npc.FrameCursor + 1) % frames.Count;
        npc.TicksRemaining = frames[npc.FrameCursor].Duration;
        ApplyFrame(npc);
    }

    /// <summary>Applies the current frame of <paramref name="npc"/> to its Godot sprite node.</summary>
    /// <param name="npc">Runtime state whose frame cursor and record are used.</param>
    private void ApplyFrame(NpcRuntime npc)
    {
        var frame = npc.Record.IdleFrames[npc.FrameCursor];
        npc.Sprite.Texture = _textures.TextureFor(frame.Path);
        npc.Sprite.Offset = new Vector2(frame.OffsetX, frame.OffsetY);
        npc.Sprite.FlipH = npc.Record.FlipH;
        npc.Sprite.Position = new Vector2(npc.Record.X, npc.Record.Y);
    }

    /// <summary>Per-NPC runtime state: the Godot sprite, the source record, and the idle animation cursor.</summary>
    private sealed class NpcRuntime
    {
        /// <summary>Godot sprite node for this NPC, parented at depth 4.</summary>
        public required Sprite2D Sprite;

        /// <summary>Source record carrying the NPC position, flip flag, and idle frame list.</summary>
        public required NpcRecord Record;

        /// <summary>Index into <see cref="NpcRecord.IdleFrames"/> for the currently displayed frame.</summary>
        public int FrameCursor;

        /// <summary>Source ticks remaining before the cursor advances to the next frame.</summary>
        public int TicksRemaining;
    }
}
