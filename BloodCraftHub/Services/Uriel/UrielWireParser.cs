using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace BloodCraftHub.Services.Uriel;

// Pure, stateless tokenizer for Uriel's machine-readable chat lines. A direct sibling of
// Services/Beelzebub/BeelzWireParser — same grammar, different prefix.
//
// Grammar (see the Uriel repo docs/BCH_INTEGRATION_HANDOFF.md §6, ApiVersion 1):
//   [URIEL:<tag>] key=value key=value ...
// - The tag is everything between "[URIEL:" and the first "]".
// - Values are bare tokens: Uriel's SafeToken guarantees no spaces (space/tab/newline -> '_')
//   inside a value, so splitting the remainder on spaces and each token on its FIRST '=' is
//   unambiguous.
// - Lists are comma-separated with no spaces: key=a,b,c
// - Sentinels: '-' = unknown, 0/1 = booleans.
// - Unknown/extra fields are kept verbatim — callers read fields by name and tolerate absence,
//   so new fields never break parsing.
internal static class UrielWireParser
{
    public const string PREFIX = "[URIEL:";

    public static bool IsUrielLine(string line)
        => !string.IsNullOrEmpty(line) && line.StartsWith(PREFIX, StringComparison.Ordinal);

    /// <summary>Parse one wire line. Returns null if it isn't a well-formed [URIEL:*] line.</summary>
    public static UrielLine Parse(string line)
    {
        if (!IsUrielLine(line)) return null;

        int close = line.IndexOf(']', PREFIX.Length);
        if (close < 0) return null;

        string tag = line.Substring(PREFIX.Length, close - PREFIX.Length).Trim();
        if (tag.Length == 0) return null;

        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string remainder = line.Substring(close + 1);
        foreach (var token in remainder.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = token.IndexOf('=');
            if (eq <= 0) continue;                       // skip malformed / valueless tokens
            fields[token.Substring(0, eq)] = token.Substring(eq + 1);   // last write wins
        }

        return new UrielLine(tag, fields, line);
    }
}

// One parsed wire line: a tag plus its key=value bag, with typed accessors.
internal sealed class UrielLine
{
    public string Tag { get; }
    public IReadOnlyDictionary<string, string> Fields { get; }
    public string Raw { get; }

    private readonly Dictionary<string, string> _fields;

    public UrielLine(string tag, Dictionary<string, string> fields, string raw)
    {
        Tag = tag;
        _fields = fields;
        Fields = fields;
        Raw = raw;
    }

    public bool Has(string key) => _fields.ContainsKey(key);

    public string Get(string key, string fallback = "")
        => _fields.TryGetValue(key, out var v) ? v : fallback;

    public int GetInt(string key, int fallback = 0)
        => _fields.TryGetValue(key, out var v) && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : fallback;

    public float GetFloat(string key, float fallback = 0f)
        => _fields.TryGetValue(key, out var v) && float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : fallback;

    // Uriel encodes booleans as 0|1; tolerate true/on as well.
    public bool GetBool(string key, bool fallback = false)
    {
        if (!_fields.TryGetValue(key, out var v)) return fallback;
        return v == "1"
            || v.Equals("true", StringComparison.OrdinalIgnoreCase)
            || v.Equals("on", StringComparison.OrdinalIgnoreCase);
    }

    // Comma-separated list (key=a,b,c). Empty / "-" sentinel -> empty list.
    public IReadOnlyList<string> GetList(string key)
    {
        var v = Get(key);
        if (string.IsNullOrEmpty(v) || v == "-") return Array.Empty<string>();
        return v.Split(',').Where(s => s.Length > 0).ToArray();
    }

    // Wire-safe free-text fields are SafeToken-sanitized (spaces -> '_'); restore for display.
    public string GetText(string key, string fallback = "")
    {
        var v = Get(key, fallback);
        return string.IsNullOrEmpty(v) ? v : v.Replace('_', ' ');
    }

    // Bare token with the "-" / empty "unknown" sentinel folded to "".
    public string GetClean(string key)
    {
        var v = Get(key);
        return (string.IsNullOrEmpty(v) || v == "-") ? "" : v;
    }
}
