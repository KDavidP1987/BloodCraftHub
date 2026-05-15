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
    BloodLegacyTab,
    UnarmedShiftTab,
    PrestigeTab,
    LevelsTab,
    AdminTab,

    // Kindred-suite tabs (companion server mods to Bloodcraft):
    KindredLogisticsTab,
    KindredLogisticsAdminTab,
    KindredCommandsPlayerTab,
    KindredAdminPlayersTab,
    KindredAdminServerTab,
    KindredAdminWorldTab,

    // Bloodcraft daily quest tab + overlay:
    DailyQuestTab,
    DailyQuestOverlay,

    // Help / Reference tabs:
    QuickStartTab,
    VanillaAdminTab,
    AboutTab,

    // Secondary overlays (independent draggable panels):
    ExperienceOverlay,
    FamiliarOverlay,
    FamiliarBrowserOverlay,
    ProfessionOverlay,

    // Legacy panel identities from BloodCraftUI — kept so the existing
    // ResizeablePanelBase config keys (Panels/<PanelType>) survive a port.
    BoxList,
    BoxContent,
    FamStats,
    TestPanel,
}
