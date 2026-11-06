using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LeoECSLiteExtensions.Analyzers
{
    /// <summary>
    /// Finds local variables that hold an entity id, and the places where they are written.
    /// </summary>
    internal static class EntityLocals
    {
        private const string NewEntityMethod = "NewEntity";
        private const string UnpackMethod = "Unpack";

        /// <summary>
        /// A local holds an entity when it is:
        /// <list type="bullet">
        /// <item>the variable of <c>foreach (var e in filter)</c>;</item>
        /// <item>the <c>out</c> of <c>packed.Unpack(world, out var e)</c>;</item>
        /// <item>set from <c>world.NewEntity()</c> or from another entity local.</item>
        /// </list>
        /// </summary>
        public static HashSet<ILocalSymbol> Find(IReadOnlyList<SyntaxNode> nodes, SemanticModel model, EcsSymbols ecs)
        {
            var entities = new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default);

            foreach (var loop in nodes.OfType<ForEachStatementSyntax>())
            {
                if (ecs.IsFilter(model.GetTypeInfo(loop.Expression).Type) && model.GetDeclaredSymbol(loop) is { } loopVariable)
                {
                    entities.Add(loopVariable);
                }
            }

            foreach (var argument in nodes.OfType<ArgumentSyntax>())
            {
                if (IsUnpackOut(argument, model) && GetLocal(argument.Expression, model) is { } unpacked)
                {
                    entities.Add(unpacked);
                }
            }

            // `var target = e;` makes `target` an entity too. Repeat until nothing new is found,
            // because a copy of a copy counts as well.
            var assignments = FindAssignments(nodes, model);
            bool foundNew;
            do
            {
                foundNew = false;

                foreach (var (target, value) in assignments)
                {
                    if (!entities.Contains(target) && IsEntityValue(value, model, entities))
                    {
                        entities.Add(target);
                        foundNew = true;
                    }
                }
            }
            while (foundNew);

            return entities;
        }

        /// <summary>
        /// All places that give the local a new value: a declaration with a value, <c>out var x</c>,
        /// <c>x = ...</c>, and <c>out x</c> / <c>ref x</c> arguments.
        /// </summary>
        public static IReadOnlyList<SyntaxNode> FindWrites(ILocalSymbol local, IReadOnlyList<SyntaxNode> nodes, SemanticModel model)
        {
            var writes = new List<SyntaxNode>();

            foreach (var node in nodes)
            {
                var writtenSymbol = node switch
                {
                    VariableDeclaratorSyntax { Initializer: not null } declarator => model.GetDeclaredSymbol(declarator),
                    SingleVariableDesignationSyntax designation => model.GetDeclaredSymbol(designation),
                    IdentifierNameSyntax name when IsWrite(name) => model.GetSymbolInfo(name).Symbol,
                    _ => null
                };

                if (SymbolEqualityComparer.Default.Equals(writtenSymbol, local))
                {
                    writes.Add(node);
                }
            }

            return writes;
        }

        /// <summary>True when the name is the target of <c>x = ...</c> or of an <c>out</c> / <c>ref</c> argument.</summary>
        public static bool IsWrite(IdentifierNameSyntax name) => name.Parent switch
        {
            AssignmentExpressionSyntax assignment => assignment.Left == name,
            ArgumentSyntax argument => !argument.RefKindKeyword.IsKind(SyntaxKind.None),
            _ => false
        };

        public static bool IsFilterLoopVariable(ILocalSymbol local) =>
            local.DeclaringSyntaxReferences.Any(reference => reference.GetSyntax() is ForEachStatementSyntax);

        private static List<(ILocalSymbol Target, ExpressionSyntax Value)> FindAssignments(
            IReadOnlyList<SyntaxNode> nodes,
            SemanticModel model)
        {
            var assignments = new List<(ILocalSymbol, ExpressionSyntax)>();

            foreach (var node in nodes)
            {
                switch (node)
                {
                    case VariableDeclaratorSyntax { Initializer: { } initializer } declarator
                        when model.GetDeclaredSymbol(declarator) is ILocalSymbol declared:
                        assignments.Add((declared, initializer.Value));
                        break;

                    case AssignmentExpressionSyntax assignment
                        when assignment.IsKind(SyntaxKind.SimpleAssignmentExpression)
                             && model.GetSymbolInfo(assignment.Left).Symbol is ILocalSymbol assigned:
                        assignments.Add((assigned, assignment.Right));
                        break;
                }
            }

            return assignments;
        }

        private static bool IsEntityValue(ExpressionSyntax value, SemanticModel model, HashSet<ILocalSymbol> entities) =>
            value switch
            {
                InvocationExpressionSyntax call => EcsSymbols.IsEcsLiteMethod(model.GetSymbolInfo(call).Symbol, NewEntityMethod),
                IdentifierNameSyntax name => model.GetSymbolInfo(name).Symbol is ILocalSymbol local && entities.Contains(local),
                _ => false
            };

        private static bool IsUnpackOut(ArgumentSyntax argument, SemanticModel model) =>
            argument.RefKindKeyword.IsKind(SyntaxKind.OutKeyword)
            && argument.Parent?.Parent is InvocationExpressionSyntax call
            && EcsSymbols.IsEcsLiteMethod(model.GetSymbolInfo(call).Symbol, UnpackMethod);

        /// <summary>The local behind <c>out var e</c>, <c>out int e</c> or <c>out e</c>.</summary>
        private static ILocalSymbol? GetLocal(ExpressionSyntax expression, SemanticModel model) => expression switch
        {
            DeclarationExpressionSyntax { Designation: SingleVariableDesignationSyntax designation } =>
                model.GetDeclaredSymbol(designation) as ILocalSymbol,
            IdentifierNameSyntax name => model.GetSymbolInfo(name).Symbol as ILocalSymbol,
            _ => null
        };
    }
}
