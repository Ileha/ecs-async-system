using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace LeoECSLiteExtensions.Analyzers
{
    /// <summary>
    /// LEOECS104: inside an EcsTask function, an entity id taken before an await is used after it.
    /// <code>
    /// var target = _world.NewEntity();
    /// await UniTask.Delay(1000, cancellationToken: ct);
    /// _pool.Add(target); // target may be destroyed, or the id reused by another entity
    /// </code>
    /// </summary>
    /// <remarks>
    /// Only EcsTask functions are checked. Other async functions may not touch ECS after an await at all (LEOECS101).
    /// </remarks>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class StaleEntityAfterAwaitAnalyzer : DiagnosticAnalyzer
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
            ImmutableArray.Create(Rules.StaleEntityAfterAwait);

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
                    nodeContext => AnalyzeFunction(nodeContext, ecs),
                    AsyncCode.FunctionKinds);
            });
        }

        private static void AnalyzeFunction(SyntaxNodeAnalysisContext context, EcsSymbols ecs)
        {
            var model = context.SemanticModel;
            var function = AsyncCode.GetAsyncFunction(context.Node, model);
            if (function == null || !ecs.IsEcsTask(function.ReturnType))
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

            var entities = EntityLocals.Find(nodes, model, ecs);
            var writesByLocal = new Dictionary<ILocalSymbol, IReadOnlyList<SyntaxNode>>(SymbolEqualityComparer.Default);

            foreach (var name in nodes.OfType<IdentifierNameSyntax>())
            {
                if (model.GetSymbolInfo(name, context.CancellationToken).Symbol is not ILocalSymbol local
                    || !entities.Contains(local)
                    || EntityLocals.IsWrite(name)
                    // An await inside a filter loop is already LEOECS102.
                    || EntityLocals.IsFilterLoopVariable(local))
                {
                    continue;
                }

                if (!writesByLocal.TryGetValue(local, out var writes))
                {
                    writes = EntityLocals.FindWrites(local, nodes, model);
                    writesByLocal.Add(local, writes);
                }

                if (awaits.Any(awaitNode => ReadsValueFromBeforeAwait(name, awaitNode, writes)))
                {
                    context.ReportDiagnostic(Diagnostic.Create(Rules.StaleEntityAfterAwait, name.GetLocation(), local.Name));
                }
            }
        }

        /// <summary>True when the read can see a value that was written before the await paused the function.</summary>
        private static bool ReadsValueFromBeforeAwait(SyntaxNode read, SyntaxNode awaitNode, IReadOnlyList<SyntaxNode> writes)
        {
            var resumePosition = AsyncCode.GetResumePosition(awaitNode);

            // The await is above the read: the value is old, unless the local is written again in between.
            if (resumePosition <= read.SpanStart)
            {
                return !writes.Any(write => write.SpanStart >= resumePosition && write.SpanStart < read.SpanStart);
            }

            // The await is below the read in the same loop: the next turn reads the old value,
            // unless the loop writes the local again before the read.
            var loop = AsyncCode.FindLoopAround(read, awaitNode);
            if (loop != null)
            {
                return !writes.Any(write => loop.Span.Contains(write.Span) && write.SpanStart < read.SpanStart);
            }

            return false;
        }
    }
}
