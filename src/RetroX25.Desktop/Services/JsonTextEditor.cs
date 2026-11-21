//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace RetroX25.Desktop.Services;

/// <summary>
/// Changes single values in the text of a JSON file and leaves every other character of it, comments included, as it
/// was.
/// </summary>
/// <remarks>
/// <para>
/// Made 2026-11-21 for the routing screen's Save (RetroIO BUGS.md, "Save drops comments"). Before, Save parsed the
/// whole file into <c>System.Text.Json.Nodes</c> and wrote it out again, which keeps no comments, so every
/// <c>//</c> line in a hand-written file was lost on the first Save.
/// </para>
/// <para>
/// A value is found by a path of property names (compared without regard to case, as the configuration reader does)
/// and array positions. The reader reports where each token starts and ends in the UTF-8 bytes, also with comments in
/// the file (<see cref="JsonCommentHandling.Skip"/>), so only the bytes of the value being changed are replaced.
/// Comments INSIDE a replaced value go with it.
/// </para>
/// </remarks>
public static class JsonTextEditor
{
    private static readonly JsonReaderOptions ReaderOptions = new JsonReaderOptions
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    // Where a path led: the value's bytes when found; otherwise the object it would go in.
    private struct Place
    {
        public bool Found;
        public bool ParentFound;
        public int PropertyStart;
        public int ValueStart;
        public int ValueEnd;
        public int ParentOpen;
        public int LastMemberEnd;
        public bool ParentHasMembers;
    }

    /// <summary>
    /// True when the path names a value in the text.
    /// </summary>
    /// <param name="text">
    /// The JSON text.
    /// </param>
    /// <param name="path">
    /// Property names (strings) and array positions (integers), from the top.
    /// </param>
    /// <returns>
    /// True when the value is there.
    /// </returns>
    public static bool Has(string text, params object[] path)
    {
        return Locate(Encoding.UTF8.GetBytes(text), path).Found;
    }

    /// <summary>
    /// Sets the value at the path to <paramref name="valueJson"/>: replaces it when it is there, or adds the property as
    /// the last member of its object when it is not.
    /// </summary>
    /// <param name="text">
    /// The JSON text.
    /// </param>
    /// <param name="valueJson">
    /// The new value, as JSON.
    /// </param>
    /// <param name="path">
    /// Property names and array positions; the last element must be a property name when the value may be missing.
    /// </param>
    /// <returns>
    /// The new text.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the object the value belongs in is not there.
    /// </exception>
    public static string Set(string text, string valueJson, params object[] path)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        Place p = Locate(bytes, path);
        if (p.Found)
        {
            string indent = IndentAt(bytes, p.PropertyStart);
            return Splice(bytes, p.ValueStart, p.ValueEnd, Reindent(valueJson, indent));
        }

        if (!p.ParentFound || path.Length == 0 || path[path.Length - 1] is not string key)
        {
            throw new InvalidOperationException("the place for '" + PathText(path) + "' is not in the file");
        }

        string parentIndent = IndentAt(bytes, p.ParentOpen);
        string memberIndent = parentIndent + "  ";
        string member = "\n" + memberIndent + JsonSerializer.Serialize(key) + ": " + Reindent(valueJson, memberIndent);
        if (p.ParentHasMembers)
        {
            return Splice(bytes, p.LastMemberEnd, p.LastMemberEnd, "," + member);
        }

        return Splice(bytes, p.ParentOpen + 1, p.ParentOpen + 1, member + "\n" + parentIndent);
    }

    /// <summary>
    /// Removes the property at the path, with the comma that separated it; nothing happens when it is not there.
    /// </summary>
    /// <param name="text">
    /// The JSON text.
    /// </param>
    /// <param name="path">
    /// Property names and array positions; the last element is the property name.
    /// </param>
    /// <returns>
    /// The new text.
    /// </returns>
    public static string Remove(string text, params object[] path)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        Place p = Locate(bytes, path);
        if (!p.Found)
        {
            return text;
        }

        int start = p.PropertyStart;
        int end = p.ValueEnd;

        // The comma after it goes; the last member has none, so the comma before it goes instead.
        int after = SkipSpace(bytes, end);
        if (after < bytes.Length && bytes[after] == (byte)',')
        {
            end = after + 1;

            // The spaces after that comma on the same line go too, so "{ a, b }" becomes "{ b }".
            while (end < bytes.Length && (bytes[end] == (byte)' ' || bytes[end] == (byte)'\t'))
            {
                end++;
            }
        }
        else
        {
            int before = start - 1;
            while (before >= 0 && IsSpace(bytes[before]))
            {
                before--;
            }

            if (before >= 0 && bytes[before] == (byte)',')
            {
                start = before;
            }
        }

        // The line the property stood on goes too, when nothing else is on it.
        int lineStart = start;
        while (lineStart > 0 && (bytes[lineStart - 1] == (byte)' ' || bytes[lineStart - 1] == (byte)'\t'))
        {
            lineStart--;
        }

        if (lineStart > 0 && bytes[lineStart - 1] == (byte)'\n' && bytes[start] != (byte)',')
        {
            int lineEnd = end;
            while (lineEnd < bytes.Length && (bytes[lineEnd] == (byte)' ' || bytes[lineEnd] == (byte)'\t' || bytes[lineEnd] == (byte)'\r'))
            {
                lineEnd++;
            }

            if (lineEnd < bytes.Length && bytes[lineEnd] == (byte)'\n')
            {
                return Splice(bytes, lineStart, lineEnd + 1, string.Empty);
            }
        }

        return Splice(bytes, start, end, string.Empty);
    }

    private static Place Locate(byte[] bytes, object[] path)
    {
        Place place = new Place { ParentOpen = -1, LastMemberEnd = -1 };
        Utf8JsonReader reader = new Utf8JsonReader(bytes, ReaderOptions);
        if (!reader.Read())
        {
            return place;
        }

        for (int level = 0; level < path.Length; level++)
        {
            bool last = level == path.Length - 1;
            if (path[level] is string name)
            {
                if (reader.TokenType != JsonTokenType.StartObject)
                {
                    return place;
                }

                int open = (int)reader.TokenStartIndex;
                int lastEnd = -1;
                bool found = false;
                while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                {
                    int propertyStart = (int)reader.TokenStartIndex;
                    bool match = string.Equals(reader.GetString(), name, StringComparison.OrdinalIgnoreCase);
                    reader.Read();
                    if (match)
                    {
                        found = true;
                        place.PropertyStart = propertyStart;
                        break;
                    }

                    reader.Skip();
                    lastEnd = (int)reader.BytesConsumed;
                }

                if (!found)
                {
                    if (last)
                    {
                        place.ParentFound = true;
                        place.ParentOpen = open;
                        place.LastMemberEnd = lastEnd;
                        place.ParentHasMembers = lastEnd >= 0;
                    }

                    return place;
                }
            }
            else if (path[level] is int index)
            {
                if (reader.TokenType != JsonTokenType.StartArray)
                {
                    return place;
                }

                int i = 0;
                bool found = false;
                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                {
                    if (i == index)
                    {
                        found = true;
                        place.PropertyStart = (int)reader.TokenStartIndex;
                        break;
                    }

                    reader.Skip();
                    i++;
                }

                if (!found)
                {
                    return place;
                }
            }
            else
            {
                throw new ArgumentException("a path holds property names and array positions only", nameof(path));
            }

            if (last)
            {
                place.Found = true;
                place.ValueStart = (int)reader.TokenStartIndex;
                reader.Skip();
                place.ValueEnd = (int)reader.BytesConsumed;
            }
        }

        return place;
    }

    // The spaces and tabs at the start of the line that holds position at.
    private static string IndentAt(byte[] bytes, int at)
    {
        int lineStart = at;
        while (lineStart > 0 && bytes[lineStart - 1] != (byte)'\n')
        {
            lineStart--;
        }

        int end = lineStart;
        while (end < at && (bytes[end] == (byte)' ' || bytes[end] == (byte)'\t'))
        {
            end++;
        }

        return Encoding.UTF8.GetString(bytes, lineStart, end - lineStart);
    }

    // A value written over several lines gets the indent of the line it starts on, so it lines up with the file.
    private static string Reindent(string valueJson, string indent)
    {
        return valueJson.Replace("\r\n", "\n").Replace("\n", "\n" + indent);
    }

    private static string Splice(byte[] bytes, int start, int end, string insert)
    {
        string before = Encoding.UTF8.GetString(bytes, 0, start);
        string after = Encoding.UTF8.GetString(bytes, end, bytes.Length - end);

        // A file written with CR LF keeps CR LF in what is put in.
        if (before.Contains("\r\n", StringComparison.Ordinal) || after.Contains("\r\n", StringComparison.Ordinal))
        {
            insert = insert.Replace("\r\n", "\n").Replace("\n", "\r\n");
        }

        return before + insert + after;
    }

    private static int SkipSpace(byte[] bytes, int at)
    {
        while (at < bytes.Length && IsSpace(bytes[at]))
        {
            at++;
        }

        return at;
    }

    private static bool IsSpace(byte b)
    {
        return b == (byte)' ' || b == (byte)'\t' || b == (byte)'\r' || b == (byte)'\n';
    }

    private static string PathText(object[] path)
    {
        List<string> parts = new List<string>();
        for (int i = 0; i < path.Length; i++)
        {
            parts.Add(Convert.ToString(path[i], System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty);
        }

        return string.Join("/", parts);
    }
}
