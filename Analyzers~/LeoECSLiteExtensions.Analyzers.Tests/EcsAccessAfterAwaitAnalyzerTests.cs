using NUnit.Framework;
using static LeoECSLiteExtensions.Analyzers.Tests.AnalyzerRunner;

namespace LeoECSLiteExtensions.Analyzers.Tests
{
    public class EcsAccessAfterAwaitAnalyzerTests
    {
        [Test]
        public void PoolAfterAwait_InTaskMethod_IsReported()
        {
            AssertReports(@"
struct Health { public int Value; }

class System1
{
    EcsPool<Health> _pool;

    async Task Helper(int entity, CancellationToken ct)
    {
        await UniTask.Delay(100, ct);
        _pool.Get(entity).Value = 0;
    }
}", ("LEOECS101", "_pool"));
        }

        [Test]
        public void PoolAfterAwait_InEcsTaskMethod_IsFine()
        {
            AssertReports(@"
struct Health { public int Value; }

class System1
{
    EcsPool<Health> _pool;

    async EcsTask Helper(int entity, CancellationToken ct)
    {
        await UniTask.Delay(100, ct);
        _pool.Get(entity).Value = 0;
    }
}");
        }

        [Test]
        public void PoolAfterAwait_InGenericEcsTaskMethod_IsFine()
        {
            AssertReports(@"
struct Health { public int Value; }

class System1
{
    EcsPool<Health> _pool;

    async EcsTask<int> ReadHealth(int entity, CancellationToken ct)
    {
        await UniTask.Delay(100, ct);
        return _pool.Get(entity).Value;
    }
}");
        }

        [Test]
        public void PoolBeforeAwait_AndInAwaitArguments_IsFine()
        {
            AssertReports(@"
struct Health { public int Value; }

class System1
{
    EcsWorld _world;
    EcsPool<Health> _pool;

    async Task Helper(int entity, CancellationToken ct)
    {
        _pool.Get(entity).Value = 0;
        await Load(_world, ct);
    }

    Task Load(EcsWorld world, CancellationToken ct) => Task.CompletedTask;
}");
        }

        [Test]
        public void PoolAboveAwait_InSameLoop_IsReported()
        {
            AssertReports(@"
struct Health { public int Value; }

class System1
{
    EcsPool<Health> _pool;

    async Task Helper(int entity, CancellationToken ct)
    {
        while (true)
        {
            _pool.Get(entity).Value = 0;
            await UniTask.Delay(100, ct);
        }
    }
}", ("LEOECS101", "_pool"));
        }

        [Test]
        public void SyncHelperThatUsesPool_CalledAfterAwait_IsReported()
        {
            AssertReports(@"
struct Health { public int Value; }

class System1
{
    EcsPool<Health> _pool;

    async Task Helper(int entity, CancellationToken ct)
    {
        await UniTask.Delay(100, ct);
        ResetHealth(entity);
    }

    void ResetHealth(int entity) => _pool.Get(entity).Value = 0;
}", ("LEOECS101", "ResetHealth"));
        }

        [Test]
        public void EcsTaskCall_AfterAwait_InTaskLambda_IsReported()
        {
            AssertReports(@"
class System1
{
    void Start(CancellationToken ct)
    {
        System.Func<Task> run = async () =>
        {
            await UniTask.Delay(100, ct);
            await Child(ct);
        };
    }

    async EcsTask Child(CancellationToken ct) => await UniTask.Delay(100, ct);
}", ("LEOECS101", "Child"));
        }
    }
}
