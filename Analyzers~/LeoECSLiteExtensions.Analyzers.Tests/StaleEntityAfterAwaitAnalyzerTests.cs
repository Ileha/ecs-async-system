using NUnit.Framework;
using static LeoECSLiteExtensions.Analyzers.Tests.AnalyzerRunner;

namespace LeoECSLiteExtensions.Analyzers.Tests
{
    public class StaleEntityAfterAwaitAnalyzerTests
    {
        [Test]
        public void EntityCopiedFromFilter_UsedAfterAwait_IsReported()
        {
            AssertReports(@"
struct Health { public int Value; }

class System1
{
    IEcsFilter _filter;
    EcsPool<Health> _pool;

    async EcsTask RunAsync(CancellationToken ct)
    {
        var target = -1;
        foreach (var entity in _filter)
        {
            target = entity;
        }

        await UniTask.Delay(1000, ct);
        _pool.Get(target).Value = 0;
    }
}", ("LEOECS104", "target"));
        }

        [Test]
        public void NewEntity_UsedAfterAwait_IsReported()
        {
            AssertReports(@"
struct Health { public int Value; }

class System1
{
    EcsWorld _world;
    EcsPool<Health> _pool;

    async EcsTask RunAsync(CancellationToken ct)
    {
        var entity = _world.NewEntity();
        await UniTask.Delay(1000, ct);
        _pool.Get(entity).Value = 0;
    }
}", ("LEOECS104", "entity"));
        }

        [Test]
        public void EntityFromBeforeAwait_InLoop_IsReported()
        {
            AssertReports(@"
class System1
{
    EcsWorld _world;

    async EcsTask RunAsync(CancellationToken ct)
    {
        var entity = _world.NewEntity();
        while (true)
        {
            _world.PackEntity(entity);
            await UniTask.Delay(1000, ct);
        }
    }
}", ("LEOECS104", "entity"));
        }

        [Test]
        public void PackedEntity_UnpackedAfterAwait_IsFine()
        {
            AssertReports(@"
struct Health { public int Value; }

class System1
{
    EcsWorld _world;
    EcsPool<Health> _pool;

    async EcsTask RunAsync(CancellationToken ct)
    {
        var packed = _world.PackEntity(_world.NewEntity());
        await UniTask.Delay(1000, ct);

        if (packed.Unpack(_world, out var entity))
        {
            _pool.Get(entity).Value = 0;
        }
    }
}");
        }

        [Test]
        public void EntityWrittenAgainAfterAwait_IsFine()
        {
            AssertReports(@"
class System1
{
    EcsWorld _world;

    async EcsTask RunAsync(CancellationToken ct)
    {
        var entity = _world.NewEntity();
        await UniTask.Delay(1000, ct);
        entity = _world.NewEntity();
        _world.PackEntity(entity);
    }
}");
        }
    }
}
