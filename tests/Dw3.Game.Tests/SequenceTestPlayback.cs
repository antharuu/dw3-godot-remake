using Dw3Production.Presentation;
using Dw3Production.Simulation;

namespace Dw3.Game.Tests;

internal sealed class SequenceTestPlayback : IBattleSequencePlayback
{
    private bool _active;

    public BattleCameraFade Camera { get; } = new();

    public bool Ready { get; set; }

    public bool MissingCamera { get; set; }

    public bool AcceptAssets { get; set; }

    public bool ChildrenReady { get; set; }

    public int Finishes { get; private set; }

    public List<BattleSequenceEvent> Applied { get; } = [];

    public void Begin(ActionSequenceContext context) => _active = true;

    public bool MotionReady(int actor) => Ready;

    public SequenceAcknowledgement Consume(BattleSequenceEvent request)
    {
        if (MissingCamera && request is CameraRequestEvent)
        {
            throw new FormatException("camera resource unavailable");
        }

        if (request is CameraRequestEvent camera)
        {
            var views = new BattleShotCameraViews
            {
                GetEnemyView = () => new SourceBattleCameraView { Projection = 280 },
                GetFighterView = (_, _) => new SourceBattleCameraView { Projection = 320 },
            };
            BattleSequenceCamera.Apply(camera, views, Camera);
        }
        else if (request is not ModelMotionEvent and not SequenceEndedEvent && !AcceptAssets)
        {
            return SequenceAcknowledgement.Pending;
        }

        Applied.Add(request);
        return SequenceAcknowledgement.Applied;
    }

    public void Finish(bool cancelled)
    {
        if (!_active)
        {
            return;
        }

        _active = false;
        Finishes++;
        Camera.Begin(new Godot.Vector3(0, 6.5f, 22), new Godot.Vector3(0, -0.4f, 0), 280, 1, true);
    }
}
