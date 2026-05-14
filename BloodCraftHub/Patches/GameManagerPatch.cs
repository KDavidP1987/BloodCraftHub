namespace BloodCraftHub.Patches;

// Fires Core.Initialize(world) once when the client World becomes available,
// then unpatches itself so it doesn't fire again.
//
// PORT FROM: LearningMods/BloodCraftUI-master/BloodCraftUI/Patches/GameManagerPatch.cs
//
// [HarmonyPatch(typeof(GameManager), "OnUpdate")]
public static class GameManagerPatch
{
    // [HarmonyPostfix]
    // public static void Postfix(GameManager __instance) { Core.Initialize(__instance.World); /* unpatch */ }
}
