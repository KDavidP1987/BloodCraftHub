using HarmonyLib;
using ProjectM;
using TMPro;
using UnityEngine.EventSystems;

namespace BloodCraftHub.Patches;

// Suspend V Rising's game input while the player is typing into one of our UI
// fields. Without this, WASD typed into a player-name field also moves the
// character, attacks fire on right-click submit, etc.
//
// Implementation: prefix-patch InputActionSystem.OnUpdate and skip the original
// when the Unity EventSystem has a TMP_InputField as its currently-selected
// GameObject. This mirrors how V Rising's own chat input pauses gameplay
// keybinds when the chat box is open.
//
// Caveats / future work:
//   - Also triggers when V Rising's own chat is focused; the game probably
//     already gates its input there, so we're just being redundant - harmless.
//   - We rely on the field being properly de-selected when the user clicks
//     away. TMP_InputField does this on click-elsewhere/Escape by default.
//   - If a more surgical patch is needed (e.g. block movement only, allow
//     ability casts), the right hook is the specific gameplay system reading
//     the input action - we'll add that targeted patch if this proves too
//     coarse.
[HarmonyPatch]
internal static class InputActionSystemPatch
{
    [HarmonyPatch(typeof(InputActionSystem), nameof(InputActionSystem.OnUpdate))]
    [HarmonyPrefix]
    private static bool OnUpdate_Prefix()
    {
        var es = EventSystem.current;
        if (es == null) return true;

        var sel = es.currentSelectedGameObject;
        if (sel == null) return true;

        // Any focused text input suspends gameplay input.
        if (sel.GetComponent<TMP_InputField>() != null)
            return false;

        return true;
    }
}
