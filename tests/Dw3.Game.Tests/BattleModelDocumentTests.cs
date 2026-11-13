using System.Text.Json;
using Dw3Production.Presentation;
using Xunit;

namespace Dw3.Game.Tests;

public sealed class BattleModelDocumentTests
{
    [Fact]
    public void ValidateAcceptsPreparedEffectModelContract()
    {
        using JsonDocument document = JsonDocument.Parse("""
            {
              "format": "dw3-battle-effect-model-v1",
              "source_invariants": {
                "motion_1_first_step": [0, 1, 0, 0],
                "motion_1_terminator": [32767, 0, 0, 0]
              }
            }
            """);

        BattleModelDocument.Validate(document.RootElement);
    }
}
