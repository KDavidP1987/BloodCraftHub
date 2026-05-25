using BloodCraftHub.Utils;
using HarmonyLib;
using Il2CppInterop.Runtime;
using ProjectM;
using ProjectM.UI;
using Unity.Entities;

namespace BloodCraftHub.Patches;

// 0.16: suppress the local character's GAMEPLAY input (movement / aim /
// ability + hotkey casts) while the BCH main panel is open, so the character
// doesn't act in the background while the user clicks buttons or types into
// forms. Chief friend-test complaint — especially bad for admins who fire
// hotkeyed abilities by accident.
//
// APPROACH HISTORY (see docs/LESSONS_LEARNED.md + memory):
//   - 0.1.x: PREFIX on InputActionSystem.OnUpdate returning false → FROZE the
//     whole game. CAUSE: InputActionSystem ALSO drives Unity UI input, so
//     skipping it starved the UI. Removed.
//   - 0.16 #1: POSTFIX on InputActionSystem zeroing EntityInput.Movement/.State.
//     Safe but INEFFECTIVE — the write persisted on read-back yet the character
//     still moved. CAUSE (found this session): GameplayInputSystem runs AFTER
//     InputActionSystem and re-populates EntityInput from live hardware,
//     overwriting our zero the same frame.
//   - 0.16 #2: adding ProjectM.UI.ChatInputFocused tag to LocalCharacter — no
//     effect; the engine's chat-focus gate isn't keyed off the character entity.
//   - 0.16 #3 (THIS): skip the actual CLIENT GAMEPLAY PRODUCER systems while the
//     panel is open:
//        * ProjectM.GameplayInputSystem.OnUpdate  -> movement + aim
//        * ProjectM.AbilityInputSystem.OnUpdate   -> ability / hotkey casts
//     These are SEPARATE systems from InputActionSystem, which keeps running —
//     so Unity UI input, the cursor, and BCH form typing all stay alive
//     (structurally NO freeze, unlike 0.1.x). The character stops moving,
//     aiming, and casting. We also zero EntityInput once while blocking so a key
//     held at open-time doesn't drift — and this time the zero STICKS, because
//     the system that used to overwrite it (GameplayInputSystem) is skipped.
//
// Gated behind Settings.SuppressGameInputWhileUIOpen (kill-switch, default OFF).
// Every callback is wrapped so any exception falls through to running the
// original system — a bug in here can NEVER freeze the game.
internal static class InputSuppression
{
    private static double _lastDiagAt;

    // 0.17: set true by the tabbed chat window's input field while it has focus,
    // so gameplay input (movement / abilities / menu hotkeys) is suppressed while
    // you type — independent of the SuppressGameInputWhileUIOpen setting, since
    // you never want to move mid-type.
    internal static bool ChatInputActive;

    internal static bool ShouldBlock()
    {
        try
        {
            if (ChatInputActive) return true;
            if (!Config.Settings.SuppressGameInputWhileUIOpen) return false;
            return Plugin.UIManager?.IsMainPanelOpen ?? false;
        }
        catch
        {
            return false;
        }
    }

    // 0.17.0: menu-hotkey gate for MenuInputSystem + OpenHUDMenuSystem. While
    // typing in chat we DO block the menu hotkeys (map = M, build = B, inventory,
    // …) so keystrokes don't open menus behind the chat. That can't trap the
    // player: ChatInputActive only stays true while our input is actually focused
    // (polled from isFocused), and pressing Escape force-releases the chat input
    // (ClientChatPatch.OnUpdate_Prefix) regardless of which systems are blocked,
    // since it reads the raw Escape key. So this is just ShouldBlock.
    internal static bool ShouldBlockMenus() => ShouldBlock();

    internal static void Diag(string msg)
    {
        double now = UnityEngine.Time.realtimeSinceStartupAsDouble;
        if (now - _lastDiagAt < 1.0) return;
        _lastDiagAt = now;
        LogUtils.LogInfo($"[InputSuppress] {msg}");
    }
}

// Movement + aim producer. Skipping its OnUpdate stops the local character from
// moving or aiming while the BCH panel is open.
[HarmonyPatch(typeof(GameplayInputSystem), nameof(GameplayInputSystem.OnUpdate))]
public static class GameplayInputSuppressionPatch
{
    [HarmonyPrefix]
    private static bool Prefix()
    {
        try
        {
            if (!InputSuppression.ShouldBlock()) return true; // run normally

            // Clear any held movement so the character doesn't drift while the
            // producer is skipped. This STICKS now, because the only system that
            // repopulates EntityInput from live hardware (this one) is about to
            // be skipped — which is exactly why attempt #1's zeroing failed.
            try
            {
                var character = Plugin.LocalCharacter;
                if (!Plugin.IsClientNull() && character != Entity.Null)
                {
                    var em = Plugin.EntityManager;
                    if (em.HasComponent<EntityInput>(character))
                    {
                        var ei = em.GetComponentData<EntityInput>(character);
                        ei.Movement = default;
                        ei.State = default;
                        em.SetComponentData(character, ei);
                    }
                }
            }
            catch
            {
                // best-effort; never let this stop the skip below
            }

            InputSuppression.Diag("blocking GameplayInputSystem.OnUpdate (panel open).");
            return false; // skip movement/aim production this frame
        }
        catch
        {
            return true; // any failure → never freeze; let the game run
        }
    }
}

// Ability / hotkey cast producer. Skipping its OnUpdate stops the character from
// casting spells or using hotkeyed abilities while the BCH panel is open.
//
// IMPORTANT (stuck-attack fix): the primary-mouse click that OPENS the BCH panel
// is also seen by the game world as a primary-attack press on the frame before
// blocking engages. If we merely skip this system, that half-started attack
// never sees its button-release and the character fires continuously until the
// panel closes. So while blocking we ALSO actively cancel the in-flight cast on
// the local character — set EntityAbilityInput.Interrupt and clear the held /
// queued cast — so the attack is released instead of frozen ON.
[HarmonyPatch(typeof(AbilityInputSystem), nameof(AbilityInputSystem.OnUpdate))]
public static class AbilityInputSuppressionPatch
{
    [HarmonyPrefix]
    private static bool Prefix()
    {
        try
        {
            if (!InputSuppression.ShouldBlock()) return true; // run normally

            try
            {
                var character = Plugin.LocalCharacter;
                if (!Plugin.IsClientNull() && character != Entity.Null)
                {
                    var em = Plugin.EntityManager;
                    if (em.HasComponent<EntityAbilityInput>(character))
                    {
                        var ai = em.GetComponentData<EntityAbilityInput>(character);
                        ai.Interrupt = true;        // cancel the active cast (kills the stuck primary attack)
                        ai.CastInput = default;     // treat the cast button as released
                        ai.QueuedCastGroup = Entity.Null;
                        ai.PrepareCastGroup = Entity.Null;
                        em.SetComponentData(character, ai);
                    }
                }
            }
            catch
            {
                // best-effort; never let this stop the skip below
            }

            InputSuppression.Diag("blocking AbilityInputSystem.OnUpdate (panel open).");
            return false; // skip ability/hotkey cast production this frame
        }
        catch
        {
            return true;
        }
    }
}

// Menu input reader. May handle radial / gamepad menu navigation. Kept as a
// belt-and-suspenders skip; the actual open-the-menu chokepoint is
// OpenHUDMenuSystem below (in-game testing showed B/M still opened with only
// this skipped, so they don't route through here on KBM).
[HarmonyPatch(typeof(MenuInputSystem), nameof(MenuInputSystem.OnUpdate))]
public static class MenuInputSuppressionPatch
{
    [HarmonyPrefix]
    private static bool Prefix()
    {
        try
        {
            // ShouldBlockMenus (NOT ShouldBlock): chat typing must not block menus/escape.
            if (!InputSuppression.ShouldBlockMenus()) return true;
            InputSuppression.Diag("blocking MenuInputSystem.OnUpdate (panel open).");
            return false;
        }
        catch
        {
            return true;
        }
    }
}

// THE HUD-menu chokepoint. Every game menu (map = M, build = B, inventory,
// spellbook, emotes, VBlood tracking, …) is a HUDMenuType opened via
// HUDMenuManager.ToggleMenu/GoToMenu, whose request this system processes in
// OnUpdate. Skipping it while the BCH panel is open means no game menu opens
// behind the panel — one skip covers them all.
//
// BUT skipping alone only DEFERS: the input reader (a separate system we don't
// block) still creates the open-request component each frame the key is pressed,
// and because we skip the consumer those requests pile up and all fire the
// instant the panel closes (observed in-game as "menus open with a delayed
// reaction"). The request comes in two component shapes — ProjectM.UI.OpenMenuEvent
// and ProjectM.UI.GoToHUDMenu (the latter has Delay/IsHandled, i.e. an explicitly
// deferred request). So while blocking we DRAIN both every frame: destroy the
// pending request entities so nothing is queued when the panel closes. The
// reader runs before this consumer in system order, so same-frame requests are
// already present here and get drained.
//
// Menu-CLOSE / ESC live elsewhere, and the UI pump is untouched, so the user can
// still close the BCH panel and there's no freeze risk.
[HarmonyPatch(typeof(OpenHUDMenuSystem), nameof(OpenHUDMenuSystem.OnUpdate))]
public static class OpenHUDMenuSuppressionPatch
{
    private static EntityQuery _openMenuEventQuery;
    private static EntityQuery _goToHudMenuQuery;
    private static bool _queriesReady;

    [HarmonyPrefix]
    private static bool Prefix()
    {
        try
        {
            // ShouldBlockMenus (NOT ShouldBlock): chat typing must not block menus/escape.
            if (!InputSuppression.ShouldBlockMenus()) return true;

            // Drain pending menu-open requests so they don't fire on unblock.
            try
            {
                var em = Plugin.EntityManager;
                if (!_queriesReady)
                {
                    _openMenuEventQuery = em.CreateEntityQuery(ComponentType.ReadOnly(Il2CppType.Of<OpenMenuEvent>()));
                    _goToHudMenuQuery = em.CreateEntityQuery(ComponentType.ReadOnly(Il2CppType.Of<GoToHUDMenu>()));
                    _queriesReady = true;
                }
                // DestroyEntity(query) is a no-op when the query is empty.
                em.DestroyEntity(_openMenuEventQuery);
                em.DestroyEntity(_goToHudMenuQuery);
            }
            catch
            {
                _queriesReady = false; // rebuild next frame (e.g. after a world reload)
            }

            InputSuppression.Diag("blocking OpenHUDMenuSystem.OnUpdate + draining OpenMenuEvent/GoToHUDMenu (panel open).");
            return false;
        }
        catch
        {
            return true;
        }
    }
}
