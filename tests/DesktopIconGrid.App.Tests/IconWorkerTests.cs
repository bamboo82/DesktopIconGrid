using DesktopIconGrid.Services;

namespace DesktopIconGrid.App.Tests;

public class IconWorkerTests
{
    [Fact]
    public async Task Runs_work_in_order_on_one_STA_thread()
    {
        using var worker = new IconWorker();
        var order = new List<int>();

        var threads = await Task.WhenAll(Enumerable.Range(0, 5).Select(i => worker.RunAsync(() =>
        {
            Thread.Sleep(10);
            order.Add(i);
            return Thread.CurrentThread;
        })));

        Assert.Equal([0, 1, 2, 3, 4], order);
        Assert.Single(threads.Distinct());
        Assert.Equal(ApartmentState.STA, threads[0].GetApartmentState());
        Assert.Equal(0, worker.Pending);
    }

    [Fact]
    public async Task A_lookup_that_overruns_its_deadline_yields_the_placeholder_and_frees_the_caller()
    {
        using var worker = new IconWorker();
        using var release = new ManualResetEventSlim();

        var result = await worker.RunAsync(() =>
        {
            release.Wait();
            return "late";
        }, TimeSpan.FromMilliseconds(100), "test");

        Assert.Null(result);
        Assert.Equal(1, worker.Pending); // still running on the worker; its late result is discarded
        release.Set();
    }

    [Fact]
    public async Task A_lookup_that_throws_faults_its_task_only()
    {
        using var worker = new IconWorker();

        await Assert.ThrowsAsync<InvalidOperationException>(() => worker.RunAsync<int>(() => throw new InvalidOperationException("boom")));
        Assert.Equal(7, await worker.RunAsync(() => 7));
    }
}
