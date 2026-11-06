namespace LeoECSLiteExtensions.Analyzers.Tests
{
    /// <summary>
    /// Tiny copies of the real ECS types, with the same names and namespaces. The analyzers find types
    /// by name, so the test snippets can compile against these instead of Unity and LeoECS.
    /// </summary>
    internal static class EcsStubs
    {
        public const string Usings = @"
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Leopotam.EcsLite;
using LeoECSLiteExtensions.Async;
using LeoECSLiteExtensions.ZenjectBridge.API;
";

        public const string Source = @"
using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Leopotam.EcsLite
{
    public sealed class EcsWorld
    {
        public int NewEntity() => 0;
        public EcsPackedEntity PackEntity(int entity) => default;
    }

    public sealed class EcsPool<T> where T : struct
    {
        public ref T Get(int entity) => throw new NotImplementedException();
    }

    public sealed class EcsFilter
    {
        public Enumerator GetEnumerator() => default;

        public struct Enumerator
        {
            public int Current => 0;
            public bool MoveNext() => false;
        }
    }

    public struct EcsPackedEntity { }

    public static class EcsEntityExtensions
    {
        public static bool Unpack(this EcsPackedEntity packed, EcsWorld world, out int entity)
        {
            entity = 0;
            return true;
        }
    }
}

namespace LeoECSLiteExtensions.ZenjectBridge.API
{
    public interface IEcsFilter
    {
        Leopotam.EcsLite.EcsFilter Value { get; }
        Leopotam.EcsLite.EcsFilter.Enumerator GetEnumerator();
    }

    public interface IEcsFilter<TInc> : IEcsFilter { }
}

namespace LeoECSLiteExtensions.Async
{
    [AsyncMethodBuilder(typeof(EcsTaskMethodBuilder))]
    public readonly struct EcsTask
    {
        public TaskAwaiter GetAwaiter() => default;
    }

    public struct EcsTaskMethodBuilder
    {
        public static EcsTaskMethodBuilder Create() => default;
        public EcsTask Task => default;
        public void Start<TStateMachine>(ref TStateMachine stateMachine) where TStateMachine : IAsyncStateMachine { }
        public void SetStateMachine(IAsyncStateMachine stateMachine) { }
        public void SetResult() { }
        public void SetException(Exception exception) { }

        public void AwaitOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
            where TAwaiter : INotifyCompletion where TStateMachine : IAsyncStateMachine { }

        public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
            where TAwaiter : ICriticalNotifyCompletion where TStateMachine : IAsyncStateMachine { }
    }

    [AsyncMethodBuilder(typeof(EcsTaskMethodBuilder<>))]
    public readonly struct EcsTask<T>
    {
        public TaskAwaiter<T> GetAwaiter() => default;
    }

    public struct EcsTaskMethodBuilder<T>
    {
        public static EcsTaskMethodBuilder<T> Create() => default;
        public EcsTask<T> Task => default;
        public void Start<TStateMachine>(ref TStateMachine stateMachine) where TStateMachine : IAsyncStateMachine { }
        public void SetStateMachine(IAsyncStateMachine stateMachine) { }
        public void SetResult(T result) { }
        public void SetException(Exception exception) { }

        public void AwaitOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
            where TAwaiter : INotifyCompletion where TStateMachine : IAsyncStateMachine { }

        public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
            where TAwaiter : ICriticalNotifyCompletion where TStateMachine : IAsyncStateMachine { }
    }
}

// Stands in for UniTask: its token is an optional parameter. The snippets use Task as the
// non-EcsTask return type, which behaves the same for the analyzers.
namespace Cysharp.Threading.Tasks
{
    public static class UniTask
    {
        public static Task Delay(int milliseconds, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
";
    }
}
