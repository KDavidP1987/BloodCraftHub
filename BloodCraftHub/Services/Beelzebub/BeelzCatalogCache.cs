using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx;
using BloodCraftHub.Utils;

namespace BloodCraftHub.Services.Beelzebub;

// Disk cache of a completed FULL catalog scan. The scan is bottlenecked on server->client chat-line volume
// (~1,700 abilities x ~2-3 chunked lines each), which is minutes for high-latency remote users. Caching it
// means a user pays that cost ONCE per Beelz version instead of every login — the next session loads the
// catalog instantly with no scan.
//
// WHAT IS STORED: the raw wire BODY of each `[BEELZ:catalog-ability]` row — the `key=value` tokens exactly
// as the parser consumed them. This is decoupled from the BeelzCatalogAbility record: adding fields to the
// record needs NO cache change (the round-trip is whatever the server sent), and on load the bodies go back
// through the SAME BeelzWireParser + ReadCatalogAbility the live scan uses, so cache and live are identical.
//
// KEYED BY Beelz plugin version: a server on a different version invalidates the cache (its ability set /
// config may differ). NOT keyed by server identity (BCH has none handy), so two servers on the SAME version
// share a cache — fine for the mostly-static metadata; the per-server DYNAMIC fields (enabled / overrides /
// review_status) are "last known" and refreshed by a manual Re-scan. The cache is a pure optimization: any
// miss / version mismatch / schema change / corruption falls through to a normal scan. Best-effort I/O —
// never throws to the caller.
internal static class BeelzCatalogCache
{
    // Bump when the header/body format changes so older caches are ignored (not the Beelz version — that's
    // tracked separately as the plugin= key). The body format is "whatever the wire sent", so this only
    // changes if THIS file's framing changes.
    private const int SchemaVersion = 1;

    internal const string PlayerScope = "player";
    internal const string AdminScope  = "admin";

    private static string CacheDir => Path.Combine(Paths.ConfigPath, MyPluginInfo.PLUGIN_NAME, "cache");
    private static string FileFor(string scope) => Path.Combine(CacheDir, $"beelz-catalog-{scope}.txt");

    /// <summary>Persist the wire bodies of a completed FULL (unfiltered) scan. No-op on an empty set.</summary>
    internal static void Save(string scope, string pluginVersion, int apiVersion, IReadOnlyList<string> bodies)
    {
        try
        {
            if (bodies == null || bodies.Count == 0) return;
            Directory.CreateDirectory(CacheDir);
            var sb = new StringBuilder();
            sb.Append("BCHCATALOG ").Append(SchemaVersion)
              .Append(" plugin=").Append(Sanitize(pluginVersion))
              .Append(" api=").Append(apiVersion)
              .Append(" count=").Append(bodies.Count).Append('\n');
            foreach (var b in bodies) sb.Append(b).Append('\n');
            File.WriteAllText(FileFor(scope), sb.ToString());
            LogUtils.LogDiagnostic($"[BeelzCache] saved {bodies.Count} {scope} rows (plugin={pluginVersion}).");
        }
        catch (Exception ex) { LogUtils.LogDebug($"[BeelzCache] save {scope} failed: {ex.Message}"); }
    }

    /// <summary>Load the wire bodies if a cache exists AND matches the schema + the given plugin version.
    /// Returns false (and null bodies) on any miss / mismatch / corruption so the caller scans normally.</summary>
    internal static bool TryLoad(string scope, string expectedPluginVersion, out List<string> bodies)
    {
        bodies = null;
        try
        {
            var path = FileFor(scope);
            if (!File.Exists(path)) return false;
            var lines = File.ReadAllLines(path);
            if (lines.Length < 2) return false;                       // header + at least one row

            int schema = -1; string plugin = "";
            foreach (var tok in lines[0].Split(' '))
            {
                if (tok == "BCHCATALOG") continue;
                int eq = tok.IndexOf('=');
                if (eq > 0) { if (tok.Substring(0, eq) == "plugin") plugin = tok.Substring(eq + 1); }
                else if (schema < 0) int.TryParse(tok, out schema);   // first bare number = schema
            }
            if (schema != SchemaVersion) return false;
            if (!string.Equals(plugin, Sanitize(expectedPluginVersion), StringComparison.Ordinal)) return false;

            bodies = new List<string>(lines.Length - 1);
            for (int i = 1; i < lines.Length; i++)
                if (lines[i].Length > 0) bodies.Add(lines[i]);
            return bodies.Count > 0;
        }
        catch (Exception ex) { LogUtils.LogDebug($"[BeelzCache] load {scope} failed: {ex.Message}"); bodies = null; return false; }
    }

    /// <summary>Delete both scope caches (e.g. a user-invoked "clear cache").</summary>
    internal static void Clear()
    {
        try
        {
            foreach (var s in new[] { PlayerScope, AdminScope })
            {
                var p = FileFor(s);
                if (File.Exists(p)) File.Delete(p);
            }
        }
        catch (Exception ex) { LogUtils.LogDebug($"[BeelzCache] clear failed: {ex.Message}"); }
    }

    // The plugin version is a single header token; strip spaces defensively so the header stays parseable.
    private static string Sanitize(string s) => string.IsNullOrEmpty(s) ? "-" : s.Replace(' ', '_');
}
