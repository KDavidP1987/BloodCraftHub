using BloodCraftHub.Utils;

namespace BloodCraftHub.Services.Uriel;

// Central gate for Uriel diagnostic detail (sibling of Services/Beelzebub/BeelzDiag).
//
// ON when EITHER:
//   • the Uriel-specific "Enable diagnostic details" toggle is set (Uriel -> Settings tab), OR
//   • BCH's global DiagnosticMode is active.
//
// Drives the verbose [Uriel][diag] wire trace logged to the BepInEx LogOutput.log so testers /
// server admins can report exactly which commands fired and what came back. HandleLine / Send are
// per-MESSAGE (not per-frame), so logging each is cheap and the early-return keeps it free when off.
// Do NOT call from per-frame code.
internal static class UrielDiag
{
    /// <summary>True when Uriel diagnostic detail should be shown / logged.</summary>
    public static bool Enabled => Config.Settings.UrielDiagnostics || Config.Settings.DiagnosticMode;

    /// <summary>Log an inbound raw [URIEL:*] line (the server -> client trace).</summary>
    public static void LogIn(string raw)
    {
        if (!Enabled || string.IsNullOrEmpty(raw)) return;
        LogUtils.LogInfo($"[Uriel][diag] << {raw}");
    }

    /// <summary>Log a detection/handshake or outbound event (probe sent, settle start, give-up).
    /// Gated like the wire trace; called per-probe / per-command, NOT per-frame.</summary>
    public static void Log(string msg)
    {
        if (!Enabled || string.IsNullOrEmpty(msg)) return;
        LogUtils.LogInfo($"[Uriel][diag] {msg}");
    }
}
