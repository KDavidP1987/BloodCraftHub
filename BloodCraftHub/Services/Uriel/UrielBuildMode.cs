using System;
using BloodCraftHub.Utils;

namespace BloodCraftHub.Services.Uriel;

// 0.26: temporary "build mode" for the Uriel object-spawn hotkeys. While ACTIVE, the player's bound
// keys move / rotate / remove the nearest spawned object so they can arrange Uriel-brought objects
// without opening a UI. This is intentionally a SESSION-ONLY flag that defaults OFF and resets OFF on
// every login (Reset, from the ClientBootstrapSystem teardown hook) — so the build keys can never
// silently stay armed across sessions and steal the player's keys during normal play. The user flips
// it on for a building session and off when done (the UI states this prominently).
//
// The keys fire from a per-frame poller registered on CoreUpdateBehavior (like the Beelz cast keybinds),
// and are SUPPRESSED while typing in a BCH field or while the main panel is open with input-suppression
// (InputSuppression.ShouldBlock) — same contract as the Beelz keybinds — so a key pressed in a text
// field can never move an object.
internal static class UrielBuildMode
{
    /// <summary>Session-only: true while the player has build mode turned on. NOT persisted; always
    /// false at login.</summary>
    public static bool Active { get; private set; }

    /// <summary>Fired when Active flips, so the UI toggle can re-label itself.</summary>
    public static event Action ActiveChanged;

    public static void SetActive(bool on)
    {
        if (Active == on) return;
        Active = on;
        LogUtils.LogInfo($"[Uriel] build-mode hotkeys {(on ? "ENABLED" : "disabled")}.");
        try { ActiveChanged?.Invoke(); } catch (Exception ex) { LogUtils.LogError($"[Uriel] BuildMode.ActiveChanged handler threw: {ex}"); }
    }

    public static void Toggle() => SetActive(!Active);

    /// <summary>Reset to OFF (on logout / server-switch). Pure field reset — fires the event so any live
    /// UI toggle re-labels, but does no ECS/UI work itself.</summary>
    public static void Reset()
    {
        if (!Active) return;
        Active = false;
        try { ActiveChanged?.Invoke(); } catch { /* teardown path — never throw */ }
    }

    // Per-frame: fire the bound build command for any key pressed this frame. Registered on
    // CoreUpdateBehavior in Plugin.Load. Cheap when inactive (one bool check). Mirrors
    // BeelzProtocolService.TickKeybinds (typing/panel suppression + Uriel-present gate).
    public static void Tick()
    {
        if (!Active) return;
        if (!MessageService.IsInitialized || !UrielState.Present) return;
        if (BloodCraftHub.Patches.InputSuppression.ShouldBlock()) return;   // never while typing / panel-suppressed

        try
        {
            if (BloodCraftHub.Config.Settings.UrielBuildMoveKey.IsDown())   UrielClient.Move();
            if (BloodCraftHub.Config.Settings.UrielBuildRotateKey.IsDown()) UrielClient.Rotate();
            if (BloodCraftHub.Config.Settings.UrielBuildRemoveKey.IsDown()) UrielClient.Despawn();
        }
        catch (Exception ex) { LogUtils.LogError($"[Uriel] build-mode key tick failed: {ex}"); }
    }
}
