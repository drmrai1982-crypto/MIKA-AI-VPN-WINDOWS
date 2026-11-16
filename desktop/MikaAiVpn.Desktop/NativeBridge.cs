using System.Runtime.InteropServices;
using System.Text;

namespace MikaAiVpn.Desktop;

internal static class NativeBridge
{
    [DllImport(
        "MikaNativeBridge.dll",
        EntryPoint = "mika_native_initialize_inspect",
        CharSet = CharSet.Unicode,
        CallingConvention = CallingConvention.Cdecl)]
    private static extern int InitializeAndInspect(
        string ffiDirectory,
        StringBuilder message,
        uint messageCapacity);

    public static (int Result, string Message) Inspect(string ffiDirectory)
    {
        var message = new StringBuilder(4096);
        var result = InitializeAndInspect(ffiDirectory, message, (uint)message.Capacity);
        return (result, message.ToString());
    }
}
