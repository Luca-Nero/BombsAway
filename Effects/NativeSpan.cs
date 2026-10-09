using System;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace BombsAway
{
    /// <summary>
    /// An IL2CPP array's elements as a span over its own memory. Its indexer looks the array up
    /// again on every element (three native calls), and Il2CppInterop's own AsSpan returns .NET
    /// 6's Span, which this netstandard build can't name. IL2CPP's collector doesn't move
    /// objects, so the span holds while the array is kept; take a fresh one each use anyway.
    /// </summary>
    internal static unsafe class NativeSpan
    {
        // Il2CppArray's header before the elements: klass, monitor, bounds, max_length
        // (Il2CppInterop's ArrayStartPointer adds the same).
        private static readonly int Header = 4 * IntPtr.Size;

        public static Span<T> Of<T>(Il2CppStructArray<T> a, int length) where T : unmanaged
            => new Span<T>((byte*)a.Pointer + Header, length);
    }
}
