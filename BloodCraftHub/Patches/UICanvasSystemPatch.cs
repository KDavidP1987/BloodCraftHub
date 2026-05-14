namespace BloodCraftHub.Patches;

// Attaches our canvases (panel + Eclipse in-place HUD) once the game's
// UICanvasBase is constructed.
//
// PORT FROM:
//   LearningMods/BloodCraftUI-master/BloodCraftUI/Patches/UICanvasSystemPatch.cs (panel canvas)
//   LearningMods/Eclipse-main/Patches/HybridPatch.cs or similar                  (HUD canvas via Core.SetCanvas)
public static class UICanvasSystemPatch
{
}
