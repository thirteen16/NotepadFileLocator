using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace NotepadFileLocator
{
    // Private layout verified against Notepad 11.2607.14.0 x64, gated by SHA-256.
    // Unknown binaries are never read using these offsets.
    internal static class MemoryPathReader
    {
        private const string Fingerprint = "03745e9e21684c0e6d9fdc50d91caa0935f4baf50f626c11987ab7d6991d58d1";
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern SafeFileHandle OpenProcess(uint access, bool inherit, uint pid);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool QueryFullProcessImageName(SafeFileHandle process, uint flags, StringBuilder path, ref uint size);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadProcessMemory(SafeFileHandle process, IntPtr address, byte[] buffer, UIntPtr size, out UIntPtr read);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);

        // false: unavailable/unsupported, caller may use UI Automation.
        // true + Error: recognized binary but inconsistent read; cancel, do not guess.
        internal static bool TryRead(IntPtr window, out ResolveResult result)
        {
            result = null;
            if (IntPtr.Size != 8 || !Native.IsNotepadWindow(window)) return false;
            uint pid;
            Native.GetWindowThreadProcessId(window, out pid);
            using (SafeFileHandle process = OpenProcess(0x1010, false, pid))
            {
                if (process.IsInvalid) return false;
                try
                {
                    var image = new StringBuilder(32768);
                    uint size = (uint)image.Capacity;
                    if (!QueryFullProcessImageName(process, 0, image, ref size) ||
                        !string.Equals(System.IO.Path.GetFileName(image.ToString()), "Notepad.exe", StringComparison.OrdinalIgnoreCase)) return false;
                    using (var stream = File.OpenRead(image.ToString()))
                    using (var sha = SHA256.Create())
                        if (BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant() != Fingerprint) return false;
                }
                catch (IOException) { return false; }
                catch (UnauthorizedAccessException) { return false; }

                try
                {
                    var reader = new Reader(process);
                    Snapshot first = reader.Capture(window), second = reader.Capture(window);
                    uint currentPid;
                    Native.GetWindowThreadProcessId(window, out currentPid);
                    if (pid != currentPid || first.Identity != second.Identity || first.Path != second.Path)
                        throw new IOException("Document changed");
                    result = new ResolveResult { Path = first.Path, SaveRequired = first.Path == null,
                        Identity = "memory:" + pid + ":" + first.Identity };
                }
                catch (IOException)
                {
                    result = new ResolveResult { Error = "记事本的窗口或文档信息正在变化，请重新双击 Esc。" };
                }
                return true;
            }
        }

        private sealed class Snapshot
        {
            internal string Identity, Path;
        }

        private sealed class Reader
        {
            private readonly SafeFileHandle process;
            internal Reader(SafeFileHandle process) { this.process = process; }
            private byte[] Read(ulong address, int count)
            {
                if (address < 0x10000 || address > 0x00007fffffffffffUL - (ulong)count || count <= 0 || count > 65536)
                    throw new IOException("Invalid memory range");
                byte[] buffer = new byte[count];
                UIntPtr read;
                if (!ReadProcessMemory(process, new IntPtr((long)address), buffer, new UIntPtr((uint)count), out read) ||
                    read.ToUInt64() != (ulong)count) throw new IOException("Memory read failed");
                return buffer;
            }
            private ulong Q(ulong address) { return BitConverter.ToUInt64(Read(address, 8), 0); }
            internal Snapshot Capture(IntPtr window)
            {
                ulong owner = (ulong)GetWindowLongPtr(window, -21).ToInt64();
                ulong manager = Q(owner + 0x20);
                byte[] id = Read(manager + 0x50, 16);
                ulong hash = 0xcbf29ce484222325;
                foreach (byte value in id) hash = unchecked((hash ^ value) * 0x100000001b3);
                ulong table = manager + 0x10, sentinel = Q(table + 8), mask = Q(table + 0x30);
                if (mask > 0xfffff || (mask & (mask + 1)) != 0) throw new IOException("Invalid table");
                ulong bucket = Q(table + 0x18) + ((hash & mask) * 16);
                ulong first = Q(bucket), node = Q(bucket + 8);
                bool found = false;
                for (int i = 0; i < 4096 && node != sentinel; i++)
                {
                    if (Read(node + 0x10, 16).SequenceEqual(id)) { found = true; break; }
                    if (node == first) break;
                    node = Q(node + 8);
                }
                if (!found) throw new IOException("No active document");
                ulong document = Q(Q(node + 0x20)), text = document + 8;
                byte[] header = Read(text, 32);
                ulong length = BitConverter.ToUInt64(header, 16), capacity = BitConverter.ToUInt64(header, 24);
                if (length > 32767 || capacity < length) throw new IOException("Invalid string");
                string path = null;
                if (length != 0)
                {
                    ulong address = capacity > 7 ? BitConverter.ToUInt64(header, 0) : text;
                    path = Encoding.Unicode.GetString(Read(address, (int)length * 2));
                    // Require an absolute drive or UNC path; never resolve a relative name.
                    if (path.IndexOf('\0') >= 0 || !(path.StartsWith(@"\\", StringComparison.Ordinal) ||
                        (path.Length >= 3 && char.IsLetter(path[0]) && path[1] == ':' && path[2] == '\\')))
                        throw new IOException("Invalid path");
                }
                if ((ulong)GetWindowLongPtr(window, -21).ToInt64() != owner || Q(owner + 0x20) != manager ||
                    !Read(manager + 0x50, 16).SequenceEqual(id) || !Read(text, 32).SequenceEqual(header))
                    throw new IOException("Document changed");
                return new Snapshot { Path = path, Identity = owner + ":" + manager + ":" +
                    BitConverter.ToString(id) + ":" + document };
            }
        }
    }
}
