using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using NUnit.Framework;

namespace LeoECSLiteExtensions.Analyzers.Tests
{
    /// <summary>
    /// Compiles a code snippet together with <see cref="EcsStubs"/> and runs all analyzers on it.
    /// </summary>
    internal static class AnalyzerRunner
    {
        // Unity compiles with C# 9.
        private static readonly CSharpParseOptions ParseOptions = new CSharpParseOptions(LanguageVersion.CSharp9);

        private static readonly ImmutableArray<DiagnosticAnalyzer> Analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(
            new EcsAccessAfterAwaitAnalyzer(),
            new AwaitInFilterLoopAnalyzer(),
            new StaleEntityAfterAwaitAnalyzer());

        // The .NET runtime of the test process: enough for System, Threading and Tasks.
        private static readonly MetadataReference[] References = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToArray();

        /// <summary>Returns every reported diagnostic as (id, flagged code), in code order.</summary>
        public static IReadOnlyList<(string Id, string Code)> Run(string snippet)
        {
            var compilation = CSharpCompilation.Create(
                "AnalyzerTests",
                new[]
                {
                    CSharpSyntaxTree.ParseText(EcsStubs.Source, ParseOptions),
                    CSharpSyntaxTree.ParseText(EcsStubs.Usings + snippet, ParseOptions),
                },
                References,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            // A snippet that does not compile would test nothing.
            var compileErrors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error);
            CollectionAssert.IsEmpty(compileErrors);

            var diagnostics = compilation.WithAnalyzers(Analyzers).GetAnalyzerDiagnosticsAsync().Result;

            return diagnostics
                .OrderBy(d => d.Location.SourceSpan.Start)
                .Select(d => (d.Id, d.Location.SourceTree!.GetText().ToString(d.Location.SourceSpan)))
                .ToList();
        }

        public static void AssertReports(string snippet, params (string Id, string Code)[] expected) =>
            CollectionAssert.AreEqual(expected, Run(snippet));
    }
}
