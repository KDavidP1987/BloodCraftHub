namespace BloodCraftHub.Patches;

// INBOUND chat-message pump.
//
// This is the most load-bearing patch in the mod. It hooks
// ClientChatSystem.OnUpdate and inspects every chat-related entity. Inside
// the loop:
//
//   1. If the message text starts with "[<digit>]:" → it's the Eclipse
//      structured protocol. Route to EclipseProtocolService.TryHandleInbound.
//      On success, consume the entity (don't let it reach the chat window).
//
//   2. Otherwise, run the legacy regex pipeline (BloodCraftUI's
//      MessageService_Processing.HandleMessage). This still handles things the
//      structured protocol doesn't cover (familiar box listings, command
//      confirmations, etc.).
//
//   3. Anything else is a real player/system message — leave it alone.
//
// PORT FROM:
//   LearningMods/BloodCraftUI-master/BloodCraftUI/Patches/ClientChatPatch.cs
//   LearningMods/Eclipse-main/Patches/ClientChatPatch.cs
//
// [HarmonyPatch(typeof(ClientChatSystem), nameof(ClientChatSystem.OnUpdate))]
public static class ClientChatPatch
{
    // [HarmonyPrefix]
    // public static void Prefix(ClientChatSystem __instance) { ... }
}
