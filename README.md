# TXT 文件定位器

适用于 Windows 11 自带的多标签记事本。在前台记事本文档中，450 毫秒内按两次 Esc，即可打开文件资源管理器并选中当前标签对应的原文件。

## 使用

1. 双击本目录的 `TXT文件定位器.exe`，程序将在系统托盘后台运行。
2. 在记事本中选中一个已保存的文件标签，然后在文档编辑区快速按两次 Esc。
3. 如需每次登录自动生效，右键托盘图标，勾选“开机自动启动”。

托盘菜单也可以暂停快捷键或退出。重复启动只保留一个实例。无需安装 AutoHotkey、Python 或 .NET SDK；使用 Windows 11 自带的 .NET Framework 4.8。

## 行为与边界

- 按当前选中标签的完整路径定位，支持多个标签、多个记事本窗口和不同目录的同名文件；不按文件名搜索磁盘。
- 单次 Esc 保持原功能，长按 Esc 不算双击。其他按键、组合键或切换窗口会取消待匹配的第一次 Esc。
- 只在 Windows 自带记事本的前台主窗口生效。Notepad++、VS Code 以及旧版记事本不在此版本的支持范围。
- 没有保存过的新文档会弹出记事本的“另存为”窗口，由你选择路径和文件名；取消不会创建文件。保存后再次双击 Esc 即可定位。已保存文件有未保存修改时，定位的是原文件，不会保存或改写文档。
- 文件被移动、删除或暂时不可访问时会提示失败，不会猜测另一个同名文件。
- 新版记事本通常需要悬停标签才能显示完整路径。程序会短暂把鼠标移到当前标签读取提示，再恢复位置，因此定位可能需要约 1～2 秒。读取期间如主动移动鼠标或切换窗口/标签，会取消本次操作，可重新双击 Esc。
- 普通权限启动即可。若记事本以管理员权限运行，普通权限工具可能无法读取它。
- 开机启动默认关闭，只在用户勾选后写入当前用户的 Run 注册表项。移动 EXE 后需要重新设置该选项。
- 不读取文档正文，不使用剪贴板，不读取记事本历史会话，不联网。

## 构建

在 Windows 的 PowerShell 中进入项目目录，运行 `./build.ps1`，生成 `TXT文件定位器.exe` 后双击运行。源码位于 `src`。仓库仅包含源码和必要资源，不包含编译产物。

后台路径解析和 Shell 调用有独立进程超时，避免记事本、网络盘或资源管理器响应异常卡住快捷键。

开发诊断参数：`--resolve <窗口句柄>` 只返回活动路径；`--locate <窗口句柄>` 执行定位；`--stop` 退出当前会话的后台实例。输出第二行是 UTF-8 Base64，避免命令行编码损坏中文路径。

实现依据：[UI Automation TabItem](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-supporttabitemcontroltype)、[ToolTip](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-supporttooltipcontroltype)、[SHOpenFolderAndSelectItems](https://learn.microsoft.com/en-us/windows/win32/api/shlobj_core/nf-shlobj_core-shopenfolderandselectitems)。
