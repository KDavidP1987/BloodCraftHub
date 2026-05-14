namespace BloodCraftHub.Services;

// INBOUND structured event channel — the durable replacement for regex parsing.
//
// PORT FROM:
//   LearningMods/Eclipse-main/Services/DataService.cs           (MAC verify, [EventId]:csv decode)
//   LearningMods/Bloodcraft-main/Services/EclipseService.cs     (server side, for reference)
//   LearningMods/Bloodcraft-main/Services/LocalizationService.cs::HandleServerReply
//
// Server protocol summary:
//   - Wire format:  "[<eventId>]:<csv-payload>;mac<base64-hmac>"
//   - eventId is NetworkEventSubType (3 values today):
//       0 = RegisterUser          — handshake; client sends first, server replies with key+configs
//       1 = ConfigsToClient       — one-shot snapshot of server-side toggles + numerical caps
//       2 = ProgressToClient      — periodic player state (XP, prestige, expertise, legacy, professions, quest)
//   - HMAC-SHA256 over "[<eventId>]:<csv>" with Core.SharedKey, base64-encoded
//
// Responsibilities:
//   1. On UI initialization, send the RegisterUser handshake (signed).
//   2. ClientChatPatch routes inbound messages whose text starts with "[" + digit + "]:"
//      to TryHandleInbound. Verify the MAC; reject silently on mismatch.
//   3. Decode the CSV payload by event id. Push parsed values into PlayerStateService.
public static class EclipseProtocolService
{
    // public enum NetworkEventSubType { RegisterUser = 0, ConfigsToClient = 1, ProgressToClient = 2 }
    //
    // public static void Register() { /* enqueue [0]:... via MessageService.SendMessage(signed:true) */ }
    //
    // /// <returns>true if the message matched the structured protocol and was consumed.</returns>
    // public static bool TryHandleInbound(string chatText) { return false; }
    //
    // private static bool VerifyMac(string body, string base64Mac) { ... }
    // private static void HandleConfigs(string csv) { ... }
    // private static void HandleProgress(string csv) { ... }
}
