namespace DesktopIconGrid.Services;

/// <summary>Takes a snapshot of the executables that are currently running, by full image path.</summary>
public static class RunningProcesses
{
    private const uint ProcessQueryLimitedInformation = 0x1000;

    /// <summary>
    /// Full image paths (case-insensitive) of every process this user is allowed to query. Protected and
    /// other-session system processes are skipped, which is fine: no shortcut points at them.
    /// </summary>
    public static HashSet<string> ImagePaths()
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!OperatingSystem.IsWindows())
        {
            return paths;
        }

        var ids = new uint[1024];
        uint needed;
        while (true)
        {
            if (!NativeMethods.K32EnumProcesses(ids, (uint)(ids.Length * sizeof(uint)), out needed))
            {
                return paths;
            }

            if (needed < ids.Length * sizeof(uint))
            {
                break;
            }

            ids = new uint[ids.Length * 2];
        }

        var count = (int)(needed / sizeof(uint));
        var buffer = new char[1024];
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
                    paths.Add(new string(buffer, 0, (int)size));
                }
            }
            finally
            {
                NativeMethods.CloseHandle(handle);
            }
        }

        return paths;
    }
}
