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
    public static double DiagServerNow;
    public static double DiagLatchedEnd;
    public static double DiagLastRefreshAt;
    public static int    DiagPollCount;
    public static string DiagLastReadSource = "init";

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
        try
        {
            if (em.HasBuffer<ProjectM.AbilityGroupSlotBuffer>(character))
            {
                var slots = em.GetBuffer<ProjectM.AbilityGroupSlotBuffer>(character);
                if (slots.Length > 3)
                {
                    var slotEntity = slots[3].GroupSlotEntity.GetEntityOnServer();
                    shiftSlotExists = (slotEntity != Unity.Entities.Entity.Null);
                }
            }
        }
        catch { /* fall through */ }

        HasShiftSpell = shiftSlotExists || _latchedShiftPrefab.GuidHash != 0;

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
