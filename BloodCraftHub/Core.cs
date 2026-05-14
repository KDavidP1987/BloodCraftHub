using BepInEx.Logging;
using Unity.Entities;

namespace BloodCraftHub;

// Central runtime singleton, modeled on Eclipse's Core.cs.
//
// - Holds the client World once GameDataManager has initialized.
// - Caches the local character/user entity lookups.
// - Owns shared HMAC key state for the structured Eclipse-style protocol
//   (see Services/EclipseProtocolService.cs and Resources/SecretManager.cs).
//
// PORT REFERENCE: LearningMods/Eclipse-main/Core.cs — coroutine helpers,
// localization shim, entity dump utility. Add as needed.
internal static class Core
{
    private static World _client;

    public static World ClientWorld => _client;
    public static EntityManager EntityManager => _client.EntityManager;
    public static ManualLogSource Log => Plugin.LogInstance;

    public static bool HasInitialized { get; private set; }

    public static Entity LocalCharacter { get; set; } = Entity.Null;
    public static Entity LocalUser { get; set; } = Entity.Null;

    // Set once Resources/secrets.json has been parsed via SecretManager.
    // Used by EclipseProtocolService to verify inbound MAC-signed messages.
    public static byte[] SharedKey { get; set; }

    public static void Initialize(World clientWorld)
    {
        if (HasInitialized) return;
        _client = clientWorld;
        HasInitialized = true;
        Log.LogInfo("Core initialized on client world.");
    }

    public static void Reset()
    {
        _client = null;
        LocalCharacter = Entity.Null;
        LocalUser = Entity.Null;
        SharedKey = null;
        HasInitialized = false;
    }
}
