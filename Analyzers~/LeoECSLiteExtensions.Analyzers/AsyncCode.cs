using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LeoECSLiteExtensions.Analyzers
{
    /// <summary>
    /// Syntax helpers shared by the rules: find async functions, their awaits,
    /// and answer "can this code run after that await?".
    /// </summary>
    /// <remarks>
    /// "After" is decided by the position in the code, not by real control flow. Code in an
    /// <c>else</c> branch below an await in the <c>if</c> branch counts as "after" too. That can give
    /// a false warning, but it never misses code that really runs after the await.
    /// </remarks>
    internal static class AsyncCode
    {
        /// <summary>Everything that can be async: methods, local functions, lambdas.</summary>
        public static readonly SyntaxKind[] FunctionKinds =
        {
            SyntaxKind.MethodDeclaration,
            SyntaxKind.LocalFunctionStatement,
            SyntaxKind.ParenthesizedLambdaExpression,
            SyntaxKind.SimpleLambdaExpression,
            SyntaxKind.AnonymousMethodExpression,
        };

        /// <summary>True for a node where a function starts: code inside it runs when the function is called.</summary>
        public static bool IsFunctionBoundary(SyntaxNode node) =>
            node is BaseMethodDeclarationSyntax
                or LocalFunctionStatementSyntax
                or AnonymousFunctionExpressionSyntax
                or AccessorDeclarationSyntax;

        public static IMethodSymbol? GetFunctionSymbol(SyntaxNode function, SemanticModel model)
        {
            var symbol = function is AnonymousFunctionExpressionSyntax
                ? model.GetSymbolInfo(function).Symbol
                : model.GetDeclaredSymbol(function);

            return symbol as IMethodSymbol;
        }

        /// <summary>The symbol of an async function, or null when the function is not async.</summary>
        public static IMethodSymbol? GetAsyncFunction(SyntaxNode function, SemanticModel model)
        {
            var symbol = GetFunctionSymbol(function, model);
            return symbol is { IsAsync: true } ? symbol : null;
        }

        /// <summary>The function around this node, e.g. the lambda or method that contains an await.</summary>
        public static IMethodSymbol? GetEnclosingFunction(SyntaxNode node, SemanticModel model)
        {
            var function = node.Ancestors().FirstOrDefault(IsFunctionBoundary);
            return function == null ? null : GetFunctionSymbol(function, model);
        }

        /// <summary>The block or expression body of a function; null for abstract and extern methods.</summary>
        public static SyntaxNode? GetBody(SyntaxNode function) => function switch
        {
            BaseMethodDeclarationSyntax method => (SyntaxNode?)method.Body ?? method.ExpressionBody,
            LocalFunctionStatementSyntax localFunction => (SyntaxNode?)localFunction.Body ?? localFunction.ExpressionBody,
            AnonymousFunctionExpressionSyntax lambda => lambda.Body,
            _ => null
        };

        /// <summary>
        /// All nodes of the body, without nested lambdas and local functions. A nested function has
        /// its own awaits and runs when it is called, so it is checked on its own.
        /// </summary>
        public static IReadOnlyList<SyntaxNode> GetOwnNodes(SyntaxNode body) =>
            body.DescendantNodesAndSelf(node => node == body || !IsFunctionBoundary(node))
                .Where(node => node == body || !IsFunctionBoundary(node))
                .ToList();

        /// <summary>Places where the function can pause: <c>await x</c> and <c>await foreach</c>.</summary>
        public static IReadOnlyList<SyntaxNode> FindAwaits(IEnumerable<SyntaxNode> nodes) =>
            nodes.Where(node => node is AwaitExpressionSyntax
                                || node is CommonForEachStatementSyntax loop && !loop.AwaitKeyword.IsKind(SyntaxKind.None))
                .ToList();

        /// <summary>
        /// The position where the function continues after the pause. Code before it (for example the
        /// arguments in <c>await Load(_world)</c>) runs before the pause.
        /// </summary>
        public static int GetResumePosition(SyntaxNode awaitNode) => awaitNode switch
        {
            // The body of `await foreach` runs after each awaited MoveNextAsync.
            CommonForEachStatementSyntax loop => loop.Expression.Span.End,
            _ => awaitNode.Span.End
        };

        public static bool RunsAfterAnyAwait(SyntaxNode node, IEnumerable<SyntaxNode> awaits) =>
            awaits.Any(awaitNode => RunsAfterAwait(node, awaitNode));

        /// <summary>
        /// True when the node is below the await, or when both are in the same loop:
        /// then the next turn of the loop runs the node again after the await.
        /// </summary>
        public static bool RunsAfterAwait(SyntaxNode node, SyntaxNode awaitNode) =>
            GetResumePosition(awaitNode) <= node.SpanStart || FindLoopAround(node, awaitNode) != null;

        /// <summary>The innermost loop of the function that contains both nodes, or null.</summary>
        public static SyntaxNode? FindLoopAround(SyntaxNode node, SyntaxNode awaitNode) =>
            awaitNode.Ancestors()
                .TakeWhile(ancestor => !IsFunctionBoundary(ancestor))
                .Where(IsLoop)
                .FirstOrDefault(loop => loop.Span.Contains(node.Span));

        private static bool IsLoop(SyntaxNode node) =>
            node is ForStatementSyntax
                or CommonForEachStatementSyntax
                or WhileStatementSyntax
                or DoStatementSyntax;
    }
}
