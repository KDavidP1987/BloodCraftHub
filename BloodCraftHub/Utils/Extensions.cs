using System;
using System.Runtime.InteropServices;
using Il2CppInterop.Runtime;
using Unity.Entities;
using UnityEngine;

namespace BloodCraftHub.Utils;

// IL2CPP / Unity.Entities helpers + small UI helpers.
//
// Ported from LearningMods/BloodCraftUI-master/BloodCraftUI/Utils/Extensions.cs,
// trimmed to what BloodCraftHub actually uses. Add more from the upstream file
// only when a call site needs them - the full upstream pulls in ProjectM /
// Stunlock types we don't yet reference.
public static class Extensions
{
    static EntityManager EntityManager => Plugin.EntityManager;

    // ---------- UI helpers ----------

    /// <summary>Return a copy of <paramref name="baseColor"/> with its alpha replaced.</summary>
    public static Color GetTransparent(this Color baseColor, float alpha = 0.7f)
    {
        return new Color(baseColor.r, baseColor.g, baseColor.b, alpha);
    }

    // ---------- Entity component access ----------

    /// <summary>True iff <paramref name="entity"/> carries component <typeparamref name="T"/>.</summary>
    public static bool Has<T>(this Entity entity)
    {
        return EntityManager.HasComponent(entity, new ComponentType(Il2CppType.Of<T>()));
    }

    /// <summary>Read component <typeparamref name="T"/> from <paramref name="entity"/> via raw pointer marshal.</summary>
    public static unsafe T Read<T>(this Entity entity) where T : struct
    {
        var componentType = new ComponentType(Il2CppType.Of<T>());
        TypeIndex typeIndex = componentType.TypeIndex;
        void* componentData = EntityManager.GetComponentDataRawRO(entity, typeIndex);
        return Marshal.PtrToStructure<T>(new IntPtr(componentData));
    }

    /// <summary>Write component <paramref name="componentData"/> back to <paramref name="entity"/>.</summary>
    public static unsafe void Write<T>(this Entity entity, T componentData) where T : struct
    {
        var componentType = new ComponentType(Il2CppType.Of<T>());
        TypeIndex typeIndex = componentType.TypeIndex;

        byte[] bytes = StructureToByteArray(componentData);
        int size = Marshal.SizeOf<T>();

        fixed (byte* p = bytes)
        {
            EntityManager.SetComponentDataRaw(entity, typeIndex, p, size);
        }
    }

    /// <summary>True iff <paramref name="entity"/> is not Entity.Null.</summary>
    public static bool HasValue(this Entity entity) => entity != Entity.Null;

    private static byte[] StructureToByteArray<T>(T structure) where T : struct
    {
        int size = Marshal.SizeOf(structure);
        byte[] bytes = new byte[size];

        IntPtr ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(structure, ptr, true);
            Marshal.Copy(ptr, bytes, 0, size);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
        return bytes;
    }
}
