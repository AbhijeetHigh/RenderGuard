using System;
using System.Collections.Generic;
using System.Text;

namespace RenderGuard.Editor
{
    /// <summary>
    /// Name and folder heuristics used to suggest placeholder candidates. Matching works on whole words
    /// (split on separators, camelCase and letter/digit boundaries), so "Temple" or "Swipe" no longer match "temp" or "wip".
    /// </summary>
    public static class PlaceholderHeuristics
    {
        private static readonly HashSet<string> StrongWords = new HashSet<string>(StringComparer.Ordinal)
        {
            "placeholder", "placeholders", "dummy", "blockout"
        };

        private static readonly HashSet<string> NameWords = new HashSet<string>(StringComparer.Ordinal)
        {
            "temporary", "temp", "proxy", "greybox", "graybox", "prototype", "wip"
        };

        private static readonly HashSet<string> FolderWords = new HashSet<string>(StringComparer.Ordinal)
        {
            "placeholder", "placeholders", "temp", "temporary", "blockout", "greybox", "graybox", "prototype", "prototypes"
        };

        public static List<string> Tokenize(string text)
        {
            var tokens = new List<string>();
            if (string.IsNullOrEmpty(text)) return tokens;
            var builder = new StringBuilder();
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (!char.IsLetterOrDigit(c))
                {
                    Flush(builder, tokens);
                    continue;
                }
                if (builder.Length > 0)
                {
                    char previous = text[i - 1];
                    bool letterDigitBoundary = char.IsLetter(previous) != char.IsLetter(c);
                    bool lowerToUpper = char.IsUpper(c) && char.IsLower(previous);
                    // "WIPDoor": split before the last capital of an upper-case run that is followed by a lower-case letter.
                    bool acronymEnd = char.IsUpper(c) && char.IsUpper(previous) && i + 1 < text.Length && char.IsLower(text[i + 1]);
                    if (letterDigitBoundary || lowerToUpper || acronymEnd) Flush(builder, tokens);
                }
                builder.Append(char.ToLowerInvariant(c));
            }
            Flush(builder, tokens);
            return tokens;
        }

        private static void Flush(StringBuilder builder, List<string> tokens)
        {
            if (builder.Length == 0) return;
            tokens.Add(builder.ToString());
            builder.Length = 0;
        }

        /// <summary>
        /// Evaluates an object or asset name. When checkFolders is true the directory part of path is also tested
        /// (used for assets and prefab roots, not for every object inside a scene that happens to live in such a folder).
        /// </summary>
        public static bool Evaluate(string itemName, string path, bool checkFolders, out float confidence, out string reason)
        {
            foreach (string token in Tokenize(itemName))
            {
                if (StrongWords.Contains(token))
                {
                    confidence = 0.92f;
                    reason = "Name contains the placeholder-related word '" + token + "'. Review before approving.";
                    return true;
                }
            }
            foreach (string token in Tokenize(itemName))
            {
                if (NameWords.Contains(token))
                {
                    confidence = 0.78f;
                    reason = "Name contains the placeholder-related word '" + token + "'. Review before approving.";
                    return true;
                }
            }

            if (checkFolders && !string.IsNullOrEmpty(path))
            {
                string[] segments = path.Replace('\\', '/').Split('/');
                // The last segment is the file name; only directories count here.
                for (int i = 0; i < segments.Length - 1; i++)
                {
                    foreach (string token in Tokenize(segments[i]))
                    {
                        if (!FolderWords.Contains(token)) continue;
                        confidence = 0.6f;
                        reason = "Stored in a folder commonly used for temporary or prototype content: " + segments[i];
                        return true;
                    }
                }
            }

            confidence = 0f;
            reason = string.Empty;
            return false;
        }
    }
}
