using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Automation;

namespace NotepadFileLocator
{
    internal static class TooltipReader
    {
        // Provider calls are contained by the caller's killable worker process.
        // This budget bounds retries and waits, not an unresponsive UIA provider.
        private const int BudgetMilliseconds = 2200;
        private static readonly object cursorGate = new object();
        private static Native.Point savedCursor, ownedCursor;
        private static bool cursorOwned;
        private static volatile bool cancelled;

        internal static void CancelAndRestoreCursor()
        {
            cancelled = true;
            lock (cursorGate)
            {
                Native.Point current;
                if (cursorOwned && Native.GetCursorPos(out current) && Same(current, ownedCursor))
                    Native.MoveCursor(savedCursor.X, savedCursor.Y);
                cursorOwned = false;
            }
        }

        private static bool MoveOwned(Native.Point position, Native.Point original)
        {
            lock (cursorGate)
            {
                if (cancelled || !Native.MoveCursor(position.X, position.Y)) return false;
                savedCursor = original;
                ownedCursor = position;
                cursorOwned = true;
                return true;
            }
        }

        internal static List<string> Read(AutomationElement root, AutomationElement selectedTab, IntPtr window, out bool unsaved)
        {
            unsaved = false;
            var empty = new List<string>();
            cancelled = false;
            var watch = Stopwatch.StartNew();
            Native.Point original;
            if (!Native.GetCursorPos(out original)) return empty;
            Native.Point lastPosition = original;
            bool movedCursor = false;
            bool subscribed = false;
            AutomationElement desktop = null;
            var capture = new Capture();
            AutomationEventHandler handler = capture.OnOpened;
            try
            {
                if (Native.GetForegroundWindow() != window) return empty;
                uint processId;
                Native.GetWindowThreadProcessId(window, out processId);
                if (processId == 0) return empty;
                capture.ProcessId = processId;
                capture.Window = window;
                var label = selectedTab.FindFirst(TreeScope.Children,
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text));
                capture.TabLabel = label == null ? null : label.Current.Name;

                Rect bounds = selectedTab.Current.BoundingRectangle;
                if (selectedTab.Current.IsOffscreen || bounds.IsEmpty ||
                    bounds.Width < 12 || bounds.Height < 10 || !Selected(selectedTab)) return empty;
                int[] identity = selectedTab.GetRuntimeId();
                var hover = new Native.Point
                {
                    X = (int)(bounds.Left + bounds.Width / 2),
                    Y = (int)(bounds.Top + bounds.Height / 2)
                };
                capture.Hover = hover;

                // A cache on the event subscription captures transient tooltip text,
                // including child Text controls, before the popup disappears.
                desktop = AutomationElement.RootElement;
                try
                {
                    using (CreateCache().Activate())
                        Automation.AddAutomationEventHandler(AutomationElement.ToolTipOpenedEvent,
                            desktop, TreeScope.Subtree, handler);
                    subscribed = true;
                }
                catch (InvalidOperationException) { }
                catch (COMException) { }

                // Always leave the tab first. A tooltip for a previously hovered tab
                // must not be mistaken for the newly selected document's path.
                Rect windowBounds = root.Current.BoundingRectangle;
                var away = new Native.Point
                {
                    X = (int)(windowBounds.Left + windowBounds.Width / 2),
                    Y = (int)(windowBounds.Top + windowBounds.Height / 2)
                };
                if (windowBounds.IsEmpty || bounds.Contains(away.X, away.Y)) return empty;
                if (!CurrentContext(selectedTab, identity, window, lastPosition)) return empty;
                if (!MoveOwned(away, original)) return empty;
                movedCursor = true;
                lastPosition = away;
                Thread.Sleep(110);
                if (!CurrentContext(selectedTab, identity, window, lastPosition)) return empty;

                // Native enumeration avoids relying on the popup's UIA ProcessId or
                // on its placement in the accessibility tree. Remember still-visible
                // old popups and require their disappearance before reusing an HWND.
                var oldPopups = new HashSet<IntPtr>(PopupWindows(window, processId));
                capture.Start();
                if (watch.ElapsedMilliseconds >= BudgetMilliseconds) return empty;
                if (!MoveOwned(hover, original)) return empty;
                lastPosition = hover;
                long nextScan = watch.ElapsedMilliseconds + 180;

                while (watch.ElapsedMilliseconds < BudgetMilliseconds)
                {
                    capture.Wait(40);
                    if (!CurrentContext(selectedTab, identity, window, lastPosition)) return empty;
                    List<string> paths = capture.Paths();
                    if (paths.Count > 0) return paths;
                    if (capture.IsUnsaved()) { unsaved = true; return empty; }

                    if (watch.ElapsedMilliseconds >= nextScan)
                    {
                        nextScan = watch.ElapsedMilliseconds + 150;
                        var windows = PopupWindows(window, processId);
                        oldPopups.RemoveWhere(h => !windows.Contains(h));
                        foreach (IntPtr popup in windows)
                        {
                            if (watch.ElapsedMilliseconds >= BudgetMilliseconds) break;
                            if (oldPopups.Contains(popup)) continue;
                            paths = ReadPopup(popup);
                            if (paths.Count != 0)
                            {
                                if (!CurrentContext(selectedTab, identity, window, lastPosition)) return empty;
                                return paths;
                            }
                        }
                    }
                }
                return empty;
            }
            catch (ElementNotAvailableException) { return empty; }
            catch (InvalidOperationException) { return empty; }
            catch (COMException) { return empty; }
            finally
            {
                capture.Close();
                // Restore first: removing a handler can itself contact a UIA provider.
                // Do not overwrite a cursor position the user has moved to meanwhile.
                if (movedCursor) CancelAndRestoreCursor();
                if (subscribed)
                {
                    try { Automation.RemoveAutomationEventHandler(AutomationElement.ToolTipOpenedEvent, desktop, handler); }
                    catch (ElementNotAvailableException) { }
                    catch (InvalidOperationException) { }
                    catch (COMException) { }
                }
                capture.Dispose();
            }
        }

        private static CacheRequest CreateCache()
        {
            var cache = new CacheRequest { TreeScope = TreeScope.Subtree, TreeFilter = Automation.RawViewCondition };
            cache.Add(AutomationElement.NameProperty);
            cache.Add(AutomationElement.HelpTextProperty);
            cache.Add(AutomationElement.ProcessIdProperty);
            cache.Add(AutomationElement.ControlTypeProperty);
            cache.Add(AutomationElement.NativeWindowHandleProperty);
            cache.Add(AutomationElement.ClassNameProperty);
            cache.Add(AutomationElement.IsOffscreenProperty);
            return cache;
        }

        private static bool CurrentContext(AutomationElement tab, int[] identity, IntPtr window, Native.Point expectedPosition)
        {
            Native.Point actual;
            if (cancelled || Native.GetForegroundWindow() != window || !Native.GetCursorPos(out actual) ||
                !Same(actual, expectedPosition) || !Selected(tab)) return false;
            int[] current = tab.GetRuntimeId();
            if (current.Length != identity.Length) return false;
            for (int i = 0; i < identity.Length; i++) if (current[i] != identity[i]) return false;
            return true;
        }

        private static bool Selected(AutomationElement tab)
        {
            object pattern;
            return tab.TryGetCurrentPattern(SelectionItemPattern.Pattern, out pattern) &&
                ((SelectionItemPattern)pattern).Current.IsSelected;
        }

        private static bool Same(Native.Point first, Native.Point second)
        {
            return first.X == second.X && first.Y == second.Y;
        }

        private static List<string> ReadPopup(IntPtr window)
        {
            try
            {
                AutomationElement popup = AutomationElement.FromHandle(window);
                // Some providers return the underlying main window for a popup HWND.
                // Never scan that window, since it contains the document editor.
                if (!IsPopupClass(popup.Current.ClassName) &&
                    popup.Current.ControlType != ControlType.ToolTip) return new List<string>();
                return CachedPaths(popup.GetUpdatedCache(CreateCache()));
            }
            catch (ElementNotAvailableException) { return new List<string>(); }
            catch (InvalidOperationException) { return new List<string>(); }
            catch (COMException) { return new List<string>(); }
        }

        private static List<string> CachedPaths(AutomationElement element)
        {
            var paths = new List<string>();
            int remaining = 64;
            CollectCached(element, paths, 0, ref remaining);
            return paths;
        }

        private static void CollectCached(AutomationElement element, List<string> paths, int depth, ref int remaining)
        {
            if (depth > 8 || remaining-- <= 0) return;
            ControlType type = element.Cached.ControlType;
            if (type == ControlType.Document || type == ControlType.Edit) return;
            if (element.Cached.IsOffscreen) return;
            foreach (string path in PathMetadata.Extract(element.Cached.Name, element.Cached.HelpText))
                if (!paths.Exists(p => string.Equals(path, p, StringComparison.OrdinalIgnoreCase))) paths.Add(path);
            AutomationElementCollection children = element.CachedChildren;
            if (children == null) return;
            foreach (AutomationElement child in children) CollectCached(child, paths, depth + 1, ref remaining);
        }

        private static bool IsPopupClass(string name)
        {
            return string.Equals(name, "Xaml_WindowedPopupClass", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "Microsoft.UI.Content.PopupWindowSiteBridge", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "tooltips_class32", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "#32774", StringComparison.OrdinalIgnoreCase);
        }

        private static List<IntPtr> PopupWindows(IntPtr rootWindow, uint processId)
        {
            var windows = new List<IntPtr>();
            EnumWindowProc visitor = delegate(IntPtr window, IntPtr state)
            {
                if (!IsWindowVisible(window)) return true;
                uint owner;
                Native.GetWindowThreadProcessId(window, out owner);
                if (owner != processId) return true;
                var className = new StringBuilder(128);
                Native.GetClassName(window, className, className.Capacity);
                if (!IsPopupClass(className.ToString())) return true;
                WindowRectangle rectangle;
                if (!GetWindowRect(window, out rectangle) || rectangle.Right <= rectangle.Left || rectangle.Bottom <= rectangle.Top)
                    return true;
                if (!windows.Contains(window)) windows.Add(window);
                return true;
            };
            EnumWindows(visitor, IntPtr.Zero);
            EnumChildWindows(rootWindow, visitor, IntPtr.Zero);
            return windows;
        }

        private sealed class Capture : IDisposable
        {
            internal uint ProcessId;
            internal IntPtr Window;
            internal Native.Point Hover;
            internal string TabLabel;
            private readonly object gate = new object();
            private readonly AutoResetEvent signal = new AutoResetEvent(false);
            private readonly List<string> paths = new List<string>();
            private bool active;
            private bool closed;
            private bool unsaved;

            internal void Start() { lock (gate) { if (!closed) active = true; } }
            internal void Wait(int milliseconds) { signal.WaitOne(milliseconds); }
            internal List<string> Paths() { lock (gate) return new List<string>(paths); }
            internal bool IsUnsaved() { lock (gate) return unsaved; }
            internal void Close() { lock (gate) { active = false; closed = true; } }
            public void Dispose() { lock (gate) signal.Dispose(); }

            internal void OnOpened(object sender, AutomationEventArgs args)
            {
                try
                {
                    lock (gate) { if (!active || closed) return; }
                    var tooltip = sender as AutomationElement;
                    if (tooltip == null || tooltip.Cached.ControlType != ControlType.ToolTip) return;
                    uint process = unchecked((uint)tooltip.Cached.ProcessId);
                    if (process != ProcessId)
                    {
                        int handle = tooltip.Cached.NativeWindowHandle;
                        if (handle == 0) return;
                        Native.GetWindowThreadProcessId(new IntPtr(handle), out process);
                        if (process != ProcessId) return;
                    }
                    Native.Point cursor;
                    if (Native.GetForegroundWindow() != Window || !Native.GetCursorPos(out cursor) || !Same(cursor, Hover)) return;
                    // WinUI raises Opened before updating IsOffscreen. The event's
                    // cached Name is already valid even while that flag is stale.
                    List<string> captured = PathMetadata.Extract(tooltip.Cached.Name, tooltip.Cached.HelpText);
                    if (captured.Count == 0) captured = CachedPaths(tooltip);
                    bool newDocument = captured.Count == 0 && !string.IsNullOrWhiteSpace(TabLabel) &&
                        string.Equals(tooltip.Cached.Name.Trim(), TabLabel.Trim(), StringComparison.Ordinal);
                    if (captured.Count == 0 && !newDocument) return;
                    lock (gate)
                    {
                        if (!active || closed) return;
                        unsaved = newDocument;
                        foreach (string path in captured)
                            if (!paths.Exists(p => string.Equals(path, p, StringComparison.OrdinalIgnoreCase))) paths.Add(path);
                        signal.Set();
                    }
                }
                catch (ElementNotAvailableException) { }
                catch (InvalidOperationException) { }
                catch (COMException) { }
            }
        }

        private delegate bool EnumWindowProc(IntPtr window, IntPtr state);
        [StructLayout(LayoutKind.Sequential)]
        private struct WindowRectangle { internal int Left, Top, Right, Bottom; }
        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowProc callback, IntPtr state);
        [DllImport("user32.dll")]
        private static extern bool EnumChildWindows(IntPtr parent, EnumWindowProc callback, IntPtr state);
        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr window, out WindowRectangle rectangle);
    }
}
