namespace BloodCraftHub.Config;

// 0.17.2 crash-bisect TEST builds.
//
// A normal release defines NONE of these constants, so IsTestVariant is false and
// the mod honors the [Compatibility] config exactly as documented. Each test variant
// is compiled with one constant (via -p:CrashVariant=... — see
// tools/package-test-variants.ps1) that HARD-disables a patch group regardless of the
// user's saved .cfg. That matters because a returning user keeps their existing
// config, so flipping a config DEFAULT wouldn't actually turn the group off for them
// (the trap that invalidated earlier recipe tests). Compiling the group out means a
// non-technical tester can just install the zip through their mod manager and the
// group is genuinely off — no config editing, no cache clearing.
//
// These force-off flags are ANDed with the config switches in Plugin.ApplyPatches, so
// a variant can only ever turn a group OFF, never force one on.
//
// 0.17.2 bisect RESULT: the input-suppression group is the crash culprit (TEST-A =
// input off survived V-Blood tracking; chat/layering off did not). The NOMENUINPUT /
// NOMOVEINPUT variants split that group to find whether it's the menu patches
// (MenuInput/OpenHUDMenu/ActionWheel) or the movement patches (Gameplay/Ability) —
// so we can keep as much of the "don't act while typing" feature as is safe.
internal static class BuildVariant
{
#if BCH_VARIANT_NOINPUT
    public const string Tag = "TEST-A · input-suppression OFF";
    public const bool ForceInputSuppressionOff = true;
    public const bool ForceMenuInputOff        = false;
    public const bool ForceMoveInputOff        = false;
    public const bool ForceChatHooksOff        = false;
    public const bool ForceOverlayLayeringOff  = false;
#elif BCH_VARIANT_NOCHAT
    public const string Tag = "TEST-B · chat hooks OFF";
    public const bool ForceInputSuppressionOff = false;
    public const bool ForceMenuInputOff        = false;
    public const bool ForceMoveInputOff        = false;
    public const bool ForceChatHooksOff        = true;
    public const bool ForceOverlayLayeringOff  = false;
#elif BCH_VARIANT_NOLAYER
    public const string Tag = "TEST-C · overlay-layering OFF";
    public const bool ForceInputSuppressionOff = false;
    public const bool ForceMenuInputOff        = false;
    public const bool ForceMoveInputOff        = false;
    public const bool ForceChatHooksOff        = false;
    public const bool ForceOverlayLayeringOff  = true;
#elif BCH_VARIANT_NOMENUINPUT
    public const string Tag = "TEST-E · menu input-suppression OFF (movement/ability ON)";
    public const bool ForceInputSuppressionOff = false;
    public const bool ForceMenuInputOff        = true;
    public const bool ForceMoveInputOff        = false;
    public const bool ForceChatHooksOff        = false;
    public const bool ForceOverlayLayeringOff  = false;
#elif BCH_VARIANT_NOMOVEINPUT
    public const string Tag = "TEST-F · movement/ability suppression OFF (menu ON)";
    public const bool ForceInputSuppressionOff = false;
    public const bool ForceMenuInputOff        = false;
    public const bool ForceMoveInputOff        = true;
    public const bool ForceChatHooksOff        = false;
    public const bool ForceOverlayLayeringOff  = false;
#elif BCH_VARIANT_NOPATCHES
    public const string Tag = "TEST-D · all optional patches OFF";
    public const bool ForceInputSuppressionOff = true;
    public const bool ForceMenuInputOff        = true;
    public const bool ForceMoveInputOff        = true;
    public const bool ForceChatHooksOff        = true;
    public const bool ForceOverlayLayeringOff  = true;
#else
    // Normal release build — no group forced off; config rules.
    public const string Tag = null;
    public const bool ForceInputSuppressionOff = false;
    public const bool ForceMenuInputOff        = false;
    public const bool ForceMoveInputOff        = false;
    public const bool ForceChatHooksOff        = false;
    public const bool ForceOverlayLayeringOff  = false;
#endif

    public static bool IsTestVariant => Tag != null;
}
