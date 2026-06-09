using System;
using BloodCraftHub.Utils;
using ProjectM;
using ProjectM.UI;
using Unity.Entities;

namespace BloodCraftHub.Patches;

// 0.25.x: THE keyboard lock while typing — done with the game's OWN building blocks.
//
// Discovery (reflection over the game's interop assemblies): the game's entire input
// pipeline is an ORDERED STACK of ProjectM.IInputContext consumers dispatched by
// InputActionSystem each frame (InputContextOrder: ChatInput=99 … MenuInput=600 …
// ActionBar=900 … Gameplay=1001). Each context gets HandleInput(InputState) and
// reports GetConsumedInputs(ref BlockInputState); actions consumed by a HIGHER
// context are filtered from every context BELOW it. When you type in the NATIVE
// chat, ClientChatSystem adds its nested ChatFocusedInputContext at order 99 — which
// is the real reason no menu, ability, or keybind fires while native chat has focus.
//
// HOW WE JOIN THE STACK (0.25.0 — third attempt within the cycle, the one that works):
// we register an instance of the game's own ClientChatSystem.ChatFocusedInputContext
// (public parameterless ctor, stateless, consumes the native-chat blocking set
// unconditionally) at order 100 while BCH should block, and REMOVE it when blocking
// ends — exactly how the native chat itself gates the context (add on focus, remove
// on defocus). Both calls go through the public AddInputContext/RemoveInputContext
// API. The dispatcher only ever calls NATIVE code; BCH supplies no callbacks at all.
//
// ATTEMPT HISTORY (read before "improving" this):
//   - attempt 1: injected a managed IInputContext implementation (ClassInjector with
//     RegisterTypeOptions.Interfaces) and looked InputActionSystem up via
//     world.GetExistingSystemManaged on the BCH-bound client world. That lookup
//     returns NULL (the system lives in a different world) and the null branch was
//     the one retry path with no log line — so the context never registered and the
//     feature silently did nothing (the 0.25.0 in-game test ran on the legacy
//     protections only; BepInEx log had zero [TypingLock] lines).
//   - attempt 2: fixed acquisition (capture from GameplayInputSystem's injected
//     _InputActionSystem reference — see CaptureFrom) and the context registered…
//     and the game then NATIVE-CRASHED on the first dispatch into the injected
//     object, with nothing in the BepInEx log (crash before the managed bodies ran —
//     both were try/catch-wrapped and self-gating). The interface's callback
//     signatures take a by-value InputState struct and a by-ref BlockInputState
//     struct; Il2CppInterop's IsTypeSupported ACCEPTS that shape, but the generated
//     native→managed trampoline does not survive the actual call. LESSON: signature
//     acceptance is not marshaling correctness — never hand the game a vtable that
//     calls back into managed code with struct parameters; register a game-
//     implemented object instead and keep our logic on OUR side of the boundary.
//
// CRASH-SAFETY: no Harmony detour anywhere (the 0.16.x crash family came from
// detouring hot menu ECS systems), no ClassInjector, no managed callbacks. Add and
// Remove run from the CoreUpdateBehavior tick (MonoBehaviour Update phase — the same
// place the native chat mutates the stack from its own update), every call is
// exception-wrapped with a short retry, and the context object is the game's own.
// Gated by the EnableInputSuppressionPatches master switch plus the
// EnableNativeTypingLock kill-switch (both default ON); flipping either off while
// registered removes the context on the next tick.
//
// The blocked set is EXACTLY the native chat's: every game action — menus, wheels,
// abilities, action bar, building, inventory item actions, admin auth, Enter —
// except the Menu_* UI-navigation range, so the mouse and BCH's own UI stay live.
// On world teardown (logout / server switch) the game destroys InputActionSystem
// and the registration with it; OnWorldTeardown() drops our refs so the next world
// re-captures and re-adds fresh (never touch objects from a disposed world).
internal static class TypingInputLock
{
    // The LIVE InputActionSystem + its world, captured from GameplayInputSystem's
    // injected _InputActionSystem reference (right instance, right world, no lookup —
    // see the 0.25.0 entry in the attempt history above for why we never use
    // GetExistingSystemManaged for this).
    private static InputActionSystem _capturedIas;
    private static World _capturedWorld;

    // The game's own chat-typing context object (stateless; consumes the native-chat
    // blocking set whenever it's in the stack). _ctx is the same object cast to the
    // interface for Add/Remove/IsRegistered; keep both refs so the wrapper stays alive.
    private static ClientChatSystem.ChatFocusedInputContext _ctxObj;
    private static IInputContext _ctx;
    private static bool _registered;     // we believe our context is in the CURRENT world's stack
    private static bool _announcedOnce;  // first successful add per session logs at Info
    private static double _retryAt;

    // Called every frame from GameplayInputSuppressionPatch.Prefix (cheap: one null
    // check once captured). Every game input consumer carries an injected
    // _InputActionSystem reference; taking it from a system we already detour gives us
    // the right instance in the right world with zero guessing. Stashing fields only —
    // the Add/Remove work stays on the CoreUpdateBehavior tick, out of the hot prefix.
    internal static void CaptureFrom(GameplayInputSystem gameplayInput)
    {
        if (_capturedIas != null || gameplayInput == null) return;
        try
        {
            var ias = gameplayInput._InputActionSystem;
            if (ias == null) return; // injected ref not populated yet — next frame
            _capturedIas = ias;
            _capturedWorld = ias.World;
            LogUtils.LogDiagnostic("[TypingLock] InputActionSystem captured from GameplayInputSystem.");
        }
        catch { /* never break the prefix; retry next frame */ }
    }

    // Per-frame (CoreUpdateBehavior): reconcile "should the lock be in the stack?"
    // with "is it?". Idle cost is a few bool checks; Add/Remove only run on a state
    // TRANSITION (start/stop typing, open/close panel with suppression on).
    internal static void Tick()
    {
        try
        {
            var ias = _capturedIas;
            if (ias == null) return; // GameplayInputSystem prefix hasn't seen the injected ref yet
            if (UnityEngine.Time.realtimeSinceStartupAsDouble < _retryAt) return;

            bool desired = Config.Settings.EnableInputSuppressionPatches
                        && Config.Settings.EnableNativeTypingLock
                        && InputSuppression.ShouldBlockMenus();
            if (desired == _registered) return;

            var world = _capturedWorld;
            if (world == null || !world.IsCreated)
            {
                // Stale capture from a dying world — drop everything; the new world's
                // GameplayInputSystem prefix re-captures and we re-add from scratch.
                OnWorldTeardown();
                return;
            }

            if (desired)
            {
                // Order 100 = just below the native chat's own ChatFocusedInputContext
                // (99), above every menu (500-600), wheel (700), action bar (900),
                // camera (1000) and gameplay (1001) context — they all see our keys as
                // consumed. Distinct instance, so we never collide with the native
                // chat's own registration of the same class at 99.
                _ctxObj ??= new ClientChatSystem.ChatFocusedInputContext();
                _ctx ??= _ctxObj.Cast<IInputContext>();
                if (!ias.IsContextRegistered(_ctx))
                    ias.AddInputContext(_ctx, world, (int)InputContextOrder.ChatInput + 1);
                _registered = true;
                if (!_announcedOnce)
                {
                    _announcedOnce = true;
                    LogUtils.LogInfo("[TypingLock] Native input context added (order 100) — game keybinds are consumed at the source while typing in BCH fields.");
                }
                else LogUtils.LogDiagnostic("[TypingLock] context added.");
            }
            else
            {
                if (_ctx != null && ias.IsContextRegistered(_ctx))
                    ias.RemoveInputContext(_ctx);
                _registered = false;
                LogUtils.LogDiagnostic("[TypingLock] context removed.");
            }
        }
        catch (Exception ex)
        {
            // Transient (e.g. the stack briefly denies modifications) — retry shortly.
            // The legacy drain + movement/ability skips still protect in the meantime,
            // and a failed REMOVE keeps retrying so the keyboard can never stay locked.
            LogUtils.LogDiagnostic($"[TypingLock] add/remove failed (will retry): {ex.Message}");
            _retryAt = UnityEngine.Time.realtimeSinceStartupAsDouble + 0.5;
        }
    }

    // Called from the ClientBootstrapSystem.OnDestroy teardown hook (logout / server
    // switch). PURE field resets — the dying world's InputActionSystem takes the old
    // registration down with it; we must not call into it.
    internal static void OnWorldTeardown()
    {
        _capturedIas = null;
        _capturedWorld = null;
        _ctxObj = null;
        _ctx = null;
        _registered = false;
        _retryAt = 0;
    }
}
