using Dw3Production.Conversion;
using Godot;

namespace Dw3Production.Presentation;

/// <summary>Manages the animated water objects on a field scene layer, advancing frame and CLUT
/// cycling each source tick and applying the decoded frame offsets to the sprite pair nodes.</summary>
internal sealed class FieldWaterLayer : IDisposable
{
    private readonly List<WaterRuntime> _waterSprites = [];
    private readonly PsxBlendMaterials _blendMaterials = new();
    private readonly FieldTextureCache _textures;

    /// <summary>Initializes a new instance of the <see cref="FieldWaterLayer"/> class backed by the given texture cache.</summary>
    /// <param name="textures">Shared field texture cache used to load water frames.</param>
    public FieldWaterLayer(FieldTextureCache textures)
    {
        _textures = textures;
    }

    /// <summary>Releases Godot resources owned by this layer.</summary>
    public void Dispose()
    {
        _blendMaterials.Dispose();
    }

    /// <summary>Instantiates sprite pairs for the water objects in the given zone.</summary>
    /// <param name="water">Water data keyed by zone ID.</param>
    /// <param name="zone">Zone whose water objects should be instantiated.</param>
    /// <param name="depthRoot">Factory that returns the parent node for a given source depth.</param>
    public void LoadZone(WaterData water, Zone zone, Func<int, Node2D> depthRoot)
    {
        _waterSprites.Clear();
        if (!water.Zones.TryGetValue(zone.Id, out var zoneWater))
        {
            return;
        }

        foreach (var obj in zoneWater)
        {
            // Keep opaque and STP passes under one sibling: source draws both parts
            // atomically at the object's callback/immediate slot.
            var pair = new Node2D();
            var opaque = new Sprite2D { Centered = false };
            var semi = new Sprite2D { Centered = false, Material = _blendMaterials.Get(-1) };
            pair.AddChild(opaque);
            pair.AddChild(semi);
            depthRoot(obj.Depth).AddChild(pair);
            var runtime = new WaterRuntime { Pair = pair, Opaque = opaque, Semi = semi, Object = obj, Frame = obj.Frame, Clut = obj.ClutRow, CycleTime = obj.CycleTime };
            ApplyFrame(runtime);
            _waterSprites.Add(runtime);
        }
    }

    /// <summary>Advances each visible water object by one source tick, cycling frames and CLUTs.</summary>
    /// <param name="visibleViewport">Camera viewport in world space used to cull off-screen objects.</param>
    public void Advance(Rect2 visibleViewport)
    {
        foreach (var water in _waterSprites.Where(water => IsVisible(water, visibleViewport)))
        {
            AdvanceObject(water);
        }
    }

    /// <summary>Returns the draw callbacks for water objects at the given source depth that use the sort-Y slot.</summary>
    /// <param name="depth">Source ordering-table depth to query.</param>
    /// <returns>A sequence of (node, priority, registration) triples for the depth-4 sort pass.</returns>
    public IEnumerable<(Node Item, int Priority, int Registration)> CallbacksAtDepth(int depth)
    {
        return _waterSprites
            .Where(water => water.Object.Depth == depth && water.Object.SortY != 0)
            .Select(water => ((Node)water.Pair, water.Object.SortY, SourceCallbackOrder.MapObjectRegistration));
    }

    /// <summary>Returns the water objects at the given source depth that use the immediate slot (sort-Y == 0).</summary>
    /// <param name="depth">Source ordering-table depth to query.</param>
    /// <returns>A sequence of water objects in reverse-index order for immediate drawing.</returns>
    public IEnumerable<WaterObject> ImmediateObjectsAtDepth(int depth)
    {
        return _waterSprites
            .Where(water => water.Object.Depth == depth && water.Object.SortY == 0)
            .OrderByDescending(water => water.Object.Index)
            .Select(water => water.Object);
    }

    /// <summary>Returns the sprite-pair node associated with <paramref name="obj"/>.</summary>
    /// <param name="obj">Water object whose pair node to retrieve.</param>
    /// <returns>The parent <see cref="Node"/> that groups the opaque and semi-transparent passes.</returns>
    public Node PairForObject(WaterObject obj) => _waterSprites.First(w => w.Object == obj).Pair;

    /// <summary>Returns the set of source depths occupied by at least one water object.</summary>
    /// <returns>A deduplicated sequence of active source depths.</returns>
    public IEnumerable<int> ActiveDepths() => _waterSprites.Select(w => w.Object.Depth).ToHashSet();

    /// <summary>Returns true when the water object's immutable origin is inside the expanded viewport bounds.</summary>
    /// <param name="water">Runtime state whose object origin and margin are tested.</param>
    /// <param name="viewport">Visible camera viewport in world space.</param>
    /// <returns><see langword="true"/> when the origin is inside the expanded viewport bounds.</returns>
    private static bool IsVisible(WaterRuntime water, Rect2 viewport)
    {
        // the map-object visibility contract tests the immutable object origin, not its frame offset,
        // with the decoded one-sided margin on the left/top only.
        var origin = new Vector2(water.Object.X, water.Object.Y);
        return origin.X >= viewport.Position.X - water.Object.Margin
            && origin.X <= viewport.End.X
            && origin.Y >= viewport.Position.Y - water.Object.Margin
            && origin.Y <= viewport.End.Y;
    }

    /// <summary>Advances the frame cycle (type 1) by one tick, wrapping at the cycle end.</summary>
    /// <param name="water">Runtime state to advance.</param>
    /// <param name="obj">Source water object containing cycle range and delay.</param>
    private static void AdvanceCycle1(WaterRuntime water, WaterObject obj)
    {
        if (water.CycleTime < 0x100)
        {
            water.Frame = water.Frame >= obj.CycleLast ? obj.CycleFirst : water.Frame + 1;
            water.CycleTime = obj.CycleDelay << 8;
        }
        else
        {
            water.CycleTime -= 0x100;
        }
    }

    /// <summary>Advances the CLUT cycle (type 2) by one tick, wrapping at the cycle end.</summary>
    /// <param name="water">Runtime state to advance.</param>
    /// <param name="obj">Source water object containing cycle range and delay.</param>
    private static void AdvanceCycle2(WaterRuntime water, WaterObject obj)
    {
        if (water.CycleTime < 0x100)
        {
            water.Clut = water.Clut >= obj.CycleLast ? obj.CycleFirst : water.Clut + 1;
            water.CycleTime = obj.CycleDelay << 8;
        }
        else
        {
            water.CycleTime -= 0x100;
        }
    }

    /// <summary>Advances the ping-pong CLUT cycle (type 3) by one tick, reversing direction at the ends.</summary>
    /// <param name="water">Runtime state to advance.</param>
    /// <param name="obj">Source water object containing cycle range and delay.</param>
    private static void AdvanceCycle3(WaterRuntime water, WaterObject obj)
    {
        var back = (water.CycleTime & 0x8000) != 0;
        var time = water.CycleTime & 0x7fff;
        if (time < 0x100)
        {
            water.Clut += back ? -1 : 1;
            time = obj.CycleDelay << 8;
            if (back && water.Clut == obj.CycleFirst - 1)
            {
                water.Clut = obj.CycleFirst + 1;
                back = false;
            }
            else if (!back && water.Clut == obj.CycleLast + 1)
            {
                water.Clut = obj.CycleLast - 1;
                back = true;
            }
        }
        else
        {
            time -= 0x100;
        }

        water.CycleTime = time | (back ? 0x8000 : 0);
    }

    /// <summary>Returns true when <paramref name="candidate"/> is the frame for the current live cycle position.</summary>
    /// <param name="obj">Water object used to resolve the cycle type and first-frame offset.</param>
    /// <param name="candidate">Frame record to test against the live position.</param>
    /// <param name="frame">Current live frame index.</param>
    /// <param name="clut">Current live CLUT row index.</param>
    /// <returns><see langword="true"/> when <paramref name="candidate"/> is the entry at the current cycle position.</returns>
    private static bool FrameMatches(WaterObject obj, WaterFrame candidate, int frame, int clut)
    {
        // Converter orders cycle-1 frames by frame ID and cycles 2/3 by CLUT row.
        // The full source values stay in the manifest but paths are opaque, so map
        // the live source value onto the deterministic exported order.
        var index = obj.Cycle switch
        {
            1 => frame - obj.CycleFirst,
            2 or 3 => clut - obj.CycleFirst,
            _ => 0,
        };
        return index >= 0 && index < obj.Frames.Count && ReferenceEquals(candidate, obj.Frames[index]);
    }

    /// <summary>Dispatches to the appropriate cycle-type advance method then re-applies the current frame.</summary>
    /// <param name="water">Runtime state to advance.</param>
    private void AdvanceObject(WaterRuntime water)
    {
        var obj = water.Object;
        if (obj.Cycle == 0)
        {
            return;
        }

        switch (obj.Cycle)
        {
            case 1:
                AdvanceCycle1(water, obj);
                break;
            case 2:
                AdvanceCycle2(water, obj);
                break;
            case 3:
                AdvanceCycle3(water, obj);
                break;
        }

        ApplyFrame(water);
    }

    /// <summary>Selects the matching frame record from the object's list and updates the sprite nodes.</summary>
    /// <param name="water">Runtime state whose current frame and CLUT are applied to the sprite pair.</param>
    private void ApplyFrame(WaterRuntime water)
    {
        var frame = water.Object.Frames.FirstOrDefault(candidate => FrameMatches(water.Object, candidate, water.Frame, water.Clut)) ?? water.Object.Frames[0];
        water.Opaque.Texture = _textures.TextureFor(frame.OpaquePath);
        water.Semi.Texture = _textures.TextureFor(frame.SemiPath);
        water.Semi.Material = _blendMaterials.Get(frame.Abr);
        var position = new Vector2(water.Object.X + frame.OffsetX, water.Object.Y + frame.OffsetY);
        water.Opaque.Position = position;
        water.Semi.Position = position;
    }

    /// <summary>Per-water-object runtime state: sprite pair nodes and live cycle position.</summary>
    private sealed class WaterRuntime
    {
        /// <summary>Gets the parent node grouping the opaque and semi-transparent sprite passes, sorted as one map object.</summary>
        public required Node2D Pair { get; init; }

        /// <summary>Gets the sprite node of the opaque pass (texels without the STP bit).</summary>
        public required Sprite2D Opaque { get; init; }

        /// <summary>Gets the sprite node of the semi-transparent pass (STP texels, drawn with the object's ABR blend).</summary>
        public required Sprite2D Semi { get; init; }

        /// <summary>Gets the source water object record: cycle type, exported frames and position.</summary>
        public required WaterObject Object { get; init; }

        /// <summary>Gets or sets the live source frame id; type-1 cycles step it from CycleFirst to CycleLast.</summary>
        public int Frame { get; set; }

        /// <summary>Gets or sets the live CLUT row of type-2 and type-3 cycle objects.</summary>
        public int Clut { get; set; }

        /// <summary>Gets or sets the 8.8 fixed-point cycle timer; type-3 objects keep their direction in bit 15.</summary>
        public int CycleTime { get; set; }
    }
}
