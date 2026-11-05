namespace DesktopIconGrid.Services;

/// <summary>Takes a snapshot of the executables that are currently running, by full image path.</summary>
public static class RunningProcesses
{
    private const uint ProcessQueryLimitedInformation = 0x1000;

    /// <summary>
    /// The process ids of every process this user is allowed to query, by full image path (case-insensitive).
    /// Protected and other-session system processes are skipped, which is fine: no shortcut points at them.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<int>> Snapshot()
    {
        var paths = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        if (!OperatingSystem.IsWindows())
        {
            return Freeze(paths);
        }

        var ids = new uint[1024];
        uint needed;
        while (true)
        {
            if (!NativeMethods.K32EnumProcesses(ids, (uint)(ids.Length * sizeof(uint)), out needed))
            {
                return Freeze(paths);
            }

            if (needed < ids.Length * sizeof(uint))
            {
                break;
            }

            ids = new uint[ids.Length * 2];
        }

        var count = (int)(needed / sizeof(uint));
        var buffer = new char[32768]; // long paths: the app manifest is longPathAware
        for (var i = 0; i < count; i++)
        {
            var handle = NativeMethods.OpenProcess(ProcessQueryLimitedInformation, false, ids[i]);
            if (handle == IntPtr.Zero)
            {
                continue;
            }

            try
            {
                var size = (uint)buffer.Length;
                if (NativeMethods.QueryFullProcessImageName(handle, 0, buffer, ref size) && size > 0)
                {
                    var path = new string(buffer, 0, (int)size);
                    if (!paths.TryGetValue(path, out var processIds))
                    {
                        paths[path] = processIds = [];
                    }

                    processIds.Add((int)ids[i]);
                }
            }
            finally
            {
                NativeMethods.CloseHandle(handle);
            }
        }

        return Freeze(paths);
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<int>> Freeze(Dictionary<string, List<int>> paths) =>
        paths.ToDictionary(p => p.Key, p => (IReadOnlyList<int>)p.Value, StringComparer.OrdinalIgnoreCase);
}
