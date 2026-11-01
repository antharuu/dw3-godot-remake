using System.Buffers.Binary;

namespace Dw3.Formats.Battle;

/// <summary>
/// Decodes the descriptor and meshes of a battle model file (fighters and fight stages share
/// this layout).
/// </summary>
/// <remarks>
/// The model file is a <see cref="ModelArchive"/>. Its descriptor entry is:
/// <code>
/// u32 textureEntry          archive entry of the texture archive
/// u32 lastBone              bone count - 1 (bone count 2..64)
/// (lastBone) x { u32 parent; u32 meshEntry; u32 keyEntry }   for bones 1..lastBone
/// </code>
/// Bone 0 is the implicit root. Each parent must name an earlier bone, and every entry
/// number must exist in the archive. Mesh entries follow <see cref="BattleMeshDecoder"/>.
/// </remarks>
public static class BattleModelDecoder
{
    private const int MinimumBones = 2;
    private const int MaximumBones = 64;
    private const int BoneRecordBytes = 12;

    /// <summary>Decodes the model whose descriptor is archive entry <paramref name="modelEntry"/>.</summary>
    /// <exception cref="FormatException">The archive, descriptor or a mesh is malformed.</exception>
    public static BattleModelStructure Decode(byte[] data, int modelEntry)
    {
        ArgumentNullException.ThrowIfNull(data);
        IReadOnlyList<ArchiveRange> bounds = ModelArchive.ReadBounds(data);
        if (modelEntry < 0 || modelEntry >= bounds.Count)
            throw new FormatException($"Model archive entry {modelEntry} is absent.");
        (int start, int end) = (bounds[modelEntry].Start, bounds[modelEntry].End);
        if (start + 8 > end)
            throw new FormatException("Model descriptor overruns its archive entry.");
        uint textureEntry = ReadU32(data, start);
        uint lastBone = ReadU32(data, start + 4);
        if (textureEntry >= bounds.Count)
            throw new FormatException($"Model texture archive entry outside file: {textureEntry}.");
        long boneCount = (long)lastBone + 1;
        if (boneCount < MinimumBones || boneCount > MaximumBones)
            throw new FormatException($"Invalid bone count {boneCount}.");
        if (start + 8 + (boneCount - 1) * BoneRecordBytes > end)
        {
            throw new FormatException("Model descriptor overruns its archive entry.");
        }

        var bones = new List<BattleModelBone> { new(0, null, null, null) };
        for (int index = 1; index < boneCount; index++)
        {
            int record = start + 8 + (index - 1) * BoneRecordBytes;
            uint parent = ReadU32(data, record);
            uint mesh = ReadU32(data, record + 4);
            uint key = ReadU32(data, record + 8);
            if (parent >= index)
                throw new FormatException($"bone {index}: parent {parent} is not an earlier bone");
            if (mesh >= bounds.Count || key >= bounds.Count)
                throw new FormatException($"bone {index}: source entry outside archive");
            bones.Add(new BattleModelBone(index, (int)parent, (int)mesh, (int)key));
        }

        var meshes = new List<BattleMesh>(bones.Count - 1);
        foreach (BattleModelBone bone in bones.Skip(1))
        {
            ArchiveRange range = bounds[bone.MeshEntry!.Value];
            meshes.Add(BattleMeshDecoder.Decode(data, range.Start, range.End, bone.Index));
        }
        return new BattleModelStructure(modelEntry, (int)textureEntry, bones, meshes);
    }

    /// <summary>Decodes all model assets. Both archive references may point into the same disc file.</summary>
    public static BattleModelPayload DecodeComplete(
        byte[] modelData, int modelEntry, byte[] motionData, int motionEntry, BattleFighterMetadata metadata)
    {
        BattleModelStructure structure = Decode(modelData, modelEntry);
        IReadOnlyList<ArchiveRange> modelBounds = ModelArchive.ReadBounds(modelData);
        var tracks = new Dictionary<int, BattleBoneTracks>();
        foreach (BattleModelBone bone in structure.Bones.Skip(1))
        {
            ArchiveRange range = modelBounds[bone.KeyEntry!.Value];
            tracks.Add(bone.Index, BattleAnimationDecoder.DecodeTracks(modelData, range.Start, range.End, bone.Index));
        }
        ArchiveRange textureRange = modelBounds[structure.TextureEntry];
        byte[] textureArchive = modelData[textureRange.Start..textureRange.End];
        IEnumerable<MeshTextureState> consumers = structure.Meshes.SelectMany(mesh => mesh.Polygons)
            .Where(polygon => polygon.Uvs.Count > 0)
            .Select(polygon => polygon.Texture);
        BattleTextureSet textures = BattleTextureDecoder.Decode(textureArchive, consumers);

        IReadOnlyList<ArchiveRange> motionBounds = ReferenceEquals(modelData, motionData) ? modelBounds : ModelArchive.ReadBounds(motionData);
        if (motionEntry < 0 || motionEntry >= motionBounds.Count || motionBounds[motionEntry].Start == 0)
            throw new FormatException($"Motion archive entry {motionEntry} is absent.");
        ArchiveRange motionRange = motionBounds[motionEntry];
        IReadOnlyList<IReadOnlyList<BattleMotionStep>> motions = BattleAnimationDecoder.DecodeMotions(motionData, motionRange.Start, motionRange.End);
        if (motions[0][0].Index != 0)
            throw new FormatException($"motion 1 does not start at index 0: {motions[0][0]}");
        return new BattleModelPayload(structure, tracks, textures, motions, metadata);
    }

    /// <summary>
    /// Decodes a fight-stage model (model and motion in the same archive file).
    /// Fight stages use the same model format as fighter models but have no fighter metadata
    /// (cameras, geometry, face); the returned payload's Metadata is a placeholder.
    /// </summary>
    /// <param name="modelData">Raw bytes of the model/motion archive file.</param>
    /// <param name="modelEntry">Archive entry of the model mesh data.</param>
    /// <param name="motionEntry">Archive entry of the animation data (from FightStageInfo).</param>
    public static BattleModelPayload DecodeStage(byte[] modelData, int modelEntry, int motionEntry)
    {
        BattleModelStructure structure = Decode(modelData, modelEntry);
        IReadOnlyList<ArchiveRange> bounds = ModelArchive.ReadBounds(modelData);
        var tracks = new Dictionary<int, BattleBoneTracks>();
        foreach (BattleModelBone bone in structure.Bones.Skip(1))
        {
            ArchiveRange range = bounds[bone.KeyEntry!.Value];
            tracks.Add(bone.Index, BattleAnimationDecoder.DecodeTracks(modelData, range.Start, range.End, bone.Index));
        }
        ArchiveRange textureRange = bounds[structure.TextureEntry];
        byte[] textureArchive = modelData[textureRange.Start..textureRange.End];
        IEnumerable<MeshTextureState> consumers = structure.Meshes.SelectMany(mesh => mesh.Polygons)
            .Where(polygon => polygon.Uvs.Count > 0)
            .Select(polygon => polygon.Texture);
        BattleTextureSet textures = BattleTextureDecoder.Decode(textureArchive, consumers);

        if (motionEntry < 0 || motionEntry >= bounds.Count || bounds[motionEntry].Start == 0)
            throw new FormatException($"Fight stage motion archive entry {motionEntry} is absent.");
        ArchiveRange motionRange = bounds[motionEntry];
        IReadOnlyList<IReadOnlyList<BattleMotionStep>> motions = BattleAnimationDecoder.DecodeMotions(modelData, motionRange.Start, motionRange.End);
        if (motions[0][0].Index != 0)
        {
            throw new FormatException($"Fight stage motion 1 does not start at index 0: {motions[0][0]}");
        }

        // Fight stages carry no fighter metadata (no cameras, geometry or face animation).
        var emptyMetadata = new BattleFighterMetadata(
            [], new BattleGeometry(0, 0),
            new BattleFace(new BattleFaceSource(0, 0, 0, 0, string.Empty), []));
        return new BattleModelPayload(structure, tracks, textures, motions, emptyMetadata);
    }

    private static uint ReadU32(byte[] data, int at) => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(at, 4));
}
