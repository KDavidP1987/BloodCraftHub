using System;
using BloodCraftHub.Utils;

namespace BloodCraftHub.Services;

// 0.11.0: poll the local character's shift-slot ability cooldown.
//
// 0.11.4 — friend-test: "Square tile renders with '—' and no countdown after
// casting; diag line stays empty." Two more bugs from the 0.11.3 rewrite:
//
//  (1) HasShiftSpell was gated on `BaseAbilityGroupOnSlot.GuidHash != 0`.
//      That's the *base* prefab in the slot — but Bloodcraft uses
//      `ReplaceAbilityOnSlotBuff` to swap class spells onto the shift slot,
//      so the base entry can be empty even though the slot is fully usable.
//      Detection has to be "slot-entity exists" instead.
//
//  (2) The diagnostic-line writer in the panel sat AFTER the early return
//      for !HasShiftSpell — so when detection failed we returned without
//      ever painting the diag values, and the empty space the user saw
//      told them nothing. Diag now updates unconditionally each poll.
//
// Detection strategy mirrors Eclipse:
//   - Each poll, read `AbilityBar_Shared.CastGroup.GetEntityOnServer()` and
//     its `AbilityGroupState.SlotIndex`. When SlotIndex==3 we know the
//     player just cast (or is currently casting) shift — latch the prefab.
//   - Buffer probe (`AbilityGroupSlotBuffer[3]`) is the secondary path —
//     for vanilla shift it works at session start, but for Bloodcraft
//     overrides we rely on the CastGroup observation.
//   - Once the prefab is latched, future polls compare CastGroup's prefab
//     against the latch to know "this poll's cast is shift's" and only
//     then refresh the cooldown latches from the cast-ability entity.
//
// Diagnostic fields surface enough internal state that the next failure
// mode is debuggable from in-game without re-shipping a build.
public static class ShiftCooldownService
{
    public static bool HasShiftSpell { get; private set; }
    public static float CooldownRemaining { get; private set; }
    public static float CooldownTotal { get; private set; }
    public static float CooldownFraction
        => CooldownTotal > 0.001f
            ? UnityEngine.Mathf.Clamp01(CooldownRemaining / CooldownTotal)
            : 0f;
    public static int CurrentCharges { get; private set; }
    public static int MaxCharges     { get; private set; }
    public static string LastError { get; private set; } = "";

    // ── Diagnostics ──
    public static int    DiagShiftPrefabHash;
    public static int    DiagCastGroupPrefabHash;
    public static int    DiagCastGroupSlotIndex = -1;
    public static int    DiagSlotGroupPrefabHash; // 0.16.x: live slot-3 group entity prefab (icon-on-load diag)
    public static double DiagServerNow;
    public static double DiagLatchedEnd;
    public static double DiagSlotCooldownEnd; // 0.29: cooldown end read from the PERSISTENT slot group entity (recast support)
    public static double DiagLastRefreshAt;
    public static int    DiagPollCount;
    public static string DiagLastReadSource = "init";

    // ── 0.16: slotted-spell icon ──
    // The actual Sprite shown in the shift slot, resolved Eclipse-style from the
    // ability group entity's MANAGED AbilityTooltipData component (.Icon is a
    // ready-to-use Sprite). Cached and keyed by the latched prefab hash so we
    // only resolve when the slotted spell changes.
    public static UnityEngine.Sprite ShiftIcon { get; private set; }
    public static int ShiftIconPrefabHash { get; private set; }

    // Lazily-built ComponentType for the managed AbilityTooltipData lookup. MUST
    // stay lazy — a static-field initializer calling ComponentType.ReadOnly /
    // Il2CppType.Of at Plugin.Load NREs inside Unity.Entities.TypeManager before
    // the ECS World exists (see CLAUDE.md + the 0.10.3 fix). First poll runs well
    // after the World is up.
    private static Unity.Entities.ComponentType? _abilityTooltipDataCT;

    // 0.16.1 crash-hardening: the managed AbilityTooltipData read (GetComponentObject)
    // this drives is the ONLY managed-component access in this poll and the suspected
    // source of the v0.16.0 GC-finalizer crash seen on some servers (a dangling managed
    // wrapper surfacing on the IL2CPP finalizer thread). If it faults repeatedly, latch
    // it off for the rest of the session so a bad server/entity state can't keep poking
    // managed-component access every poll. The cooldown readout is unaffected.
    private static int  _iconResolutionFaults;
    private static bool _iconResolutionDisabled;
    private const int   ICON_RESOLUTION_FAULT_LIMIT = 5;

    private const double POLL_INTERVAL_SECONDS = 0.1;
    private static double _lastPollAt;

    // ── Latches ──
    private static Unity.Entities.Entity _latchedCharacter = Unity.Entities.Entity.Null;
    private static Stunlock.Core.PrefabGUID _latchedShiftPrefab;
    private static double _latchedCooldownEnd;
    private static float  _latchedCooldownTotal;
    private static int    _latchedCurrentCharges = 1;
    private static int    _latchedMaxCharges     = 1;

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
            if (LastError != ex.Message)
            {
                LogUtils.LogWarning($"ShiftCooldownService poll failed: {ex.Message}");
                LastError = ex.Message;
            }
            DiagLastReadSource = $"err:{ex.GetType().Name}";
        }
    }

    private static void PollOnce()
    {
        DiagPollCount++;

        // 0.11.5 fix: Core.LocalCharacter / Core.HasInitialized are stub-state
        // that was never wired up (GameManagerPatch.cs:12 has the
        // `Core.Initialize(world)` call commented out). The live values are
        // populated on Plugin by InitializationPatch.cs:67. Same for the
        // entity manager.
        var character = Plugin.LocalCharacter;
        if (Plugin.IsClientNull() || character == Unity.Entities.Entity.Null)
        {
            HasShiftSpell = false;
            DiagLastReadSource = "no-char";
            return;
        }
        var em = Plugin.EntityManager;

        if (character != _latchedCharacter)
        {
            _latchedCharacter      = character;
            _latchedShiftPrefab    = default;
            _latchedCooldownEnd    = 0;
            _latchedCooldownTotal  = 0;
            _latchedCurrentCharges = 1;
            _latchedMaxCharges     = 1;
            ShiftIcon              = null;
            ShiftIconPrefabHash    = 0;
        }

        // ── Step 1: probe AbilityBar_Shared for cast state ──
        Unity.Entities.Entity castGroupEntity = Unity.Entities.Entity.Null;
        Unity.Entities.Entity castAbilityEntity = Unity.Entities.Entity.Null;
        Stunlock.Core.PrefabGUID castGroupPrefab = default;
        int castGroupSlotIndex = -1;

        if (em.HasComponent<ProjectM.AbilityBar_Shared>(character))
        {
            var bar = em.GetComponentData<ProjectM.AbilityBar_Shared>(character);
            castGroupEntity   = bar.CastGroup.GetEntityOnServer();
            castAbilityEntity = bar.CastAbility.GetEntityOnServer();

            if (castGroupEntity != Unity.Entities.Entity.Null)
            {
                if (em.HasComponent<ProjectM.AbilityGroupState>(castGroupEntity))
                    castGroupSlotIndex = em.GetComponentData<ProjectM.AbilityGroupState>(castGroupEntity).SlotIndex;
                if (em.HasComponent<Stunlock.Core.PrefabGUID>(castGroupEntity))
                    castGroupPrefab = em.GetComponentData<Stunlock.Core.PrefabGUID>(castGroupEntity);
            }
        }
        DiagCastGroupPrefabHash = castGroupPrefab.GuidHash;
        DiagCastGroupSlotIndex  = castGroupSlotIndex;

        // ── Step 2: latch the shift's prefab when we observe a slot-3 cast ──
        if (castGroupSlotIndex == 3 && castGroupPrefab.GuidHash != 0
            && !castGroupPrefab.Equals(_latchedShiftPrefab))
        {
            _latchedShiftPrefab   = castGroupPrefab;
            _latchedCooldownEnd   = 0;
            _latchedCooldownTotal = 0;
        }

        // Secondary latch path via slot buffer (vanilla case where the base
        // prefab IS the equipped one — for Bloodcraft overrides this is
        // expected to be empty and the slot-3 observation above will fill
        // _latchedShiftPrefab the moment the user casts shift).
        if (_latchedShiftPrefab.GuidHash == 0)
        {
            try
            {
                if (em.HasBuffer<ProjectM.AbilityGroupSlotBuffer>(character))
                {
                    var slots = em.GetBuffer<ProjectM.AbilityGroupSlotBuffer>(character);
                    if (slots.Length > 3)
                    {
                        var pf = slots[3].BaseAbilityGroupOnSlot;
                        if (pf.GuidHash != 0) _latchedShiftPrefab = pf;
                    }
                }
            }
            catch { /* fall through */ }
        }
        DiagShiftPrefabHash = _latchedShiftPrefab.GuidHash;

        // ── Step 3: detect "shift slot is equipped" via slot-entity presence ──
        // NOT via prefab non-zero, because Bloodcraft's ReplaceAbilityOnSlotBuff
        // leaves the base prefab empty even when the slot is fully usable.
        bool shiftSlotExists = false;
        Unity.Entities.Entity shiftSlotGroupEntity = Unity.Entities.Entity.Null;
        try
        {
            if (em.HasBuffer<ProjectM.AbilityGroupSlotBuffer>(character))
            {
                var slots = em.GetBuffer<ProjectM.AbilityGroupSlotBuffer>(character);
                if (slots.Length > 3)
                {
                    shiftSlotGroupEntity = slots[3].GroupSlotEntity.GetEntityOnServer();
                    shiftSlotExists = (shiftSlotGroupEntity != Unity.Entities.Entity.Null);
                }
            }
        }
        catch { /* fall through */ }

        HasShiftSpell = shiftSlotExists || _latchedShiftPrefab.GuidHash != 0;

        // ── 0.16.x: icon-on-load — latch from the LIVE slot-3 group entity ──
        // Friend-test: the icon only appeared after the first cast. Reason: for
        // Bloodcraft class-spell overrides the BASE slot prefab (read above) is
        // empty, so _latchedShiftPrefab stayed 0 until a slot-3 cast was observed.
        // The live GroupSlotEntity, however, reflects the overridden ability once
        // its replace-on-slot buff has applied at login — so reading ITS prefab
        // can resolve the icon before the first cast. Best-effort: if it doesn't
        // carry a usable prefab pre-cast, we fall through and resolve on cast as
        // before (harmless).
        DiagSlotGroupPrefabHash = 0;
        if (shiftSlotGroupEntity != Unity.Entities.Entity.Null)
        {
            try
            {
                if (em.HasComponent<Stunlock.Core.PrefabGUID>(shiftSlotGroupEntity))
                {
                    var slotPrefab = em.GetComponentData<Stunlock.Core.PrefabGUID>(shiftSlotGroupEntity);
                    DiagSlotGroupPrefabHash = slotPrefab.GuidHash;
                    if (_latchedShiftPrefab.GuidHash == 0 && slotPrefab.GuidHash != 0)
                        _latchedShiftPrefab = slotPrefab;   // resolves the icon pre-cast when available
                }
            }
            catch { /* fall through — icon resolves on first cast as before */ }
        }

        // ── Resolve the slotted spell's icon (Eclipse approach) ──
        // Read the managed AbilityTooltipData.Icon off the ability group entity.
        // Only (re)resolve when we don't already have the icon for the current
        // latched spell, so this is a no-op once cached.
        //
        // 0.16.1 crash-hardening: this managed read is the suspected source of the
        // v0.16.0 GC-finalizer crash on some servers. The icon has NO consumer other
        // than the Shift overlay, so there is no reason to touch managed components
        // unless that overlay is actually shown. Gate the whole thing on the overlay
        // being visible AND the icon enabled — `ShowShiftSpellIcon` is now a genuine
        // kill-switch (previously it only hid an already-resolved icon) — and skip
        // entirely once the circuit-breaker has latched off.
        bool wantIcon = !_iconResolutionDisabled
                        && Config.Settings.ShowShiftSpellOverlay
                        && Config.Settings.ShowShiftSpellIcon;
        if (!wantIcon || _latchedShiftPrefab.GuidHash == 0)
        {
            ShiftIcon = null;
            ShiftIconPrefabHash = 0;
        }
        else if (ShiftIcon == null || ShiftIconPrefabHash != _latchedShiftPrefab.GuidHash)
        {
            int want = _latchedShiftPrefab.GuidHash;
            // Resolution order:
            //   1) cast-group entity when a slot-3 cast is in flight (Eclipse's path),
            //   2) the persistent live slot entity,
            //   3) the ability-group PREFAB entity.
            // 0.16.x: friend-test diag showed the live slot entity knows the
            // prefab (`slot` non-zero) but carries NO managed AbilityTooltipData
            // (`ic 0`) — that component lives on the prefab TEMPLATE, not the live
            // slot instance. Reading it from the prefab entity is what resolves
            // the icon ON LOAD (before any cast).
            bool resolved = (castGroupSlotIndex == 3 && TryReadShiftIcon(em, castGroupEntity, want))
                            || TryReadShiftIcon(em, shiftSlotGroupEntity, want);
            if (!resolved)
                TryReadShiftIconFromPrefab(em, _latchedShiftPrefab);
        }

        // ── Step 4: refresh cooldown latches when the current cast IS shift ──
        if (HasShiftSpell
            && _latchedShiftPrefab.GuidHash != 0
            && castGroupPrefab.GuidHash != 0
            && castGroupPrefab.Equals(_latchedShiftPrefab)
            && castAbilityEntity != Unity.Entities.Entity.Null)
        {
            bool readOk = false;
            if (em.HasComponent<ProjectM.AbilityCooldownData>(castAbilityEntity))
            {
                float t = em.GetComponentData<ProjectM.AbilityCooldownData>(castAbilityEntity).Cooldown._Value;
                if (t > 0f) { _latchedCooldownTotal = t; readOk = true; }
            }
            if (em.HasComponent<ProjectM.AbilityCooldownState>(castAbilityEntity))
            {
                double end = em.GetComponentData<ProjectM.AbilityCooldownState>(castAbilityEntity).CooldownEndTime;
                if (end > _latchedCooldownEnd) { _latchedCooldownEnd = end; readOk = true; }
            }
            if (em.HasComponent<ProjectM.AbilityChargesState>(castGroupEntity))
            {
                _latchedCurrentCharges = em.GetComponentData<ProjectM.AbilityChargesState>(castGroupEntity).CurrentCharges;
            }
            if (em.HasComponent<ProjectM.AbilityChargesData>(castGroupEntity))
            {
                int max = em.GetComponentData<ProjectM.AbilityChargesData>(castGroupEntity).MaxCharges;
                if (max > 0) _latchedMaxCharges = max;
            }
            if (readOk)
            {
                DiagLastReadSource = "cast";
                DiagLastRefreshAt = UnityEngine.Time.realtimeSinceStartupAsDouble;
            }
            else
            {
                DiagLastReadSource = "match-noread";
            }
        }
        else
        {
            DiagLastReadSource = HasShiftSpell
                ? (_latchedShiftPrefab.GuidHash == 0 ? "no-prefab-yet" : "idle")
                : "no-slot";
        }

        // ── Step 4b (0.29): RECAST / persistent-cooldown fallback ──
        // The cast-time read in Step 4 only fires WHILE a cast is in flight (bar.CastAbility non-null).
        // Recast abilities (cast → the slot shows a "recast" icon → the REAL cooldown only starts when you
        // recast, or when the recast window expires) usually apply their cooldown at a moment when NO cast
        // is being observed — so the transient read never sees it and the overlay sticks on the recast icon
        // with no timer (tester report). The shift slot's GROUP entity PERSISTS and carries the group-level
        // cooldown/charge state, so read it every poll as a fallback. Fully guarded (HasComponent + try) and
        // additive: the cooldown end only moves FORWARD (same rule as Step 4), so it can't fight the
        // cast-time read or invent a phantom timer — when the recast window is still open the group's end
        // time is in the past (ready), which correctly shows no cooldown, exactly like vanilla.
        DiagSlotCooldownEnd = 0;
        if (shiftSlotGroupEntity != Unity.Entities.Entity.Null)
        {
            try
            {
                if (em.HasComponent<ProjectM.AbilityCooldownData>(shiftSlotGroupEntity))
                {
                    float t = em.GetComponentData<ProjectM.AbilityCooldownData>(shiftSlotGroupEntity).Cooldown._Value;
                    if (t > 0f) _latchedCooldownTotal = t;
                }
                if (em.HasComponent<ProjectM.AbilityCooldownState>(shiftSlotGroupEntity))
                {
                    double end = em.GetComponentData<ProjectM.AbilityCooldownState>(shiftSlotGroupEntity).CooldownEndTime;
                    DiagSlotCooldownEnd = end;
                    if (end > _latchedCooldownEnd) { _latchedCooldownEnd = end; DiagLastReadSource = "slot-cd"; }
                }
                if (em.HasComponent<ProjectM.AbilityChargesState>(shiftSlotGroupEntity))
                    _latchedCurrentCharges = em.GetComponentData<ProjectM.AbilityChargesState>(shiftSlotGroupEntity).CurrentCharges;
                if (em.HasComponent<ProjectM.AbilityChargesData>(shiftSlotGroupEntity))
                {
                    int max = em.GetComponentData<ProjectM.AbilityChargesData>(shiftSlotGroupEntity).MaxCharges;
                    if (max > 0) _latchedMaxCharges = max;
                }
            }
            catch { /* best-effort; the cast-time read remains the primary path */ }
        }

        // ── Step 5: compute display values from latched data + running clock ──
        double serverNow = GetServerTimeOnServer();
        DiagServerNow = serverNow;
        DiagLatchedEnd = _latchedCooldownEnd;

        float remaining = (float)(_latchedCooldownEnd - serverNow);
        if (remaining < 0f) remaining = 0f;
        CooldownRemaining = remaining;
        CooldownTotal     = _latchedCooldownTotal;
        CurrentCharges    = _latchedCurrentCharges;
        MaxCharges        = _latchedMaxCharges;
    }

    // Reads the managed AbilityTooltipData.Icon off an ability group entity, but
    // only trusts it when that entity actually IS the latched shift spell — the
    // base slot entity can hold a different/empty ability group than a Bloodcraft
    // override, so blindly trusting it would show the wrong icon. Returns true and
    // caches the sprite on success.
    private static bool TryReadShiftIcon(Unity.Entities.EntityManager em, Unity.Entities.Entity groupEntity, int wantHash)
    {
        if (groupEntity == Unity.Entities.Entity.Null) return false;
        try
        {
            // 0.16.1: never touch a stale/destroyed entity — a managed read off an
            // entity the engine has recycled is a prime finalizer-crash vector.
            if (!em.Exists(groupEntity)) return false;

            // Guard: reject an entity whose prefab is a *different* real ability
            // group. (hash 0 = empty base override — falls through to a null Icon
            // and returns false harmlessly below.)
            if (em.HasComponent<Stunlock.Core.PrefabGUID>(groupEntity))
            {
                int h = em.GetComponentData<Stunlock.Core.PrefabGUID>(groupEntity).GuidHash;
                if (h != 0 && h != wantHash) return false;
            }

            _abilityTooltipDataCT ??= Unity.Entities.ComponentType.ReadOnly(
                Il2CppInterop.Runtime.Il2CppType.Of<ProjectM.AbilityTooltipData>());
            if (!em.HasComponent(groupEntity, _abilityTooltipDataCT.Value)) return false;

            var ttd = em.GetComponentObject<ProjectM.AbilityTooltipData>(
                groupEntity, _abilityTooltipDataCT.Value);
            if (ttd != null && ttd.Icon != null)
            {
                ShiftIcon = ttd.Icon;
                ShiftIconPrefabHash = wantHash;
                return true;
            }
        }
        catch (Exception ex) { RecordIconFault(ex); }
        return false;
    }

    // 0.16.1: after repeated faults, permanently stop attempting icon resolution
    // for this session. The icon is a cosmetic nicety; the cooldown readout (the
    // overlay's real job) keeps working. Async finalizer crashes can't be caught
    // here, but this stops us re-entering a known-bad managed access every poll.
    private static void RecordIconFault(Exception ex)
    {
        if (_iconResolutionDisabled) return;
        if (++_iconResolutionFaults >= ICON_RESOLUTION_FAULT_LIMIT)
        {
            _iconResolutionDisabled = true;
            ShiftIcon = null;
            ShiftIconPrefabHash = 0;
            LogUtils.LogWarning(
                $"ShiftCooldownService: disabling slotted-spell icon resolution for this session " +
                $"after {_iconResolutionFaults} faults (last: {ex.Message}). Cooldown readout is unaffected.");
        }
    }

    // 0.16.x: resolve the icon from the ability-group PREFAB entity (via
    // PrefabCollectionSystem) when the live slot/cast entities don't carry the
    // managed AbilityTooltipData. Friend-test diag confirmed the live slot entity
    // knows the prefab but has no tooltip component — this prefab lookup is what
    // makes the icon appear ON LOAD for both vanilla shift and Bloodcraft
    // class-spell overrides.
    private static void TryReadShiftIconFromPrefab(Unity.Entities.EntityManager em, Stunlock.Core.PrefabGUID prefab)
    {
        if (prefab.GuidHash == 0) return;
        try
        {
            var world = Plugin.EntityManager.World;
            var prefabSys = world?.GetExistingSystemManaged<ProjectM.PrefabCollectionSystem>();
            if (prefabSys == null) return;
            if (prefabSys._PrefabGuidToEntityMap.TryGetValue(prefab, out var prefabEntity))
                TryReadShiftIcon(em, prefabEntity, prefab.GuidHash);
        }
        catch (Exception ex) { RecordIconFault(ex); }
    }

    private static double GetServerTimeOnServer()
    {
        // 0.11.5 fix: Core.ClientWorld was always null (Core.Initialize never
        // ran). Use Plugin's EntityManager.World instead — that's the one
        // populated by GameManagerPatch.
        if (Plugin.IsClientNull()) return UnityEngine.Time.timeAsDouble;
        var world = Plugin.EntityManager.World;
        if (world == null) return UnityEngine.Time.timeAsDouble;
        var mapper = world.GetExistingSystemManaged<ProjectM.Scripting.ClientScriptMapper>();
        if (mapper == null) return UnityEngine.Time.timeAsDouble;
        return mapper._ClientGameManager.ServerTime.TimeOnServer;
    }
}
