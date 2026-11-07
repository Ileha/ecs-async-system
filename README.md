# LeoECS Lite Async Run System

LeoECS Lite run system whose body is an async `EcsTask`.

## How it works

Code after every `await` resumes inside this system's `IEcsRunSystem.Run`, so it keeps the normal system order. You can await any awaitable inside `RunAsync`, including `Task` and [UniTask](https://github.com/cysharp/unitask) (through `ecs-task`).

## Usage sample 

```cs
public class AsyncSystem : AsyncRunSystem
{
    protected override async EcsTask RunAsync(CancellationToken cancellationToken)
    {
        
        //Do some work here (called when IEcsRunSystem.Run)

        await Task.Delay(1000, cancellationToken);

        //resumes in the first AsyncSystem.Run after the delay completes

        await NestedAsyncOperation(cancellationToken);

        //resumes in the first AsyncSystem.Run after NestedAsyncOperation completes
    }

    protected override void ReleaseResources(IEcsSystems systems)
    {
        //use this instead of IEcsDestroySystem.Destroy
    }

    private async EcsTask NestedAsyncOperation(CancellationToken cancellationToken)
    {
        //EcsTask needed to be returned in case of usages in method ecs parts on order to ensure 
        //that all of them would be called in correct time in terms of system.Run
    }
}
```

## Prevent of dangerous usage

Supports code analysis to prevent dangerous operation with async and ecs [Analyzers](./Analyzers~/README.md)  
Main statements are:
- if async method operates with ECS world it should return `EcsTask`
- await is forbidden during filter enumeration
- if entity used between await's it should be packed before and unpacked after await (warning, not error). This makes sure that the operation happens with the same entity and it's not deleted/reused during async operation.

## Dependencies

Depends on: [com.alexey-developer89.ecs-task](https://github.com/Ileha/ecs-task), [com.leopotam.ecslite](https://github.com/Leopotam/ecslite).
