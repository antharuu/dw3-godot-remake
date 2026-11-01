using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Dw3.Analyzers;

/// <summary>
/// Walks a single member body and computes its cognitive complexity following
/// G. Ann Campbell's public specification (SonarSource, "Cognitive Complexity,
/// a new way of measuring understandability").
///
/// Rules implemented:
/// <list type="bullet">
/// <item>+1 per structural increment (if, switch, loop, catch, ternary) plus one per nesting level.</item>
/// <item>+1 flat for else / else-if (continuations — no nesting applied).</item>
/// <item>+1 per sequence of identical binary logical operators (&amp;&amp;, ||).</item>
/// <item>+1 for a direct recursive call to the analysed member.</item>
/// <item>+1 for each lambda / anonymous method / local function (adds one nesting level for its body).</item>
/// </list>
/// </summary>
internal sealed class CognitiveComplexityWalker : CSharpSyntaxWalker
{
    /// <summary>The semantic model for the syntax tree being walked.</summary>
    private readonly SemanticModel _semanticModel;

    /// <summary>Symbol of the member being measured, used for recursion detection.</summary>
    private readonly ISymbol? _memberSymbol;

    /// <summary>Current nesting depth; increases on entry to nesting constructs, decreases on exit.</summary>
    private int _nesting;

    /// <summary>Accumulated cognitive complexity for the member.</summary>
    private int _complexity;

    /// <summary>
    /// Initializes a new instance of <see cref="CognitiveComplexityWalker"/>.
    /// </summary>
    /// <param name="semanticModel">Semantic model for the compilation.</param>
    /// <param name="symbol">Symbol of the member being analysed; used for recursion detection. May be null.</param>
    internal CognitiveComplexityWalker(SemanticModel semanticModel, ISymbol? symbol)
    {
        _semanticModel = semanticModel;
        _memberSymbol = symbol;
    }

    /// <summary>Gets the cognitive complexity computed so far.</summary>
    internal int Complexity => _complexity;

    // -------------------------------------------------------------------------
    // Static helpers (placed first per SA1204)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="node"/> is the right-hand operand
    /// of a binary expression with the same operator kind, meaning it is part of a sequence
    /// already counted at the outer node.
    /// </summary>
    /// <param name="node">The binary expression node to test.</param>
    /// <returns>True when the node is nested inside a same-operator sequence.</returns>
    private static bool IsNestedInSameOperator(BinaryExpressionSyntax node)
    {
        return node.Parent is BinaryExpressionSyntax parent
               && parent.Kind() == node.Kind();
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="node"/> is the right-hand operand
    /// of a binary pattern with the same combinator kind, meaning it is part of a sequence
    /// already counted at the outer node.
    /// </summary>
    /// <param name="node">The binary pattern node to test.</param>
    /// <returns>True when the node is nested inside a same-combinator pattern sequence.</returns>
    private static bool IsNestedInSamePattern(BinaryPatternSyntax node)
    {
        return node.Parent is BinaryPatternSyntax parent
               && parent.Kind() == node.Kind();
    }

    // -------------------------------------------------------------------------
    // Conditionals
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    public override void VisitIfStatement(IfStatementSyntax node)
    {
        var isElseIf = node.Parent is ElseClauseSyntax;

        if (isElseIf)
        {
            // else-if is a linear continuation: +1, no nesting change.
            _complexity++;
            VisitIfChildren(node);
        }
        else
        {
            // Regular if: structural increment + current nesting.
            _complexity += 1 + _nesting;
            _nesting++;
            VisitIfChildren(node);
            _nesting--;
        }
    }

    /// <inheritdoc/>
    public override void VisitElseClause(ElseClauseSyntax node)
    {
        if (node.Statement is IfStatementSyntax)
        {
            // else-if branch: VisitIfStatement handles the +1.
            Visit(node.Statement);
        }
        else
        {
            // Pure else: +1, no nesting change; body visited at current nesting.
            _complexity++;
            Visit(node.Statement);
        }
    }

    /// <inheritdoc/>
    public override void VisitConditionalExpression(ConditionalExpressionSyntax node)
    {
        // Ternary ?: structural increment + current nesting, body at increased nesting.
        _complexity += 1 + _nesting;
        _nesting++;
        Visit(node.Condition);
        Visit(node.WhenTrue);
        Visit(node.WhenFalse);
        _nesting--;
    }

    // -------------------------------------------------------------------------
    // Switch
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    public override void VisitSwitchStatement(SwitchStatementSyntax node)
    {
        _complexity += 1 + _nesting;
        _nesting++;
        Visit(node.Expression);
        foreach (var section in node.Sections)
        {
            Visit(section);
        }

        _nesting--;
    }

    /// <inheritdoc/>
    public override void VisitSwitchExpression(SwitchExpressionSyntax node)
    {
        // C# 8+ switch expression (pattern-matching).
        _complexity += 1 + _nesting;
        _nesting++;
        Visit(node.GoverningExpression);
        foreach (var arm in node.Arms)
        {
            Visit(arm);
        }

        _nesting--;
    }

    // -------------------------------------------------------------------------
    // Loops
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    public override void VisitForStatement(ForStatementSyntax node)
    {
        _complexity += 1 + _nesting;
        _nesting++;
        base.VisitForStatement(node);
        _nesting--;
    }

    /// <inheritdoc/>
    public override void VisitForEachStatement(ForEachStatementSyntax node)
    {
        _complexity += 1 + _nesting;
        _nesting++;
        base.VisitForEachStatement(node);
        _nesting--;
    }

    /// <inheritdoc/>
    public override void VisitWhileStatement(WhileStatementSyntax node)
    {
        _complexity += 1 + _nesting;
        _nesting++;
        base.VisitWhileStatement(node);
        _nesting--;
    }

    /// <inheritdoc/>
    public override void VisitDoStatement(DoStatementSyntax node)
    {
        _complexity += 1 + _nesting;
        _nesting++;
        base.VisitDoStatement(node);
        _nesting--;
    }

    // -------------------------------------------------------------------------
    // Exception handling
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    public override void VisitCatchClause(CatchClauseSyntax node)
    {
        _complexity += 1 + _nesting;
        _nesting++;
        base.VisitCatchClause(node);
        _nesting--;
    }

    // -------------------------------------------------------------------------
    // Jumps to labels
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    public override void VisitGotoStatement(GotoStatementSyntax node)
    {
        // Only bare "goto <label>" adds complexity.
        // "goto case" and "goto default" are flow within a switch and are not counted.
        if (node.Kind() == SyntaxKind.GotoStatement)
        {
            _complexity++;
        }

        base.VisitGotoStatement(node);
    }

    // -------------------------------------------------------------------------
    // Logical operator sequences
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    public override void VisitBinaryExpression(BinaryExpressionSyntax node)
    {
        var kind = node.Kind();
        var isLogical = kind == SyntaxKind.LogicalAndExpression
                     || kind == SyntaxKind.LogicalOrExpression;

        if (isLogical && !IsNestedInSameOperator(node))
        {
            // Count only the root of each sequence of identical operators.
            _complexity++;
        }

        base.VisitBinaryExpression(node);
    }

    /// <summary>
    /// Handles C# 9+ binary pattern combinators (<c>and</c> / <c>or</c> inside
    /// <c>is</c> or <c>switch</c> patterns).
    /// Sonar S3776 treats these the same as <c>&amp;&amp;</c> / <c>||</c>:
    /// each distinct sequence of the same combinator adds one increment.
    /// </summary>
    public override void VisitBinaryPattern(BinaryPatternSyntax node)
    {
        var kind = node.Kind();
        var isLogical = kind == SyntaxKind.AndPattern || kind == SyntaxKind.OrPattern;

        if (isLogical && !IsNestedInSamePattern(node))
        {
            _complexity++;
        }

        base.VisitBinaryPattern(node);
    }

    // -------------------------------------------------------------------------
    // Recursion
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    public override void VisitInvocationExpression(InvocationExpressionSyntax node)
    {
        if (_memberSymbol != null)
        {
            var calledSymbol = _semanticModel.GetSymbolInfo(node).Symbol;
            if (calledSymbol != null
                && SymbolEqualityComparer.Default.Equals(calledSymbol, _memberSymbol))
            {
                _complexity++;
            }
        }

        base.VisitInvocationExpression(node);
    }

    // -------------------------------------------------------------------------
    // Nesting: lambdas and local functions
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    public override void VisitSimpleLambdaExpression(SimpleLambdaExpressionSyntax node)
    {
        // Lambda expressions increase the nesting level for their body, but do NOT add a
        // structural increment to the enclosing member (unlike Java nested classes).
        // Sonar S3776 for C# does not count the lambda itself, only control flow inside it.
        _nesting++;
        base.VisitSimpleLambdaExpression(node);
        _nesting--;
    }

    /// <inheritdoc/>
    public override void VisitParenthesizedLambdaExpression(ParenthesizedLambdaExpressionSyntax node)
    {
        _nesting++;
        base.VisitParenthesizedLambdaExpression(node);
        _nesting--;
    }

    /// <inheritdoc/>
    public override void VisitAnonymousMethodExpression(AnonymousMethodExpressionSyntax node)
    {
        _nesting++;
        base.VisitAnonymousMethodExpression(node);
        _nesting--;
    }

    /// <inheritdoc/>
    public override void VisitLocalFunctionStatement(LocalFunctionStatementSyntax node)
    {
        // Local functions increase nesting for their body, but do NOT add a structural
        // increment (consistent with Sonar S3776 C# behaviour for nested callables).
        _nesting++;
        base.VisitLocalFunctionStatement(node);
        _nesting--;
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    /// <summary>Visits the children of an if-statement manually to control nesting.</summary>
    /// <param name="node">The if-statement node whose children to visit.</param>
    private void VisitIfChildren(IfStatementSyntax node)
    {
        Visit(node.Condition);
        Visit(node.Statement);
        if (node.Else != null)
        {
            Visit(node.Else);
        }
    }
}
