using UnityEngine;
using Vector3 = UnityEngine.Vector3;

namespace BombsAway
{
    /// <summary>
    /// The air's drift, for whatever hangs in it (the smoke, for now). Not a simulation: one
    /// horizontal wind for the whole map, its heading wandering slowly (up to about 50 degrees
    /// either way) round a base picked per scene, its speed gusting round Config.WindSpeed.
    /// Meant to move into FruitLib as a shared wind once something else wants one.
    /// </summary>
    internal static class Breeze
    {
        private static float _base = float.NaN;   // heading in degrees from +Z, picked per scene
        private static float _phase;

        /// <summary>The wind right now, metres per second, horizontal.</summary>
        public static Vector3 Velocity { get; private set; }

        public static void Tick()
        {
            if (float.IsNaN(_base))
            {
                _base = Config.WindHeading >= 0f ? Config.WindHeading : Random.Range(0f, 360f);
                _phase = Random.Range(0f, 100f);
            }
            float t = Time.time + _phase;
            float heading = _base + 50f * Wander(t * 0.013f);
            float speed = Mathf.Max(0f, Config.WindSpeed) * (1f + 0.3f * Wander(t * 0.11f + 17f));
            float rad = heading * Mathf.Deg2Rad;
            Velocity = new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad)) * speed;
        }

        /// <summary>A new scene: a new prevailing wind.</summary>
        public static void OnScene() => _base = float.NaN;

        /// <summary>Smooth -1..1, never repeating in practice: three detuned sines.</summary>
        private static float Wander(float t)
            => (Mathf.Sin(t * 1.7f) + 0.6f * Mathf.Sin(t * 2.9f + 1.3f) + 0.8f * Mathf.Sin(t * 0.7f + 2.1f)) / 2.4f;
    }
}
