using BepInEx.Logging;

namespace BloodCraftHub.Utils;

public static class LogUtils
{
    private static ManualLogSource _log;
    public static void Init(ManualLogSource log) => _log = log;

    public static void LogInfo(object msg)    => _log?.LogInfo(msg);
    public static void LogWarning(object msg) => _log?.LogWarning(msg);
    public static void LogError(object msg)   => _log?.LogError(msg);
    public static void LogDebug(object msg)   => _log?.LogDebug(msg);

    /// <summary>0.15.0: gated diagnostic logging. Emits at LogInfo level
    /// (so it shows up in the standard BepInEx console + log file) but
    /// ONLY when Settings.DiagnosticMode is true. The intent is a "user
    /// hits an issue → toggles diagnostic on → reproduces → sends log"
    /// workflow. Cheap when off (one bool check + early return), so
    /// sprinkling these at user-action paths (UI clicks, overlay
    /// toggles, protocol state transitions, hotkey fires) is fine. Do
    /// NOT call from per-frame paths — those would spam ~60 lines/sec
    /// when diagnostic mode is on. Tagged with [DIAG] prefix in the
    /// log so the user can grep their share.</summary>
    public static void LogDiagnostic(object msg)
    {
        if (!Config.Settings.DiagnosticMode) return;
        _log?.LogInfo("[DIAG] " + msg);
    }
}
