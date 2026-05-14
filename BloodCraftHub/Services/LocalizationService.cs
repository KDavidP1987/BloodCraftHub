namespace BloodCraftHub.Services;

// Stunlock.Localization shim — registers our English.json strings as AssetGuid keys
// so they can be referenced like any other localized string in the game.
//
// PORT FROM: LearningMods/Eclipse-main/Services/LocalizationService.cs
//
// Used for tooltips on familiar items, ability names that are normally pulled
// from the game's locale tables, and any string the UI surfaces via a
// LocalizationKey rather than a raw C# string.
public class LocalizationService
{
    // public LocalizationService() { /* load Resources/Localization/English.json + register with Stunlock.Localization */ }
}
