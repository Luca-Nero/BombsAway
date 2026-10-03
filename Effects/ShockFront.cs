using UnityEngine;

namespace BombsAway
{
    /// <summary>
    /// One blast's shock front, for the visual distortion (Shockwave.cs in the mod, ShockwaveKit in
    /// the Unity kit; this file is shared verbatim, edit the mod's copy and copy it over).
    ///
    /// The front runs at the shock speed its own overpressure gives it, c·√(1 + 6/7·Δp/p₀)
    /// (Rankine-Hugoniot, ideal air), with Δp from the Mills fit to Kingery-Bulmash that FruitLib's
    /// blast uses (FruitBlast.IncidentKPa). So it leaves the charge at kilometres a second and
    /// slows to the speed of sound as it weakens: a 3 kg warhead's front is 37 m out after 0.1 s,
    /// a Mk 84's 200 m after half a second. It shows while it is strong (the bend grows with the
    /// square root of the overpressure, full from 40 kPa) and fades out as it drops to
    /// <c>FadeKPa</c>, which for most charges is a few tenths of a second.
    ///
    /// Behind the front the air is compressed over the positive phase, modelled as a shell whose
    /// density falls linearly from the front to <see cref="Thickness"/> behind it. The shader
    /// bends each pixel's ray by how the density summed along it changes across the line of sight;
    /// <see cref="PeakBend"/> is that change's largest value for this shell, so the shader can
    /// scale it to a size in pixels.
    /// </summary>
    internal sealed class ShockFront
    {
        public const float SpeedOfSound = 343f;
        public const float AmbientKPa = 101.325f;
        /// <summary>Overpressure at which the bend is full, kPa.</summary>
        public const float FullKPa = 40f;
        /// <summary>Positive-phase length per kg^⅓ (metres): about 1 m behind a 3 kg front, 6 m behind a Mk 84's.</summary>
        public const float ThickPerKg = 0.6f;

        public Vector3 Center;
        /// <summary>The charge as the blast sees it (kg TNT, doubled-ish on the ground), and its cube root.</summary>
        public readonly float W, W3;
        public float Radius, Age;
        public readonly float FadeKPa;

        public ShockFront(Vector3 center, float kgTnt, float fadeKPa)
        {
            Center = center;
            W = Mathf.Max(1e-3f, kgTnt);
            W3 = Mathf.Pow(W, 1f / 3f);
            Radius = 0.5f * W3;
            FadeKPa = Mathf.Max(0.5f, fadeKPa);
        }

        /// <summary>Peak incident overpressure at scaled distance z (m/kg^⅓), kPa: Mills (1987), as FruitBlast.</summary>
        public static float IncidentKPa(float z)
        {
            z = Mathf.Max(0.05f, z);
            return Mathf.Clamp(1772f / (z * z * z) - 114f / (z * z) + 108f / z, 0f, 50000f);
        }

        /// <summary>The front's overpressure now, kPa.</summary>
        public float KPa => IncidentKPa(Radius / W3);

        /// <summary>The front's speed now, m/s.</summary>
        public float Speed => SpeedOfSound * Mathf.Sqrt(1f + 6f / 7f * KPa / AmbientKPa);

        /// <summary>Moves the front on by <paramref name="dt"/> seconds (in short steps near the charge, where it is fast).</summary>
        public void Step(float dt)
        {
            Age += dt;
            while (dt > 0f)
            {
                float h = Mathf.Min(dt, 0.002f);
                Radius += Speed * h;
                dt -= h;
            }
        }

        /// <summary>The positive phase behind the front, metres.</summary>
        public float Thickness => Mathf.Clamp(ThickPerKg * W3, 0.2f, 0.35f * Radius);

        /// <summary>How strongly it bends light now, 0 to 1: √ of the overpressure, faded out on the way down to FadeKPa.</summary>
        public float Strength
        {
            get
            {
                float p = KPa;
                float s = Mathf.Sqrt(Mathf.Clamp01(p / FullKPa));
                float f = Mathf.Clamp01((p - FadeKPa) / FadeKPa);
                return s * f * f * (3f - 2f * f);
            }
        }

        /// <summary>Weak enough to be gone.</summary>
        public bool Done => KPa <= FadeKPa;

        /// <summary>Density summed along a ray passing d from the centre (as the shader's Projected).</summary>
        public static float Projected(float d, float r, float l)
        {
            d = Mathf.Max(d, 1e-4f);
            if (d >= r) return 0f;
            float a = Mathf.Max(r - l, 0f);
            float hR = Mathf.Sqrt(r * r - d * d);
            float ha = Mathf.Sqrt(Mathf.Max(a * a - d * d, 0f));
            float A = Mathf.Max(a, d);
            float s = 0.5f * (hR * r - ha * A + d * d * Mathf.Log((hR + r) / (ha + A)));
            return 2f / l * (s - a * (hR - ha));
        }

        /// <summary>
        /// The bend (−dP/dd) a tenth of a thickness inside the rim, which the shader scales to 1.
        /// Right at the front it grows without limit (rays graze the jump in density), so the
        /// shader soft-clamps it at twice this; a thickness in it is about −0.35 (the inner band).
        /// </summary>
        public static float PeakBend(float r, float l)
        {
            float e = Mathf.Max(l * 0.02f, 1e-3f), d = r - 0.1f * l;
            return Mathf.Max(1e-4f, -(Projected(d + e, r, l) - Projected(d - e, r, l)) / (2f * e));
        }
    }
}
