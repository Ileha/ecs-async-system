using System.Linq;
using Microsoft.CodeAnalysis;

namespace LeoECSLiteExtensions.Analyzers
{
    /// <summary>
    /// Knows which types belong to ECS. Created once per compilation.
    /// </summary>
    internal sealed class EcsSymbols
    {
        private const string EcsLiteNamespace = "Leopotam.EcsLite";

        // Values of types from these namespaces read or change ECS state: worlds, pools, filters, archetypes.
        private static readonly string[] EcsNamespaces =
        {
            EcsLiteNamespace,
            "LeoECSLiteExtensions.ZenjectBridge",
            "LeoECSLiteExtensions.Archetypes",
        };

        // Packed entities are plain data: they are the safe way to keep an entity across an await.
        private static readonly string[] PlainDataTypeNames = { "EcsPackedEntity", "EcsPackedEntityWithWorld" };

        private readonly INamedTypeSymbol? _ecsFilter;
        private readonly INamedTypeSymbol? _bridgeFilter;
        private readonly INamedTypeSymbol? _ecsTask;
        private readonly INamedTypeSymbol? _ecsTaskOfT;

        private EcsSymbols(Compilation compilation)
        {
            _ecsFilter = compilation.GetTypeByMetadataName("Leopotam.EcsLite.EcsFilter");
            _bridgeFilter = compilation.GetTypeByMetadataName("LeoECSLiteExtensions.ZenjectBridge.API.IEcsFilter");
            _ecsTask = compilation.GetTypeByMetadataName("LeoECSLiteExtensions.Async.EcsTask");
            _ecsTaskOfT = compilation.GetTypeByMetadataName("LeoECSLiteExtensions.Async.EcsTask`1");
        }

        /// <summary>Returns null when the assembly does not reference LeoECS Lite: there is nothing to check.</summary>
        public static EcsSymbols? TryCreate(Compilation compilation) =>
            compilation.GetTypeByMetadataName("Leopotam.EcsLite.EcsWorld") == null ? null : new EcsSymbols(compilation);

        /// <summary>True for <c>EcsTask</c> and <c>EcsTask&lt;T&gt;</c>.</summary>
        public bool IsEcsTask(ITypeSymbol? type) => IsSameType(type, _ecsTask) || IsSameType(type, _ecsTaskOfT);

        /// <summary>True for <c>EcsFilter</c> and for the Zenject bridge filters (<c>IEcsFilter&lt;...&gt;</c>).</summary>
        public bool IsFilter(ITypeSymbol? type)
        {
            if (type == null)
            {
                return false;
            }

            return IsSameType(type, _ecsFilter)
                   || IsSameType(type, _bridgeFilter)
                   || type.AllInterfaces.Any(i => IsSameType(i, _bridgeFilter));
        }

        /// <summary>
        /// True when a value of this type gives access to ECS state. EcsTask counts too:
        /// an EcsTask method can start only inside a system Run.
        /// </summary>
        public bool IsEcsStateType(ITypeSymbol? type)
        {
            if (type == null || PlainDataTypeNames.Contains(type.Name))
            {
                return false;
            }

            if (IsEcsTask(type))
            {
                return true;
            }

            var typeNamespace = type.ContainingNamespace?.ToDisplayString() ?? string.Empty;
            return EcsNamespaces.Any(ecsNamespace =>
                typeNamespace == ecsNamespace || typeNamespace.StartsWith(ecsNamespace + "."));
        }

        /// <summary>
        /// True for a local, parameter, field or property that holds an ECS value, and for a method that returns one.
        /// </summary>
        public bool HoldsEcsState(ISymbol? symbol) => symbol switch
        {
            ILocalSymbol local => IsEcsStateType(local.Type),
            IParameterSymbol parameter => IsEcsStateType(parameter.Type),
            IFieldSymbol field => IsEcsStateType(field.Type),
            IPropertySymbol property => IsEcsStateType(property.Type),
            IMethodSymbol method => IsEcsStateType(method.ReturnType),
            _ => false
        };

        /// <summary>True for a LeoECS Lite method with this name, e.g. <c>NewEntity</c> or the <c>Unpack</c> extension.</summary>
        public static bool IsEcsLiteMethod(ISymbol? symbol, string name) =>
            symbol is IMethodSymbol method
            && method.Name == name
            && method.ContainingNamespace?.ToDisplayString() == EcsLiteNamespace;

        private static bool IsSameType(ITypeSymbol? type, INamedTypeSymbol? expected) =>
            expected != null && SymbolEqualityComparer.Default.Equals(type?.OriginalDefinition, expected);
    }
}
