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
    public async Task Local_lookups_go_before_slow_storage_lookups()
    {
        using var worker = new IconWorker();
        using var gate = new ManualResetEventSlim();
        var order = new List<string>();
        var blocker = worker.RunAsync(() =>
        {
            gate.Wait();
            return 0;
        });
        var slow = worker.RunAsync(() =>
        {
            order.Add("nas");
            return 1;
        }, slow: true);
        var local = worker.RunAsync(() =>
        {
            order.Add("local");
            return 1;
        });
        gate.Set();

        await Task.WhenAll(blocker, slow, local);

        Assert.Equal(["local", "nas"], order);
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
        Assert.False(worker.IsRetired); // a private worker is not replaced
        release.Set();
    }

    [Fact]
    public async Task The_shared_worker_is_replaced_when_a_lookup_hangs_and_waiting_lookups_move_over()
    {
        var before = IconWorker.Shared;
        using var release = new ManualResetEventSlim();

        var hung = before.RunAsync(() =>
        {
            release.Wait();
            return 1;
        }, TimeSpan.FromMilliseconds(200), "hung");
        var waiting = before.RunAsync(() => 2); // queued behind the hung lookup

        Assert.Equal(0, await hung);
        Assert.True(before.IsRetired);
        Assert.NotSame(before, IconWorker.Shared);
        Assert.Equal(2, await waiting); // ran on the successor
        Assert.Equal(3, await IconWorker.Shared.RunAsync(() => 3));
        release.Set();
    }

    [Fact]
    public async Task A_lookup_that_throws_faults_its_task_only()
    {
        using var worker = new IconWorker();

        await Assert.ThrowsAsync<InvalidOperationException>(() => worker.RunAsync<int>(() => throw new InvalidOperationException("boom")));
        Assert.Equal(7, await worker.RunAsync(() => 7));
    }

    [Theory]
    [InlineData(@"\\nas\share\tool.exe", true)]
    [InlineData(@"\\?\UNC\nas\share\tool.exe", true)]
    [InlineData("", false)]
    [InlineData("notepad", false)]
    [InlineData("https://example.com", false)]
    public void Network_paths_count_as_slow_storage(string path, bool slow) =>
        Assert.Equal(slow, IconWorker.IsOnSlowStorage(path));

    [Fact]
    public void The_system_drive_is_not_slow_storage() =>
        Assert.False(IconWorker.IsOnSlowStorage(Environment.GetFolderPath(Environment.SpecialFolder.Windows)));
}
