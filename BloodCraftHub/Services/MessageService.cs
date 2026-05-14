namespace BloodCraftHub.Services;

// OUTBOUND chat-injection pipeline.
//
// Phase 1 stub: just provides ProcessAllMessages() so the UI framework's
// CoreUpdateBehavior.Update() can call it every frame without NREing.
//
// PORT FROM (Phase 3):
//   LearningMods/BloodCraftUI-master/BloodCraftUI/Services/MessageService.cs           (queue + ChatMessageEvent construction)
//   LearningMods/BloodCraftUI-master/BloodCraftUI/Services/MessageService_Processing.cs (BCCOM_* command constants + InterceptFlag enum)
//   LearningMods/Eclipse-main/Services/DataService.cs                                  (HMAC signing of outbound messages)
public static class MessageService
{
    // Called every frame from Behaviors/CoreUpdateBehavior.Update.
    // No-op until the outbound queue is implemented.
    public static void ProcessAllMessages()
    {
    }

    // public enum InterceptFlag { None, FamStats, FamBoxes, FamBoxContents, ... }
    // public static void EnqueueMessage(string command, InterceptFlag waitingFor = InterceptFlag.None) { }
    // public static void SendMessage(string text, bool signed = false) { }
}
