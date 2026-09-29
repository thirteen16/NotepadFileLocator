using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: System.Reflection.AssemblyTitle("TXT 文件定位器")]
[assembly: System.Reflection.AssemblyVersion("1.0.0.0")]

namespace NotepadFileLocator
{
    internal static class Program
    {
        internal const string StopEvent = "Local\\NotepadFileLocator.Stop.v1";

        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length == 2 && (args[0] == "--resolve" || args[0] == "--locate"))
                return ResolveWorker(args[1], args[0] == "--locate");
            if (args.Length == 1 && args[0] == "--stop")
            {
                try { using (var stop = EventWaitHandle.OpenExisting(StopEvent)) stop.Set(); }
                catch (WaitHandleCannotBeOpenedException) { }
                return 0;
            }
            bool created;
            using (var mutex = new Mutex(true, "Local\\NotepadFileLocator.Instance.v1", out created))
            {
                if (!created) return 0;
                try
                {
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    using (var context = new LocatorContext()) Application.Run(context);
                    return 0;
                }
                catch (Exception error)
                {
                    MessageBox.Show("启动失败：" + error.Message, "TXT 文件定位器", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return 1;
                }
                finally { mutex.ReleaseMutex(); }
            }
        }

        private static ResolveResult ReadMetadata(IntPtr handle, ResolveResult expected = null)
        {
            ResolveResult result = null;
            var thread = new Thread(delegate()
            {
                try { result = ActiveFileResolver.Resolve(handle, expected); }
                catch (Exception) { result = new ResolveResult { Error = "暂时无法读取记事本的标签信息，请回到文档编辑区后重试。" }; }
            });
            thread.IsBackground = true;
            thread.SetApartmentState(ApartmentState.MTA);
            thread.Start();
            if (!thread.Join(3500))
            {
                TooltipReader.CancelAndRestoreCursor();
                result = new ResolveResult { Error = "记事本响应超时，请稍后重试。" };
            }
            return result;
        }

        private static int ResolveWorker(string handleText, bool reveal)
        {
            long handle;
            if (!long.TryParse(handleText, out handle) || handle == 0) return 2;
            IntPtr window = new IntPtr(handle);
            ResolveResult result = ReadMetadata(window);
            if (result == null) return 3;
            if (reveal && result.Error == null && result.SaveRequired)
            {
                ResolveResult current = ReadMetadata(window, result);
                if (current == null || current.Error != null || !current.SaveRequired || current.Identity != result.Identity ||
                    !Native.OpenSaveAs(window))
                    result.Error = "未能打开保存窗口，请回到原文档后重试。";
            }
            else if (reveal && result.Error == null)
            {
                IntPtr item = IntPtr.Zero;
                bool comInitialized = false;
                try
                {
                    Marshal.ThrowExceptionForHR(Native.CoInitializeEx(IntPtr.Zero, 2));
                    comInitialized = true;
                    if (!File.Exists(result.Path)) result.Error = "原文件不存在或当前不可访问，可能已被移动、删除，或所在磁盘已断开。";
                    else
                    {
                        uint attributes;
                        Marshal.ThrowExceptionForHR(Native.SHParseDisplayName(result.Path, IntPtr.Zero, out item, 0, out attributes));
                        ResolveResult current = ReadMetadata(window, result);
                        if (current == null || current.Error != null || result.Identity != current.Identity ||
                            !string.Equals(result.Path, current.Path, StringComparison.OrdinalIgnoreCase) ||
                            Native.GetForegroundWindow() != window)
                            result.Error = "当前窗口或标签页已改变，请重新双击 Esc。";
                        else
                            Marshal.ThrowExceptionForHR(Native.SHOpenFolderAndSelectItems(item, 0, IntPtr.Zero, 0));
                    }
                }
                catch (Exception) { result.Error = "无法在资源管理器中定位文件，请确认文件路径仍然有效。"; }
                finally
                {
                    if (item != IntPtr.Zero) Marshal.FreeCoTaskMem(item);
                    if (comInitialized) Native.CoUninitialize();
                }
            }
            Console.WriteLine(result.Error != null ? "ERROR" : result.SaveRequired ? "SAVE" : "OK");
            Console.WriteLine(Convert.ToBase64String(Encoding.UTF8.GetBytes(result.Error ?? result.Path ?? "新文档需要选择保存位置。")));
            return 0;
        }
    }

    internal sealed class LocatorContext : ApplicationContext
    {
        private readonly Control dispatcher = new Control();
        private readonly NotifyIcon tray;
        private readonly Icon icon;
        private readonly ContextMenuStrip menu;
        private readonly ToolStripMenuItem pause;
        private readonly ToolStripMenuItem startup;
        private readonly EscapeGesture gesture = new EscapeGesture();
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly Native.HookProc callback;
        private readonly EventWaitHandle stop;
        private readonly RegisteredWaitHandle stopRegistration;
        private readonly System.Windows.Forms.Timer foregroundTimer;
        private IntPtr hook;
        private IntPtr previousForeground;
        private bool paused;
        private bool busy;
        private bool disposing;

        internal LocatorContext()
        {
            var handle = dispatcher.Handle;
            icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            menu = new ContextMenuStrip();
            menu.Items.Add("使用方法", null, delegate { ShowHelp(); });
            pause = new ToolStripMenuItem("暂停快捷键", null, delegate
            {
                paused = !paused;
                pause.Checked = paused;
                gesture.CancelPending();
                tray.Text = paused ? "TXT 文件定位器（已暂停）" : "TXT 文件定位器：记事本中双击 Esc";
            });
            menu.Items.Add(pause);
            startup = new ToolStripMenuItem("开机自动启动", null, delegate { ToggleStartup(); });
            menu.Items.Add(startup);
            menu.Opening += delegate { startup.Checked = StartupEnabled(); };
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出", null, delegate { ExitThread(); });
            tray = new NotifyIcon { Icon = icon, Text = "TXT 文件定位器：记事本中双击 Esc", ContextMenuStrip = menu, Visible = true };
            tray.DoubleClick += delegate { ShowHelp(); };
            callback = OnKeyboard;
            hook = Native.SetWindowsHookEx(13, callback, Native.GetModuleHandle(null), 0);
            if (hook == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            stop = new EventWaitHandle(false, EventResetMode.AutoReset, Program.StopEvent);
            stopRegistration = ThreadPool.RegisterWaitForSingleObject(stop,
                delegate { Post(delegate { ExitThread(); }); }, null, Timeout.Infinite, true);
            foregroundTimer = new System.Windows.Forms.Timer { Interval = 50 };
            foregroundTimer.Tick += delegate
            {
                IntPtr current = Native.GetForegroundWindow();
                if (current != previousForeground) { gesture.CancelPending(); previousForeground = current; }
            };
            previousForeground = Native.GetForegroundWindow();
            foregroundTimer.Start();
            Notify("已启动。在记事本的文档编辑区，快速按两次 Esc 即可定位文件。", ToolTipIcon.Info);
        }

        private IntPtr OnKeyboard(int code, IntPtr message, IntPtr dataPointer)
        {
            if (code >= 0)
            {
                try
                {
                    var data = (Native.KeyboardData)Marshal.PtrToStructure(dataPointer, typeof(Native.KeyboardData));
                    bool injected = (data.Flags & 0x10) != 0;
                    int kind = message.ToInt32();
                    if (kind == 0x101 || kind == 0x105) gesture.KeyUp((int)data.Key, injected);
                    else if (kind == 0x100 || kind == 0x104)
                    {
                        IntPtr foreground = Native.GetForegroundWindow();
                        if (foreground != previousForeground) { gesture.CancelPending(); previousForeground = foreground; }
                        bool eligible = !paused && !busy && Native.IsNotepadWindow(foreground);
                        if (gesture.KeyDown((int)data.Key, clock.ElapsedMilliseconds, foreground.ToInt64(),
                            eligible, Native.ModifiersDown(), injected))
                        {
                            busy = true;
                            Post(delegate { Locate(foreground); });
                            return new IntPtr(1);
                        }
                    }
                }
                catch { gesture.CancelPending(); }
            }
            return Native.CallNextHookEx(hook, code, message, dataPointer);
        }

        private async void Locate(IntPtr window)
        {
            try
            {
                ResolveResult result = await Task.Run(() => LocateInWorker(window));
                if (!disposing && result.Error != null && Native.GetForegroundWindow() == window)
                    Notify(result.Error, ToolTipIcon.Warning);
            }
            catch (Exception) { Notify("定位失败，请稍后重试。", ToolTipIcon.Warning); }
            finally
            {
                gesture.CancelPending();
                busy = false;
            }
        }

        private static ResolveResult LocateInWorker(IntPtr window)
        {
            // Both UI Automation and shell work are bounded by this process timeout.
            var start = new ProcessStartInfo(Application.ExecutablePath, "--locate " + window.ToInt64())
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
                WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory
            };
            using (Process worker = Process.Start(start))
            {
                // Concurrent draining prevents even unusually long paths from filling the pipe.
                Task<string> output = worker.StandardOutput.ReadToEndAsync();
                if (!worker.WaitForExit(8500))
                {
                    try { worker.Kill(); } catch (InvalidOperationException) { }
                    return new ResolveResult { Error = "记事本响应超时，请稍后重试。" };
                }
                string[] lines = output.Result.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                if (worker.ExitCode != 0 || lines.Length != 2) return new ResolveResult { Error = "无法读取当前文件路径，请稍后重试。" };
                string value = Encoding.UTF8.GetString(Convert.FromBase64String(lines[1]));
                return lines[0] == "OK" || lines[0] == "SAVE" ? new ResolveResult { Path = value } : new ResolveResult { Error = value };
            }
        }

        private void Post(Action action)
        {
            if (disposing || dispatcher.IsDisposed) return;
            try { dispatcher.BeginInvoke(action); } catch (InvalidOperationException) { }
        }

        private void Notify(string text, ToolTipIcon kind)
        {
            if (!disposing) tray.ShowBalloonTip(4000, "TXT 文件定位器", text, kind);
        }

        private void ShowHelp()
        {
            MessageBox.Show("1. 在 Windows 11 自带记事本中选中文件标签。\n2. 在文档编辑区，450 毫秒内按两次 Esc。\n3. 已保存文件会在资源管理器中定位；新文档会弹出另存为窗口。\n\n支持多标签、多窗口和同名文件。新文档保存后，再双击 Esc 即可定位。\n单次 Esc 保持原有功能；长按不会触发。\n右键托盘图标可暂停、设置开机启动或退出。\n\n兼容性取决于记事本是否提供标签的完整路径。",
                "TXT 文件定位器", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private bool StartupEnabled()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey))
                return key != null && string.Equals(key.GetValue("NotepadFileLocator") as string,
                    "\"" + Application.ExecutablePath + "\"", StringComparison.OrdinalIgnoreCase);
        }

        private void ToggleStartup()
        {
            try
            {
                bool enabled = StartupEnabled();
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (enabled) key.DeleteValue("NotepadFileLocator", false);
                    else key.SetValue("NotepadFileLocator", "\"" + Application.ExecutablePath + "\"");
                }
                startup.Checked = !enabled;
            }
            catch (Exception error) { Notify("无法设置开机启动：" + error.Message, ToolTipIcon.Warning); }
        }

        protected override void Dispose(bool disposingNow)
        {
            if (disposingNow && !disposing)
            {
                disposing = true;
                if (hook != IntPtr.Zero) Native.UnhookWindowsHookEx(hook);
                if (foregroundTimer != null) foregroundTimer.Dispose();
                if (stopRegistration != null) stopRegistration.Unregister(null);
                if (stop != null) stop.Dispose();
                if (tray != null) { tray.Visible = false; tray.Dispose(); }
                if (menu != null) menu.Dispose();
                if (icon != null) icon.Dispose();
                dispatcher.Dispose();
            }
            base.Dispose(disposingNow);
        }
    }
}
