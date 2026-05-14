namespace BloodCraftHub.Services;

// In-place HUD overlay manager — Eclipse's UI approach.
//
// PORT FROM: LearningMods/Eclipse-main/Services/CanvasService.cs
//
// Owns references to the game's UICanvasBase and the overlay GameObjects under it
// (experience bar, legacy bar, expertise bar, shift slot indicator, etc.). Each
// overlay is updated from PlayerStateService.
//
// Use this when you want the data shown on top of the existing game HUD rather
// than inside a draggable mod panel. Panels and overlays can coexist.
public static class CanvasService
{
    // public static void Attach(UICanvasBase canvas) { ... }
    // public static void Update() { ... }   // wired into CoreUpdateBehavior
    // public static void Detach() { ... }
}
