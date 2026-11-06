using System.Collections.Concurrent;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LeoECSLiteExtensions.Analyzers
{
    /// <summary>
    /// Answers "does using this symbol touch ECS state?". A symbol touches ECS when it holds an ECS value
    /// (a world, pool, filter...) or when it is a method of this project whose body touches ECS, directly or
    /// through the methods it calls. The second part catches <c>await X; Helper();</c> where Helper reads a pool.
    /// </summary>
    /// <remarks>Created once per compilation and shared by all threads of the analyzer.</remarks>
    internal sealed class EcsUsage
    {
        // Deep enough for helpers that call helpers, small enough to keep compile time low.
        private const int MaxCallDepth = 3;

        private readonly Compilation _compilation;
        private readonly EcsSymbols _ecs;
        private readonly ConcurrentDictionary<IMethodSymbol, bool> _methodCache =
            new ConcurrentDictionary<IMethodSymbol, bool>(SymbolEqualityComparer.Default);

        public EcsUsage(Compilation compilation, EcsSymbols ecs)
        {
            _compilation = compilation;
            _ecs = ecs;
        }

        public bool UsesEcs(ISymbol? symbol) => UsesEcs(symbol, MaxCallDepth);

        private bool UsesEcs(ISymbol? symbol, int callDepthLeft)
        {
            if (_ecs.HoldsEcsState(symbol))
            {
                return true;
            }

            return symbol is IMethodSymbol method && callDepthLeft > 0 && MethodBodyUsesEcs(method, callDepthLeft);
        }

        private bool MethodBodyUsesEcs(IMethodSymbol method, int callDepthLeft)
        {
            // Extension and generic calls point to a "constructed" copy of the method; the source belongs to the original.
            method = (method.ReducedFrom ?? method).OriginalDefinition;

            // Only full-depth answers are cached: a shallow scan can miss ECS use that a deeper scan finds.
            var isFullDepth = callDepthLeft == MaxCallDepth;
            if (isFullDepth && _methodCache.TryGetValue(method, out var cached))
            {
                return cached;
            }

            var result = ScanMethodBody(method, callDepthLeft);

            if (isFullDepth)
            {
                _methodCache.TryAdd(method, result);
            }

            return result;
        }

        private bool ScanMethodBody(IMethodSymbol method, int callDepthLeft)
        {
            // Methods from DLLs (Unity, LeoECS itself) have no source here. The ECS values they return are
            // already caught by HoldsEcsState.
            foreach (var reference in method.DeclaringSyntaxReferences)
            {
                var declaration = reference.GetSyntax();

#pragma warning disable RS1030 // The helper can live in another file, so it needs that file's semantic model.
                var model = _compilation.GetSemanticModel(declaration.SyntaxTree);
#pragma warning restore RS1030

                var usesEcs = declaration.DescendantNodes()
                    .OfType<SimpleNameSyntax>()
                    .Any(name => UsesEcs(model.GetSymbolInfo(name).Symbol, callDepthLeft - 1));

                if (usesEcs)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
