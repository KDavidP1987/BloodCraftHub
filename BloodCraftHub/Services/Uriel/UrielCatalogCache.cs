using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using BepInEx;
using BloodCraftHub.Utils;

namespace BloodCraftHub.Services.Uriel;

// Disk cache of a completed FULL Uriel object catalog scan. The catalog can be many pages
// (≤20 objects/page over chat), so a full browse is slow on a high-latency connection. Caching it means
// a user pays that cost ONCE per Uriel version instead of every session — the next login loads the
// "everything that exists to collect" browse instantly. Sibling of BeelzCatalogCache.
//
// Stored format: one tab-separated row per object — guid \t disc \t cat \t label — keyed (in the
// header) by Uriel plugin version. A server on a different version invalidates the cache (its prefab
// set / blocklist may differ). The cache is a pure optimization: any miss / mismatch / corruption
// falls through to a normal scan. Best-effort I/O — never throws to the caller.
internal static class UrielCatalogCache
{
    private const int SchemaVersion = 1;

    private static string CacheDir => Path.Combine(Paths.ConfigPath, MyPluginInfo.PLUGIN_NAME, "cache");
    private static string CacheFile => Path.Combine(CacheDir, "uriel-catalog.txt");

    internal static void Save(string pluginVersion, IReadOnlyList<UrielObject> objects)
    {
        try
        {
            if (objects == null || objects.Count == 0) return;
            Directory.CreateDirectory(CacheDir);
            var sb = new StringBuilder();
            sb.Append("BCHURIELCATALOG ").Append(SchemaVersion)
              .Append(" plugin=").Append(Sanitize(pluginVersion))
              .Append(" count=").Append(objects.Count).Append('\n');
            foreach (var o in objects)
                sb.Append(o.Guid.ToString(CultureInfo.InvariantCulture)).Append('\t')
                  .Append(o.Discoverable ? '1' : '0').Append('\t')
                  .Append(o.Category ?? "decor").Append('\t')
                  .Append((o.Label ?? "").Replace('\t', ' ')).Append('\n');
            File.WriteAllText(CacheFile, sb.ToString());
            LogUtils.LogDiagnostic($"[UrielCache] saved {objects.Count} catalog rows (plugin={pluginVersion}).");
        }
        catch (Exception ex) { LogUtils.LogDebug($"[UrielCache] save failed: {ex.Message}"); }
    }

    internal static bool TryLoad(string expectedPluginVersion, out List<UrielObject> objects)
    {
        objects = null;
        try
        {
            if (!File.Exists(CacheFile)) return false;
            var lines = File.ReadAllLines(CacheFile);
            if (lines.Length < 2) return false;

            int schema = -1; string plugin = "";
            foreach (var tok in lines[0].Split(' '))
            {
                if (tok == "BCHURIELCATALOG") continue;
                int eq = tok.IndexOf('=');
                if (eq > 0) { if (tok.Substring(0, eq) == "plugin") plugin = tok.Substring(eq + 1); }
                else if (schema < 0) int.TryParse(tok, out schema);
            }
            if (schema != SchemaVersion) return false;
            if (!string.Equals(plugin, Sanitize(expectedPluginVersion), StringComparison.Ordinal)) return false;

            var result = new List<UrielObject>(lines.Length - 1);
            for (int i = 1; i < lines.Length; i++)
            {
                if (lines[i].Length == 0) continue;
                var p = lines[i].Split('\t');
                if (p.Length < 4) continue;
                if (!int.TryParse(p[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int guid)) continue;
                result.Add(new UrielObject(guid, p[1] == "1", p[3], string.IsNullOrEmpty(p[2]) ? "decor" : p[2]));
            }
            objects = result;
            return result.Count > 0;
        }
        catch (Exception ex) { LogUtils.LogDebug($"[UrielCache] load failed: {ex.Message}"); objects = null; return false; }
    }

    internal static void Clear()
    {
        try { if (File.Exists(CacheFile)) File.Delete(CacheFile); }
        catch (Exception ex) { LogUtils.LogDebug($"[UrielCache] clear failed: {ex.Message}"); }
    }

    private static string Sanitize(string s) => string.IsNullOrEmpty(s) ? "-" : s.Replace(' ', '_');
}
