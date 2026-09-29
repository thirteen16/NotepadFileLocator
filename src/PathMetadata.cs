using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace NotepadFileLocator
{
    internal static class PathMetadata
    {
        // Only called with selected-tab metadata, never document text or saved sessions.
        internal static List<string> Extract(params string[] metadata)
        {
            var result = new List<string>();
            foreach (string value in metadata)
            {
                if (string.IsNullOrWhiteSpace(value)) continue;
                foreach (string raw in value.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string line = raw.Trim().Trim('\u202a', '\u202b', '\u202c', '\u200e', '\u200f').Trim();
                    // A localized label may precede the path. Do not guess relative paths.
                    Match start = Regex.Match(line, @"(?:[A-Za-z]:\\|\\\\[^\\])");
                    if (!start.Success) continue;
                    string candidate = line.Substring(start.Index).TrimEnd('"', '\u202c', '\u200e', '\u200f');
                    try
                    {
                        if (candidate.IndexOfAny(Path.GetInvalidPathChars()) >= 0) continue;
                        string path = Path.GetFullPath(candidate);
                        if (!result.Exists(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase))) result.Add(path);
                    }
                    catch (ArgumentException) { }
                    catch (NotSupportedException) { }
                    catch (PathTooLongException) { }
                }
            }
            return result;
        }
    }
}
