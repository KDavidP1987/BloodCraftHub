namespace BloodCraftHub.Patches;

// Fires UI bring-up + EclipseProtocolService.Register once Core.LocalCharacter
// is resolved (i.e. the player has actually entered the world).
//
// PORT FROM: LearningMods/BloodCraftUI-master/BloodCraftUI/Patches/InitializationPatch.cs
//
// Hooks the system that runs after the local character entity is established —
// typically HUDActivateConsumableSystem or similar. See the BloodCraftUI port
// source for the specific target.
public static class InitializationPatch
{
    // [HarmonyPrefix]
    // public static void Prefix( /* target system */ )
    // {
    //     if (already-fired) return;
    //     Plugin.UIManager.SetupAndShowUI();
    //     EclipseProtocolService.Register();
    // }
}
