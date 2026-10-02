using MelonLoader;
using UnityEngine;
using Vector3 = UnityEngine.Vector3;

namespace BombsAway
{
    // ══════════════════════════════════════════════════════════════════════════════
    // The binoculars: a laser rangefinder binocular, the platform for fire support.
    // Right mouse brings them to the eyes (BinocularView: zoom, slowed look, LrfOverlay).
    // Holding left mouse there lases what is under the reticle; held on it for LaseTime (as
    // the Javelin's lock), the fix is made and the mission called on it (FireMission). The
    // lase restarts if the point under the reticle jumps (LaseTolerance of the range, at least
    // 2 m) and stops at nothing ("NO RETURN"). The warhead key picks the mission type.
    // ══════════════════════════════════════════════════════════════════════════════

    public partial class Core
    {
        private static float _laseProgress;
        private static bool _laseAnchored, _laseNoReturn, _laseLatched;
        private static Vector3 _laseAnchor;
        private static float _laseTick;
        private static bool _lrfReading;
        private static float _lrfRange, _lrfAz, _lrfEl;

        private static void TickBinoculars(float dt)
        {
            bool held = Holding(Ordnance.Binoculars);
            if (!held)
            {
                // Put down or swapped: the eyes come off at once.
                if (BinocularView.Ads > 0f) BinocularView.Reset();
                StopLase();
                _laseLatched = false;
                return;
            }

            BinocularView.Tick(Input.GetMouseButton(1), dt);

            if (Input.GetKeyDown(Config.WarheadModeKey))
            {
                int n = System.Enum.GetValues(typeof(FireMissionType)).Length;
                FireMission.Type = (FireMissionType)(((int)FireMission.Type + 1) % n);
                Sfx.PlayHeld("CluClick");
            }

            // A fix needs the button let go before the next lase.
            if (!Input.GetMouseButton(0)) { _laseLatched = false; StopLase(); return; }
            if (_laseLatched || !BinocularView.Up) { StopLase(); return; }

            var cam = Camera.main;
            if (cam == null) return;
            Vector3 eye = cam.transform.position;
            if (!LaseHit(eye, cam.transform.forward, out Vector3 point))
            {
                _laseNoReturn = true;
                _laseAnchored = false;
                _laseProgress = 0f;
                _lrfReading = false;
                return;
            }
            _laseNoReturn = false;

            float range = Vector3.Distance(eye, point);
            float tol = Mathf.Max(2f, range * Config.LaseTolerance);
            if (!_laseAnchored || (point - _laseAnchor).sqrMagnitude > tol * tol)
            {
                _laseAnchored = true;
                _laseAnchor = point;
                _laseProgress = 0f;
            }
            Reading(eye, _laseAnchor);

            _laseProgress += dt / Mathf.Max(0.2f, Config.LaseTime);
            _laseTick -= dt;
            if (_laseTick <= 0f) { Sfx.PlayHeld("LrfTick"); _laseTick = Mathf.Lerp(0.4f, 0.09f, _laseProgress); }
            if (_laseProgress < 1f) return;

            // The fix: call the mission on it.
            Sfx.PlayHeld("LrfFix");
            _laseLatched = true;
            Vector3 mark = _laseAnchor;
            StopLase();
            _lrfReading = true;
            if (Config.Dbg1) MelonLogger.Msg($"[LRF] fix at {mark}, {_lrfRange:F0} m, az {_lrfAz:F0} mil");
            FireMission.Call(mark, eye);
        }

        private static void StopLase()
        {
            _laseProgress = 0f;
            _laseAnchored = false;
            _laseNoReturn = false;
            _laseTick = 0f;
        }

        /// <summary>Range, azimuth (mils from +Z, clockwise) and elevation (mils) from the eye to the point.</summary>
        private static void Reading(Vector3 eye, Vector3 point)
        {
            Vector3 d = point - eye;
            float flat = new Vector2(d.x, d.z).magnitude;
            _lrfRange = d.magnitude;
            _lrfAz = Mathf.Repeat(Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, 360f) * (6400f / 360f);
            _lrfEl = Mathf.Atan2(d.y, Mathf.Max(0.01f, flat)) * Mathf.Rad2Deg * (6400f / 360f);
            _lrfReading = true;
        }

        private static bool LaseHit(Vector3 from, Vector3 dir, out Vector3 point)
        {
            point = default;
            int mask = Config.WorldLayerMask & ~(1 << 2);
            var hits = Physics.RaycastAll(from, dir, Mathf.Max(10f, Config.LaseRange), mask, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            foreach (var h in hits)
            {
                if (h.collider == null || h.distance < 0.3f || h.distance >= best) continue;
                best = h.distance;
                point = h.point;
            }
            return best < float.MaxValue;
        }

        /// <summary>OnGUI: the eyepieces and readout while they are up, and the radio net.</summary>
        private static void DrawFireSupport()
        {
            if (BinocularView.Ads > 0f && Holding(Ordnance.Binoculars))
            {
                var st = new LrfState
                {
                    Ads = BinocularView.Ads,
                    Lasing = _laseProgress > 0f,
                    Progress = _laseProgress,
                    NoReturn = _laseNoReturn,
                    HasReading = _lrfReading,
                    Range = _lrfRange, AzimuthMils = _lrfAz, ElevationMils = _lrfEl,
                    Mission = FireMission.TypeName,
                    Rounds = Mathf.Clamp(Config.ArtyRounds, 1, 24),
                };
                st.HasMark = FireMission.TryMark(out st.Mark, out st.MarkStatus);
                LrfOverlay.Draw(st);
            }
            RadioLog.Draw();
        }
    }
}
