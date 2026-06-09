namespace BloodCraftHub.Patches;

// INTENTIONALLY EMPTY — do NOT patch EscapeMenuView.OnDestroy.
//
// History: the ancestor mod (LearningMods/BloodCraftUI-master) hooked
// EscapeMenuView.OnDestroy to reset its UI when the player left the game. We ported
// that in 0.18.1 to hide BCH's overlays on the main menu — and it RE-INTRODUCED the
// logout "exit to desktop" crash. Confirmed by elimination: even a no-op prefix that
// only set a flag still crashed. Running ANY BCH managed code through the Harmony
// detour on EscapeMenuView.OnDestroy *during the world/HUD teardown* tips a native
// Il2CppInterop fault, which a managed try/catch cannot catch and which deferring the
// work doesn't avoid (the detour itself fires during teardown).
//
// So: leave the teardown completely untouched. Logout returns to the main menu cleanly.
// The "overlays linger over the main menu" cleanup must be driven from a SAFE place
// (e.g. an in-game heartbeat polled on CoreUpdateBehavior: hide BCH's UI when the
// client stops ticking, show it when it resumes) — never from a teardown hook.
public static class EscapeMenuPatch
{
}
