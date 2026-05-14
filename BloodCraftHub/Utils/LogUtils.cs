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
}
