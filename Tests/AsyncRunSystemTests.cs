#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Leopotam.EcsLite;
using LeoECSLiteExtensions.Async;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace LeoECSLiteExtensions.Tests
{
    public class AsyncRunSystemTests
    {
        private const int WorkerTimeoutMs = 5000;

        [Test]
        public void ForeignAwaiter_CompletedOutsideRun_ResumesOnNextRun()
        {
            var signal = new ManualSignal();
            var system = new ScriptSystem(async (log, _) =>
            {
                log.Add("before");
                await signal;
                log.Add($"after:{IsInsideRun()}");
            });

            system.Run(null!);
            signal.Complete();

            CollectionAssert.AreEqual(new[] { "before" }, system.Log);

            system.Run(null!);

            CollectionAssert.AreEqual(new[] { "before", "after:True" }, system.Log);
        }

        [Test]
        public void ForeignAwaiter_CompletedOnWorkerThread_ResumesOnMainThreadInsideRun()
        {
            var mainThreadId = Environment.CurrentManagedThreadId;
            var signal = new ManualSignal();
            var system = new ScriptSystem(async (log, _) =>
            {
                await signal;
                log.Add($"thread:{Environment.CurrentManagedThreadId == mainThreadId}");
            });

            system.Run(null!);
            Assert.IsTrue(Task.Run(signal.Complete).Wait(WorkerTimeoutMs));
            system.Run(null!);

            CollectionAssert.AreEqual(new[] { "thread:True" }, system.Log);
        }

        [Test]
        public void NestedEcsTask_ResumesParentInSameRun()
        {
            var signal = new ManualSignal();
            var system = new ScriptSystem(async (log, _) =>
            {
                await Child(signal, log);
                log.Add("parent");
            });

            system.Run(null!);
            signal.Complete();
            system.Run(null!);

            CollectionAssert.AreEqual(new[] { "child", "parent" }, system.Log);
        }

        [Test]
        public void SyncCycle_RunsOncePerRun()
        {
            var system = new ScriptSystem((log, _) =>
            {
                log.Add("cycle");
                return default;
            }, repeat: true);

            system.Run(null!);
            system.Run(null!);

            CollectionAssert.AreEqual(new[] { "cycle", "cycle" }, system.Log);
        }

        [Test]
        public void FaultedCycle_IsLogged_AndNextCycleStarts()
        {
            var cycle = 0;
            var system = new ScriptSystem(async (log, _) =>
            {
                cycle++;
                await Task.Yield();
                if (cycle == 1)
                {
                    throw new InvalidOperationException("boom");
                }

                log.Add("second");
            }, repeat: true);

            LogAssert.Expect(LogType.Exception, new Regex("boom"));

            system.Run(null!); // cycle 1 starts
            system.Run(null!); // cycle 1 faults, cycle 2 starts
            system.Run(null!); // cycle 2 finishes, cycle 3 starts

            CollectionAssert.AreEqual(new[] { "second" }, system.Log);
        }

        [Test]
        public void Destroy_CancelsPendingCycle_AndRunsFinally()
        {
            var system = new ScriptSystem(async (log, ct) =>
            {
                try
                {
                    await Task.Delay(Timeout.Infinite, ct);
                }
                finally
                {
                    log.Add($"finally:{IsInsideRun()}");
                }
            });

            system.Run(null!);
            system.Destroy(null!);

            CollectionAssert.AreEqual(new[] { "finally:True" }, system.Log);
        }

        [Test]
        public void EcsTaskMethod_CalledOutsideRun_Throws()
        {
            Assert.Throws<InvalidOperationException>(() => _ = Child(new ManualSignal(), new List<string>()));
        }

        [Test]
        public void GenericEcsTask_ReturnsValueAfterForeignAwait_InsideRun()
        {
            var signal = new ManualSignal();
            var system = new ScriptSystem(async (log, _) =>
            {
                var value = await ChildWithResult(signal);
                log.Add($"value:{value}:{IsInsideRun()}");
            });

            system.Run(null!);
            signal.Complete();
            system.Run(null!);

            CollectionAssert.AreEqual(new[] { "value:42:True" }, system.Log);
        }

        [Test]
        public void GenericEcsTask_CompletedWithoutAwait_ReturnsValueInSameRun()
        {
            var system = new ScriptSystem(async (log, _) =>
            {
                log.Add($"from-result:{await EcsTask.FromResult(7)}");
                log.Add($"default:{await default(EcsTask<int>)}");
            });

            system.Run(null!);

            CollectionAssert.AreEqual(new[] { "from-result:7", "default:0" }, system.Log);
        }

        [Test]
        public void GenericEcsTask_Exception_IsRethrownAtAwait()
        {
            var signal = new ManualSignal();
            var system = new ScriptSystem(async (log, _) =>
            {
                try
                {
                    await FailingChild(signal);
                }
                catch (InvalidOperationException exception)
                {
                    log.Add($"caught:{exception.Message}");
                }
            });

            system.Run(null!);
            signal.Complete();
            system.Run(null!);

            CollectionAssert.AreEqual(new[] { "caught:boom" }, system.Log);
        }

        private static async EcsTask Child(ManualSignal signal, List<string> log)
        {
            await signal;
            log.Add("child");
        }

        private static async EcsTask<int> ChildWithResult(ManualSignal signal)
        {
            await signal;
            return 42;
        }

        private static async EcsTask<int> FailingChild(ManualSignal signal)
        {
            await signal;
            throw new InvalidOperationException("boom");
        }

        private static bool IsInsideRun() => SynchronizationContext.Current is EcsSystemSyncContext;

        private sealed class ScriptSystem : AsyncRunSystem
        {
            private readonly Func<List<string>, CancellationToken, EcsTask> _script;
            private readonly bool _repeat;

            private bool _hasRun;

            public ScriptSystem(Func<List<string>, CancellationToken, EcsTask> script, bool repeat = false)
            {
                _script = script;
                _repeat = repeat;
            }

            public List<string> Log { get; } = new();

            protected override EcsTask RunAsync(CancellationToken cancellationToken)
            {
                if (_hasRun && !_repeat)
                {
                    return default;
                }

                _hasRun = true;
                return _script(Log, cancellationToken);
            }
        }

        /// Awaitable that ignores SynchronizationContext and resumes on whatever thread completes it,
        /// like UniTask PlayerLoop sources.
        private sealed class ManualSignal : INotifyCompletion
        {
            private Action? _continuation;
            private bool _isCompleted;

            public bool IsCompleted => _isCompleted;

            public ManualSignal GetAwaiter() => this;

            public void GetResult()
            {
            }

            public void OnCompleted(Action continuation) => _continuation = continuation;

            public void Complete()
            {
                _isCompleted = true;
                _continuation?.Invoke();
            }
        }
    }
}
