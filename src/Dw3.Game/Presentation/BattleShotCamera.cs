namespace Dw3Production.Presentation;

/// <summary>Supplies the random value used to choose the next battle shot list.</summary>
public interface IShotCameraRng
{
    /// <summary>Returns the next signed random value.</summary>
    int Next();
}

/// <summary>Mutable camera frame used by the battle renderer.</summary>
public sealed class SourceBattleCameraView
{
    /// <summary>Viewpoint X coordinate.</summary>
    public int Vpx;
    /// <summary>Viewpoint Y coordinate.</summary>
    public int Vpy;
    /// <summary>Viewpoint Z coordinate.</summary>
    public int Vpz;
    /// <summary>Look-at X coordinate.</summary>
    public int Vrx;
    /// <summary>Look-at Y coordinate.</summary>
    public int Vry;
    /// <summary>Look-at Z coordinate.</summary>
    public int Vrz;
    /// <summary>Camera-frame X translation.</summary>
    public int Tx;
    /// <summary>Camera-frame Y translation.</summary>
    public int Ty;
    /// <summary>Camera-frame Z translation.</summary>
    public int Tz;
    /// <summary>Camera-frame X rotation, with 4096 units per turn.</summary>
    public int RotationX;
    /// <summary>Camera-frame Y rotation, with 4096 units per turn.</summary>
    public int RotationY;
    /// <summary>Camera-frame Z rotation, with 4096 units per turn.</summary>
    public int RotationZ;
    /// <summary>View roll.</summary>
    public int Rz;
    /// <summary>Projection-plane distance.</summary>
    public int Projection;
}

/// <summary>Provides fresh overview and fighter camera frames for battle shots.</summary>
public sealed class BattleShotCameraViews
{
    /// <summary>Gets a fresh battle overview frame.</summary>
    public required Func<SourceBattleCameraView> GetEnemyView { get; init; }

    /// <summary>Gets a fresh frame for a fighter slot and camera index.</summary>
    public required Func<int, int, SourceBattleCameraView> GetFighterView { get; init; }
}

/// <summary>Advances the scripted camera shots used while choosing a battle command.</summary>
public sealed class BattleShotCamera
{
    private const int TurnUnits = 4096;
    private const int SideDistance = 5120;

    private static readonly (int Duration, int Behavior)[][] ShotLists =
    [
        [(1800, 1), (480, 9), (360, 3), (300, 4), (480, 9)],
        [(360, 6), (900, 1), (300, 5), (480, 9)],
        [(1920, 1), (360, 6), (180, 7), (480, 9)],
        [(90, 10), (90, 5)],
    ];

    private static readonly int[][] Successors =
    [
        [1, 2, 1],
        [0, 2, 2],
        [0, 1, 0],
        [0, 1, 2],
    ];

    private readonly IShotCameraRng _rng;
    private readonly BattleShotCameraViews _views;
    private bool _firstFrame = true;
    private int _orbitYaw;

    /// <summary>Creates a shot camera using caller-supplied random and view providers.</summary>
    public BattleShotCamera(IShotCameraRng rng, BattleShotCameraViews views)
    {
        _rng = rng;
        _views = views;
        List = 3;
        Shot = 0;
        Time = 90;
        Substate = 10;
        PlayerVisible = true;
        EnemyVisible = true;
        View = _views.GetEnemyView();
        _orbitYaw = View.RotationY;
    }

    /// <summary>Creates a shot camera with neutral test views.</summary>
    public BattleShotCamera(IShotCameraRng rng, int enemyCameraCount)
        : this(rng, new BattleShotCameraViews
        {
            GetEnemyView = NeutralView,
            GetFighterView = static (_, _) => NeutralView(),
        })
    {
        _ = enemyCameraCount;
    }

    /// <summary>Gets the current shot-list index.</summary>
    public int List { get; private set; }

    /// <summary>Gets the current shot index within the list.</summary>
    public int Shot { get; private set; }

    /// <summary>Gets the frames remaining in the current shot.</summary>
    public int Time { get; private set; }

    /// <summary>Gets the current shot behavior identifier.</summary>
    public int Substate { get; private set; }

    /// <summary>Gets the camera frame to render.</summary>
    public SourceBattleCameraView View { get; private set; }

    /// <summary>Gets a value indicating whether gets whether the player's fighter is visible.</summary>
    public bool PlayerVisible { get; private set; }

    /// <summary>Gets a value indicating whether gets whether the enemy fighter is visible.</summary>
    public bool EnemyVisible { get; private set; }

    /// <summary>Advances the battle shot by one frame.</summary>
    public void Step()
    {
        ApplyBehavior();
        _firstFrame = false;
        Time--;
        if (Time != 0)
            return;

        PlayerVisible = true;
        EnemyVisible = true;
        Shot++;
        if (Shot == ShotLists[List].Length)
        {
            var choice = Math.Abs(_rng.Next()) % 3;
            List = Successors[List][choice];
            Shot = 0;
        }

        (Time, Substate) = ShotLists[List][Shot];
        _firstFrame = true;
    }

    /// <summary>Advances the battle shot by the requested number of frames.</summary>
    public void StepFrames(int frames)
    {
        for (var frame = 0; frame < frames; frame++)
            Step();
    }

    /// <summary>Restores both fighters and replaces the current view with a fresh overview.</summary>
    public void Stop()
    {
        PlayerVisible = true;
        EnemyVisible = true;
        View = _views.GetEnemyView();
    }

    private void ApplyBehavior()
    {
        if (_firstFrame)
            BeginBehavior();

        switch (Substate)
        {
            case 1:
                View.RotationY = WrapTurn(unchecked(View.RotationY + 2));
                _orbitYaw = View.RotationY;
                break;
            case 3:
            case 6:
            case 9:
                View.RotationY = WrapTurn(unchecked(View.RotationY + 1));
                break;
            case 4:
            case 5:
                View.RotationY = WrapTurn(unchecked(View.RotationY - 1));
                break;
        }
    }

    private void BeginBehavior()
    {
        switch (Substate)
        {
            case 1:
                View = _views.GetEnemyView();
                View.RotationY = _orbitYaw;
                break;
            case 3:
                BeginPlayerSide();
                break;
            case 4:
                BeginEnemySide();
                break;
            case 5:
                BeginPlayerSide();
                View.RotationY = WrapTurn(unchecked(View.RotationY + 227));
                break;
            case 6:
                BeginEnemySide();
                View.RotationY = WrapTurn(unchecked(View.RotationY - 227));
                break;
            case 7:
                EnemyVisible = false;
                View = _views.GetFighterView(0, 10);
                break;
            case 9:
                View = _views.GetEnemyView();
                View.Vpx = -5120;
                View.Vpy = -10240;
                View.Vpz = 0;
                View.Vrx = 0;
                View.Vry = 0;
                View.Vrz = 0;
                View.Projection = 200;
                View.RotationY = WrapTurn(unchecked(View.RotationY - 341));
                break;
            case 10:
                View = _views.GetEnemyView();
                break;
        }
    }

    private void BeginPlayerSide()
    {
        EnemyVisible = false;
        View = _views.GetFighterView(0, 8);
        View.Tz = unchecked(View.Tz - SideDistance);
        View.Vpz = unchecked(View.Vpz + SideDistance);
        View.Vrz = unchecked(View.Vrz + SideDistance);
    }

    private void BeginEnemySide()
    {
        PlayerVisible = false;
        View = _views.GetFighterView(16, 0);
        View.Tz = unchecked(View.Tz + SideDistance);
        View.Vpz = unchecked(View.Vpz - SideDistance);
        View.Vrz = unchecked(View.Vrz - SideDistance);
    }

    private static int WrapTurn(int angle)
    {
        var wrapped = angle % TurnUnits;
        return wrapped < 0 ? wrapped + TurnUnits : wrapped;
    }

    private static SourceBattleCameraView NeutralView() => new() { Projection = 280 };
}
