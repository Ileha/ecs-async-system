using NUnit.Framework;
using static LeoECSLiteExtensions.Analyzers.Tests.AnalyzerRunner;

namespace LeoECSLiteExtensions.Analyzers.Tests
{
    public class AwaitInFilterLoopAnalyzerTests
    {
        [Test]
        public void AwaitInsideBridgeFilterLoop_IsReported()
        {
            AssertReports(@"
struct Health { }

class System1
{
    IEcsFilter<Health> _filter;

    async EcsTask RunAsync(CancellationToken ct)
    {
        foreach (var entity in _filter)
        {
            await UniTask.Delay(100, ct);
        }
    }
}", ("LEOECS102", "await"));
        }

        [Test]
        public void AwaitInsideRawFilterLoop_IsReported()
        {
            AssertReports(@"
class System1
{
    IEcsFilter _filter;

    async EcsTask RunAsync(CancellationToken ct)
    {
        foreach (var entity in _filter.Value)
        {
            if (entity > 0)
            {
                await UniTask.Delay(100, ct);
            }
        }
    }
}", ("LEOECS102", "await"));
        }

        [Test]
        public void AwaitAfterFilterLoop_IsFine()
        {
            AssertReports(@"
class System1
{
    IEcsFilter _filter;

    async EcsTask RunAsync(CancellationToken ct)
    {
        var count = 0;
        foreach (var entity in _filter)
        {
            count++;
        }

        await UniTask.Delay(count, ct);
    }
}");
        }
    }
}
