namespace BloodCraftHub.UI;

// Root UI manager. Owns the panel registry and routes show/hide.
//
// PORT FROM:
//   LearningMods/BloodCraftUI-master/BloodCraftUI/UI/BCUIManager.cs (panel routing, PanelType enum)
//   LearningMods/BloodCraftUI-master/ModernUI/UIManagerBase.cs       (folded into UI/Framework/)
//
// Extension pattern: each new panel adds a PanelType enum value, a class in
// UI/Panels/, and a registration in AddPanel().
public class BCHubUIManager
{
    public enum PanelType
    {
        // Existing from BloodCraftUI:
        FamStats,
        BoxList,
        BoxContent,
        // New for the merged mod (Eclipse-side):
        Progress,
        Prestige,
        Legacy,
        Expertise,
        Profession,
        Quest,
    }

    public void SetupAndShowUI()
    {
        // TODO: instantiate canvas + panels, call AddPanel for each PanelType.
    }

    public void Hide()
    {
        // TODO
    }

    public void Show()
    {
        // TODO
    }
}
