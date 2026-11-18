using System.Runtime.InteropServices;
using System.Text;

namespace MikaAiVpn.Desktop;

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct NativeTelemetry
{
    public uint StructSize;
    public uint AbiVersion;
    public int State;
    public int Backend;
    public int ActiveMode;
    public uint RttMs;
    public ulong DownloadBytesPerSecond;
    public ulong UploadBytesPerSecond;
    public ulong TotalDownloadBytes;
    public ulong TotalUploadBytes;
    public ulong UptimeSeconds;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string ConnectedPeer;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string StatusMessage;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string LastError;
}

internal static class NativeBridge
{
    private const uint AbiVersion = 19;

    [DllImport("MikaNativeBridge.dll", EntryPoint = "mika_native_initialize_inspect",
        CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
    private static extern int InitializeAndInspect(string ffiDirectory, StringBuilder message, uint messageCapacity);

    [DllImport("MikaNativeBridge.dll", EntryPoint = "mika_native_session_init",
        CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
    private static extern int InitializeSession(string ffiDirectory, StringBuilder message, uint messageCapacity);

    [DllImport("MikaNativeBridge.dll", EntryPoint = "mika_native_session_start",
        CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
    private static extern int StartSession(string dataDirectory, ulong tunEngine, uint protocol, StringBuilder message, uint messageCapacity);

    [DllImport("MikaNativeBridge.dll", EntryPoint = "mika_native_session_stop",
        CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
    private static extern int StopSession(StringBuilder message, uint messageCapacity);

    [DllImport("MikaNativeBridge.dll", EntryPoint = "mika_native_session_telemetry",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern int ReadTelemetry(ref NativeTelemetry telemetry);

    [DllImport("MikaNativeBridge.dll", EntryPoint = "mika_native_session_shutdown",
        CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
    private static extern int ShutdownSession(StringBuilder message, uint messageCapacity);

    private static StringBuilder NewMessage() => new(2048);

    public static (int Result, string Message) Inspect(string ffiDirectory)
    {
        var message = NewMessage();
        var result = InitializeAndInspect(ffiDirectory, message, (uint)message.Capacity);
        return (result, message.ToString());
    }

    public static (int Result, string Message) Initialize(string ffiDirectory)
    {
        var message = NewMessage();
        var result = InitializeSession(ffiDirectory, message, (uint)message.Capacity);
        return (result, message.ToString());
    }

    public static (int Result, string Message) Start(string dataDirectory, ulong tunEngine = 2, uint protocol = 0)
    {
        var message = NewMessage();
        var result = StartSession(dataDirectory, tunEngine, protocol, message, (uint)message.Capacity);
        return (result, message.ToString());
    }

    public static (int Result, string Message) Stop()
    {
        var message = NewMessage();
        var result = StopSession(message, (uint)message.Capacity);
        return (result, message.ToString());
    }

    public static (int Result, NativeTelemetry Telemetry) Telemetry()
    {
        var telemetry = new NativeTelemetry
        {
            StructSize = (uint)Marshal.SizeOf<NativeTelemetry>(),
            AbiVersion = AbiVersion,
            ConnectedPeer = string.Empty,
            StatusMessage = string.Empty,
            LastError = string.Empty
        };
        var result = ReadTelemetry(ref telemetry);
        return (result, telemetry);
    }

    public static (int Result, string Message) Shutdown()
    {
        var message = NewMessage();
        var result = ShutdownSession(message, (uint)message.Capacity);
        return (result, message.ToString());
    }
}
