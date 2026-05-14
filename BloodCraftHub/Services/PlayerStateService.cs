namespace BloodCraftHub.Services;

// Merged player-state store. Single source of truth for both panels and overlays.
//
// PORT FROM:
//   LearningMods/BloodCraftUI-master/BloodCraftUI/Services/BloodCraftStateService.cs (familiar boxes, equipment, active fam)
//   LearningMods/Eclipse-main/Services/DataService.cs                                (XP, prestige, legacy, expertise, professions, quest state)
//
// Fed by:
//   - EclipseProtocolService for structured data (preferred path).
//   - MessageService_Processing regex handlers for anything the protocol doesn't cover (familiar box contents, etc.).
//
// Consumed by:
//   - UI panels via subscription / observation (event handlers or polled getters).
//   - HUD overlays under UI/Overlays/.
public static class PlayerStateService
{
    // Examples — fill in concrete fields as you port:
    //
    // public struct ProgressState
    // {
    //     public float ExpPercent;
    //     public int   ExpLevel;
    //     public int   ExpPrestige;
    //     public int   ClassId;
    //     public float LegacyPercent;
    //     public int   LegacyLevel;
    //     public float ExpertisePercent;
    //     public int   ExpertiseLevel;
    //     // ... etc.
    // }
    //
    // public static ProgressState Progress { get; private set; }
    // public static event System.Action ProgressChanged;
    //
    // public static void UpdateProgress(in ProgressState next) { Progress = next; ProgressChanged?.Invoke(); }
}
