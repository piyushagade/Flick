using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Flick.App.Services;

public sealed record MicrophoneStatus(bool HasCaptureDevice, bool IsMuted, bool IsPrivacyDenied);

/// <summary>
/// Provides global microphone mute/unmute (via Windows Core Audio IAudioEndpointVolume)
/// and global camera allow/deny toggle (via Windows Privacy ConsentStore).
/// </summary>
public static class DevicePrivacyService
{
    private const string WebcamConsentKeyPath =
        @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\webcam";
    private const string MicConsentKeyPath =
        @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\microphone";

    private static readonly Guid ClsidMMDeviceEnumerator = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    private static readonly Guid IidIAudioEndpointVolume = new("5CDF2C82-841E-4546-9722-0CF74078229A");

    public static MicrophoneStatus GetMicrophoneStatus()
    {
        bool hasDevice = KnockAudioService.GetAvailableMicrophones().Count > 0;
        bool isPrivacyDenied = false;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(MicConsentKeyPath, writable: false);
            if (key?.GetValue("Value") is string val &&
                string.Equals(val, "Deny", StringComparison.OrdinalIgnoreCase))
            {
                isPrivacyDenied = true;
            }
        }
        catch { }

        bool isMuted = false;
        try
        {
            var type = Type.GetTypeFromCLSID(ClsidMMDeviceEnumerator);
            if (type is not null && Activator.CreateInstance(type) is IMMDeviceEnumerator enumerator)
            {
                try
                {
                    isMuted = (GetCaptureEndpointMute(enumerator, 2) ?? GetCaptureEndpointMute(enumerator, 0)) == true;
                }
                finally
                {
                    Marshal.ReleaseComObject(enumerator);
                }
            }
        }
        catch { }

        return new MicrophoneStatus(hasDevice, isMuted, isPrivacyDenied);
    }

    public static bool ToggleMicrophoneMute()
    {
        try
        {
            var type = Type.GetTypeFromCLSID(ClsidMMDeviceEnumerator);
            if (type is null) return false;

            var enumerator = (IMMDeviceEnumerator?)Activator.CreateInstance(type);
            if (enumerator is null) return false;

            try
            {
                // Read current mute state from eCommunications (2) or eConsole (0) capture endpoint.
                bool? currentMute = GetCaptureEndpointMute(enumerator, 2) ?? GetCaptureEndpointMute(enumerator, 0);
                if (!currentMute.HasValue) return false;

                bool nextMute = !currentMute.Value;
                bool okComm = SetCaptureEndpointMute(enumerator, 2, nextMute);
                bool okConsole = SetCaptureEndpointMute(enumerator, 0, nextMute);
                return okComm || okConsole;
            }
            finally
            {
                Marshal.ReleaseComObject(enumerator);
            }
        }
        catch
        {
            return false;
        }
    }

    public static bool ToggleCameraAccess()
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(WebcamConsentKeyPath, writable: true);
            if (key is null) return false;

            string current = key.GetValue("Value") as string ?? "Allow";
            string next = string.Equals(current, "Deny", StringComparison.OrdinalIgnoreCase) ? "Allow" : "Deny";
            key.SetValue("Value", next, RegistryValueKind.String);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool? GetCaptureEndpointMute(IMMDeviceEnumerator enumerator, int role)
    {
        if (enumerator.GetDefaultAudioEndpoint(1 /* eCapture */, role, out var device) != 0 || device is null)
        {
            return null;
        }

        try
        {
            var iid = IidIAudioEndpointVolume;
            if (device.Activate(ref iid, 23 /* CLSCTX_ALL */, 0, out object? volObj) != 0 ||
                volObj is not IAudioEndpointVolume vol)
            {
                return null;
            }

            try
            {
                return vol.GetMute(out bool muted) == 0 ? muted : null;
            }
            finally
            {
                Marshal.ReleaseComObject(vol);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(device);
        }
    }

    private static bool SetCaptureEndpointMute(IMMDeviceEnumerator enumerator, int role, bool mute)
    {
        if (enumerator.GetDefaultAudioEndpoint(1 /* eCapture */, role, out var device) != 0 || device is null)
        {
            return false;
        }

        try
        {
            var iid = IidIAudioEndpointVolume;
            if (device.Activate(ref iid, 23 /* CLSCTX_ALL */, 0, out object? volObj) != 0 ||
                volObj is not IAudioEndpointVolume vol)
            {
                return false;
            }

            try
            {
                var ctx = Guid.Empty;
                return vol.SetMute(mute, ref ctx) == 0;
            }
            finally
            {
                Marshal.ReleaseComObject(vol);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(device);
        }
    }

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int dwStateMask, out nint ppDevices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice? ppEndpoint);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int dwClsCtx, nint pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object? ppInterface);
    }

    [ComImport]
    [Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(nint pNotify);
        [PreserveSig] int UnregisterControlChangeNotify(nint pNotify);
        [PreserveSig] int GetChannelCount(out uint pnChannelCount);
        [PreserveSig] int SetMasterVolumeLevel(float fLevelDB, ref Guid pguidEventContext);
        [PreserveSig] int SetMasterVolumeLevelScalar(float fLevel, ref Guid pguidEventContext);
        [PreserveSig] int GetMasterVolumeLevel(out float pfLevelDB);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float pfLevel);
        [PreserveSig] int SetChannelVolumeLevel(uint nChannel, float fLevelDB, ref Guid pguidEventContext);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint nChannel, float fLevel, ref Guid pguidEventContext);
        [PreserveSig] int GetChannelVolumeLevel(uint nChannel, out float pfLevelDB);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint nChannel, out float pfLevel);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool bMute, ref Guid pguidEventContext);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool pbMute);
    }
}
