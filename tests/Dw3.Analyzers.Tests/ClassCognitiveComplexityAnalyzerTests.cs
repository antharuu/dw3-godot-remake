using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace Dw3.Analyzers.Tests;

/// <summary>
/// Verifies DW3001 — class cognitive complexity limit.
///
/// Each test calls <see cref="GetRawDiagnosticsAsync"/> to build an in-process
/// <see cref="CSharpCompilation"/>, run the analyser and collect diagnostics.
/// <see cref="MeasureAsync"/> runs with threshold 0 so any non-zero complexity
/// triggers DW3001 and the exact value can be read from the message.
///
/// Test coverage:
/// <list type="bullet">
/// <item>Individual constructs: if/else-if/else, ternary, switch, loops, catch.</item>
/// <item>Nesting increments (spec examples from G. Ann Campbell).</item>
/// <item>Logical operator sequences (same operator = one group).</item>
/// <item>Lambda nesting increment.</item>
/// <item>Partial classes — complexity accumulates across all declarations.</item>
/// <item>Nested types — counted independently, not added to enclosing type.</item>
/// <item>Threshold trigger — DW3001 fires when total exceeds limit.</item>
/// <item>Configurable threshold — DW3001 fires at threshold 0 for complexity 1.</item>
/// </list>
/// </summary>
public sealed class ClassCognitiveComplexityAnalyzerTests
{
    // -------------------------------------------------------------------------
    // Individual constructs
    // -------------------------------------------------------------------------

    [Fact]
    public async Task EmptyMethod_ZeroComplexity()
    {
        var c = await MeasureAsync(@"
public class C
{
    public void M() { }
}");
        Assert.Equal(0, c);
    }

    [Fact]
    public async Task SingleIf_OneComplexity()
    {
        var c = await MeasureAsync(@"
public class C
{
    public void M(int x) { if (x > 0) { } }
}");
        Assert.Equal(1, c);
    }

    [Fact]
    public async Task IfElseIfElse_ThreeComplexity()
    {
        // if: +1, else-if: +1 (flat), else: +1 (flat) → total 3
        var c = await MeasureAsync(@"
public class C
{
    public void M(int x)
    {
        if (x > 0) { }
        else if (x < 0) { }
        else { }
    }
}");
        Assert.Equal(3, c);
    }

    [Fact]
    public async Task NestedIf_NestingIncrement()
    {
        // outer if: +1+0=1, inner if: +1+1=2 → total 3
        var c = await MeasureAsync(@"
public class C
{
    public void M(int x, int y)
    {
        if (x > 0)
        {
            if (y > 0) { }
        }
    }
}");
        Assert.Equal(3, c);
    }

    [Fact]
    public async Task NestedTernary_NestingIncrement()
    {
        // outer ternary: +1+0=1, inner ternary: +1+1=2 → total 3
        var c = await MeasureAsync(@"
public class C
{
    public int M(int x, int y) => x > 0 ? (y > 0 ? y : -y) : 0;
}");
        Assert.Equal(3, c);
    }

    [Fact]
    public async Task LogicalAndSequence_OneGroup()
    {
        // a && b && c → one && sequence → +1
        var c = await MeasureAsync(@"
public class C
{
    public bool M(bool a, bool b, bool cc) => a && b && cc;
}");
        Assert.Equal(1, c);
    }

    [Fact]
    public async Task MixedLogicalOperators_TwoGroups()
    {
        // a && b || c → one && group (+1) and one || group (+1) → total 2
        var c = await MeasureAsync(@"
public class C
{
    public bool M(bool a, bool b, bool cc) => a && b || cc;
}");
        Assert.Equal(2, c);
    }

    [Fact]
    public async Task SwitchStatement_OneNested()
    {
        // switch: +1+0=1, case labels add nothing
        var c = await MeasureAsync(@"
public class C
{
    public void M(int x)
    {
        switch (x)
        {
            case 1: break;
            case 2: break;
            default: break;
        }
    }
}");
        Assert.Equal(1, c);
    }

    [Fact]
    public async Task SwitchExpression_OneNested()
    {
        var c = await MeasureAsync(@"
public class C
{
    public string M(int x) => x switch { 1 => ""a"", 2 => ""b"", _ => ""c"" };
}");
        Assert.Equal(1, c);
    }

    [Fact]
    public async Task ForLoop_OneNested()
    {
        var c = await MeasureAsync(@"
public class C
{
    public void M()
    {
        for (int i = 0; i < 10; i++) { }
    }
}");
        Assert.Equal(1, c);
    }

    [Fact]
    public async Task WhileLoop_OneNested()
    {
        var c = await MeasureAsync(@"
public class C
{
    public void M(bool cond)
    {
        while (cond) { }
    }
}");
        Assert.Equal(1, c);
    }

    [Fact]
    public async Task DoWhileLoop_OneNested()
    {
        var c = await MeasureAsync(@"
public class C
{
    public void M(bool cond)
    {
        do { } while (cond);
    }
}");
        Assert.Equal(1, c);
    }

    [Fact]
    public async Task ForEachLoop_OneNested()
    {
        var c = await MeasureAsync(@"
using System.Collections.Generic;
public class C
{
    public void M(IEnumerable<int> items)
    {
        foreach (var item in items) { }
    }
}");
        Assert.Equal(1, c);
    }

    [Fact]
    public async Task CatchClause_OneNested()
    {
        var c = await MeasureAsync(@"
public class C
{
    public void M()
    {
        try { }
        catch (System.Exception) { }
    }
}");
        Assert.Equal(1, c);
    }

    [Fact]
    public async Task Lambda_AddsOneAndNestingForBody()
    {
        // lambda: +1+0=1, if inside lambda at nesting=1: +1+1=2 → total 3
        var c = await MeasureAsync(@"
using System;
public class C
{
    public void M()
    {
        Action<int> fn = x => { if (x > 0) { } };
    }
}");
        Assert.Equal(3, c);
    }

    // -------------------------------------------------------------------------
    // Partial classes
    // -------------------------------------------------------------------------

    [Fact]
    public async Task PartialClass_SumsAcrossDeclarations()
    {
        var c = await MeasureAsync(@"
public partial class C
{
    public void M1(int x) { if (x > 0) { } }
}
public partial class C
{
    public void M2(bool b) { while (b) { } }
}");
        Assert.Equal(2, c);
    }

    // -------------------------------------------------------------------------
    // Nested types
    // -------------------------------------------------------------------------

    [Fact]
    public async Task NestedType_NotCountedInParent()
    {
        // Outer has no complex members; complexity for Inner is excluded.
        var c = await MeasureAsync(@"
public class Outer
{
    public void M() { }
    public class Inner
    {
        public void N(int x) { if (x > 0) { } }
    }
}");
        Assert.Equal(0, c);
    }

    // -------------------------------------------------------------------------
    // Threshold trigger
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExceedsDefaultThreshold_EmitsDw3001()
    {
        // 18 methods × (if +1, else-if +1, else +1) = 54 > 50
        var methods = string.Concat(
            Enumerable.Range(0, 18).Select(i =>
                $"    public void M{i}(int x) {{ if (x > 0) {{ }} else if (x < 0) {{ }} else {{ }} }}\n"));

        var source = $@"
public class HighComplexity
{{
{methods}
}}";
        var diagnostics = await GetRawDiagnosticsAsync(source, threshold: 50);
        Assert.Contains(diagnostics, d => d.Id == "DW3001");
    }

    [Fact]
    public async Task AtDefaultThreshold_NoDiagnostic()
    {
        // 16 methods × 3 = 48 ≤ 50 → no DW3001
        var methods = string.Concat(
            Enumerable.Range(0, 16).Select(i =>
                $"    public void M{i}(int x) {{ if (x > 0) {{ }} else if (x < 0) {{ }} else {{ }} }}\n"));

        var source = $@"
public class EdgeComplexity
{{
{methods}
}}";
        var diagnostics = await GetRawDiagnosticsAsync(source, threshold: 50);
        Assert.DoesNotContain(diagnostics, d => d.Id == "DW3001");
    }

    // -------------------------------------------------------------------------
    // Configurable threshold
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ConfigurableThreshold_ZeroFiresOnComplexityOne()
    {
        // threshold = 0 → complexity 1 triggers DW3001
        const string source = @"
public class C
{
    public void M(int x) { if (x > 0) { } }
}";

        var diagnostics = await GetRawDiagnosticsAsync(source, threshold: 0);
        var dw3001 = diagnostics.Where(d => d.Id == "DW3001").ToList();

        Assert.Single(dw3001);

        var msg = dw3001[0].GetMessage(System.Globalization.CultureInfo.InvariantCulture);
        Assert.Contains("'C'", msg, System.StringComparison.Ordinal);
        Assert.Contains("complexity of 1", msg, System.StringComparison.Ordinal);
        Assert.Contains("maximum 0", msg, System.StringComparison.Ordinal);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Measures the cognitive complexity of the first type in <paramref name="source"/>
    /// by running DW3001 with threshold 0 and parsing the emitted diagnostic.
    /// Returns zero when no DW3001 is emitted (complexity is zero).
    /// </summary>
    private static async Task<int> MeasureAsync(string source)
    {
        var diagnostics = await GetRawDiagnosticsAsync(source, threshold: 0);
        var dw3001 = diagnostics.Where(d => d.Id == "DW3001").ToList();

        if (dw3001.Count == 0)
        {
            return 0;
        }

        // Message: "Class '{name}' has a cognitive complexity of {N} (maximum {M})"
        var msg = dw3001[0].GetMessage(System.Globalization.CultureInfo.InvariantCulture);
        const string marker = "cognitive complexity of ";
        var start = msg.IndexOf(marker, System.StringComparison.Ordinal) + marker.Length;
        var end = msg.IndexOf(' ', start);
        return int.Parse(msg.AsSpan(start, end - start), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Compiles <paramref name="source"/> with DW3001 at the given <paramref name="threshold"/>
    /// and returns the resulting analyzer diagnostics.
    /// </summary>
    private static async Task<IReadOnlyList<Diagnostic>> GetRawDiagnosticsAsync(
        string source,
        int threshold)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, path: "Test.cs");

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            [syntaxTree],
            [
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Action).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Collections.Generic.IEnumerable<>).Assembly.Location),
            ],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var analyzerOptions = new AnalyzerOptions(
            [],
            new ConstantAnalyzerConfigOptionsProvider(
                "Test.cs",
                ClassCognitiveComplexityAnalyzer.EditorConfigKey,
                threshold.ToString(System.Globalization.CultureInfo.InvariantCulture)));

        var withAnalyzers = compilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(new ClassCognitiveComplexityAnalyzer()),
            analyzerOptions);

        return await withAnalyzers.GetAllDiagnosticsAsync();
    }
}
