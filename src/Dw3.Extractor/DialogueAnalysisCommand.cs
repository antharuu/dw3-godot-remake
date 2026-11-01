using System.Text.Json;
using Dw3.Disc;
using Dw3.Formats.Dialogue;
using Dw3.Formats.Field;

namespace Dw3.Extractor;

/// <summary>
/// <c>analyze-dialogue --disc &lt;cue&gt; [--details]</c>: runs the stage dialogue analysis on the disc
/// and prints a summary, one line per failed stage and, with <c>--details</c>, one JSON line per
/// analysed stage. Nothing is written to disk.
/// </summary>
internal static class DialogueAnalysisCommand
{
    public static int Run(IReadOnlyList<string> arguments, TextWriter output, TextWriter error)
    {
        string? cuePath = null;
        bool details = false;
        for (int index = 1; index < arguments.Count; index++)
        {
            switch (arguments[index])
            {
                case "--disc" when index + 1 < arguments.Count:
                    cuePath = arguments[++index];
                    break;
                case "--details":
                    details = true;
                    break;
                default:
                    error.WriteLine($"Unknown analyze-dialogue option: {arguments[index]}");
                    error.WriteLine("Usage: Dw3.Extractor analyze-dialogue --disc <path/to/disc.cue> [--details]");
                    return 2;
            }
        }
        if (string.IsNullOrWhiteSpace(cuePath))
        {
            error.WriteLine("Usage: Dw3.Extractor analyze-dialogue --disc <path/to/disc.cue> [--details]");
            return 2;
        }

        try
        {
            using DiscSource disc = DiscSource.Open(cuePath);
            disc.IdentifyEdition();
            RomAnalysisResult result = DiscDialogueAnalysisReader.Read(disc, disc.ListFiles());
            Report(result, output, details);
            return 0;
        }
        catch (DiscSourceException exception)
        {
            error.WriteLine($"Error [{exception.ErrorCode}]: {exception.Message}");
            return 1;
        }
        catch (Exception exception) when (exception is FormatException or InvalidDataException)
        {
            error.WriteLine($"Error [DIALOGUE_ANALYSIS_FAILED]: {exception.Message}");
            return 1;
        }
    }

    /// <summary>Writes the summary line, the failures and optionally the per-stage details.</summary>
    internal static void Report(RomAnalysisResult result, TextWriter output, bool details)
    {
        int total = result.Stages.Count + result.Failures.Count;
        int withoutActors = result.Stages.Count(stage => stage.ActorListAddress is null);
        int actors = result.Stages.Sum(stage => stage.Actors.Count);
        int talkTables = result.Stages.Sum(stage => stage.Actors.Count(actor => actor.Entry.TalksPtr != 0));
        output.WriteLine(
            $"dialogue-analysis stages={total} analysed={result.Stages.Count} failed={result.Failures.Count} " +
            $"no_actor_list={withoutActors} actors={actors} talk_tables={talkTables} " +
            $"progress_ranges={result.Progress.Ranges.Count} special_records={result.Progress.SpecialRecordCount} " +
            $"progress_specials={result.Progress.SpecialProgressRanges.Count}");
        foreach (StageAnalysisException failure in result.Failures)
            output.WriteLine($"failed {failure.Message}");
        if (details)
            foreach (StageDialogueAnalysis stage in result.Stages)
                output.WriteLine(StageJson(stage));
    }

    private static string StageJson(StageDialogueAnalysis stage)
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteNumber("mode", stage.Mode);
            json.WriteNumber("stage", stage.StageId);
            json.WriteNumber("textSelector", stage.TextFile.Selector);
            json.WriteNumber("textFileId", stage.TextFile.FileId);
            json.WriteString("textFile", stage.TextFile.IsoPath);
            json.WriteNumber("talkFile", stage.TextFile.TalkIndex);
            if (stage.ActorListAddress is uint list)
                json.WriteString("actorList", $"0x{list:X8}");
            else
                json.WriteNull("actorList");
            json.WriteStartArray("actors");
            foreach (StageActorDialogue actor in stage.Actors)
            {
                json.WriteStartObject();
                json.WriteNumber("id", actor.Index);
                json.WriteString("address", $"0x{actor.Address:X8}");
                json.WriteNumber("character", actor.Entry.ActorId);
                json.WriteNumber("x", actor.Entry.X);
                json.WriteNumber("y", actor.Entry.Y);
                json.WriteNumber("dir", actor.Entry.Direction);
                json.WriteNumber("width", actor.Width);
                json.WriteString("talkTable", $"0x{actor.Entry.TalksPtr:X8}");
                json.WriteStartArray("talks");
                foreach (FieldTalk talk in actor.Talks)
                {
                    json.WriteStartObject();
                    json.WriteNumber("entry", talk.Entry);
                    WritePairs(json, "conditions", talk.Conditions);
                    WritePairs(json, "actions", talk.Actions);
                    json.WriteEndObject();
                }
                json.WriteEndArray();
                json.WriteEndObject();
            }
            json.WriteEndArray();
            json.WriteEndObject();
        }
        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static void WritePairs(Utf8JsonWriter json, string name, IReadOnlyList<NpcCondition> pairs)
    {
        json.WriteStartArray(name);
        foreach (NpcCondition pair in pairs)
        {
            json.WriteStartArray();
            json.WriteNumberValue(pair.Code);
            json.WriteNumberValue(pair.Value);
            json.WriteEndArray();
        }
        json.WriteEndArray();
    }
}
