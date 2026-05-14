namespace BloodCraftHub.Services;

// Companion partial to MessageService.cs - holds the command-string constants
// and (in Phase 3b) the inbound regex pipeline + InterceptFlag enum.
//
// Source for the constant list:
//   LearningMods/Bloodcraft-main/Commands/*  - VampireCommandFramework command groups
//   LearningMods/BloodCraftUI-master/BloodCraftUI/Services/MessageService_Processing.cs
//
// Why constants and not strings sprinkled at call sites:
//   1. Bloodcraft renames commands across versions; one place to update.
//   2. The inbound regex pipeline keys on the exact command string in many cases
//      ("you typed X" responses).
//   3. Phase 6 plan is to move this to a JSON config so the user can tune.
public static partial class MessageService
{
    // ---------- Familiar (.fam) ----------
    public const string BCCOM_FAM_BOXES          = ".fam boxes";
    public const string BCCOM_FAM_LIST_FROM_BOX  = ".fam lb";      // followed by box name
    public const string BCCOM_FAM_BIND_FROM_BOX  = ".fam b";       // followed by index
    public const string BCCOM_FAM_UNBIND         = ".fam u";
    public const string BCCOM_FAM_TOGGLE         = ".fam toggle";
    public const string BCCOM_FAM_COMBAT         = ".fam combat";
    public const string BCCOM_FAM_PRESTIGE       = ".fam pr";
    public const string BCCOM_FAM_RESET_STATS    = ".fam rs";
    public const string BCCOM_FAM_GET_LEVEL      = ".fam gl";
    public const string BCCOM_FAM_ENABLE_EQUIP   = ".fam smartbind";

    // ---------- Leveling (.lvl) ----------
    public const string BCCOM_LVL_GET            = ".lvl get";

    // ---------- Prestige ----------
    public const string BCCOM_PRESTIGE_GET       = ".prestige get";

    // ---------- Blood legacy (.bl) ----------
    public const string BCCOM_BL_GET             = ".bl get";

    // ---------- Class (.class) ----------
    public const string BCCOM_CLASS_LIST          = ".class l";
    public const string BCCOM_CLASS_LIST_SPELLS   = ".class lsp";
    public const string BCCOM_CLASS_LIST_STATS    = ".class lst";
    public const string BCCOM_CLASS_TOGGLE_SHIFT  = ".class shift";
    // .class s <Class>, .class c <Class>, .class csp <#> take args - constructed at call site.

    // ---------- Weapon expertise (.wep) ----------
    public const string BCCOM_WEP_GET            = ".wep get";
    public const string BCCOM_WEP_LIST           = ".wep l";
    public const string BCCOM_WEP_LIST_STATS     = ".wep lst";
    public const string BCCOM_WEP_RESET_STATS    = ".wep rst";
    public const string BCCOM_WEP_LOCK_SPELLS    = ".wep locksp";
    // .wep cst <Weapon> <Stat> takes args - constructed at call site.

    // Phase 3b will add:
    //   public enum InterceptFlag { None, FamStats, FamBoxes, FamBoxContents, ... }
    //   public static InterceptFlag CurrentIntercept { get; set; }
    //   public static bool HandleMessage(string chatText) { ... }
}
