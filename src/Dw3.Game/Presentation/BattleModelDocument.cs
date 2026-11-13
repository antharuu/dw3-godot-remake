using System.Globalization;
using System.Text.Json;
using Godot;

namespace Dw3Production.Presentation;

/// <summary>
/// Reads the plain-data sections of a battle-model JSON written by the extractor (format
/// <c>dw3-battle-model-v8</c>): format checks, stage lights and background, fighter geometry,
/// key tracks, motions and fighter cameras. Scene nodes are built elsewhere.
/// </summary>
internal static class BattleModelDocument
{
    private const string FighterOrStageFormat = "dw3-battle-model-v8";
    private const string EffectFormat = "dw3-battle-effect-model-v1";
    private const int MotionTerminator = 0x7FFF;

    /// <summary>
    /// Rejects a document of another format, or whose motion 1 does not start at step index 0 and
    /// end on the 0x7FFF terminator. The step body is fighter-specific, so only those two
    /// universal invariants are pinned.
    /// </summary>
    /// <param name="root">Document root.</param>
    /// <exception cref="InvalidOperationException">The format or a motion-1 invariant differs.</exception>
    public static void Validate(JsonElement root)
    {
        string? format = root.GetProperty("format").GetString();
        if (format is not (FighterOrStageFormat or EffectFormat))
        {
            throw new InvalidOperationException("unexpected external battle model format");
        }

        var invariants = root.GetProperty("source_invariants");
        var first = invariants.GetProperty("motion_1_first_step");
        if (first.GetArrayLength() < 4 || first[0].GetInt32() != 0)
        {
            throw new InvalidOperationException("battle model motion-1 does not start at index 0");
        }

        var terminator = invariants.GetProperty("motion_1_terminator");
        if (terminator.GetArrayLength() < 4 || terminator[0].GetInt32() != MotionTerminator)
        {
            throw new InvalidOperationException("battle model motion-1 terminator changed");
        }
    }

    /// <summary>Reads the stage light set, present only in a fight-stage document.</summary>
    /// <param name="root">Document root.</param>
    /// <returns>The three directional lights and the ambient colour, or null for a fighter.</returns>
    /// <exception cref="InvalidOperationException">The set does not hold exactly three lights.</exception>
    public static BattleModel.BattleLightSet? ReadStageLights(JsonElement root) =>
        root.TryGetProperty("lights", out var lights) ? ReadLightSet(lights) : null;

    /// <summary>Reads the stage background colour, stored as three 0..255 components.</summary>
    /// <param name="root">Document root.</param>
    /// <returns>The colour, or null when the document has none.</returns>
    public static Color? ReadBackgroundColor(JsonElement root) =>
        root.TryGetProperty("bg_color", out var bg)
            ? new Color(bg[0].GetInt32() / 255f, bg[1].GetInt32() / 255f, bg[2].GetInt32() / 255f)
            : null;

    /// <summary>Reads the fighter id and its placement geometry, absent from a stage document.</summary>
    /// <param name="root">Document root.</param>
    /// <returns>The id, the distance from the arena centre and the height (PS1 units), or null.</returns>
    public static (int FighterId, int Distance, int Height)? ReadFighter(JsonElement root)
    {
        if (!root.TryGetProperty("fighter_id", out var fighterId))
        {
            return null;
        }

        var geometry = root.GetProperty("geometry");
        return (fighterId.GetInt32(), geometry.GetProperty("distance").GetInt32(), geometry.GetProperty("height").GetInt32());
    }

    /// <summary>
    /// Reads the key tracks: an object keyed by decimal bone index, each holding three channels
    /// (position, rotation, scale) of <c>[frame, x, y, z]</c> keys.
    /// </summary>
    /// <param name="tracks">The <c>tracks</c> object.</param>
    /// <returns>The channels by bone, each key stored as (x, y, z, frame).</returns>
    /// <exception cref="InvalidOperationException">A track does not hold exactly three channels.</exception>
    public static Dictionary<int, List<Vector4>[]> ReadTracks(JsonElement tracks)
    {
        var result = new Dictionary<int, List<Vector4>[]>();
        foreach (var track in tracks.EnumerateObject())
        {
            var sets = new List<Vector4>[3];
            var channelIndex = 0;
            foreach (var channel in track.Value.EnumerateArray())
            {
                sets[channelIndex++] = ReadKeys(channel);
            }

            if (channelIndex != 3)
            {
                throw new InvalidOperationException("battle model key track is not three channels");
            }

            result.Add(int.Parse(track.Name, CultureInfo.InvariantCulture), sets);
        }

        return result;
    }

    /// <summary>
    /// Reads every motion as a list of <c>[index, count, frame, endFrame]</c> steps, ending on the
    /// 0x7FFF terminator step.
    /// </summary>
    /// <param name="motions">The <c>motions</c> array.</param>
    /// <returns>The motions, index 0 being motion 1 (the idle).</returns>
    /// <exception cref="InvalidOperationException">The array is empty.</exception>
    public static List<List<List<int>>> ReadMotions(JsonElement motions)
    {
        var result = new List<List<List<int>>>();
        foreach (var motion in motions.EnumerateArray())
        {
            result.Add([.. motion.EnumerateArray().Select(step => new List<int> { step[0].GetInt32(), step[1].GetInt32(), step[2].GetInt32(), step[3].GetInt32() })]);
        }

        if (result.Count == 0)
        {
            throw new InvalidOperationException("battle model has no motions");
        }

        return result;
    }

    /// <summary>Reads the fighter cameras: each is a camPos and a camRef triple plus a projection distance.</summary>
    /// <param name="root">Document root.</param>
    /// <returns>The views in PS1 integer units, or null for a fight stage, which has no camera.</returns>
    public static List<BattleModel.BattleCameraView>? ReadCameras(JsonElement root)
    {
        if (!root.TryGetProperty("cameras", out var cameras))
        {
            return null;
        }

        return [.. cameras.EnumerateArray().Select(camera => new BattleModel.BattleCameraView(
            ReadTriple(camera.GetProperty("camPos")),
            ReadTriple(camera.GetProperty("camRef")),
            camera.GetProperty("proj").GetInt32()))];
    }

    /// <summary>Reads a three-element integer array.</summary>
    /// <param name="element">A JSON array of at least three integers.</param>
    /// <returns>The first three values.</returns>
    public static int[] ReadTriple(JsonElement element) =>
        [element[0].GetInt32(), element[1].GetInt32(), element[2].GetInt32()];

    /// <summary>Reads one position, rotation, or scale key channel.</summary>
    /// <param name="channel">JSON array of frame and component tuples.</param>
    /// <returns>Keys stored as X, Y, Z, then frame.</returns>
    private static List<Vector4> ReadKeys(JsonElement channel) =>
        [.. channel.EnumerateArray().Select(key => new Vector4(key[1].GetSingle(), key[2].GetSingle(), key[3].GetSingle(), key[0].GetSingle()))];

    /// <summary>Reads the ambient colour and three directional lights of a stage.</summary>
    /// <param name="value">JSON object containing the stage light set.</param>
    /// <returns>The decoded light set.</returns>
    /// <exception cref="InvalidOperationException">The object does not contain exactly three lights.</exception>
    private static BattleModel.BattleLightSet ReadLightSet(JsonElement value)
    {
        var lights = new List<BattleModel.BattleLight>();
        foreach (var light in value.GetProperty("lights").EnumerateArray())
        {
            var dir = ReadTriple(light.GetProperty("dir"));
            var rgb = ReadTriple(light.GetProperty("rgb"));

            // Same handed conversion as mesh normals: PS1 Y-down/Z-forward -> Godot Y-up/-Z.
            lights.Add(new BattleModel.BattleLight(
                new Vector3(dir[0], -dir[1], -dir[2]).Normalized(),
                new Color(rgb[0] / 255f, rgb[1] / 255f, rgb[2] / 255f)));
        }

        if (lights.Count != 3)
        {
            throw new InvalidOperationException("battle stage light set is not three lights");
        }

        var ambient = ReadTriple(value.GetProperty("ambient"));
        return new BattleModel.BattleLightSet(lights, new Color(ambient[0] / 255f, ambient[1] / 255f, ambient[2] / 255f));
    }
}
