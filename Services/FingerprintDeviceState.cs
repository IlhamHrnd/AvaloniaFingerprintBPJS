using System;

namespace AvaloniaFingerprint.Services
{
    public enum FingerprintReaderState
    {
        NoDevice = 0,

        Detected = 1,

        Initializing = 2,

        Ready = 3,

        NotReady = 4,
    }

    public sealed record FingerprintDeviceState(
        FingerprintReaderState State,
        string? DeviceName,
        string? DeviceId,
        string? Detail)
    {
        public bool IsConnected => State == FingerprintReaderState.Ready;

        public static readonly FingerprintDeviceState None = new(FingerprintReaderState.NoDevice, null, null, null);

        public static FingerprintDeviceState AsNoDevice(string? detail = null) =>
            new(FingerprintReaderState.NoDevice, null, null, detail);

        public static FingerprintDeviceState AsDetected(string? deviceName, string? deviceId, string? detail = null) =>
            new(FingerprintReaderState.Detected, deviceName, deviceId, detail);

        public static FingerprintDeviceState AsInitializing(string? deviceName, string? deviceId, string? detail = null) =>
            new(FingerprintReaderState.Initializing, deviceName, deviceId, detail);

        public static FingerprintDeviceState AsReady(string? deviceName, string? deviceId, string? detail = null) =>
            new(FingerprintReaderState.Ready, deviceName, deviceId, detail);

        public static FingerprintDeviceState AsNotReady(string? deviceName, string? deviceId, string? detail = null) =>
            new(FingerprintReaderState.NotReady, deviceName, deviceId, detail);
    }

    public delegate FingerprintDeviceState? FingerprintSdkProbe(Action<FingerprintDeviceState> publish);
}
