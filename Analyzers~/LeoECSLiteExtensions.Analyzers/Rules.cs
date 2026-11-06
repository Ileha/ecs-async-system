using Microsoft.CodeAnalysis;

namespace LeoECSLiteExtensions.Analyzers
{
    /// <summary>
    /// ECS rules of the async group. The ids continue the LEOECS numbering of the source generators; 1xx is the
    /// async group. See README.md for examples.
    /// </summary>
    internal static class Rules
    {
        private const string Category = "LeoECS.Async";

        public static readonly DiagnosticDescriptor EcsAccessAfterAwait = new DiagnosticDescriptor(
            id: "LEOECS101",
            title: "ECS is used after an await outside EcsTask",
            messageFormat: "'{0}' uses ECS after an await, but this function returns '{1}', so this code runs " +
                           "outside the system Run. Return EcsTask instead.",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "Only EcsTask resumes the code after an await inside the owning system's Run. " +
                         "UniTask and Task resume in the PlayerLoop or on a worker thread, outside the ECS system order.");

        public static readonly DiagnosticDescriptor AwaitInFilterLoop = new DiagnosticDescriptor(
            id: "LEOECS102",
            title: "Await inside a filter loop",
            messageFormat: "Do not await inside 'foreach' over '{0}'. The filter stays locked while the method waits. " +
                           "Collect the entities first, then await.",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);


        public static readonly DiagnosticDescriptor StaleEntityAfterAwait = new DiagnosticDescriptor(
            id: "LEOECS104",
            title: "Entity id is used after an await",
            messageFormat: "Entity '{0}' was taken before an await. It can be destroyed or reused while the method waits. " +
                           "Keep an EcsPackedEntity and unpack it after the await.",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

    }
}
