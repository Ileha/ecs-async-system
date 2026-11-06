using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace LeoECSLiteExtensions.Analyzers
{
    /// <summary>
    /// LEOECS102: an await inside <c>foreach</c> over a filter.
    /// <code>
    /// foreach (var entity in _filter)
    /// {
    ///     await UniTask.Delay(100, cancellationToken: ct); // the filter stays locked while waiting
    /// }
    /// </code>
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class AwaitInFilterLoopAnalyzer : DiagnosticAnalyzer
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
            ImmutableArray.Create(Rules.AwaitInFilterLoop);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();

            context.RegisterCompilationStartAction(compilationContext =>
            {
                var ecs = EcsSymbols.TryCreate(compilationContext.Compilation);
                if (ecs == null)
                {
                    return;
                }

                compilationContext.RegisterSyntaxNodeAction(
                    nodeContext => AnalyzeAwait(nodeContext, ecs),
                    SyntaxKind.AwaitExpression,
                    SyntaxKind.ForEachStatement,
                    SyntaxKind.ForEachVariableStatement);
            });
        }

        private static void AnalyzeAwait(SyntaxNodeAnalysisContext context, EcsSymbols ecs)
        {
            var awaitKeyword = GetAwaitKeyword(context.Node);
            if (awaitKeyword.IsKind(SyntaxKind.None))
            {
                return;
            }

            var loopsAround = context.Node.Ancestors()
                .TakeWhile(node => !AsyncCode.IsFunctionBoundary(node))
                .OfType<CommonForEachStatementSyntax>();

            foreach (var loop in loopsAround)
            {
                // `foreach (var e in await GetFilter())` waits before the loop starts, which is fine.
                if (!loop.Statement.Span.Contains(context.Node.Span))
                {
                    continue;
                }

                var collectionType = context.SemanticModel.GetTypeInfo(loop.Expression, context.CancellationToken).Type;
                if (!ecs.IsFilter(collectionType))
                {
                    continue;
                }

                context.ReportDiagnostic(Diagnostic.Create(
                    Rules.AwaitInFilterLoop,
                    awaitKeyword.GetLocation(),
                    loop.Expression.ToString()));
                return;
            }
        }

        /// <summary>The `await` keyword of `await x` or `await foreach`; default when the node does not wait.</summary>
        private static SyntaxToken GetAwaitKeyword(SyntaxNode node) => node switch
        {
            AwaitExpressionSyntax awaitExpression => awaitExpression.AwaitKeyword,
            CommonForEachStatementSyntax loop => loop.AwaitKeyword,
            _ => default
        };
    }
}
