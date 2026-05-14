using BepInEx.Configuration;

namespace BloodCraftHub.Config;

// BepInEx-bound runtime configuration.
//
// PORT FROM:
//   LearningMods/BloodCraftUI-master/BloodCraftUI/Config/Settings.cs  (panel/feature gating flags)
//   LearningMods/Eclipse-main/Plugin.cs::InitConfig                   (HUD/feature toggles)
//
// Merge strategy: union of both, deduplicated. Eclipse uses one section
// ("UIOptions"); BloodCraftUI uses one section ("General"). Group related
// toggles into their own sections for clarity ("UI", "HUD", "Familiars", etc.).
public class Settings
{
    private ConfigEntry<float> _uiTransparency;
    private ConfigEntry<bool>  _showExperienceBar;
    private ConfigEntry<bool>  _showPrestige;
    private ConfigEntry<bool>  _showLegacyBar;
    private ConfigEntry<bool>  _showExpertiseBar;
    private ConfigEntry<bool>  _showFamiliarDetails;
    private ConfigEntry<bool>  _showProfessions;
    private ConfigEntry<bool>  _showQuestTracker;
    private ConfigEntry<bool>  _showShiftSlot;
    private ConfigEntry<bool>  _isFamStatsPanelEnabled;
    private ConfigEntry<bool>  _isBindButtonEnabled;
    private ConfigEntry<bool>  _autoEnableFamiliarEquipment;
    private ConfigEntry<bool>  _eclipsed;

    public float UITransparency             => _uiTransparency.Value;
    public bool  ShowExperienceBar          => _showExperienceBar.Value;
    public bool  ShowPrestige               => _showPrestige.Value;
    public bool  ShowLegacyBar              => _showLegacyBar.Value;
    public bool  ShowExpertiseBar           => _showExpertiseBar.Value;
    public bool  ShowFamiliarDetails        => _showFamiliarDetails.Value;
    public bool  ShowProfessions            => _showProfessions.Value;
    public bool  ShowQuestTracker           => _showQuestTracker.Value;
    public bool  ShowShiftSlot              => _showShiftSlot.Value;
    public bool  IsFamStatsPanelEnabled     => _isFamStatsPanelEnabled.Value;
    public bool  IsBindButtonEnabled        => _isBindButtonEnabled.Value;
    public bool  AutoEnableFamiliarEquipment => _autoEnableFamiliarEquipment.Value;
    public bool  Eclipsed                   => _eclipsed.Value;

    public Settings InitConfig()
    {
        var cfg = Plugin.Instance.Config;

        _uiTransparency             = cfg.Bind("UI",        nameof(UITransparency),             0.85f, "Background opacity of mod panels (0.0 transparent .. 1.0 opaque).");

        _showExperienceBar          = cfg.Bind("HUD",       nameof(ShowExperienceBar),          true,  "Show the leveling experience bar (requires Bloodcraft LevelingSystem).");
        _showPrestige               = cfg.Bind("HUD",       nameof(ShowPrestige),               true,  "Show prestige level next to the experience bar.");
        _showLegacyBar              = cfg.Bind("HUD",       nameof(ShowLegacyBar),              true,  "Show the blood legacy bar (requires Bloodcraft LegacySystem).");
        _showExpertiseBar           = cfg.Bind("HUD",       nameof(ShowExpertiseBar),           true,  "Show the weapon expertise bar (requires Bloodcraft ExpertiseSystem).");
        _showFamiliarDetails        = cfg.Bind("HUD",       nameof(ShowFamiliarDetails),        true,  "Show summarized familiar details bar.");
        _showProfessions            = cfg.Bind("HUD",       nameof(ShowProfessions),            true,  "Show the professions tab.");
        _showQuestTracker           = cfg.Bind("HUD",       nameof(ShowQuestTracker),           true,  "Show the quest tracker overlay.");
        _showShiftSlot              = cfg.Bind("HUD",       nameof(ShowShiftSlot),              true,  "Show the shift-slot indicator.");

        _isFamStatsPanelEnabled     = cfg.Bind("Panels",    nameof(IsFamStatsPanelEnabled),     true,  "Enable the familiar stats panel.");
        _isBindButtonEnabled        = cfg.Bind("Panels",    nameof(IsBindButtonEnabled),        true,  "Show the bind button in the familiar boxes panel.");

        _autoEnableFamiliarEquipment = cfg.Bind("Familiars", nameof(AutoEnableFamiliarEquipment), false, "Automatically issue the familiar-equipment enable command on UI bring-up.");

        _eclipsed                   = cfg.Bind("Advanced",  nameof(Eclipsed),                   true,  "Use fast update interval (0.1s) for live data. Disable if performance suffers (drops to 1s).");

        return this;
    }
}
