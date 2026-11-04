namespace Dw3Production.Presentation;

/// <summary>Separates each bone's most recently published pose from the fixed origin of its active blend.</summary>
internal sealed class BattleBlendState
{
    private readonly Dictionary<int, BattleBlendPose> _origins = [];
    private readonly Dictionary<int, BattleBlendPose> _published = [];

    /// <summary>Records a pose that was visibly published for one bone.</summary>
    /// <param name="bone">Source bone index.</param>
    /// <param name="pose">Pose now visible for the bone.</param>
    public void Publish(int bone, BattleBlendPose pose) => _published[bone] = pose;

    /// <summary>Captures the current visible pose as the fixed origin for one new blend target.</summary>
    /// <param name="bone">Source bone index.</param>
    public void CaptureOrigin(int bone) => _origins[bone] = Published(bone);

    /// <summary>Sets the fixed origin for a blend whose source comes from an explicit animation frame.</summary>
    /// <param name="bone">Source bone index.</param>
    /// <param name="pose">Sampled source-frame pose.</param>
    public void SetOrigin(int bone, BattleBlendPose pose) => _origins[bone] = pose;

    /// <summary>Gets the fixed pose retained for the active blend target.</summary>
    /// <param name="bone">Source bone index.</param>
    /// <returns>The retained origin, or the identity pose when no pose has been published.</returns>
    public BattleBlendPose Origin(int bone) => _origins.TryGetValue(bone, out var pose) ? pose : BattleBlendPose.Identity;

    /// <summary>Gets the most recently published pose for a bone.</summary>
    /// <param name="bone">Source bone index.</param>
    /// <returns>The published pose, or the identity pose when no pose has been published.</returns>
    public BattleBlendPose Published(int bone) => _published.TryGetValue(bone, out var pose) ? pose : BattleBlendPose.Identity;
}
