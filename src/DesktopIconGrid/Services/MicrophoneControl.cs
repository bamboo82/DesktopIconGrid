using System.Runtime.InteropServices;
using DesktopIconGrid.Core.Diagnostics;

namespace DesktopIconGrid.Services;

/// <summary>One look at an audio endpoint (the microphone or the speakers).</summary>
/// <param name="State">Whether it is muted and its level (0–100); <see langword="null"/> when there is none or it could not be read.</param>
/// <param name="Failed">
/// Windows' audio calls failed (the audio service is stopped, an odd driver): there may well be a device, so a tile must
/// not say there is none (review 24). app.log has the reason.
/// </param>
public readonly record struct AudioReading((bool Muted, double Volume)? State, bool Failed = false)
{
    /// <summary>No such device.</summary>
    public static AudioReading NotFound => default;

    public static AudioReading Unreadable => new(null, Failed: true);
}

/// <summary>
/// The default microphone's mute switch and level, through Core Audio (<c>IAudioEndpointVolume</c>): the device
/// meetings use (the communications default), else the general default. The same for the speakers. No device reads as
/// <see cref="AudioReading.NotFound"/>, a failure on the way as <see cref="AudioReading.Unreadable"/>.
/// </summary>
public static class MicrophoneControl
{
    private const int ECapture = 1;
    private const int ERoleConsole = 0;
    private const int ERoleCommunications = 2;
    private const uint ClsCtxAll = 23;

    /// <summary><c>E_NOTFOUND</c> (<c>HRESULT_FROM_WIN32(ERROR_NOT_FOUND)</c>): what <c>GetDefaultAudioEndpoint</c> answers when there is no such device.</summary>
    private const int ENotFound = unchecked((int)0x80070490);

    /// <summary>Whether the microphone is muted and its level (0–100).</summary>
    public static AudioReading Read() => Read(speakers: false);

    /// <summary>Mutes or unmutes the microphone; its new state.</summary>
    public static AudioReading Toggle() => Toggle(speakers: false);

    /// <summary>The default speakers' mute state and volume (0–100).</summary>
    public static AudioReading ReadSpeakers() => Read(speakers: true);

    public static AudioReading ToggleSpeakers() => Toggle(speakers: true);

    /// <summary>
    /// Turns the speakers up or down by wheel notches (<see cref="Core.SpecialIcons.VolumeSteps"/>); turning up also
    /// unmutes. Their new state.
    /// </summary>
    public static AudioReading StepSpeakers(int notches) =>
        Use(speakers: true, volume =>
        {
            Marshal.ThrowExceptionForHR(volume.GetMute(out var muted));
            Marshal.ThrowExceptionForHR(volume.GetMasterVolumeLevelScalar(out var level));
            var next = Core.SpecialIcons.VolumeSteps.After(level * 100.0, notches);
            var context = Guid.Empty;
            Marshal.ThrowExceptionForHR(volume.SetMasterVolumeLevelScalar((float)(next / 100.0), ref context));
            if (muted && notches > 0)
            {
                Marshal.ThrowExceptionForHR(volume.SetMute(false, ref context));
                muted = false;
            }

            return (muted, next);
        });

    private static AudioReading Read(bool speakers) =>
        Use(speakers, volume =>
        {
            Marshal.ThrowExceptionForHR(volume.GetMute(out var muted));
            Marshal.ThrowExceptionForHR(volume.GetMasterVolumeLevelScalar(out var level));
            return (muted, Math.Clamp(level * 100.0, 0, 100));
        });

    private static AudioReading Toggle(bool speakers) =>
        Use(speakers, volume =>
        {
            Marshal.ThrowExceptionForHR(volume.GetMute(out var muted));
            var context = Guid.Empty;
            Marshal.ThrowExceptionForHR(volume.SetMute(!muted, ref context));
            Marshal.ThrowExceptionForHR(volume.GetMasterVolumeLevelScalar(out var level));
            return (!muted, Math.Clamp(level * 100.0, 0, 100)); // the tile redraws from this at once
        });

    private const int ERender = 0;
    private const int ERoleMultimedia = 1;

    private static AudioReading Use(bool speakers, Func<IAudioEndpointVolume, (bool Muted, double Volume)> work)
    {
        object? enumerator = null;
        IMMDevice? device = null;
        object? endpoint = null;
        try
        {
            enumerator = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"), throwOnError: true)!);
            var devices = (IMMDeviceEnumerator)enumerator!;
            // The microphone meetings use, else the general one; the speakers sound comes out of, else the media ones.
            var (flow, first, second) = speakers ? (ERender, ERoleConsole, ERoleMultimedia) : (ECapture, ERoleCommunications, ERoleConsole);
            var answer = devices.GetDefaultAudioEndpoint(flow, first, out device);
            if (answer != 0 || device is null)
            {
                answer = devices.GetDefaultAudioEndpoint(flow, second, out device);
                if (answer != 0 || device is null)
                {
                    if (answer is 0 or ENotFound)
                    {
                        return AudioReading.NotFound;
                    }

                    Note(speakers, $"0x{answer:X8}");
                    return AudioReading.Unreadable;
                }
            }

            var iid = typeof(IAudioEndpointVolume).GUID;
            Marshal.ThrowExceptionForHR(device.Activate(ref iid, ClsCtxAll, IntPtr.Zero, out endpoint));
            var state = work((IAudioEndpointVolume)endpoint);
            _ = Interlocked.Exchange(ref LastProblem(speakers), null);
            return new AudioReading(state);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Everything at the COM boundary: ThrowExceptionForHR maps HRESULTs to many exception types
            // (E_INVALIDARG, E_NOTIMPL, E_POINTER…), and an odd audio driver must not mean an error box every 2 s.
            Note(speakers, ex.Message);
            return AudioReading.Unreadable;
        }
        finally
        {
            Release(endpoint);
            Release(device);
            Release(enumerator);
        }
    }

    private static string? _microphoneProblem;
    private static string? _speakersProblem;

    private static ref string? LastProblem(bool speakers) => ref speakers ? ref _speakersProblem : ref _microphoneProblem;

    /// <summary>
    /// Logs why the device could not be reached: a warning when the reason is new, a debug line when it repeats (the
    /// tiles read every 2 seconds).
    /// </summary>
    private static void Note(bool speakers, string problem)
    {
        var text = $"The {(speakers ? "speakers" : "microphone")} could not be reached: {problem}";
        if (Interlocked.Exchange(ref LastProblem(speakers), text) == text)
        {
            AppLog.Default.Debug("Audio", text);
        }
        else
        {
            AppLog.Default.Warning("Audio", text);
        }
    }

    private static void Release(object? com)
    {
        if (com is not null && Marshal.IsComObject(com))
        {
            Marshal.ReleaseComObject(com);
        }
    }

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig]
        int EnumAudioEndpoints(int dataFlow, uint stateMask, out IntPtr devices);

        [PreserveSig]
        int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice? endpoint);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig]
        int Activate(ref Guid iid, uint clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
    }

    /// <summary>The first methods of <c>IAudioEndpointVolume</c>, in vtable order, up to the last one used.</summary>
    [ComImport]
    [Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        [PreserveSig]
        int RegisterControlChangeNotify(IntPtr notify);

        [PreserveSig]
        int UnregisterControlChangeNotify(IntPtr notify);

        [PreserveSig]
        int GetChannelCount(out uint count);

        [PreserveSig]
        int SetMasterVolumeLevel(float levelDb, ref Guid context);

        [PreserveSig]
        int SetMasterVolumeLevelScalar(float level, ref Guid context);

        [PreserveSig]
        int GetMasterVolumeLevel(out float levelDb);

        [PreserveSig]
        int GetMasterVolumeLevelScalar(out float level);

        [PreserveSig]
        int SetChannelVolumeLevel(uint channel, float levelDb, ref Guid context);

        [PreserveSig]
        int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context);

        [PreserveSig]
        int GetChannelVolumeLevel(uint channel, out float levelDb);

        [PreserveSig]
        int GetChannelVolumeLevelScalar(uint channel, out float level);

        [PreserveSig]
        int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);

        [PreserveSig]
        int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }
}
