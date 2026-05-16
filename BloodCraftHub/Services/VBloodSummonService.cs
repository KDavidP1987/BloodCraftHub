using System;
using BloodCraftHub.Utils;

namespace BloodCraftHub.Services;

// 0.10.5: shared smart-summon service. UI surfaces call into this so the
// unbind→cb→bind chain stays in one place.
//
// 0.10.9 rewrite to support per-variant targeting. The post-0.10.9
// scanner snapshots EVERY captured V-Blood instance with its precise
// (box, index, isShiny, shinySchool, isPrimal) tuple, so the summon path
// no longer needs to guess via "first match by name" — callers pass the
// variant flags explicitly and we resolve the exact entry.
//
// Two public entry points:
//   - SummonVariant(name, isShiny, isPrimal): the canonical path used
//     by the per-variant chip view. Refuses with a status message if a
//     V-Blood scan is currently running (we don't want a Summon to race
//     a scan that's mid-iteration of `.fam cb` / `.fam l`).
//   - SummonVBlood(name): name-only fallback for the Familiar Browser
//     overlay's quick-summon path. Picks whichever variant we have
//     (basic preferred, then shiny, then primal, then primal-shiny) so
//     a one-click summon stays sensible when the user doesn't care.
//
// Summon flow (per variant target):
//   1. Refuse if a scan is running.
//   2. Find the matching VBloodInstance in the collection.
//   3. Unbind any active familiar (only when fam.HasActive is true; see
//      0.10.8 fix in PlayerStateService for why this matters).
//   4. If the instance's box differs from PlayerStateService.ActiveBox,
//      call SetActiveBox and send `.fam cb <box>` so any subsequent
//      FlushBoxContent keys correctly.
//   5. Send `.fam b <index>` immediately — we already know the exact
//      slot from the scan, no need to fetch box contents first.
public static class VBloodSummonService
{
    public static event Action<string> StatusChanged;
    public static string LastStatus { get; private set; } = "";

    /// <summary>0.10.9: target the specific captured variant of <paramref
    /// name="name"/>. The scan provides exact box+index per variant, so
    /// no .fam l round-trip is needed.</summary>
    public static void SummonVariant(string name, bool isShiny, bool isPrimal)
    {
        if (string.IsNullOrEmpty(name)) return;
        if (VBloodScannerService.Scanning)
        {
            SetStatus("Scan in progress — wait for it to finish before summoning.");
            return;
        }
        if (!MessageService.IsInitialized)
        {
            SetStatus("Can't summon — character not yet bound. Try again in a moment.");
            return;
        }
        if (!PlayerStateService.VBloodCollection.TryGetValue(name, out var slot))
        {
            SetStatus($"{name}: not in collection. Run a scan first.");
            return;
        }
        if (!slot.TryGetVariant(isShiny, isPrimal, out var instance))
        {
            SetStatus($"{name}: this variant isn't in your collection. Run a scan first.");
            return;
        }

        IssueSummon(slot, instance, VariantLabel(isShiny, isPrimal));
    }

    /// <summary>Name-only summon. Picks the first available variant in
    /// preference order basic → shiny → primal → primal-shiny so the
    /// quick-summon path in the Familiar Browser overlay still does
    /// something sensible when the caller doesn't pick a variant.</summary>
    public static void SummonVBlood(string name)
    {
        if (string.IsNullOrEmpty(name)) return;
        if (VBloodScannerService.Scanning)
        {
            SetStatus("Scan in progress — wait for it to finish before summoning.");
            return;
        }
        if (!PlayerStateService.VBloodCollection.TryGetValue(name, out var slot)
            || slot.Instances == null || slot.Instances.Count == 0)
        {
            SetStatus($"{name}: not in collection. Run a scan first.");
            return;
        }

        // Preference order matches the chip-view convention: basic first.
        if (slot.TryGetVariant(isShiny: false, isPrimal: false, out var inst)
         || slot.TryGetVariant(isShiny: true,  isPrimal: false, out inst)
         || slot.TryGetVariant(isShiny: false, isPrimal: true,  out inst)
         || slot.TryGetVariant(isShiny: true,  isPrimal: true,  out inst))
        {
            IssueSummon(slot, inst,
                inst.IsPrimal
                    ? (inst.IsShiny ? "Primal Shiny" : "Primal")
                    : (inst.IsShiny ? "Shiny" : "Basic"));
            return;
        }

        SetStatus($"{name}: no resolvable variant in collection. Run a scan first.");
    }

    private static void IssueSummon(
        PlayerStateService.VBloodCaptureStatus slot,
        PlayerStateService.VBloodInstance instance,
        string variantLabel)
    {
        if (string.IsNullOrEmpty(instance.Box) || instance.Index <= 0)
        {
            SetStatus($"{slot.Name}: variant has no resolvable box/index. Re-scan recommended.");
            return;
        }

        // Unbind any active familiar — but ONLY if one is actually bound.
        // See 0.10.8 PlayerStateService.FamiliarState.HasActive comment for
        // background on why the Name/Level check is wrong.
        var fam = PlayerStateService.Familiar;
        if (fam.HasActive)
            MessageService.EnqueueMessage(MessageService.BCCOM_FAM_UNBIND);

        // Align ActiveBox + send .fam cb if needed. The bind index is exact
        // — we don't need to round-trip a `.fam l` first.
        bool sameBox = !string.IsNullOrEmpty(PlayerStateService.ActiveBox)
                    && string.Equals(instance.Box, PlayerStateService.ActiveBox, StringComparison.OrdinalIgnoreCase);
        if (!sameBox)
        {
            PlayerStateService.SetActiveBox(instance.Box);
            MessageService.EnqueueMessage(string.Format(MessageService.BCCOM_FAM_SWITCH_BOX_FORMAT, instance.Box));
        }
        MessageService.EnqueueMessage(string.Format(MessageService.BCCOM_FAM_BIND_BY_INDEX_FORMAT, instance.Index));

        string shinyHint = !string.IsNullOrEmpty(instance.ShinySchool) ? $" {instance.ShinySchool}" : "";
        SetStatus($"Summoning {variantLabel}{shinyHint} {slot.Name} from {instance.Box} (slot {instance.Index})…");
    }

    private static string VariantLabel(bool isShiny, bool isPrimal)
        => isPrimal
            ? (isShiny ? "Primal Shiny" : "Primal")
            : (isShiny ? "Shiny" : "Basic");

    private static void SetStatus(string text)
    {
        LastStatus = text ?? "";
        try { StatusChanged?.Invoke(LastStatus); }
        catch (Exception ex) { LogUtils.LogError($"VBloodSummonService StatusChanged subscriber threw: {ex}"); }
    }
}
