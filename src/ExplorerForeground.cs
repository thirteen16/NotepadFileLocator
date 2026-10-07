using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using System.Drawing;

namespace NotepadFileLocator
{
    internal static class ExplorerForeground
    {
        [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();
        [DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(uint process);
        internal static IDisposable Begin(string path, IntPtr source)
        {
            IntPtr existing = FindWindow(path, false);
            if (Native.GetForegroundWindow() != source) throw new InvalidOperationException("Foreground changed");
            if (existing != IntPtr.Zero)
            {
                if (!BringForward(existing, source)) throw new InvalidOperationException("Foreground unavailable");
                return new ForegroundLease(null, source);
            }
            var relay = new RelayWindow { ShowInTaskbar = false, FormBorderStyle = FormBorderStyle.None,
                StartPosition = FormStartPosition.Manual, Location = new Point(-100, -100),
                Size = new Size(1, 1), Opacity = 0 };
            relay.Show();
            if (!BringForward(relay.Handle, source)) { relay.Dispose(); throw new InvalidOperationException("Foreground unavailable"); }
            uint pid;
            Native.GetWindowThreadProcessId(GetShellWindow(), out pid);
            AllowSetForegroundWindow(pid);
            return new ForegroundLease(relay, source);
        }
        // No pixels, taskbar button or Alt+Tab entry. This window transfers our
        // foreground eligibility BEFORE asking the Shell to display a new window.
        private sealed class RelayWindow : Form
        {
            protected override CreateParams CreateParams
            { get { var parameters = base.CreateParams; parameters.ExStyle |= 0x80; return parameters; } }
        }
        private sealed class ForegroundLease : IDisposable
        {
            private readonly Form relay;
            private readonly IntPtr source;
            internal ForegroundLease(Form relay, IntPtr source) { this.relay = relay; this.source = source; }
            public void Dispose()
            {
                if (relay == null) return;
                if (Native.GetForegroundWindow() == relay.Handle) BringForward(source, relay.Handle);
                relay.Dispose();
            }
        }
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
        [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);
        [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint from, uint to, bool attach);
        [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr window);

        // Verification only: never activate or reorder Explorer after opening it.
        internal static bool WaitActivated(string path)
        {
            var timer = Stopwatch.StartNew();
            while (timer.ElapsedMilliseconds < 1800)
            {
                IntPtr target = FindWindow(path, true);
                if (target != IntPtr.Zero && Native.GetForegroundWindow() == target) return true;
                // Pump the transparent foreground window so Explorer can complete
                // the cross-thread activation during asynchronous Shell navigation.
                Application.DoEvents();
                Thread.Sleep(30);
            }
            return false;
        }

        private static IntPtr FindWindow(string path, bool requireSelection)
        {
            object shell = null, windows = null;
            try
            {
                shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application"));
                windows = Call(shell, "Windows");
                    int count = Convert.ToInt32(Get(windows, "Count"));
                    for (int i = 0; i < count; i++)
                    {
                        object window = null, document = null, folder = null, self = null, selected = null;
                        try
                        {
                            window = Call(windows, "Item", i);
                            if (window == null) continue;
                            if (!string.Equals(Path.GetFileName(Convert.ToString(Get(window, "FullName"))),
                                "explorer.exe", StringComparison.OrdinalIgnoreCase)) continue;
                            document = Get(window, "Document");
                            folder = Get(document, "Folder");
                            self = Get(folder, "Self");
                            if (!string.Equals(Convert.ToString(Get(self, "Path")).TrimEnd('\\'),
                                Path.GetDirectoryName(path).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) continue;
                            IntPtr target = GetAncestor(new IntPtr(unchecked((long)(uint)Convert.ToInt64(Get(window, "HWND")))), 2);
                            if (!requireSelection) return target;
                            selected = Call(document, "SelectedItems");
                            int selectedCount = Convert.ToInt32(Get(selected, "Count"));
                            for (int j = 0; j < selectedCount; j++)
                            {
                                object item = null;
                                try
                                {
                                    item = Call(selected, "Item", j);
                                    if (!string.Equals(Convert.ToString(Get(item, "Path")), path, StringComparison.OrdinalIgnoreCase)) continue;
                                    if (target != IntPtr.Zero && Native.GetForegroundWindow() == target) return target;
                                }
                                finally { Release(item); }
                            }
                        }
                        catch (COMException) { }
                        catch (TargetInvocationException) { }
                        finally { Release(selected); Release(self); Release(folder); Release(document); Release(window); }
                    }
            }
            catch (COMException) { }
            catch (TargetInvocationException) { }
            finally { Release(windows); Release(shell); }
            return IntPtr.Zero;
        }

        private static bool BringForward(IntPtr target, IntPtr source)
        {
            IntPtr foreground = Native.GetForegroundWindow();
            if (foreground == target) return true;
            // Do not steal focus if the user switched to another application meanwhile.
            if (foreground != source) return false;
            uint ignored;
            uint foregroundThread = Native.GetWindowThreadProcessId(source, out ignored);
            uint currentThread = GetCurrentThreadId();
            uint targetThread = Native.GetWindowThreadProcessId(target, out ignored);
            bool attached = false;
            bool targetAttached = false;
            try
            {
                if (Native.GetForegroundWindow() != source) return Native.GetForegroundWindow() == target;
                if (currentThread != foregroundThread)
                    attached = AttachThreadInput(currentThread, foregroundThread, true);
                if (attached)
                {
                    if (targetThread != currentThread && targetThread != foregroundThread)
                        targetAttached = AttachThreadInput(currentThread, targetThread, true);
                    if (IsIconic(target)) ShowWindow(target, 9); // Restore with activation, before Shell navigation.
                    BringWindowToTop(target);
                    SetForegroundWindow(target);
                }
                else SetForegroundWindow(target);
            }
            finally
            {
                if (targetAttached) AttachThreadInput(currentThread, targetThread, false);
                if (attached) AttachThreadInput(currentThread, foregroundThread, false);
            }
            // Activation of another thread's window can complete asynchronously.
            for (int i = 0; i < 10; i++)
            {
                foreground = Native.GetForegroundWindow();
                if (foreground == target) return true;
                if (foreground != source) break;
                Application.DoEvents();
                Thread.Sleep(20);
            }
            return false;
        }

        private static object Get(object obj, string name)
        { return obj.GetType().InvokeMember(name, BindingFlags.GetProperty, null, obj, null); }
        private static object Call(object obj, string name, params object[] args)
        { return obj.GetType().InvokeMember(name, BindingFlags.InvokeMethod, null, obj, args); }
        private static void Release(object obj)
        { if (obj != null && Marshal.IsComObject(obj)) Marshal.ReleaseComObject(obj); }
    }
}
