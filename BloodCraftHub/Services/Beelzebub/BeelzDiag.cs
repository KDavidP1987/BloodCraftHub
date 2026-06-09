using BloodCraftHub.Utils;

namespace BloodCraftHub.Services.Beelzebub;

// Central gate for Beelzebub diagnostic detail. Drives both the in-UI ID/raw-name
// display (the loadout "ID" column, slot-row IDs, richer hover) and the verbose
// wire trace logged to the BepInEx LogOutput.log.
//
// It is ON when EITHER:
//   • the Beelzebub-specific "Enable diagnostic details" toggle is set
//     (Beelzebub → Settings tab — the one testers flip), OR
//   • BCH's global DiagnosticMode is active (Off/Session/Always — the existing
//     0.15.0 "toggle on → reproduce → send log" workflow).
//
// The trace is intended for testers/server admins reporting which abilities work:
// they flip the toggle, reproduce, and copy the [Beelz][diag] lines from the log.
// HandleLine / Send are per-MESSAGE (not per-frame), so logging each is cheap; the
// early-return keeps it free when the toggle is off. Do NOT call from per-frame code.
internal static class BeelzDiag
{
    /// <summary>True when Beelzebub diagnostic detail should be shown/logged.</summary>
    public static bool Enabled => Config.Settings.BeelzDiagnostics || Config.Settings.DiagnosticMode;

    /// <summary>Log an inbound raw [BEELZ:*] line (the server → client trace).</summary>
    public static void LogIn(string raw)
    {
        if (!Enabled || string.IsNullOrEmpty(raw)) return;
        LogUtils.LogInfo($"[Beelz][diag] << {raw}");
    }

    /// <summary>0.18.3: log a detection/handshake event (probe sent, settle start, give-up). Gated like
    /// the wire trace; called per-probe (~every few seconds), NOT per-frame, so the Enabled check is cheap.</summary>
    public static void Log(string msg)
    {
        if (!Enabled || string.IsNullOrEmpty(msg)) return;
        LogUtils.LogInfo($"[Beelz][diag] {msg}");
    }
}
