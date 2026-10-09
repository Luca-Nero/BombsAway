using MelonLoader;
using UnityEngine;
using Vector3 = UnityEngine.Vector3;

namespace BombsAway
{
    // ══════════════════════════════════════════════════════════════════════════════
    // The binoculars: a laser rangefinder binocular, the platform for fire support.
    // Right mouse brings their screen up (LrfDisplay, like the Javelin's CLU; BinocularView:
    // slowed look, zoom levels on the wheel).
    // Holding left mouse there lases what is under the reticle; held on it for LaseTime (as
    // the Javelin's lock), the fix is made and the mission called on it (FireMission). The
    // lase holds the point it first ranged; the reticle only has to stay within LaseHoldMils of
    // it, so breath and sway (seven times larger through the glass) don't break it. Off it, the
    // fill pauses for up to LaseGrace before the lase starts over. Nothing to range at the
    // start shows "NO RETURN". Q / E (MissionPrevKey / MissionNextKey) pick the mission type
    // while they're up, the warhead key at any time.
    // ══════════════════════════════════════════════════════════════════════════════

    public partial class Core
    {
        private static float _laseProgress;
        private static bool _laseAnchored, _laseNoReturn, _laseLatched;
        private static Vector3 _laseAnchor;
        private static float _laseTick;
        private static float _laseOff;        // seconds the reticle has been off the lased point
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

            // Not to the eye (nor lased with) until they've finished spawning in.
            BinocularView.Tick(Input.GetMouseButton(1) && HeldReady(Ordnance.Binoculars), dt);

            // The mission: Q / E while they're up (the screen's strip shows it), the warhead key any time.
            int step = 0;
            if (Input.GetKeyDown(Config.WarheadModeKey)) step = 1;
            if (BinocularView.Up)
            {
                if (Input.GetKeyDown(Config.MissionPrevKey)) step = -1;
                if (Input.GetKeyDown(Config.MissionNextKey)) step = 1;
            }
            if (step != 0)
            {
                FireMission.Step(step);
                Sfx.PlayHeld("CluClick");
            }

            // A fix needs the button let go before the next lase.
            if (!Input.GetMouseButton(0)) { _laseLatched = false; StopLase(); return; }
            if (_laseLatched || !BinocularView.Up) { StopLase(); return; }

            // From the screen's camera when there is one (the reticle is the middle of its picture),
            // where LateUpdate will lift it to, not where the hip pose still holds it.
            Vector3 eye, fwd;
            if (!HeldFeed(out eye, out fwd))
            {
                var cam = Camera.main;
                if (cam == null) return;
                eye = cam.transform.position; fwd = cam.transform.forward;
            }

            if (_laseAnchored)
            {
                // Held on the point: within LaseHoldMils of it counts. Off it, pause, then start over.
                float offMils = Vector3.Angle(fwd, _laseAnchor - eye) * (6400f / 360f);
                if (offMils > Mathf.Max(1f, Config.LaseHoldMils))
                {
                    _laseOff += dt;
                    if (_laseOff <= Mathf.Max(0f, Config.LaseGrace)) { Reading(eye, _laseAnchor); return; }
                    _laseAnchored = false;
                }
                else _laseOff = 0f;
            }

            if (!_laseAnchored)
            {
                if (!FireMission.PathHit(eye, fwd, Mathf.Max(10f, Config.LaseRange), out Vector3 point, out _, 0.3f))
                {
                    _laseNoReturn = true;
                    _laseProgress = 0f;
                    _lrfReading = false;
                    return;
                }
                _laseNoReturn = false;
                _laseAnchored = true;
                _laseAnchor = point;
                _laseProgress = 0f;
                _laseOff = 0f;
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
            _laseOff = 0f;
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

        /// <summary>What the rangefinder's screen shows this frame.</summary>
        private static LrfState LrfStateNow()
        {
            var st = new LrfState
            {
                Lasing = _laseAnchored,
                Progress = _laseProgress,
                OffMark = _laseOff > 0f,
                LasePoint = _laseAnchor,
                NoReturn = _laseNoReturn,
                HasReading = _lrfReading,
                Range = _lrfRange, AzimuthMils = _lrfAz, ElevationMils = _lrfEl,
                MissionLine = FireMission.Describe,
                MissionIndex = FireMission.Index,
                ZoomLevel = BinocularView.Level,
            };
            st.HasMark = FireMission.TryMark(out st.Mark, out st.MarkStatus);
            return st;
        }

        /// <summary>OnGUI: the radio net.</summary>
        private static void DrawFireSupport() => RadioLog.Draw();
    }
}
