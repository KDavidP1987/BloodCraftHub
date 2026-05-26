using System;
using System.Reflection;
using System.Text;
using BloodCraftHub.Utils;
using HarmonyLib;

namespace BloodCraftHub.Patches;

// 0.17.3 TEMPORARY DIAGNOSTIC — REMOVE after we've captured the data.
//
// Goal: learn what V Rising's social menu (P key) passes when you click a context-menu
// entry on a player (e.g. right-click -> Whisper). We want to redirect that whisper
// into BloodCraftHub's own chat window, and to read the full connected-player roster.
// The reference assemblies give us the method NAME (SocialMenuMapper
// .HandleMemberContextMenuEntryClicked) but not its body or precise signature, so this
// prefix logs the runtime signature + the instance type + every argument's type and
// value. From a single right-click -> Whisper in game, the BepInEx log will show us the
// data shape (which arg/enum identifies "Whisper", and how the target player is
// addressed). Logging only: the original method ALWAYS runs, so the social menu behaves
// exactly as normal. Patched by name via AccessTools because SocialMenuMapper is a
// game-internal type we don't reference directly.
[HarmonyPatch]
internal static class SocialMenuDiagPatch
{
    private static MethodBase TargetMethod()
    {
        var t = AccessTools.TypeByName("ProjectM.UI.SocialMenuMapper");
        return t == null ? null : AccessTools.Method(t, "HandleMemberContextMenuEntryClicked");
    }

    [HarmonyPrefix]
    private static void Prefix(object __instance, object[] __args, MethodBase __originalMethod)
    {
        try
        {
            var sb = new StringBuilder("[SocialDiag] ");
            sb.Append(__originalMethod?.DeclaringType?.Name).Append('.').Append(__originalMethod?.Name).Append('(');
            var ps = __originalMethod?.GetParameters();
            if (ps != null)
                for (int i = 0; i < ps.Length; i++)
                    sb.Append(i > 0 ? ", " : string.Empty).Append(ps[i].ParameterType.FullName).Append(' ').Append(ps[i].Name);
            sb.Append(")  instance=").Append(__instance?.GetType().FullName ?? "null");
            sb.Append("  args=[");
            if (__args != null)
                for (int i = 0; i < __args.Length; i++)
                {
                    var a = __args[i];
                    sb.Append(i > 0 ? "  ||  " : string.Empty);
                    sb.Append(a == null ? "null" : (a.GetType().FullName + " = " + SafeToString(a)));
                }
            sb.Append(']');
            LogUtils.LogWarning(sb.ToString());
        }
        catch (Exception ex) { LogUtils.LogWarning($"[SocialDiag] log failed: {ex}"); }
    }

    private static string SafeToString(object o)
    {
        try { return o.ToString(); } catch { return "<ToString threw>"; }
    }
}
