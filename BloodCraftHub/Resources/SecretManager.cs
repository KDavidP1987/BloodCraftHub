using System;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;

namespace BloodCraftHub.Resources;

// Loads the HMAC shared key (base64) from the embedded secrets.json.
// Key is the SAME value Bloodcraft (server) and Eclipse (client) ship - a
// public pre-shared default, not an admin secret. See Resources/secrets.json.
//
// Why regex instead of System.Text.Json:
//   BepInEx's IL2CPP runtime under V Rising doesn't reliably resolve
//   System.Text.Json 9.x at load time - referencing the NuGet package
//   produces a FileNotFoundException at Plugin.Load. Our secrets.json is
//   trivially structured (one string property we care about) so a regex
//   is correct, dependency-free, and faster than spinning up a JSON parser.
public static class SecretManager
{
    private const string ResourceName = "BloodCraftHub.Resources.secrets.json";

    // Matches:  "NEW_SHARED_KEY"  :  "<base64-chars-and-padding>"
    // Tolerates whitespace, escaped or non-escaped key, base64 alphabet only.
    private static readonly Regex KeyRegex = new(
        @"""NEW_SHARED_KEY""\s*:\s*""([A-Za-z0-9+/=]+)""",
        RegexOptions.Compiled);

    private static byte[] _key;
    private static bool   _loaded;

    /// <summary>
    /// Returns the decoded shared key bytes, or null if the resource is missing /
    /// malformed / contains a placeholder empty key. Callers should defend against
    /// null - the Eclipse protocol just won't work in that case.
    /// </summary>
    public static byte[] GetSharedKey()
    {
        if (_loaded) return _key;
        _loaded = true;

        try
        {
            var asm = Assembly.GetExecutingAssembly();
            using var stream = asm.GetManifestResourceStream(ResourceName);
            if (stream == null) return _key = null;

            using var reader = new StreamReader(stream);
            string json = reader.ReadToEnd();

            var match = KeyRegex.Match(json);
            if (!match.Success) return _key = null;

            string b64 = match.Groups[1].Value;
            if (string.IsNullOrEmpty(b64)) return _key = null;

            return _key = Convert.FromBase64String(b64);
        }
        catch
        {
            return _key = null;
        }
    }
}
