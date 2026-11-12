using System.Runtime.InteropServices;
using DesktopIconGrid.Core.Diagnostics;

namespace DesktopIconGrid.Services;

/// <summary>
/// The default microphone's mute switch and level, through Core Audio (<c>IAudioEndpointVolume</c>): the device
/// meetings use (the communications default), else the general default. No device, or no audio service, reads as
/// <see langword="null"/>.
/// </summary>
public static class MicrophoneControl
{
    private const int ECapture = 1;
    private const int ERoleConsole = 0;
    private const int ERoleCommunications = 2;
    private const uint ClsCtxAll = 23;

    /// <summary>Whether the microphone is muted and its level (0–100), or <see langword="null"/> when there is none.</summary>
    public static (bool Muted, double Volume)? Read() => Read(speakers: false);

    /// <summary>Mutes or unmutes the microphone; the new state, or <see langword="null"/> when there is none.</summary>
    public static bool? Toggle() => Toggle(speakers: false);

    /// <summary>The default speakers' mute state and volume (0–100), or <see langword="null"/> when there are none.</summary>
    public static (bool Muted, double Volume)? ReadSpeakers() => Read(speakers: true);

    public static bool? ToggleSpeakers() => Toggle(speakers: true);

    /// <summary>
    /// Turns the speakers up or down by wheel notches (<see cref="Core.SpecialIcons.VolumeSteps"/>); turning up also
    /// unmutes. The new state, or <see langword="null"/> when there are no speakers.
    /// </summary>
    public static (bool Muted, double Volume)? StepSpeakers(int notches) =>
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

            return ((bool, double)?)(muted, next);
        });

    private static (bool Muted, double Volume)? Read(bool speakers) =>
        Use(speakers, volume =>
        {
            Marshal.ThrowExceptionForHR(volume.GetMute(out var muted));
            Marshal.ThrowExceptionForHR(volume.GetMasterVolumeLevelScalar(out var level));
            return ((bool, double)?)(muted, Math.Clamp(level * 100.0, 0, 100));
        });

    private static bool? Toggle(bool speakers) =>
        Use(speakers, volume =>
        {
            Marshal.ThrowExceptionForHR(volume.GetMute(out var muted));
            var context = Guid.Empty;
            Marshal.ThrowExceptionForHR(volume.SetMute(!muted, ref context));
            return (bool?)!muted;
        });

    private const int ERender = 0;
    private const int ERoleMultimedia = 1;

    private static T? Use<T>(bool speakers, Func<IAudioEndpointVolume, T?> work)
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
            if (devices.GetDefaultAudioEndpoint(flow, first, out device) != 0 || device is null)
            {
                if (devices.GetDefaultAudioEndpoint(flow, second, out device) != 0 || device is null)
                {
                    return default; // no such device
                }
            }

            var iid = typeof(IAudioEndpointVolume).GUID;
            Marshal.ThrowExceptionForHR(device.Activate(ref iid, ClsCtxAll, IntPtr.Zero, out endpoint));
            return work((IAudioEndpointVolume)endpoint);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Everything at the COM boundary: ThrowExceptionForHR maps HRESULTs to many exception types
            // (E_INVALIDARG, E_NOTIMPL, E_POINTER…), and an odd audio driver must not mean an error box every 2 s.
            AppLog.Default.Debug("Audio", $"The microphone could not be reached: {ex.Message}");
            return default;
        }
        finally
        {
            Release(endpoint);
            Release(device);
            Release(enumerator);
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
