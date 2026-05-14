using System;
using System.IO;
using System.Reflection;
using System.Text.Json;

namespace BloodCraftHub.Resources;

// Loads the HMAC shared key (base64) from the embedded secrets.json so we can
// sign outbound Eclipse-protocol messages and verify inbound ones.
//
// Key is the SAME value Bloodcraft (server) and Eclipse (client) ship - it's a
// public pre-shared default, not an admin secret. See Resources/secrets.json.
public static class SecretManager
{
    private const string ResourceName = "BloodCraftHub.Resources.secrets.json";

    private static byte[] _key;
    private static bool   _loaded;

    /// <summary>
    /// Returns the decoded shared key bytes, or null if the resource is missing
    /// / malformed / contains a placeholder empty key. Callers should defend
    /// against null — when null, the Eclipse protocol just won't work and we
    /// fall back to the regex pipeline (Phase 3b, future).
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
            var json = reader.ReadToEnd();
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("NEW_SHARED_KEY", out var keyProp))
                return _key = null;

            var b64 = keyProp.GetString();
            if (string.IsNullOrEmpty(b64)) return _key = null;

            return _key = Convert.FromBase64String(b64);
        }
        catch
        {
            return _key = null;
        }
    }
}
