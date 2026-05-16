using System;
using BloodCraftHub.Utils;

namespace BloodCraftHub.Services;

// 0.11.0: poll the local character's shift-slot ability cooldown.
//
// Eclipse exposes this via a draggable HUD tile that clones the game's
// AbilityBarEntry prefab and reads AbilityCooldownState / AbilityChargesState
// off the active ability entity (LearningMods/Eclipse-main/Services/
// CanvasService.cs:1130). BCH is intentionally lighter — we read the same
// game-state components but render into BCH's own ResizeablePanelBase
// (ShiftSpellOverlayPanel) instead of cloning a game prefab.
//
// All IL2CPP access is wrapped in a single try/catch so a type-bridge hiccup
// (or the player not being in-world yet) downgrades the overlay to
// "—" without nuking the per-frame ticker. Update cadence is 0.1s
// (10 Hz) — same as Eclipse's ShiftUpdateLoop — fast enough that the cooldown
// ring/bar updates smoothly, cheap enough that the cost is negligible.
public static class ShiftCooldownService
{
    /// <summary>True if a shift ability is currently equipped on slot 3 and
    /// we have valid state to render. When false, callers should display a
    /// "no shift spell equipped" placeholder.</summary>
    public static bool HasShiftSpell { get; private set; }

    /// <summary>Seconds remaining on the current cooldown. 0 when ready.</summary>
    public static float CooldownRemaining { get; private set; }

    /// <summary>Total seconds for the current cooldown (used to compute the
    /// 0..1 fill fraction). 0 when no cooldown is in flight.</summary>
    public static float CooldownTotal { get; private set; }

    /// <summary>0..1 fill fraction (1 = full cooldown remaining, 0 = ready).</summary>
    public static float CooldownFraction
        => CooldownTotal > 0.001f
            ? UnityEngine.Mathf.Clamp01(CooldownRemaining / CooldownTotal)
            : 0f;

    /// <summary>Current charges available (0..MaxCharges). Some class shifts
    /// have multiple charges; for single-cast shifts this stays 1 (ready) or
    /// 0 (recharging).</summary>
    public static int CurrentCharges { get; private set; }
    public static int MaxCharges     { get; private set; }

    /// <summary>Last error captured from the polling loop. Surfaced in the
    /// overlay so the user can see why a value is "—". Cleared on success.</summary>
    public static string LastError { get; private set; } = "";

    private const double POLL_INTERVAL_SECONDS = 0.1;
    private static double _lastPollAt;

    /// <summary>Per-frame tick. Cheap when called every frame because it
    /// short-circuits on the poll interval. Wire from
    /// CoreUpdateBehavior.Actions in Plugin.Load.</summary>
    public static void Tick()
    {
        var now = UnityEngine.Time.realtimeSinceStartupAsDouble;
        if (now - _lastPollAt < POLL_INTERVAL_SECONDS) return;
        _lastPollAt = now;

        try
        {
            PollOnce();
            if (!string.IsNullOrEmpty(LastError)) LastError = "";
        }
        catch (Exception ex)
        {
            // Don't spam the log every poll if the same exception recurs.
            if (LastError != ex.Message)
            {
                LogUtils.LogWarning($"ShiftCooldownService poll failed: {ex.Message}");
                LastError = ex.Message;
            }
            HasShiftSpell = false;
            CooldownRemaining = 0f;
            CooldownTotal = 0f;
            CurrentCharges = 0;
            MaxCharges = 0;
        }
    }

    private static void PollOnce()
    {
        var character = Core.LocalCharacter;
        if (!Core.HasInitialized || character == Unity.Entities.Entity.Null)
        {
            HasShiftSpell = false;
            return;
        }
        var em = Core.EntityManager;

        // Step 1: read the character's AbilityBar_Shared component. It
        // references the current cast group + cast ability entities.
        if (!em.HasComponent<ProjectM.AbilityBar_Shared>(character))
        {
            HasShiftSpell = false;
            return;
        }
        var abilityBar = em.GetComponentData<ProjectM.AbilityBar_Shared>(character);

        // Step 2: resolve the ability group entity. The CastGroup field is a
        // NetworkedEntity pointer; in the client world we need the local
        // entity via GetEntityOnServer (the name is historical — same call
        // works client-side to resolve the local entity).
        Unity.Entities.Entity groupEntity = abilityBar.CastGroup.GetEntityOnServer();
        if (groupEntity == Unity.Entities.Entity.Null)
        {
            HasShiftSpell = false;
            return;
        }

        // Step 3: confirm this is the shift slot (index 3). If the local
        // character has no shift spell currently bound, the cast group might
        // be a different slot — bail out so we don't paint stale data.
        if (!em.HasComponent<ProjectM.AbilityGroupState>(groupEntity))
        {
            HasShiftSpell = false;
            return;
        }
        var groupState = em.GetComponentData<ProjectM.AbilityGroupState>(groupEntity);
        if (groupState.SlotIndex != 3)
        {
            HasShiftSpell = false;
            return;
        }
        HasShiftSpell = true;

        // Step 4: read cooldown total from AbilityCooldownData on the group
        // (the static spec) and cooldown end time from AbilityCooldownState on
        // the cast entity (the live state).
        float cdTotal = 0f;
        if (em.HasComponent<ProjectM.AbilityCooldownData>(groupEntity))
        {
            var cdData = em.GetComponentData<ProjectM.AbilityCooldownData>(groupEntity);
            cdTotal = cdData.Cooldown._Value;
        }

        // Cast entity holds the live cooldown end-time.
        double cdEnd = 0;
        Unity.Entities.Entity castEntity = abilityBar.CastAbility.GetEntityOnServer();
        if (castEntity != Unity.Entities.Entity.Null
            && em.HasComponent<ProjectM.AbilityCooldownState>(castEntity))
        {
            var cdState = em.GetComponentData<ProjectM.AbilityCooldownState>(castEntity);
            cdEnd = cdState.CooldownEndTime;
        }

        double serverNow = GetServerTimeOnServer();
        float remaining = (float)(cdEnd - serverNow);
        if (remaining < 0f) remaining = 0f;
        CooldownRemaining = remaining;
        CooldownTotal     = UnityEngine.Mathf.Max(0f, cdTotal);

        // Step 5: charges (multi-charge shifts like Reaper class). If the
        // component isn't present we treat the ability as single-shot.
        if (em.HasComponent<ProjectM.AbilityChargesState>(groupEntity))
        {
            var chargesState = em.GetComponentData<ProjectM.AbilityChargesState>(groupEntity);
            CurrentCharges = chargesState.CurrentCharges;
        }
        else
        {
            CurrentCharges = remaining > 0f ? 0 : 1;
        }
        if (em.HasComponent<ProjectM.AbilityChargesData>(groupEntity))
        {
            var chargesData = em.GetComponentData<ProjectM.AbilityChargesData>(groupEntity);
            MaxCharges = chargesData.MaxCharges;
        }
        else
        {
            MaxCharges = 1;
        }
    }

    /// <summary>Read the shared "server time" — the float64 seconds counter
    /// the server's ability system uses to compare against CooldownEndTime.
    /// Eclipse reads this via Core.ServerTime.TimeOnServer; we resolve the
    /// same path through ClientScriptMapper at call time so a session reset
    /// doesn't leave us holding a stale reference.</summary>
    private static double GetServerTimeOnServer()
    {
        var world = Core.ClientWorld;
        if (world == null) return UnityEngine.Time.timeAsDouble;
        var mapper = world.GetExistingSystemManaged<ProjectM.Scripting.ClientScriptMapper>();
        if (mapper == null) return UnityEngine.Time.timeAsDouble;
        // _ClientGameManager is an IL2CPP struct/wrapper — can't compare to
        // null. If it's uninitialized the property access throws and the
        // outer try/catch keeps the overlay alive.
        return mapper._ClientGameManager.ServerTime.TimeOnServer;
    }
}
