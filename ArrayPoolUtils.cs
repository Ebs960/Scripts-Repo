using System;
using System.Buffers;

/// <summary>
/// Small wrapper around ArrayPool to centralize renting/returning and zeroing behavior.
/// </summary>
public static class ArrayPoolUtils
{
    public static T[] Rent<T>(int length, bool clear = true)
    {
        var arr = ArrayPool<T>.Shared.Rent(length);
        if (clear)
        {
            Array.Clear(arr, 0, length);
        }
        return arr;
    }

    public static void Return<T>(T[] arr, bool clear = false)
    {
        if (arr == null) return;
        if (clear)
        {
            Array.Clear(arr, 0, arr.Length);
        }
        ArrayPool<T>.Shared.Return(arr);
    }

    // Convenience typed aliases for readability
    public static float[] RentFloat(int len) => Rent<float>(len, true);
    public static void ReturnFloat(float[] a, bool clear = false) => Return<float>(a, clear);

    public static int[] RentInt(int len) => Rent<int>(len, true);
    public static void ReturnInt(int[] a, bool clear = false) => Return<int>(a, clear);

    public static bool[] RentBool(int len) => Rent<bool>(len, true);
    public static void ReturnBool(bool[] a, bool clear = false) => Return<bool>(a, clear);

    public static System.Numerics.Vector2[] RentVec2Num(int len) => Rent<System.Numerics.Vector2>(len, true);
}
