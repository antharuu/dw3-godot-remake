using Godot;

namespace Dw3Production.Presentation;

/// <summary>
/// The battle camera's interpolation between source views: position, look-at target and
/// projection distance move linearly from the view held when a new one is requested, at a step per
/// 60 Hz frame.
/// </summary>
internal sealed class BattleCameraFade
{
    private readonly BattleSequenceCameraTransition _source = new();
    private Vector3 _positionFrom = new(0, 6.5f, 22f);
    private Vector3 _targetFrom = new(0, -0.4f, 0);
    private float _projectionFrom = 280f;
    private Vector3 _positionTo = new(0, 6.5f, 22f);
    private Vector3 _targetTo = new(0, -0.4f, 0);
    private float _projectionTo = 280f;
    private float _fade = 1f;
    private float _fadeStep = 1f;
    private bool _sourceActive;
    private float _rollFrom;
    private float _rollTo;

    /// <summary>Gets the view twist in radians, distinct from the camera-parent rotation.</summary>
    public float Roll { get; private set; }

    /// <summary>Gets the current camera position in Godot world units.</summary>
    public Vector3 Position { get; private set; } = new(0, 6.5f, 22f);

    /// <summary>Gets the current look-at target in Godot world units.</summary>
    public Vector3 Target { get; private set; } = new(0, -0.4f, 0);

    /// <summary>Gets the current PS1 projection distance (screen-space focal length in pixels).</summary>
    public float Projection { get; private set; } = 280f;

    /// <summary>Starts moving toward a new view from the current one.</summary>
    /// <param name="position">Destination position.</param>
    /// <param name="target">Destination look-at target.</param>
    /// <param name="projection">Destination projection distance.</param>
    /// <param name="fadeStep">Fraction of the move covered per 60 Hz frame.</param>
    /// <param name="snap">True to jump to the destination at once.</param>
    /// <param name="roll">View twist in radians around the viewing axis.</param>
    public void Begin(Vector3 position, Vector3 target, float projection, float fadeStep, bool snap, float roll = 0)
    {
        _sourceActive = false;
        _positionFrom = Position;
        _targetFrom = Target;
        _projectionFrom = Projection;
        _rollFrom = Roll;
        _rollTo = roll;
        _positionTo = position;
        _targetTo = target;
        _projectionTo = projection;
        _fade = snap ? 1f : 0f;
        _fadeStep = fadeStep;
        if (snap)
        {
            Position = _positionTo;
            Target = _targetTo;
            Projection = _projectionTo;
            Roll = _rollTo;
        }
    }

    /// <summary>Remembers an ordinary source view for a later sequence fade.</summary>
    /// <param name="view">Menu or overview frame.</param>
    public void RememberSource(SourceBattleCameraView view) => _source.Seed(view);

    /// <summary>Starts a sequence cut or fixed-point fade in authored camera coordinates.</summary>
    /// <param name="view">Destination view.</param>
    /// <param name="time">Normalized source duration.</param>
    public void BeginSource(SourceBattleCameraView view, int time)
    {
        _sourceActive = true;
        _source.Begin(view, time);
        ApplySource();
    }

    /// <summary>Advances the move by presented time; a finished move holds its destination.</summary>
    /// <param name="delta">Presented time in seconds.</param>
    public void Step(float delta)
    {
        if (_sourceActive)
        {
            _source.Step(delta);
            ApplySource();
            return;
        }

        if (_fade >= 1f)
        {
            return;
        }

        _fade = Mathf.Min(1f, _fade + (_fadeStep * delta * 60f));
        var t = _fade;
        Position = _positionFrom.Lerp(_positionTo, t);
        Target = _targetFrom.Lerp(_targetTo, t);
        Projection = Mathf.Lerp(_projectionFrom, _projectionTo, t);
        Roll = Mathf.Lerp(_rollFrom, _rollTo, t);
    }

    /// <summary>Composes the interpolated authored frame exactly once for the physical camera.</summary>
    private void ApplySource()
    {
        var position = BattleSceneMath.ComposeCameraPosition(_source.Current, reference: false);
        var target = BattleSceneMath.ComposeCameraPosition(_source.Current, reference: true);
        Position = new Vector3(position.X, position.Y, position.Z);
        Target = new Vector3(target.X, target.Y, target.Z);
        Projection = _source.Current.Projection;
        Roll = _source.Current.Rz * Mathf.Tau / 4096f;
    }
}
