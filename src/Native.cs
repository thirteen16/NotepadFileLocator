using System;
using System.Runtime.InteropServices;
using System.Text;

namespace NotepadFileLocator
{
    internal static class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct KeyInput
        {
            internal ushort Key, Scan;
            internal uint Flags, Time;
            internal UIntPtr Extra;
        }
        [StructLayout(LayoutKind.Explicit, Size = 40)]
        private struct Input
        {
            [FieldOffset(0)] internal uint Type;
            [FieldOffset(8)] internal KeyInput Keyboard;
        }
        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint count, Input[] inputs, int size);

        internal static bool OpenSaveAs(IntPtr window)
        {
            if (GetForegroundWindow() != window || ModifiersDown()) return false;
            var inputs = new Input[6];
            ushort[] keys = { 0x11, 0x10, 0x53, 0x53, 0x10, 0x11 };
            for (int i = 0; i < inputs.Length; i++)
                inputs[i] = new Input { Type = 1, Keyboard = new KeyInput { Key = keys[i], Flags = i >= 3 ? 2u : 0u } };
            return SendInput(6, inputs, Marshal.SizeOf(typeof(Input))) == 6;
        }
        internal delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);
        [StructLayout(LayoutKind.Sequential)]
        internal struct KeyboardData
        {
            public uint Key, ScanCode, Flags, Time;
            public UIntPtr ExtraInfo;
        }
        [StructLayout(LayoutKind.Sequential)]
        internal struct Point { public int X, Y; }
        [DllImport("user32.dll")]
        internal static extern bool GetCursorPos(out Point point);
        [DllImport("user32.dll")]
        internal static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")]
        internal static extern void mouse_event(uint flags, uint x, uint y, uint data, UIntPtr extra);
        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int index);
        internal static bool MoveCursor(int x, int y)
        {
            int left = GetSystemMetrics(76), top = GetSystemMetrics(77);
            int width = GetSystemMetrics(78), height = GetSystemMetrics(79);
            if (width <= 0 || height <= 0) return false;
            uint nx = (uint)(((long)(x - left) * 65536 + 32768) / width);
            uint ny = (uint)(((long)(y - top) * 65536 + 32768) / height);
            mouse_event(0xC001, nx, ny, 0, UIntPtr.Zero);
            System.Threading.Thread.Sleep(20);
            return SetCursorPos(x, y);
        }
        [DllImport("ole32.dll")]
        internal static extern int CoInitializeEx(IntPtr reserved, uint model);
        [DllImport("ole32.dll")]
        internal static extern void CoUninitialize();
        [DllImport("user32.dll", SetLastError = true)]
        internal static extern IntPtr SetWindowsHookEx(int id, HookProc callback, IntPtr module, uint thread);
        [DllImport("user32.dll")]
        internal static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")]
        internal static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        internal static extern IntPtr GetModuleHandle(string name);
        [DllImport("user32.dll")]
        internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern int GetClassName(IntPtr window, StringBuilder text, int count);
        [DllImport("user32.dll")]
        internal static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll")]
        internal static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
        [DllImport("user32.dll")]
        internal static extern IntPtr GetWindow(IntPtr window, uint command);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
        internal static extern int SHParseDisplayName(string name, IntPtr bind, out IntPtr item, uint flags, out uint attributes);
        [DllImport("shell32.dll", PreserveSig = true)]
        internal static extern int SHOpenFolderAndSelectItems(IntPtr item, uint count, IntPtr children, uint flags);

        internal static bool IsNotepadWindow(IntPtr window)
        {
            if (window == IntPtr.Zero || GetWindow(window, 4) != IntPtr.Zero) return false;
            var name = new StringBuilder(128);
            GetClassName(window, name, name.Capacity);
            return string.Equals(name.ToString(), "Notepad", StringComparison.OrdinalIgnoreCase);
        }

        internal static bool ModifiersDown()
        {
            return (GetAsyncKeyState(0x10) & 0x8000) != 0 ||
                   (GetAsyncKeyState(0x11) & 0x8000) != 0 ||
                   (GetAsyncKeyState(0x12) & 0x8000) != 0 ||
                   (GetAsyncKeyState(0x5B) & 0x8000) != 0 ||
                   (GetAsyncKeyState(0x5C) & 0x8000) != 0;
        }
    }
}
