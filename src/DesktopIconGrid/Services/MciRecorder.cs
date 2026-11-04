using System.Runtime.InteropServices;
using System.Text;
using DesktopIconGrid.Resources;

namespace DesktopIconGrid.Services;

/// <summary>Records from the default microphone to a WAV file through the Windows MCI wave audio device.</summary>
public sealed class MciRecorder : IDisposable
{
    private static int _counter;
    private readonly string Alias = $"dig_capture_{Interlocked.Increment(ref _counter)}";
    private bool _open;

    public bool IsRecording { get; private set; }

    public void Start()
    {
        if (IsRecording)
        {
            return;
        }

        Send($"open new type waveaudio alias {Alias}");
        _open = true;
        try
        {
            Send($"set {Alias} time format ms bitspersample 16 channels 1 samplespersec 22050 bytespersec 44100 alignment 2");
            Send($"record {Alias}");
            IsRecording = true;
        }
        catch
        {
            Close();
            throw;
        }
    }

    /// <summary>Stops recording and writes the WAV file.</summary>
    public void StopAndSave(string path)
    {
        if (!IsRecording)
        {
            return;
        }

        try
        {
            Send($"stop {Alias}");
            Send($"save {Alias} \"{path}\"");
        }
        finally
        {
            Close();
        }
    }

    public void Cancel() => Close();

    public void Dispose() => Close();

    private void Close()
    {
        if (_open)
        {
            NativeMethods.mciSendString($"close {Alias}", null, 0, IntPtr.Zero);
            _open = false;
        }

        IsRecording = false;
    }

    private static void Send(string command)
    {
        var error = NativeMethods.mciSendString(command, null, 0, IntPtr.Zero);
        if (error != 0)
        {
            throw new InvalidOperationException(Describe(error));
        }
    }

    private static string Describe(int error)
    {
        var buffer = new StringBuilder(256);
        return NativeMethods.mciGetErrorString(error, buffer, buffer.Capacity) && buffer.Length > 0
            ? buffer.ToString()
            : string.Format(Strings.Sound_RecordingFailed, error);
    }

    private static class NativeMethods
    {
        [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
        public static extern int mciSendString(string command, StringBuilder? returnValue, int returnLength, IntPtr callback);

        [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool mciGetErrorString(int error, StringBuilder buffer, int length);
    }
}
