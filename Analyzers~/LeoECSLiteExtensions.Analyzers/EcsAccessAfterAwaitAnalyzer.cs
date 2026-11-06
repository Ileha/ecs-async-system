using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace LeoECSLiteExtensions.Analyzers
{
    /// <summary>
    /// LEOECS101: an async function that does not return EcsTask touches ECS after an await.
    /// <code>
    /// async UniTask Helper(CancellationToken ct)
    /// {
    ///     await UniTask.Delay(100, cancellationToken: ct);
    ///     _pool.Get(entity); // runs in the PlayerLoop, not in the system Run
    /// }
    /// </code>
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class EcsAccessAfterAwaitAnalyzer : DiagnosticAnalyzer
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
            ImmutableArray.Create(Rules.EcsAccessAfterAwait);

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

                var ecsUsage = new EcsUsage(compilationContext.Compilation, ecs);
                compilationContext.RegisterSyntaxNodeAction(
                    nodeContext => AnalyzeFunction(nodeContext, ecs, ecsUsage),
                    AsyncCode.FunctionKinds);
            });
        }

        private static void AnalyzeFunction(SyntaxNodeAnalysisContext context, EcsSymbols ecs, EcsUsage ecsUsage)
        {
            var function = AsyncCode.GetAsyncFunction(context.Node, context.SemanticModel);

            // EcsTask functions are the safe place: their builder resumes every await inside the system Run.
            if (function == null || ecs.IsEcsTask(function.ReturnType))
            {
                return;
            }

            var body = AsyncCode.GetBody(context.Node);
            if (body == null)
            {
                return;
            }

            var nodes = AsyncCode.GetOwnNodes(body);
            var awaits = AsyncCode.FindAwaits(nodes);
            if (awaits.Count == 0)
            {
                return;
            }

            var reportedStatements = new HashSet<SyntaxNode>();

            foreach (var name in nodes.OfType<SimpleNameSyntax>())
            {
                if (!AsyncCode.RunsAfterAnyAwait(name, awaits))
                {
                    continue;
                }

                var symbol = context.SemanticModel.GetSymbolInfo(name, context.CancellationToken).Symbol;
                if (!ecsUsage.UsesEcs(symbol))
                {
                    continue;
                }

                // One report per statement: `_filter.Pools.Inc1.Get(e)` is one mistake, not three.
                var statement = (SyntaxNode?)name.FirstAncestorOrSelf<StatementSyntax>() ?? name;
                if (!reportedStatements.Add(statement))
                {
                    continue;
                }

                context.ReportDiagnostic(Diagnostic.Create(
                    Rules.EcsAccessAfterAwait,
                    name.GetLocation(),
                    name.Identifier.ValueText,
                    function.ReturnType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
            }
        }
    }
}
