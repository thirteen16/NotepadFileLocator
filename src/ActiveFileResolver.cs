using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows.Automation;

namespace NotepadFileLocator
{
    internal sealed class ResolveResult
    {
        internal string Path;
        internal string Error;
        internal string Identity;
        internal bool SaveRequired;
    }

    internal static class ActiveFileResolver
    {
        internal static ResolveResult Resolve(IntPtr window, ResolveResult expected = null)
        {
            if (Native.GetForegroundWindow() != window || !Native.IsNotepadWindow(window))
                return Fail("前台窗口已改变，请回到记事本后重试。");
            uint processId;
            Native.GetWindowThreadProcessId(window, out processId);
            using (Process process = Process.GetProcessById((int)processId))
            {
                if (!string.Equals(process.ProcessName, "Notepad", StringComparison.OrdinalIgnoreCase))
                    return Fail("此快捷键仅支持 Windows 自带记事本。");
            }
            ResolveResult direct;
            if (MemoryPathReader.TryRead(window, out direct))
            {
                if (Native.GetForegroundWindow() != window)
                    return Fail("前台窗口已改变，请回到记事本后重试。");
                if (expected != null && (direct.Identity != expected.Identity ||
                    direct.SaveRequired != expected.SaveRequired ||
                    !string.Equals(direct.Path, expected.Path, StringComparison.OrdinalIgnoreCase)))
                    return Fail("当前窗口或标签页已改变，请重新双击 Esc。");
                return direct;
            }
            // Never reuse a memory snapshot through the different UIA identity scheme.
            if (expected != null && expected.Identity != null && expected.Identity.StartsWith("memory:", StringComparison.Ordinal))
                return Fail("无法再次核对当前文档，请重新双击 Esc。");
            AutomationElement root = AutomationElement.FromHandle(window);
            var tabs = root.FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TabItem));
            AutomationElement selected = null;
            foreach (AutomationElement tab in tabs)
            {
                object pattern;
                if (tab.TryGetCurrentPattern(SelectionItemPattern.Pattern, out pattern) &&
                    ((SelectionItemPattern)pattern).Current.IsSelected)
                {
                    if (selected != null) return Fail("无法唯一确定当前标签页，请回到文档编辑区后重试。");
                    selected = tab;
                }
            }

            List<string> paths;
            string identity;
            bool unsaved = false;
            if (selected != null)
            {
                identity = string.Join(",", selected.GetRuntimeId().Select(n => n.ToString()).ToArray());
                paths = PathMetadata.Extract(selected.Current.HelpText, selected.Current.Name,
                    selected.GetCurrentPropertyValue(AutomationElement.ItemStatusProperty) as string);
                // Some WinUI versions put tooltip metadata on the tab header's children.
                if (paths.Count == 0)
                {
                    var children = selected.FindAll(TreeScope.Children, Condition.TrueCondition);
                    foreach (AutomationElement child in children)
                    {
                        ControlType type = child.Current.ControlType;
                        if (type == ControlType.Text || type == ControlType.Button || type == ControlType.Image)
                            paths.AddRange(PathMetadata.Extract(child.Current.HelpText));
                    }
                }
                paths = paths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (expected != null && identity == expected.Identity)
                {
                    // Revalidate identity after slow disk/Shell work without hovering a second time.
                    if (paths.Count == 0)
                    {
                        if (expected.SaveRequired) unsaved = true;
                        else paths.Add(expected.Path);
                    }
                }
                else if (paths.Count == 0)
                    paths = TooltipReader.Read(root, selected, window, out unsaved);
                if (!((SelectionItemPattern)selected.GetCurrentPattern(SelectionItemPattern.Pattern)).Current.IsSelected)
                    return Fail("标签页已改变，请重新双击 Esc。");
            }
            else
            {
                if (tabs.Count != 0) return Fail("未找到当前选中的文档标签，请回到文档编辑区后重试。");
                // Classic versions can expose an absolute title, but a basename is insufficient.
                paths = PathMetadata.Extract(root.Current.HelpText);
                identity = "window:" + window.ToInt64();
            }
            if (Native.GetForegroundWindow() != window) return Fail("前台窗口已改变，请回到记事本后重试。");
            if (paths.Count == 0 && unsaved)
                return new ResolveResult { SaveRequired = true, Identity = identity };
            if (paths.Count == 0)
                return Fail("未取得完整文件路径。新文档请先保存；已保存的文件请保持鼠标不动后重试。");
            if (paths.Count != 1) return Fail("当前标签的路径信息不明确，已取消定位以免选错文件。");
            return new ResolveResult { Path = paths[0], Identity = identity };
        }

        private static ResolveResult Fail(string message) { return new ResolveResult { Error = message }; }

    }
}
