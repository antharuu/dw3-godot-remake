using Godot;

namespace Dw3Production.Presentation;

/// <summary>A report canvas layer whose drawing is supplied by its owner, so one view is drawn in ordered passes.</summary>
internal sealed partial class FightReportLayer : Control
{
    /// <summary>Initializes a new instance of the <see cref="FightReportLayer"/> class.</summary>
    public FightReportLayer() => MouseFilter = MouseFilterEnum.Ignore;

    /// <summary>Gets or sets the pass drawn on this layer.</summary>
    public Action<FightReportLayer>? Painter { get; set; }

    /// <summary>Runs the owner's pass.</summary>
    public override void _Draw() => Painter?.Invoke(this);
}
