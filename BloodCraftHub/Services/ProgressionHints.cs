namespace BloodCraftHub.Services;

// 0.17.3: "you're leaving power on the table" hints. When the player hasn't set up a
// Bloodcraft element yet — no CLASS chosen, or no WEAPON EXPERTISE / BLOOD LEGACY bonus
// stats picked — surface a short, friendly nudge on that page + overlay so new players
// know there's free power to claim. Gated by Settings.ShowMissingElementHints (so players
// who intentionally skip a system aren't nagged) AND only shown for systems the server
// actually has enabled (IsSystemEnabled reflects the server config once flags settle).
//
// "Missing" = the actionable, free-power lever for each system:
//   Class     -> no class selected (PlayerClass.None).
//   Expertise -> no expertise bonus stats chosen (BonusStatsRaw empty).
//   Legacy    -> no legacy bonus stats chosen (BonusStatsRaw empty).
internal static class ProgressionHints
{
    // ⚔ is in the TMP fallback font (confirmed-OK glyph); avoid ⚠ (renders as a box).
    internal const string HintColorHex = "#FFC94A"; // amber — attention without alarm

    internal const string ClassMsg     = "⚔ No class selected — pick one for bonus stats & abilities (free power you're missing).";
    internal const string ExpertiseMsg = "⚔ No weapon expertise stats chosen — pick stats to turn your expertise into real damage.";
    internal const string LegacyMsg    = "⚔ No blood legacy stats chosen — pick stats to claim the legacy's free bonuses.";

    private static bool On => Config.Settings.ShowMissingElementHints;

    internal static bool ClassMissing()
        => On
           && PlayerStateService.IsSystemEnabled(PlayerStateService.SystemKind.Class)
           && PlayerStateService.Experience.Class == PlayerStateService.PlayerClass.None;

    internal static bool ExpertiseStatsMissing()
        => On
           && PlayerStateService.IsSystemEnabled(PlayerStateService.SystemKind.Expertise)
           && string.IsNullOrEmpty(PlayerStateService.Expertise.BonusStatsRaw);

    internal static bool LegacyStatsMissing()
        => On
           && PlayerStateService.IsSystemEnabled(PlayerStateService.SystemKind.Legacy)
           && string.IsNullOrEmpty(PlayerStateService.Legacy.BonusStatsRaw);

    // Convenience: the colored hint markup, or null when nothing to show.
    internal static string ClassHint()     => ClassMissing()     ? Colored(ClassMsg)     : null;
    internal static string ExpertiseHint() => ExpertiseStatsMissing() ? Colored(ExpertiseMsg) : null;
    internal static string LegacyHint()    => LegacyStatsMissing()    ? Colored(LegacyMsg)    : null;

    internal static string Colored(string msg) => $"<color={HintColorHex}>{msg}</color>";
}
