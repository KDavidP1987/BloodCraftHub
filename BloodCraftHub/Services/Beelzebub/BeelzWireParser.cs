using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace BloodCraftHub.Services.Beelzebub;

// Pure, stateless tokenizer for Beelzebub's machine-readable chat lines.
//
// Grammar (see Beelzebub repo docs/BCH_INTEGRATION_HANDOFF.md §1, ApiVersion 22):
//   [BEELZ:<tag>] key=value key=value ...
// - The tag is everything between "[BEELZ:" and the first "]".
// - Values are bare tokens: Beelzebub's SafeToken() guarantees no spaces
//   (space/tab/newline -> '_') and no '=' (-> '-') inside a value, so splitting
//   the remainder on spaces and each token on its FIRST '=' is unambiguous.
// - Lists are comma-separated with no spaces: key=a,b,c
// - Unknown/extra fields are kept verbatim — the source emits more fields than
//   the handoff table documents, and "the source file wins". Callers read fields
//   by name and tolerate absence, so new fields never break parsing.
internal static class BeelzWireParser
{
    public const string PREFIX = "[BEELZ:";

    public static bool IsBeelzLine(string line)
        => !string.IsNullOrEmpty(line) && line.StartsWith(PREFIX, StringComparison.Ordinal);

    /// <summary>Parse one wire line. Returns null if it isn't a well-formed [BEELZ:*] line.</summary>
    public static BeelzLine Parse(string line)
    {
        if (!IsBeelzLine(line)) return null;

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
            string key = token.Substring(0, eq);
            string val = token.Substring(eq + 1);
            fields[key] = val;                            // last write wins (duplicates not expected)
        }

        return new BeelzLine(tag, fields, line);
    }
}

// One parsed wire line: a tag plus its key=value bag, with typed accessors.
internal sealed class BeelzLine
{
    public string Tag { get; }
    public IReadOnlyDictionary<string, string> Fields { get; }
    public string Raw { get; }

    private readonly Dictionary<string, string> _fields;

    public BeelzLine(string tag, Dictionary<string, string> fields, string raw)
    {
        Tag = tag;
        _fields = fields;
        Fields = fields;
        Raw = raw;
    }

    public bool Has(string key) => _fields.ContainsKey(key);

    // Re-serialize the parsed fields to a space-separated `key=value` body (no `[BEELZ:tag]` prefix).
    // Loss-free round-trip through Parse(): wire values are SafeToken-encoded (never contain spaces), so
    // splitting on spaces reproduces the exact same field bag. Used by the catalog disk cache to persist a
    // scanned row in the same form the parser consumes.
    public string ToWireBody()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var kv in _fields)
        {
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(kv.Key).Append('=').Append(kv.Value);
        }
        return sb.ToString();
    }

    public string Get(string key, string fallback = "")
        => _fields.TryGetValue(key, out var v) ? v : fallback;

    public int GetInt(string key, int fallback = 0)
        => _fields.TryGetValue(key, out var v) && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : fallback;

    public float GetFloat(string key, float fallback = 0f)
        => _fields.TryGetValue(key, out var v) && float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : fallback;

    // Beelzebub encodes booleans as 0|1; tolerate true/on as well.
    public bool GetBool(string key, bool fallback = false)
    {
        if (!_fields.TryGetValue(key, out var v)) return fallback;
        return v == "1"
            || v.Equals("true", StringComparison.OrdinalIgnoreCase)
            || v.Equals("on", StringComparison.OrdinalIgnoreCase);
    }

    // Single-char source tag (s=R|V|S). Returns '\0' if absent.
    public char GetChar(string key, char fallback = '\0')
    {
        var v = Get(key);
        return v.Length > 0 ? v[0] : fallback;
    }

    // Comma-separated list (key=a,b,c). Empty or "any"/"none" sentinels -> empty list.
    public IReadOnlyList<string> GetList(string key)
    {
        var v = Get(key);
        if (string.IsNullOrEmpty(v)) return Array.Empty<string>();
        if (v.Equals("any", StringComparison.OrdinalIgnoreCase) ||
            v.Equals("none", StringComparison.OrdinalIgnoreCase))
            return Array.Empty<string>();
        return v.Split(',').Where(s => s.Length > 0).ToArray();
    }

    // Free-text fields are SafeToken-sanitized (spaces -> '_'); restore for display.
    public string GetText(string key, string fallback = "")
    {
        var v = Get(key, fallback);
        return string.IsNullOrEmpty(v) ? v : v.Replace('_', ' ');
    }

    // Beelzebub writes "-" (and sometimes "auto"/"none") for an UNSET field. These accessors fold
    // those sentinels to null / "" so callers can distinguish "no override" from a real value —
    // used for the per-ability shaping overrides (cooldown_override=, heal_mult=, …) added in
    // Beelz v0.65–v0.87. NOTE: tri-state shaping fields (interruptible/free_move/cast_speed) keep
    // their raw value via Get() because "auto" is meaningful there (engine default), not "unset".
    public float? GetFloatOpt(string key)
    {
        if (!_fields.TryGetValue(key, out var v) || IsUnset(v)) return null;
        return float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : (float?)null;
    }

    public int? GetIntOpt(string key)
    {
        if (!_fields.TryGetValue(key, out var v) || IsUnset(v)) return null;
        return int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : (int?)null;
    }

    // Bare token with "unset" sentinels folded to "" (e.g. school=none, category_override=-).
    public string GetClean(string key)
    {
        var v = Get(key);
        return IsUnset(v) ? "" : v;
    }

    // Free-text (underscores restored) with "unset" sentinels folded to "" (e.g. desc=-).
    public string GetCleanText(string key)
    {
        var v = GetText(key);
        return IsUnset(v) ? "" : v;
    }

    private static bool IsUnset(string v)
        => string.IsNullOrEmpty(v) || v == "-"
           || v.Equals("auto", StringComparison.OrdinalIgnoreCase)
           || v.Equals("none", StringComparison.OrdinalIgnoreCase);
}
