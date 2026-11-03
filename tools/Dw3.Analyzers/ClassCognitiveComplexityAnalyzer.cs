using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Dw3.Analyzers;

/// <summary>
/// Roslyn diagnostic analyser DW3001 — class cognitive complexity limit.
///
/// Computes the total cognitive complexity of a named type by summing the per-member
/// complexity (each method, constructor, property accessor and operator) across all
/// partial declarations. Nested types count for themselves, not for their enclosing type.
/// Reports DW3001 when the total exceeds the configured threshold (default 50).
///
/// The per-member complexity follows the G. Ann Campbell specification as implemented
/// by <see cref="CognitiveComplexityWalker"/>.
///
/// Configure the threshold in <c>.editorconfig</c>:
/// <code>
/// dw3_class_cognitive_complexity_max = 50
/// </code>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ClassCognitiveComplexityAnalyzer : DiagnosticAnalyzer
{
    /// <summary>The default maximum cognitive complexity per class.</summary>
    public const int DefaultMaxComplexity = 50;

    /// <summary>The .editorconfig key that overrides <see cref="DefaultMaxComplexity"/>.</summary>
    public const string EditorConfigKey = "dw3_class_cognitive_complexity_max";

    /// <summary>The custom tag required for diagnostics reported in CompilationEndAction.</summary>
    private static readonly string[] CompilationEndTags = ["CompilationEnd"];

    /// <summary>DW3001 — class cognitive complexity exceeds the configured limit.</summary>
    private static readonly DiagnosticDescriptor Rule = new(
        id: "DW3001",
        title: "Class cognitive complexity is too high",
        messageFormat: "Class '{0}' has a cognitive complexity of {1} (maximum {2})",
        category: "Maintainability",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "The sum of the cognitive complexity of all methods, constructors "
                   + "and property accessors in the class exceeds the configured limit. "
                   + "Break the type into smaller, more focused types or extract methods.",
        helpLinkUri: "https://github.com/antharuu/dw3-remake/blob/main/docs/CODE_RULES.md#dw3001",
        customTags: CompilationEndTags);

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(RegisterPerCompilationActions);
    }

    // -------------------------------------------------------------------------
    // Complexity computation
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns the sum of cognitive complexity for every directly-declared member
    /// of <paramref name="typeDecl"/>. Nested type declarations are excluded (they are
    /// processed independently).
    /// </summary>
    /// <param name="typeDecl">The type declaration node to measure.</param>
    /// <param name="semanticModel">The semantic model for the syntax tree containing <paramref name="typeDecl"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Total cognitive complexity of all direct members.</returns>
    internal static int ComputeTypeComplexity(
        TypeDeclarationSyntax typeDecl,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        var total = 0;

        foreach (var member in typeDecl.Members)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Nested types emit DW3001 on their own RegisterSyntaxNodeAction invocation.
            if (member is TypeDeclarationSyntax)
            {
                continue;
            }

            total += MeasureMember(member, semanticModel);
        }

        return total;
    }

    // -------------------------------------------------------------------------
    // Per-compilation accumulation
    // -------------------------------------------------------------------------

    /// <summary>
    /// Registers syntax-node and compilation-end actions so that partial classes can
    /// have their complexity accumulated across files before a single diagnostic is emitted.
    /// </summary>
    /// <param name="compilationContext">The compilation-start context.</param>
    private static void RegisterPerCompilationActions(CompilationStartAnalysisContext compilationContext)
    {
        var accumulator = new ComplexityAccumulator();

        compilationContext.RegisterSyntaxNodeAction(
            accumulator.OnTypeDeclarationNode,
            SyntaxKind.ClassDeclaration,
            SyntaxKind.RecordDeclaration,
            SyntaxKind.StructDeclaration,
            SyntaxKind.RecordStructDeclaration,
            SyntaxKind.InterfaceDeclaration);

        compilationContext.RegisterCompilationEndAction(accumulator.OnCompilationEnd);
    }

    /// <summary>
    /// Computes the cognitive complexity of a single member.
    /// For properties and indexers, sums the complexity of each accessor body.
    /// </summary>
    /// <param name="member">The member declaration to measure.</param>
    /// <param name="semanticModel">The semantic model.</param>
    /// <returns>Cognitive complexity of the member.</returns>
    private static int MeasureMember(MemberDeclarationSyntax member, SemanticModel semanticModel)
    {
        switch (member)
        {
            case MethodDeclarationSyntax method:
                return WalkBody(method.Body, method.ExpressionBody, semanticModel, method);

            case ConstructorDeclarationSyntax ctor:
                return WalkBody(ctor.Body, ctor.ExpressionBody, semanticModel, ctor);

            case DestructorDeclarationSyntax dtor:
                return WalkBody(dtor.Body, dtor.ExpressionBody, semanticModel, dtor);

            case OperatorDeclarationSyntax op:
                return WalkBody(op.Body, op.ExpressionBody, semanticModel, op);

            case ConversionOperatorDeclarationSyntax conv:
                return WalkBody(conv.Body, conv.ExpressionBody, semanticModel, conv);

            case PropertyDeclarationSyntax prop:
                return MeasureAccessorListOrArrow(prop.AccessorList, prop.ExpressionBody, semanticModel);

            case IndexerDeclarationSyntax indexer:
                return MeasureAccessorListOrArrow(indexer.AccessorList, indexer.ExpressionBody, semanticModel);

            case EventDeclarationSyntax evt:
                return MeasureAccessorListOrArrow(evt.AccessorList, expressionBody: null, semanticModel);

            default:
                return 0;
        }
    }

    /// <summary>
    /// Measures complexity for a member that can have either an accessor list or an
    /// arrow expression body (properties, indexers, events).
    /// </summary>
    /// <param name="accessorList">Optional accessor list (get/set/add/remove).</param>
    /// <param name="expressionBody">Optional arrow expression body.</param>
    /// <param name="semanticModel">The semantic model.</param>
    /// <returns>Total complexity of all bodies.</returns>
    private static int MeasureAccessorListOrArrow(
        AccessorListSyntax? accessorList,
        ArrowExpressionClauseSyntax? expressionBody,
        SemanticModel semanticModel)
    {
        if (expressionBody != null)
        {
            var arrowWalker = new CognitiveComplexityWalker(semanticModel, symbol: null);
            arrowWalker.Visit(expressionBody.Expression);
            return arrowWalker.Complexity;
        }

        if (accessorList == null)
        {
            return 0;
        }

        var total = 0;
        foreach (var accessor in accessorList.Accessors)
        {
            total += WalkBody(accessor.Body, accessor.ExpressionBody, semanticModel, accessor);
        }

        return total;
    }

    /// <summary>
    /// Walks a block body or an arrow-expression body and returns its cognitive complexity.
    /// Returns zero when both are null (abstract or interface member with no body).
    /// </summary>
    /// <param name="body">Optional block body.</param>
    /// <param name="expressionBody">Optional arrow expression body.</param>
    /// <param name="semanticModel">The semantic model.</param>
    /// <param name="memberNode">The member node, used to resolve its symbol for recursion detection.</param>
    /// <returns>Cognitive complexity of the body.</returns>
    private static int WalkBody(
        BlockSyntax? body,
        ArrowExpressionClauseSyntax? expressionBody,
        SemanticModel semanticModel,
        SyntaxNode memberNode)
    {
        var symbol = semanticModel.GetDeclaredSymbol(memberNode);
        SyntaxNode? rootNode = body ?? (SyntaxNode?)expressionBody?.Expression;

        if (rootNode == null)
        {
            return 0;
        }

        var walker = new CognitiveComplexityWalker(semanticModel, symbol);
        walker.Visit(rootNode);
        return walker.Complexity;
    }

    // -------------------------------------------------------------------------
    // Per-compilation accumulator (inner type)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Thread-safe accumulator that collects per-type complexity across all partial
    /// declarations during concurrent syntax-node analysis, then reports DW3001 at
    /// compilation end.
    /// </summary>
    private sealed class ComplexityAccumulator
    {
        /// <summary>Accumulated complexity per named type symbol.</summary>
        private readonly ConcurrentDictionary<INamedTypeSymbol, int> _complexityMap =
            new ConcurrentDictionary<INamedTypeSymbol, int>(SymbolEqualityComparer.Default);

        /// <summary>
        /// Called for every type declaration node; computes and accumulates its complexity.
        /// </summary>
        /// <param name="context">The syntax-node analysis context.</param>
        public void OnTypeDeclarationNode(SyntaxNodeAnalysisContext context)
        {
            var node = (TypeDeclarationSyntax)context.Node;

            // Nested types are reported on their own invocation.
            if (node.Parent is TypeDeclarationSyntax)
            {
                return;
            }

            if (context.SemanticModel.GetDeclaredSymbol(node, context.CancellationToken)
                    is not INamedTypeSymbol symbol)
            {
                return;
            }

            var complexity = ComputeTypeComplexity(node, context.SemanticModel, context.CancellationToken);

            _complexityMap.AddOrUpdate(symbol, complexity, (_, existing) => existing + complexity);
        }

        /// <summary>
        /// Called once at the end of compilation; reports DW3001 for every type whose
        /// accumulated complexity exceeds the configured threshold.
        /// </summary>
        /// <param name="context">The compilation-end analysis context.</param>
        public void OnCompilationEnd(CompilationAnalysisContext context)
        {
            foreach (var entry in _complexityMap)
            {
                context.CancellationToken.ThrowIfCancellationRequested();

                var symbol = entry.Key;
                var totalComplexity = entry.Value;

                var threshold = ReadThreshold(context.Options, symbol);

                if (totalComplexity <= threshold)
                {
                    continue;
                }

                var location = GetMainLocation(symbol, context.CancellationToken);
                context.ReportDiagnostic(Diagnostic.Create(
                    Rule,
                    location,
                    symbol.Name,
                    totalComplexity,
                    threshold));
            }
        }

        /// <summary>
        /// Returns the identifier location of the first partial declaration of
        /// <paramref name="symbol"/>, or <see cref="Location.None"/> when unavailable.
        /// </summary>
        /// <param name="symbol">The named type symbol.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The location to attach the diagnostic to.</returns>
        private static Location GetMainLocation(
            INamedTypeSymbol symbol,
            System.Threading.CancellationToken cancellationToken)
        {
            if (symbol.DeclaringSyntaxReferences.IsEmpty)
            {
                return Location.None;
            }

            var mainSyntax = symbol.DeclaringSyntaxReferences[0]
                .GetSyntax(cancellationToken) as TypeDeclarationSyntax;

            return mainSyntax?.Identifier.GetLocation() ?? Location.None;
        }

        /// <summary>
        /// Reads the configured threshold from <c>.editorconfig</c> for the primary
        /// source file of <paramref name="symbol"/>. Falls back to
        /// <see cref="DefaultMaxComplexity"/> when the key is absent or invalid.
        /// </summary>
        /// <param name="options">Analyzer options (carries the config-options provider).</param>
        /// <param name="symbol">The type symbol whose primary file is queried.</param>
        /// <returns>The configured complexity threshold.</returns>
        private static int ReadThreshold(AnalyzerOptions options, INamedTypeSymbol symbol)
        {
            foreach (var syntaxRef in symbol.DeclaringSyntaxReferences)
            {
                var configOptions = options.AnalyzerConfigOptionsProvider
                    .GetOptions(syntaxRef.SyntaxTree);

                if (configOptions.TryGetValue(EditorConfigKey, out var raw)
                    && int.TryParse(raw, out var parsed)
                    && parsed >= 0)
                {
                    return parsed;
                }
            }

            return DefaultMaxComplexity;
        }
    }
}
