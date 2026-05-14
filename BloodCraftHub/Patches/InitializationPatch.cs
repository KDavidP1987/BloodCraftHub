using System;
using BloodCraftHub.Utils;
using HarmonyLib;
using ProjectM;
using ProjectM.Network;
using ProjectM.UI;
using Unity.Collections;
using Unity.Entities;

namespace BloodCraftHub.Patches;

// Fires UI bring-up once the player's character HUD is alive, and feeds
// LocalCharacter / LocalUser to the rest of the mod as soon as the client
// data system surfaces them.
//
// PORT REFERENCE: LearningMods/BloodCraftUI-master/BloodCraftUI/Patches/InitializationPatch.cs
[HarmonyPatch]
public static class InitializationPatch
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(CharacterHUDEntry), nameof(CharacterHUDEntry.Awake))]
    private static void CharacterHUDEntry_Awake_Postfix()
    {
        try
        {
            if (Plugin.UIManager == null || Plugin.UIManager.IsInitialized) return;
            LogUtils.LogInfo("Creating BloodCraftHub UI...");
            Plugin.UIOnInitialize();
        }
        catch (Exception ex)
        {
            LogUtils.LogError($"InitializationPatch failed: {ex}");
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(CommonClientDataSystem), nameof(CommonClientDataSystem.OnUpdate))]
    private static void CommonClientDataSystem_OnUpdate_Postfix(CommonClientDataSystem __instance)
    {
        if (Plugin.UIManager == null || !Plugin.UIManager.IsInitialized) return;
        Plugin.GameDataOnInitialize(__instance.World);

        // Try to capture LocalUser entity from the first matching query.
        var entities = __instance.__query_1840110770_0.ToEntityArray(Allocator.Temp);
        try
        {
            foreach (var e in entities)
            {
                if (e.Has<LocalUser>()) { /* Phase 3: MessageService.SetUser(e); */ break; }
            }
        }
        finally { entities.Dispose(); }

        // Try to capture LocalCharacter entity from the second matching query.
        entities = __instance.__query_1840110770_1.ToEntityArray(Allocator.Temp);
        try
        {
            foreach (var e in entities)
            {
                if (e.Has<LocalCharacter>())
                {
                    Plugin.LocalCharacter = e;
                    // Phase 3: MessageService.SetCharacter(e);
                    break;
                }
            }
        }
        finally { entities.Dispose(); }
    }
}
