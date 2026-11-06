# Async ECS Analyzers

Roslyn analyzers that catch wrong use of ECS in async code while it compiles. They run in assemblies
that reference `Leopotam.EcsLite`. Unity shows the results in the Console, and Rider / Visual Studio
underline them in the editor. Compile time only: no runtime cost.

Build (from the Unity project root; copies the DLL to `Analyzers/`):
`dotnet build Packages/ecs-async-system/Analyzers~/LeoECSLiteExtensions.Analyzers -c Release`

## Rules

| Id | Severity | What it catches |
|---|---|---|
| LEOECS101 | Error | ECS is used after an `await` in an async function that does not return `EcsTask` |
| LEOECS102 | Error | `await` inside `foreach` over a filter |
| LEOECS104 | Warning | Entity id taken before an `await` and used after it |

```csharp
// LEOECS101: after the await this runs in UniTask's PlayerLoop, not in the system Run.
async UniTask Helper(int entity, CancellationToken ct)
{
    await UniTask.Delay(100, cancellationToken: ct);
    _pool.Get(entity).Value = 0;   // ❌ return EcsTask instead
}

// LEOECS102: the filter stays locked while the method waits.
foreach (var entity in _filter)
{
    await UniTask.Delay(100, cancellationToken: ct);   // ❌
}

// LEOECS104: the entity can be destroyed or its id reused while the method waits.
var target = _world.NewEntity();
await UniTask.Delay(1000, cancellationToken: ct);
_pool.Add(target);   // ❌ keep an EcsPackedEntity, unpack it after the await
```

"ECS" means a value of a type from `Leopotam.EcsLite`, `LeoECSLiteExtensions.ZenjectBridge`
or `LeoECSLiteExtensions.Archetypes` (a world, pool, filter, ...), or an `EcsTask`. Everywhere in these rules, `EcsTask<T>` counts as `EcsTask`.
`EcsPackedEntity` is plain data and is allowed. LEOECS101 also follows calls to methods of this
project up to 3 levels deep, so `await X; Helper();` is reported when `Helper` uses a pool inside.

To turn a rule off for one intended case, use `#pragma warning disable LEOECS104` or
`[SuppressMessage("LeoECS.Async", "LEOECS104")]`.

## Known limits

- "After an await" is decided by the position in the code, not by real control flow. Code in an
  `else` branch below an await in the `if` branch counts as "after" too.
- LEOECS104 only knows entities from a filter `foreach`, `NewEntity()`, `Unpack(out ...)`, and copies of
  them. An `int entity` parameter is not tracked.
- `await using` is not treated as a pause point.

## Directory layout

- `Analyzers~/LeoECSLiteExtensions.Analyzers/`: the analyzer sources. Unity ignores folders ending in `~`.
  - `Rules.cs`: ids, messages and severities.
  - `EcsSymbols.cs`: which types count as ECS.
  - `AsyncCode.cs`: finds awaits and answers "can this code run after that await?".
  - `EcsUsage.cs`: "does this symbol touch ECS?", including helper methods.
  - `EntityLocals.cs`: which locals hold entity ids, and where they are written.
  - `*Analyzer.cs`: one file per rule.
- `Analyzers~/LeoECSLiteExtensions.Analyzers.Tests/`: NUnit tests (like the Unity tests), one small code example per case.
- `Analyzers/LeoECSLiteExtensions.Analyzers.dll`: the compiled analyzer that Unity loads. Its `.meta`
  has the `RoslynAnalyzer` label and is disabled for every platform, so it is never part of a build.
  Do not replace or regenerate that `.meta` file.

## Build and test

Run from the Unity project root:

```
dotnet test Packages/ecs-async-system/Analyzers~/LeoECSLiteExtensions.Analyzers.Tests
dotnet build Packages/ecs-async-system/Analyzers~/LeoECSLiteExtensions.Analyzers -c Release
```

The Release build copies the DLL to `Analyzers/`. Unity loads the new DLL after the next Asset
Database refresh. Develop with the tests: they are much faster than waiting for Unity to reload.

The project targets `netstandard2.0` and references `Microsoft.CodeAnalysis.CSharp` 4.3.1, the same as
the source generators, to stay compatible with Unity's analyzer loading.
