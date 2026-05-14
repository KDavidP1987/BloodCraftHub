namespace BloodCraftHub.Services;

// OUTBOUND chat-injection pipeline.
//
// PORT FROM:
//   LearningMods/BloodCraftUI-master/BloodCraftUI/Services/MessageService.cs           (queue + ChatMessageEvent construction)
//   LearningMods/BloodCraftUI-master/BloodCraftUI/Services/MessageService_Processing.cs (BCCOM_* command string constants + InterceptFlag enum)
//   LearningMods/Eclipse-main/Services/DataService.cs                                  (HMAC signing of outbound messages)
//
// Contract:
//   - EnqueueMessage(string command, InterceptFlag expectedReply)
//   - ProcessAllMessages() called per-frame by CoreUpdateBehavior
//   - SendMessage builds a ChatMessageEvent ECS entity with MessageType.Local
//     attributed to Core.LocalUser. Server treats it as a typed chat command.
//   - When signed:true, append ";mac{hmac}" using Core.SharedKey before injection.
public static class MessageService
{
    // public enum InterceptFlag { None, FamStats, FamBoxes, FamBoxContents, ... }
    // public static void EnqueueMessage(string command, InterceptFlag waitingFor = InterceptFlag.None) { }
    // public static void ProcessAllMessages() { }
    // public static void SendMessage(string text, bool signed = false) { }

    // public const string BCCOM_FAM_BOXES = ".fam boxes";
    // public const string BCCOM_FAM_STATS = ".fam stats";
    // ... etc — see MessageService_Processing.cs in BloodCraftUI for the full list.
}
