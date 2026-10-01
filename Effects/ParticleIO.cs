using System;
using System.Reflection;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using MelonLoader;
using UnityEngine;

namespace BombsAway
{
    /// <summary>
    /// ParticleSystem.GetParticles / SetParticles that work.
    ///
    /// Unity declares the array parameter of both as <c>[Out] Particle[]</c>. Il2CppInterop
    /// reads the [Out] attribute as a C# <c>out</c> parameter: its generated wrapper hands the
    /// game a null array (our array is never passed), then writes the "result" pointer into
    /// our array object. The game throws on the null, and Il2CppInterop crashes the process
    /// while formatting that exception (an AccessViolation in il2cpp_format_exception), so not
    /// even a try/catch helps.
    ///
    /// This calls the same game methods through il2cpp_runtime_invoke with the array passed
    /// as it should be. A failure is logged once, without formatting the exception, and the
    /// calls return 0 / do nothing from then on.
    /// </summary>
    internal static unsafe class ParticleIO
    {
        private static IntPtr _get, _set;
        private static bool _resolved, _broken;

        /// <summary>False once the calls are missing or have failed: callers should stop feeding the system.</summary>
        public static bool Ok => Ready();

        /// <summary>Copies up to <paramref name="size"/> live particles into <paramref name="arr"/>; how many it copied.</summary>
        public static int Get(ParticleSystem ps, Il2CppStructArray<ParticleSystem.Particle> arr, int size)
        {
            if (!Ready() || ps == null || arr == null || size <= 0) return 0;
            size = Math.Min(size, arr.Length);
            void** args = stackalloc void*[2];
            args[0] = (void*)arr.Pointer;
            args[1] = &size;
            IntPtr exc = IntPtr.Zero;
            IntPtr ret = IL2CPP.il2cpp_runtime_invoke(_get, ps.Pointer, args, ref exc);
            if (exc != IntPtr.Zero || ret == IntPtr.Zero) { Fail("GetParticles"); return 0; }
            return *(int*)IL2CPP.il2cpp_object_unbox(ret);
        }

        /// <summary>
        /// Makes the system hold exactly the first <paramref name="size"/> particles of
        /// <paramref name="arr"/> (adding or removing particles as needed, up to maxParticles).
        /// </summary>
        public static void Set(ParticleSystem ps, Il2CppStructArray<ParticleSystem.Particle> arr, int size)
        {
            if (!Ready() || ps == null || arr == null || size < 0) return;
            size = Math.Min(size, arr.Length);
            void** args = stackalloc void*[2];
            args[0] = (void*)arr.Pointer;
            args[1] = &size;
            IntPtr exc = IntPtr.Zero;
            IL2CPP.il2cpp_runtime_invoke(_set, ps.Pointer, args, ref exc);
            if (exc != IntPtr.Zero) Fail("SetParticles");
        }

        private static bool Ready()
        {
            if (_broken) return false;
            if (_resolved) return true;
            _resolved = true;
            // The method infos the generated wrappers use: (Particle[], int) overloads.
            const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static;
            var t = typeof(ParticleSystem);
            _get = (IntPtr)(t.GetField("NativeMethodInfoPtr_GetParticles_Public_Int32_Il2CppStructArray_1_Particle_Int32_0", F)?.GetValue(null) ?? IntPtr.Zero);
            _set = (IntPtr)(t.GetField("NativeMethodInfoPtr_SetParticles_Public_Void_Il2CppStructArray_1_Particle_Int32_0", F)?.GetValue(null) ?? IntPtr.Zero);
            if (_get == IntPtr.Zero || _set == IntPtr.Zero)
            {
                _broken = true;
                MelonLogger.Warning("[ParticleIO] GetParticles/SetParticles not found in the interop assembly; smoke puffs won't be drawn.");
            }
            return !_broken;
        }

        private static void Fail(string what)
        {
            _broken = true;
            // Not formatted: formatting an IL2CPP exception is what crashes.
            MelonLogger.Warning($"[ParticleIO] {what} raised an IL2CPP exception; particle writes are off until restart.");
        }
    }
}
