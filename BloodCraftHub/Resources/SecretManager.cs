using System.IO;
using System.Reflection;
using System.Text.Json;

namespace BloodCraftHub.Resources;

// Loads the HMAC shared key from the embedded secrets.json so we can
// authenticate inbound Eclipse-protocol messages and sign outbound ones.
//
// PORT FROM: LearningMods/Eclipse-main/Resources/Secrets.cs
//
// The shipped secrets.json is a placeholder. The real key is distributed by
// the Bloodcraft server admin and must match what the server's EclipseService
// uses. Do not commit a real key to source control.
public static class SecretManager
{
    private const string ResourceName = "BloodCraftHub.Resources.secrets.json";

    public static string GetNewSharedKey()
    {
        try
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName);
            if (stream == null) return string.Empty;
            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("newSharedKey", out var k) ? (k.GetString() ?? string.Empty) : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}
