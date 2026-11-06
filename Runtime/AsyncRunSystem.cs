#nullable enable
using System;
using System.Threading;
using Leopotam.EcsLite;
using UnityEngine;

namespace LeoECSLiteExtensions.Async
{
    /// Run system whose body is an async <see cref="EcsTask"/>. Code after every await resumes
    /// inside this system's Run, so it keeps the normal system order. One cycle runs at a time;
    /// a new cycle starts in the Run where the previous one finished.
    ///
    /// Rules for <see cref="RunAsync"/>:
    /// - Helpers that touch ECS state after an await must also return <see cref="EcsTask"/>.
    /// - ref locals / Span are not allowed in async methods (C# 9): do ECS access in sync helpers.
    /// - Never await inside a filter foreach, and keep entities packed across awaits.
    /// - World state changes across an await: other systems ran and one-frame events are gone.
    /// - Pass the token to awaitables, so destroy can finish pending cycles and run their finally blocks.
    public abstract class AsyncRunSystem : IEcsRunSystem, IEcsDestroySystem
    {
        private const int MaxDestroyDrainPasses = 8;

        private readonly EcsSystemSyncContext _context = new();
        private readonly CancellationTokenSource _destroyCts = new();
        private readonly CancellationToken _destroyToken;

        private EcsTask _cycle;

        public AsyncRunSystem()
        {
            _destroyToken = _destroyCts.Token;
        }

        public void Run(IEcsSystems systems)
        {
            using var scope = _context.Enter();

            _context.Drain();

            if (!_cycle.IsCompleted)
            {
                return;
            }

            ReportFault(_cycle);
            _cycle = RunAsync(_destroyToken);
        }

        public void Destroy(IEcsSystems systems)
        {
            using (_context.Enter())
            {
                // Awaitables bound to the token complete as canceled right here and resume inline.
                _destroyCts.Cancel();

                for (var i = 0; i < MaxDestroyDrainPasses && _context.HasPending; i++)
                {
                    _context.Drain();
                }
            }

            if (!_cycle.IsCompleted)
            {
                Debug.LogWarning($"{GetType().Name} destroyed with a pending async cycle; its finally blocks will not run");
            }

            ReportFault(_cycle);

            _context.Dispose();
            _destroyCts.Dispose();

            ReleaseResources(systems);
        }

        protected abstract EcsTask RunAsync(CancellationToken cancellationToken);

        /// Called after the pending cycle is finished or abandoned.
        protected virtual void ReleaseResources(IEcsSystems systems)
        {
        }

        private void ReportFault(EcsTask cycle)
        {
            var exception = cycle.Exception;

            if (exception == null)
            {
                return;
            }

            if (exception is OperationCanceledException && _destroyCts.IsCancellationRequested)
            {
                return;
            }

            Debug.LogException(exception);
        }
    }
}
