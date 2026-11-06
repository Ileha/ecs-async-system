# LeoECS Lite Extensions Async Analyzers

Roslyn analyzers that catch wrong use of `AsyncRunSystem` / `EcsTask` while the code compiles.
Unity shows the results in the Console, and Rider / Visual Studio underline them in the editor.
They only run at compile time: nothing is added to the game and there is no runtime cost.

The rules live in two DLLs:
- this package (`com.alexey-developer89.ecs-async-system`): LEOECS101, 102, 104. They need ECS and
  run in assemblies that reference `Leopotam.EcsLite`.
- `com.alexey-developer89.ecs-task`: LEOECS103, 105. They need only `EcsTask`, so they also run in
  assemblies without ECS (e.g. EcsTask GOAP actions).

## Rules

| Id | Severity | What it catches |
|---|---|---|
| LEOECS101 | Error | ECS is used after an `await` in an async function that does not return `EcsTask` |
| LEOECS102 | Error | `await` inside `foreach` over a filter |
| LEOECS103 | Warning | Awaited call in `EcsTask` code that could take the `CancellationToken` but does not (ecs-task) |
| LEOECS104 | Warning | Entity id taken before an `await` and used after it |
| LEOECS105 | Error | `EcsTask` call used as a statement, without `await` (ecs-task) |

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

// LEOECS103: without the token, the owner (system destroy, GOAP action end) cannot stop the wait.
await UniTask.Delay(1000);   // ❌ pass cancellationToken: ct

// LEOECS104: the entity can be destroyed or its id reused while the method waits.
var target = _world.NewEntity();
await UniTask.Delay(1000, cancellationToken: ct);
_pool.Add(target);   // ❌ keep an EcsPackedEntity, unpack it after the await

// LEOECS105: exceptions are lost and the code below does not wait.
MoveToCover(ct);       // ❌
await MoveToCover(ct); // ✅
_ = MoveToCover(ct);   // ✅ explicit "fire and forget"
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

- `Analyzers~/LeoECSLiteExtensions.Analyzers/`: the ECS analyzer sources. Unity ignores folders ending in `~`.
  - `Rules.cs`: ids, messages and severities of the ECS rules.
  - `EcsSymbols.cs`: which types count as ECS.
  - `AsyncCode.cs` (linked from ecs-task, one source for both DLLs): finds awaits and answers
    "can this code run after that await?".
  - `EcsUsage.cs`: "does this symbol touch ECS?", including helper methods.
  - `EntityLocals.cs`: which locals hold entity ids, and where they are written.
  - `*Analyzer.cs`: one file per rule.
- `Analyzers~/LeoECSLiteExtensions.Analyzers.Tests/`: NUnit tests (like the Unity tests), one small code example per case.
- `ecs-task/Analyzers~/LeoECSLiteExtensions.EcsTask.Analyzers/` (+ `.Tests`): LEOECS103/105, `EcsTaskSymbols.cs`, `AsyncCode.cs`.
  Its test stubs have no ECS types on purpose.
- `Analyzers/LeoECSLiteExtensions.Analyzers.dll` (and `ecs-task/Analyzers/LeoECSLiteExtensions.EcsTask.Analyzers.dll`):
  the compiled analyzers that Unity loads. Its `.meta`
  has the `RoslynAnalyzer` label and is disabled for every platform, so it is never part of a build.
  Do not replace or regenerate that `.meta` file.

## Build and test

Run from the Unity project root:

```
dotnet test Packages/ecs-task/Analyzers~/LeoECSLiteExtensions.EcsTask.Analyzers.Tests
dotnet test Packages/ecs-async-system/Analyzers~/LeoECSLiteExtensions.Analyzers.Tests
dotnet build Packages/ecs-task/Analyzers~/LeoECSLiteExtensions.EcsTask.Analyzers -c Release
dotnet build Packages/ecs-async-system/Analyzers~/LeoECSLiteExtensions.Analyzers -c Release
```

Each Release build copies its DLL to `Analyzers/` of its package. Unity loads the new DLL after the next Asset
Database refresh. Develop with the tests: they are much faster than waiting for Unity to reload.

The project targets `netstandard2.0` and references `Microsoft.CodeAnalysis.CSharp` 4.3.1, the same as
the source generators, to stay compatible with Unity's analyzer loading.
