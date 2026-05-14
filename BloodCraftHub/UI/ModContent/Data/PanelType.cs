namespace BloodCraftHub.UI.ModContent.Data;

// Identity tag for each kind of panel/overlay BloodCraftHub can host.
//
// The copied UI framework (UniverseLib panels) references PanelType for panel
// identity + persistent config keys, so we keep the enum in the same namespace
// path that the upstream BloodCraftUI used. New tabs/overlays add a value here.
public enum PanelType
{
    Base,                   // root content / floating button host

    // Primary-overlay tabs (the main UI):
    FamiliarsTab,
    BoxesTab,
    ClassTab,
    ExpertiseTab,
    UnarmedShiftTab,
    PrestigeTab,
    LevelsTab,
    AdminTab,

    // Help / Reference tabs:
    QuickStartTab,

    // Secondary overlays (independent draggable panels):
    ExperienceOverlay,
    FamiliarOverlay,

    // Legacy panel identities from BloodCraftUI — kept so the existing
    // ResizeablePanelBase config keys (Panels/<PanelType>) survive a port.
    BoxList,
    BoxContent,
    FamStats,
    TestPanel,
}
