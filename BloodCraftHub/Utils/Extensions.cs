namespace BloodCraftHub.Utils;

// IL2CPP / Unity.Entities helpers used across services.
//
// PORT FROM: LearningMods/BloodCraftUI-master/BloodCraftUI/Utils/Extensions.cs
//
// Expected helpers (signatures, not bodies, listed here for reference):
//
//   public static T    Read<T>(this Entity entity) where T : struct
//   public static bool Has<T>(this Entity entity)  where T : struct
//   public static void Write<T>(this Entity entity, T component) where T : struct
//   public static bool Exists(this Entity entity)
//   public static bool TryGetBuffer<T>(this Entity entity, out DynamicBuffer<T> buffer)
//
// These wrap EntityManager calls with the static Core.EntityManager handle.
public static class Extensions
{
    // TODO: port from BloodCraftUI/Utils/Extensions.cs once VampireReferenceAssemblies
    // is restored and the Unity.Entities types resolve.
}
