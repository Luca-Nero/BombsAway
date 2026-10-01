using UnityEngine;
using Vector3 = UnityEngine.Vector3;

namespace BombsAway
{
    // ══════════════════════════════════════════════════════════════════════════════
    // The AT-4 in hand (Missile/AT4Rig.cs). A fresh tube comes up in its transport pose and
    // is made ready before it can fire, each step shaped by the force behind it:
    //  - Pin (AT4PinTime): worried loose in its boss, then yanked out to the right and let go
    //    at the yank's speed, tag and all, as a loose part.
    //  - Cock (AT4CockTime): the lever is pushed against its spring, gives, and slams forward
    //    onto its stop.
    //  - Safety (AT4SafetyTime): a thumb flick, past the stop and back.
    // The sights stay folded until the tube first comes to the eye, then spring up, rear
    // first. Firing presses the button home.
    // ══════════════════════════════════════════════════════════════════════════════

    public partial class Core
    {
        private enum AT4Step { None, Pin, Cock, Safety, Armed }

        private static AT4Rig _at4;
        private static AT4Step _at4Step;
        private static float _at4Time;
        private static bool _at4Jolted;
        private static float _sightsTime = -1f;   // since the sights began to flip; <0 = still folded
        private static float _buttonTime = -1f;   // since the button went down; <0 = not pressed

        private const float AT4PinTravel = 0.06f;
        private const float AT4PinTug = 0.35f;          // share of AT4PinTime worrying it loose
        private const float AT4PinTugTravel = 0.004f;
        private const float AT4CockGive = 0.4f;         // share of AT4CockTime the spring holds out
        private const float AT4CockGiveAngle = 18f;     // how far it goes while it holds
        private const float AT4SightTime = 0.12f, AT4SightLag = 0.04f;

        /// <summary>How long the steps take together: the next tube comes up this much ahead of the reload.</summary>
        private static float AT4ArmTime => Config.AT4PinTime + Config.AT4CockTime + Config.AT4SafetyTime + 0.1f;

        /// <summary>Ready to fire: made safe to arm and armed (or no model to wait for).</summary>
        private static bool AT4Armed => _at4 == null || _at4Step == AT4Step.Armed;

        private static void BindAT4()
        {
            _at4 = _heldKind == Ordnance.Rocket && _held != null ? AT4Rig.Bind(_held.transform) : null;
            _at4?.Safe();
            _at4Step = _at4 != null ? AT4Step.Pin : AT4Step.None;
            _at4Time = 0f;
            _at4Jolted = false;
            _sightsTime = -1f;
            _buttonTime = -1f;
        }

        private static void DropAT4()
        {
            _at4 = null;
            _at4Step = AT4Step.None;
        }

        /// <summary>The button goes home as it fires.</summary>
        private static void AT4Fired() { if (_at4 != null) _buttonTime = 0f; }

        private static void TickAT4(float dt)
        {
            if (_at4 == null) return;

            // Sights: up the first time it comes to the eye, rear then front, each past its stop.
            if (_sightsTime < 0f && _ads > 0f) { _sightsTime = 0f; Kick(Vector3.back, 40f); Sfx.PlayHeld("AT4Sights"); }
            if (_sightsTime >= 0f && _sightsTime < AT4SightTime + AT4SightLag + 0.05f)
            {
                _sightsTime += dt;
                _at4.Sights(ClaymoreRig.BackOutUnclamped(_sightsTime / AT4SightTime),
                            ClaymoreRig.BackOutUnclamped((_sightsTime - AT4SightLag) / AT4SightTime));
            }

            // Button: slammed home, eases back out under its spring.
            if (_buttonTime >= 0f)
            {
                _buttonTime += dt;
                float t = _buttonTime < 0.03f ? _buttonTime / 0.03f : Mathf.Clamp01(1f - (_buttonTime - 0.12f) / 0.1f);
                _at4.Press(t);
                if (_buttonTime > 0.25f) _buttonTime = -1f;
            }

            if (_raise < 1f || _at4Step == AT4Step.Armed) return;
            _at4Time += dt;
            switch (_at4Step)
            {
                case AT4Step.Pin:
                {
                    float T = Mathf.Max(0.02f, Config.AT4PinTime);
                    float u = Mathf.Clamp01(_at4Time / T);
                    float dist;
                    if (u < AT4PinTug)
                    {
                        // Worked back and forth in the boss, a few millimetres.
                        if (!_at4Jolted) { Kick(Vector3.right, 60f); _at4Jolted = true; Sfx.PlayHeld("AT4Pin"); }
                        float a = u / AT4PinTug;
                        dist = AT4PinTugTravel * a * (1f + 0.5f * Mathf.Sin(a * Mathf.PI * 4f));
                    }
                    else
                    {
                        // Free: an accelerating yank.
                        float v = (u - AT4PinTug) / (1f - AT4PinTug);
                        dist = AT4PinTugTravel + (AT4PinTravel - AT4PinTugTravel) * v * v * v;
                    }
                    _at4.PinOut(dist);
                    if (u < 1f) return;

                    // Let go at the yank's final speed (the cubic's slope), the tag flapping it round.
                    float speed = 3f * (AT4PinTravel - AT4PinTugTravel) / ((1f - AT4PinTug) * T);
                    Vector3 right = _held.transform.right;
                    ReleasePart(_at4.Pin, 0.03f, _camVel + right * speed + Vector3.up * 0.4f,
                                _held.transform.forward * 18f + Random.onUnitSphere * 6f);
                    _at4.Pin = null;
                    Kick(Vector3.left, 120f);   // the hand springs back as it comes free
                    NextAT4(AT4Step.Cock);
                    return;
                }
                case AT4Step.Cock:
                {
                    // The spring holds it, then gives: slow, then all at once onto the stop.
                    float u = Mathf.Clamp01(_at4Time / Mathf.Max(0.02f, Config.AT4CockTime));
                    float travel;
                    if (u < AT4CockGive) travel = AT4CockGiveAngle * (u / AT4CockGive);
                    else
                    {
                        if (!_at4Jolted) { Kick(Vector3.forward, 50f); _at4Jolted = true; }
                        float w = (u - AT4CockGive) / (1f - AT4CockGive);
                        travel = AT4CockGiveAngle + (AT4Rig.CockUp - AT4CockGiveAngle) * w * w;
                    }
                    _at4.CockAngle(-AT4Rig.CockUp + travel);
                    if (u < 1f) return;
                    Kick(Vector3.forward, 200f);   // it hits the stop
                    Sfx.PlayHeld("AT4Cock");
                    NextAT4(AT4Step.Safety);
                    return;
                }
                case AT4Step.Safety:
                {
                    float u = _at4Time / Mathf.Max(0.02f, Config.AT4SafetyTime);
                    _at4.SafetyAngle(-AT4Rig.SafetyUp * (1f - C4Rig.BackOut(u)));
                    if (u < 1f) return;
                    _at4.SafetyAngle(0f);
                    Kick(Vector3.forward, 40f);
                    Sfx.PlayHeld("AT4Safety");
                    NextAT4(AT4Step.Armed);
                    return;
                }
            }
        }

        private static void NextAT4(AT4Step step)
        {
            _at4Step = step;
            _at4Time = 0f;
            _at4Jolted = false;
        }
    }
}
