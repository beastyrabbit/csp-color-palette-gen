using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;

namespace CspPaletteCompanion.App;

internal static partial class NativeMethods
{
    internal const uint InputKeyboard = 1;
    internal const uint KeyEventKeyUp = 0x0002;
    internal const ushort VirtualKeyControl = 0x11;
    internal const ushort VirtualKeyC = 0x43;
    internal const uint WindowMessageNull = 0;
    internal const uint SendMessageAbortIfHung = 0x0002;

    private const int DwmWindowCornerPreference = 33;
    private const int DwmWindowCornerPreferenceRound = 2;
    private const uint NoError = 0;
    private const uint ErrorInsufficientBuffer = 122;
    private const int AddressFamilyInternetV4 = 2;
    private const int AddressFamilyInternetV6 = 23;
    private const int TcpTableOwnerPidListener = 3;
    private const int TcpRowV4Bytes = 24;
    private const int TcpRowV6Bytes = 56;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        nint windowHandle,
        int attribute,
        ref int value,
        int size);

    /// <summary>
    /// On Windows 10 the attribute is unrecognised and the call returns a non-zero
    /// HRESULT, which is ignored: square corners are the documented degradation.
    /// </summary>
    internal static void ApplyRoundedCorners(nint windowHandle)
    {
        var preference = DwmWindowCornerPreferenceRound;
        try
        {
            DwmSetWindowAttribute(
                windowHandle,
                DwmWindowCornerPreference,
                ref preference,
                sizeof(int));
        }
        catch (DllNotFoundException)
        {
        }
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetForegroundWindow(nint windowHandle);

    /// <summary>
    /// The same string yields the same message number in every process, which is what
    /// lets a second launch reach the first instance without sharing any other state.
    /// </summary>
    [LibraryImport("user32.dll", EntryPoint = "RegisterWindowMessageW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial uint RegisterWindowMessage(string message);

    [LibraryImport("user32.dll", EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool PostMessage(nint windowHandle, uint message, nuint wParam, nint lParam);

    // HWND_BROADCAST is not usable here: it skips invisible owned windows, and in tray
    // mode the window is both — ShowInTaskbar="False" makes WPF give it a hidden owner.
    // EnumWindows sees every top-level window, and the property is how the right one
    // identifies itself across the process boundary.
    [LibraryImport("user32.dll", EntryPoint = "SetPropW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetProp(nint windowHandle, string name, nint value);

    [LibraryImport("user32.dll", EntryPoint = "GetPropW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    internal static partial nint GetProp(nint windowHandle, string name);

    [LibraryImport("user32.dll", EntryPoint = "RemovePropW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    internal static partial nint RemoveProp(nint windowHandle, string name);

    [DllImport("iphlpapi.dll")]
    private static extern uint GetExtendedTcpTable(
        nint table,
        ref int size,
        [MarshalAs(UnmanagedType.Bool)] bool order,
        int addressFamily,
        int tableClass,
        int reserved);

    /// <summary>
    /// True when <paramref name="processId"/> owns the listening socket on
    /// <paramref name="address"/>:<paramref name="port"/>. This is what makes the
    /// liveness proof honest: a live process of the right name says nothing about who
    /// holds the ephemeral port a credential is about to be sent to. The table is the
    /// one behind <c>netstat -o</c>, so an unprivileged caller gets owning PIDs.
    /// </summary>
    internal static bool OwnsListener(int processId, IPAddress address, ushort port)
    {
        ArgumentNullException.ThrowIfNull(address);

        var isV6 = address.AddressFamily == AddressFamily.InterNetworkV6;
        var family = isV6 ? AddressFamilyInternetV6 : AddressFamilyInternetV4;
        var rowBytes = isV6 ? TcpRowV6Bytes : TcpRowV4Bytes;

        var size = 0;
        var status = GetExtendedTcpTable(
            nint.Zero,
            ref size,
            false,
            family,
            TcpTableOwnerPidListener,
            0);
        if (status is not (NoError or ErrorInsufficientBuffer) || size < sizeof(int))
        {
            return false;
        }

        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            status = GetExtendedTcpTable(
                buffer,
                ref size,
                false,
                family,
                TcpTableOwnerPidListener,
                0);
            if (status != NoError)
            {
                return false;
            }

            var count = Marshal.ReadInt32(buffer);
            for (var index = 0; index < count; index++)
            {
                var row = buffer + sizeof(int) + (index * rowBytes);
                if (ReadListenerPort(row, isV6) != port ||
                    Marshal.ReadInt32(row, isV6 ? 52 : 20) != processId)
                {
                    continue;
                }

                if (MatchesLocalAddress(row, isV6, address))
                {
                    return true;
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return false;
    }

    // dwLocalPort carries the port in network byte order in the low half of the DWORD.
    private static ushort ReadListenerPort(nint row, bool isV6)
    {
        var raw = Marshal.ReadInt32(row, isV6 ? 20 : 8);
        return (ushort)(((raw & 0xFF) << 8) | ((raw >> 8) & 0xFF));
    }

    private static bool MatchesLocalAddress(nint row, bool isV6, IPAddress address)
    {
        if (isV6)
        {
            var bytes = new byte[16];
            Marshal.Copy(row, bytes, 0, bytes.Length);
            var local = new IPAddress(bytes);
            // A wildcard listener genuinely owns the loopback endpoint it is being
            // checked for, so it is a match rather than a refusal.
            return local.Equals(address) || local.Equals(IPAddress.IPv6Any);
        }

        var raw = Marshal.ReadInt32(row, sizeof(int));
        var localV4 = new IPAddress(BitConverter.GetBytes(raw));
        return localV4.Equals(address) || localV4.Equals(IPAddress.Any);
    }

    [LibraryImport("user32.dll")]
    internal static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    internal static partial uint GetClipboardSequenceNumber();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumWindows(EnumWindowsCallback callback, nint parameter);

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(nint windowHandle, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindowVisible(nint windowHandle);

    [DllImport("user32.dll")]
    internal static extern nint GetWindow(nint windowHandle, uint command);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetWindowTextLength(nint windowHandle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetWindowText(nint windowHandle, StringBuilder text, int maximumCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetClassName(nint windowHandle, StringBuilder className, int maximumCount);

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial uint SendInput(
        uint inputCount,
        [In] Input[] inputs,
        int inputSize);

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial nint SendMessageTimeout(
        nint windowHandle,
        uint message,
        nuint wParam,
        nint lParam,
        uint flags,
        uint timeoutMilliseconds,
        out nuint result);

    [StructLayout(LayoutKind.Sequential)]
    internal struct Input
    {
        internal uint Type;
        internal InputUnion Union;
    }

    [StructLayout(LayoutKind.Explicit, Size = 32)]
    internal struct InputUnion
    {
        [FieldOffset(0)]
        internal KeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct KeyboardInput
    {
        internal ushort VirtualKey;
        internal ushort ScanCode;
        internal uint Flags;
        internal uint Time;
        internal nuint ExtraInfo;
    }

    internal delegate bool EnumWindowsCallback(nint windowHandle, nint parameter);
}
