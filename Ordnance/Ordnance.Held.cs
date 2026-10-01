using System.Collections.Generic;
using Il2CppInterop.Runtime;
using MelonLoader;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;
using Quaternion = UnityEngine.Quaternion;
using Vector3 = UnityEngine.Vector3;

namespace BombsAway
{
    // ══════════════════════════════════════════════════════════════════════════════
    // The charge in hand (grenade, C4), and what happens before it leaves the hand, thrown
    // or placed alike.
    //
    // The model hangs under the item instance the game parents into its SelectedItemPivot,
    // so it sways and lags with the camera like a native item. Its pose is given in the
    // camera's axes (Config.Hold*): the pivot's rotation relative to the camera, snapped to
    // 90 degrees when it is attached, converts it. The snap drops whatever sway or lag the
    // pivot had at that moment.
    //
    // A throw, shaped like the forces behind it rather than eased:
    //  - Pin (PinYankTime): a short tug that worries it against the cotter, then an
    //    accelerating yank; it leaves at the yank's final speed with the ring flipping.
    //  - Spoon (SpoonSnapTime): the striker spring takes up the slack, then snaps the lever
    //    open ever faster. It is let go at full speed: the spin it had and the flick that
    //    spin gives its centre of mass about the hinge, so physics carries the motion on.
    //  - A beat (LaunchDelay) to see the spoon go, then only the body leaves the hand.
    // Each jolt kicks a damped spring on the grenade's own tilt, so the hand reacts.
    // Pin and spoon become loose physics parts. After RearmDelay a fresh grenade comes up
    // into view (RaiseTime).
    //
    // The C4 arms instead (Ordnance.C4Rig.cs): the thumb flicks the hazard cover open, the
    // toggle resists and then snaps over, LED and screen come on, the antenna shoots out in
    // two stages. Nothing comes off; the charge leaves the hand armed.
    //
    // The claymore deploys (Ordnance.ClaymoreRig.cs): its two leg pairs flip out one after
    // the other, the sensor head pops up out of its slot, and the three lenses light up
    // left to right, one tick each.
    //
    // The missile item holds the Javelin launcher instead, with its CLU display live
    // (Missile/JavelinClu.cs). Right mouse lifts it to the eye: the display comes up centred
    // at AdsScreenFraction of the screen height and the world behind it blurs (AdsBlur). That
    // pose is set in LateUpdate from the camera, over the hip pose Update left.
    //
    // The AT-4 item holds its launcher on the shoulder. Right mouse brings the rear aperture to
    // the eye (AT4EyeRelief out, the sight line along the view); the rocket leaves along the
    // tube. It is single-shot: a moment after firing the spent tube is tossed aside as a loose
    // physics part, and a fresh one comes up once the reload (RocketReloadTime) is nearly done.
    // ══════════════════════════════════════════════════════════════════════════════

    public partial class Core
    {
        private enum ThrowStage { None, PullPin, Spoon, C4Cover, C4Switch, C4Antenna, ClayLegs, ClayHead, ClayLenses, Launch }

        private static Transform _heldParent;
        private static Ordnance _heldKind;
        private static GameObject _held;
        private static C4Rig _rig;
        private static ClaymoreRig _clay;
        private static JavelinClu _clu;
        private static WarheadLabel _label;                  // a launcher's HEAT / HE stencil
        private static float _ads;                          // 0 = at the hip, 1 = up at the eye (launcher)
        private static float _heldDisplayH;                 // the CLU display's height, metres
        private static Transform _heldPin, _heldSpoon;
        private static Vector3 _heldHome, _heldLowered;   // root local position: in hand / out of view
        private static float _raise = 1f;                  // 0 = lowered, 1 = in hand
        private static float _rearmAt = -1f;               // a fresh grenade comes up then; <0 = none pending
        private static bool _pivotLogged;

        private static ThrowStage _stage;
        private static float _stageTime;
        private static bool _jolted;                              // this stage's kick given
        private static int _ticks;                                // this stage's later beats given
        private static bool _placing;                             // set down at the end, not thrown
        private static RaycastHit _placeClickHit;                 // the target when clicked
        private static Vector3 _pinHome, _pinDir, _pinFlipAxis;   // root space
        private static Quaternion _spoonHome;
        private static Vector3 _spoonAxis, _spoonOut;             // root space
        private static Vector3 _camPrev, _camVel;

        // The hand's reaction: a damped spring on the grenade's tilt (rotation vector, degrees, root space).
        private static Quaternion _heldBaseRot;
        private static Vector3 _kick, _kickVel;

        private static readonly List<Collider> _looseThisThrow = new List<Collider>();
        // Loose parts in the world, until when, and what was made for them (a spent tube's label).
        private static readonly List<(GameObject go, float until, Object[] owned)> _looseParts = new List<(GameObject, float, Object[])>();
        private static float _spentAt = -1f;                      // the fired AT-4 is tossed then; <0 = none

        private const float PinPullDistance = 0.07f;
        private const float PinTug = 0.35f;          // share of PinYankTime spent working it loose
        private const float PinTugDistance = 0.004f;
        private const float PinFlip = 50f;           // the ring swings over as it comes free
        private const float SpoonSwing = 70f;        // open this far when let go; its spin does the rest
        private const float SpoonCreep = 0.25f;      // share of SpoonSnapTime taking up slack
        private const float SpoonCreepAngle = 5f;
        private const float KickStiffness = 420f;
        private const float KickDamping = 16f;       // under-damped: one small wobble
        private const float RaiseDrop = 0.12f;
        private const int MaxLooseParts = 24;

        private static bool ThrowAnimating => _stage != ThrowStage.None;

        // ── Attach / detach ─────────────────────────────────────────────────────

        /// <summary>Only ordnance with a bundled model has one in hand; the rest act at once.</summary>
        private static bool HasHeldModel(Ordnance o) => OrdnanceModels.HeldPrefab(o) != null;

        private static void AttachHeld(Transform parent, Ordnance kind)
        {
            DetachHeld();
            _heldParent = parent;
            _heldKind = kind;
            if (parent == null || !Config.ShowHeldModels) return;
            // Still reloading after the last shot: the fresh tube comes up when it is nearly done.
            if (kind == Ordnance.Rocket && !RocketReady) _rearmAt = Mathf.Max(Time.time, _rocketReadyAt - Config.RaiseTime - AT4ArmTime);
            else SpawnHeld(raise: true);
        }

        /// <summary>What a launcher made for itself: the CLU's cameras and textures, the label's texture.</summary>
        private static void DropLauncherParts()
        {
            _clu?.Dispose();
            _clu = null;
            _label?.Dispose();
            _label = null;
            DropAT4();
        }

        private static string WarheadText => MissileWarheadMode == WarheadMode.HE ? "HE" : "HEAT";

        private static void DetachHeld()
        {
            _ads = 0f;
            AdsBlur.Set(0f);
            _stage = ThrowStage.None;
            _rearmAt = -1f;
            _spentAt = -1f;
            if (_held != null) Object.Destroy(_held);
            _held = null;
            _heldPin = _heldSpoon = null;
            _rig = null; _clay = null; DropLauncherParts();
            _heldParent = null;
        }

        private static void SpawnHeld(bool raise)
        {
            var prefab = OrdnanceModels.HeldPrefab(_heldKind);
            var cam = Camera.main;
            if (prefab == null || _heldParent == null || cam == null) return;

            _held = new GameObject("BA_Held" + _heldKind);
            _held.layer = _heldParent.gameObject.layer;
            // The AT-4 on the viewmodel camera: at the eye its tube runs back past the cheek,
            // inside the world camera's near plane (Effects/ViewmodelCamera.cs).
            if (_heldKind == Ordnance.Rocket)
            {
                int vm = ViewmodelCamera.Layer(cam);
                if (vm >= 0) _held.layer = vm;
            }
            _held.transform.SetParent(_heldParent, false);
            OrdnanceModels.AddParts(prefab, _held.transform, _held.layer, shadows: false);
            if (_heldKind == Ordnance.Smoke) TintBand(_held.transform, SmokeColour);
            _heldPin = _held.transform.Find("Pin");
            _heldSpoon = _held.transform.Find("Spoon");
            _rig = C4Rig.Bind(_held.transform);
            _rig?.Safe();
            _clay = ClaymoreRig.Bind(_held.transform);
            _clay?.Stowed();
            if (_heldKind == Ordnance.Missile)
            {
                _clu = JavelinClu.Attach(_held.transform);
                var hud = _held.transform.Find("Hud");
                _heldDisplayH = hud != null ? hud.GetComponent<MeshFilter>().sharedMesh.bounds.size.y : 0.1f;
            }
            if (IsLauncher(_heldKind)) _label = WarheadLabel.Attach(_held.transform, WarheadText);
            BindAT4();

            // Parts are only offset from the root, so root space is part space plus an offset.
            if (_heldPin != null)
            {
                _pinHome = _heldPin.localPosition;
                var m = _heldPin.GetComponent<MeshFilter>().sharedMesh;
                _pinDir = m.bounds.center.sqrMagnitude > 1e-8f ? m.bounds.center.normalized : Vector3.back;
                _pinFlipAxis = Vector3.Cross(_pinDir, Vector3.down).normalized;   // tips the ring downward
            }
            if (_heldSpoon != null)
            {
                _spoonHome = _heldSpoon.localRotation;
                Vector3 hinge = _heldSpoon.localPosition;
                Vector3 arm = _heldSpoon.GetComponent<MeshFilter>().sharedMesh.bounds.center;   // hinge -> middle of the spoon
                _spoonOut = new Vector3(hinge.x, 0f, hinge.z).normalized;
                _spoonAxis = Vector3.Cross(arm, _spoonOut).normalized;   // positive swing carries the arm outward
            }

            // Camera axes -> pivot axes. Scale compensated so Hold* and every root-space
            // distance below are metres.
            Quaternion toCam = Snap90(Quaternion.Inverse(_heldParent.rotation) * cam.transform.rotation);
            float s = Mathf.Abs(_heldParent.lossyScale.x) > 1e-4f ? _heldParent.lossyScale.x : 1f;
            _held.transform.localScale = Vector3.one / s;
            HoldPose(_heldKind, out Vector3 offset, out Vector3 euler);
            _heldBaseRot = toCam * Quaternion.Euler(euler);
            _held.transform.localRotation = _heldBaseRot;
            _kick = _kickVel = Vector3.zero;
            _heldHome = toCam * offset / s;
            _heldLowered = _heldHome + toCam * new Vector3(0f, -RaiseDrop, 0f) / s;
            _raise = raise ? 0f : 1f;
            ApplyRaise();

            if (!_pivotLogged)
            {
                _pivotLogged = true;
                Vector3 local = cam.transform.InverseTransformPoint(_heldParent.position);
                MelonLogger.Msg($"[Held] item pivot '{_heldParent.name}' at camera-space {local.ToString("F3")}, " +
                                $"rotation vs camera {(Quaternion.Inverse(cam.transform.rotation) * _heldParent.rotation).eulerAngles.ToString("F1")}, " +
                                $"snapped {toCam.eulerAngles.ToString("F0")}, scale {_heldParent.lossyScale.ToString("F3")}, layer {_held.layer}");
            }
        }

        /// <summary>Camera-axes hold pose (metres right/up/forward, Euler degrees) per charge.</summary>
        private static void HoldPose(Ordnance o, out Vector3 offset, out Vector3 euler)
        {
            if (o == Ordnance.Missile)
            {
                offset = new Vector3(Config.JavelinHipOffsetX, Config.JavelinHipOffsetY, Config.JavelinHipOffsetZ);
                euler = new Vector3(Config.JavelinHipPitch, Config.JavelinHipYaw, Config.JavelinHipRoll);
                return;
            }
            if (o == Ordnance.Rocket)
            {
                offset = new Vector3(Config.AT4CarryOffsetX, Config.AT4CarryOffsetY, Config.AT4CarryOffsetZ);
                euler = new Vector3(Config.AT4CarryPitch, Config.AT4CarryYaw, Config.AT4CarryRoll);
                return;
            }
            if (o == Ordnance.Claymore)
            {
                offset = new Vector3(Config.MineHoldOffsetX, Config.MineHoldOffsetY, Config.MineHoldOffsetZ);
                euler = new Vector3(Config.MineHoldPitch, Config.MineHoldYaw, Config.MineHoldRoll);
                return;
            }
            if (o == Ordnance.C4)
            {
                offset = new Vector3(Config.C4HoldOffsetX, Config.C4HoldOffsetY, Config.C4HoldOffsetZ);
                euler = new Vector3(Config.C4HoldPitch, Config.C4HoldYaw, Config.C4HoldRoll);
                return;
            }
            offset = new Vector3(Config.HoldOffsetX, Config.HoldOffsetY, Config.HoldOffsetZ);
            euler = new Vector3(Config.HoldPitch, Config.HoldYaw, Config.HoldRoll);
        }

        /// <summary>The charge left the hand: a fresh one comes up later.</summary>
        private static void ConsumeHeld()
        {
            if (_held == null) return;
            Object.Destroy(_held);
            _held = null;
            _heldPin = _heldSpoon = null;
            _rig = null; _clay = null; DropLauncherParts();
            _rearmAt = Time.time + Config.RearmDelay;
        }

        // ── Throw ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Starts the pin-and-spoon sequence, ending in a throw, or with <paramref name="placeAt"/>
        /// in setting the body down there. False when there is no model in hand to animate: the
        /// caller then acts at once. True also swallows a click while the next grenade is still
        /// coming up.
        /// </summary>
        private static bool BeginThrow(RaycastHit? placeAt)
        {
            if (_heldParent == null || !Config.ShowHeldModels) return false;
            if (_held == null) return _rearmAt >= 0f;
            if (ThrowAnimating || _raise < 1f) return true;

            _looseThisThrow.Clear();
            _placing = placeAt.HasValue;
            if (_placing) _placeClickHit = placeAt.Value;
            NextStage(_rig != null       ? ThrowStage.C4Cover
                    : _clay != null      ? ThrowStage.ClayLegs
                    : _heldPin != null   ? ThrowStage.PullPin
                    : _heldSpoon != null ? ThrowStage.Spoon
                    :                      ThrowStage.Launch);
            return true;
        }

        private static void NextStage(ThrowStage stage)
        {
            _stage = stage;
            _stageTime = 0f;
            _jolted = false;
            _ticks = 0;
        }

        /// <summary>Jolts the grenade in hand: tips its top toward <paramref name="towards"/> (root space).</summary>
        private static void Kick(Vector3 towards, float degPerSec)
        {
            Vector3 axis = Vector3.Cross(Vector3.up, towards);
            if (axis.sqrMagnitude > 1e-6f) _kickVel += axis.normalized * degPerSec;
        }

        private static void TickThrow(float dt)
        {
            _stageTime += dt;
            switch (_stage)
            {
                case ThrowStage.PullPin:
                {
                    float T = Mathf.Max(0.02f, Config.PinYankTime);
                    float u = Mathf.Clamp01(_stageTime / T);
                    float dist, twist = 0f, flip = 0f;
                    if (u < PinTug)
                    {
                        // Worked against the cotter: a few millimetres and a twist back and forth.
                        if (!_jolted) { Kick(_pinDir, 90f); _jolted = true; Sfx.PlayHeld("GrenadePin"); }
                        float a = u / PinTug;
                        dist = PinTugDistance * a * a * (3f - 2f * a);
                        twist = Mathf.Sin(a * Mathf.PI * 3f) * 12f * (1f - 0.5f * a);
                    }
                    else
                    {
                        // Free: an accelerating yank, the ring tipping over as it comes out.
                        float v = (u - PinTug) / (1f - PinTug);
                        dist = PinTugDistance + (PinPullDistance - PinTugDistance) * v * v * v;
                        flip = PinFlip * v * v;
                    }
                    _heldPin.localPosition = _pinHome + _pinDir * dist;
                    _heldPin.localRotation = Quaternion.AngleAxis(twist, _pinDir) * Quaternion.AngleAxis(flip, _pinFlipAxis);
                    if (u < 1f) return;

                    // Let go at the yank's final speed (d/dt of the cubic), still flipping.
                    float yankT = (1f - PinTug) * T;
                    float speed = 3f * (PinPullDistance - PinTugDistance) / yankT;
                    float flipRate = 2f * PinFlip / yankT * Mathf.Deg2Rad;
                    Vector3 away = _held.transform.TransformDirection(_pinDir).normalized;
                    Vector3 flipW = _held.transform.TransformDirection(_pinFlipAxis).normalized;
                    ReleasePart(_heldPin, 0.012f, _camVel + away * speed + Vector3.up * 0.3f,
                                flipW * flipRate + Random.onUnitSphere * 6f);
                    _heldPin = null;
                    Kick(-_pinDir, 160f);   // the hand springs back as the pull lets go
                    NextStage(ThrowStage.Spoon);
                    return;
                }
                case ThrowStage.Spoon:
                {
                    if (_heldSpoon != null)
                    {
                        float T = Mathf.Max(0.01f, Config.SpoonSnapTime);
                        float u = Mathf.Clamp01(_stageTime / T);
                        float angle;
                        if (u < SpoonCreep) angle = SpoonCreepAngle * (u / SpoonCreep);
                        else
                        {
                            // The spring pushes the whole way: the lever only gets faster.
                            if (!_jolted) { Kick(-_spoonOut, 260f); _jolted = true; }
                            float w = (u - SpoonCreep) / (1f - SpoonCreep);
                            angle = SpoonCreepAngle + (SpoonSwing - SpoonCreepAngle) * w * w;
                        }
                        _heldSpoon.localRotation = Quaternion.AngleAxis(angle, _spoonAxis) * _spoonHome;
                        if (u < 1f) return;

                        // Let go at full spin: its angular velocity, and the velocity that spin gives
                        // its centre of mass about the hinge, plus the spring's outward shove.
                        float omega = 2f * (SpoonSwing - SpoonCreepAngle) / ((1f - SpoonCreep) * T) * Mathf.Deg2Rad;
                        Vector3 axisW = _held.transform.TransformDirection(_spoonAxis).normalized;
                        Vector3 outW = _held.transform.TransformDirection(_spoonOut).normalized;
                        var spoonMesh = _heldSpoon.GetComponent<MeshFilter>().sharedMesh;
                        Vector3 arm = _heldSpoon.TransformPoint(spoonMesh.bounds.center) - _heldSpoon.position;
                        Vector3 flick = Vector3.Cross(axisW * omega, arm);
                        ReleasePart(_heldSpoon, 0.02f, _camVel + flick + outW * 1.5f + Vector3.up * 1f, axisW * omega);
                        Sfx.PlayHeld("GrenadeSpoon");
                        _heldSpoon = null;
                    }
                    NextStage(ThrowStage.Launch);
                    return;
                }
                case ThrowStage.C4Cover:
                {
                    // A thumb flick: fastest at the start, overshoots the stop and settles back.
                    if (!_jolted) { Kick(Vector3.forward, 60f); _jolted = true; Sfx.PlayHeld("C4Cover"); }
                    float u = _stageTime / Mathf.Max(0.01f, Config.C4CoverTime);
                    _rig.CoverAngle(C4Rig.CoverOpen * C4Rig.BackOut(u));
                    if (u < 1f) return;
                    _rig.CoverAngle(C4Rig.CoverOpen);
                    NextStage(ThrowStage.C4Switch);
                    return;
                }
                case ThrowStage.C4Switch:
                {
                    // A toggle resists, then snaps over centre: slow, then all at once.
                    float u = Mathf.Clamp01(_stageTime / Mathf.Max(0.01f, Config.C4SwitchTime));
                    _rig.LeverAngle(-C4Rig.LeverThrow + 2f * C4Rig.LeverThrow * u * u * u);
                    if (u < 1f) return;
                    _rig.Lit(true, true);
                    Kick(Vector3.forward, 110f);   // the click
                    Sfx.PlayHeld("C4Switch");
                    NextStage(ThrowStage.C4Antenna);
                    return;
                }
                case ThrowStage.C4Antenna:
                {
                    // Spring-loaded, two stages; the screen flickers while it boots.
                    if (!_jolted) { Kick(Vector3.back, 50f); _jolted = true; Sfx.PlayHeld("C4Antenna"); }
                    float u = _stageTime / Mathf.Max(0.01f, Config.C4AntennaTime);
                    _rig.Antenna(C4Rig.BackOut(u / 0.6f), C4Rig.BackOut((u - 0.4f) / 0.6f));
                    _rig.Lit(true, u > 0.6f || ((int)(_stageTime / 0.025f) & 1) == 0);
                    if (u < 1f) return;
                    _rig.Armed();
                    Sfx.PlayHeld("C4Armed");
                    NextStage(ThrowStage.Launch);
                    return;
                }
                case ThrowStage.ClayLegs:
                {
                    // One pair, then the other: each snaps down past its stop and settles.
                    float u = _stageTime / Mathf.Max(0.01f, Config.MineLegsTime);
                    if (!_jolted) { Kick(Vector3.right, 70f); _jolted = true; Sfx.PlayHeld("ClayLeg"); }
                    if (_ticks == 0 && u >= 0.25f) { Kick(Vector3.left, 70f); _ticks = 1; Sfx.PlayHeld("ClayLeg"); }
                    _clay.Legs(ClaymoreRig.BackOutUnclamped(u / 0.75f), ClaymoreRig.BackOutUnclamped((u - 0.25f) / 0.75f));
                    if (u < 1f) return;
                    _clay.Legs(1f, 1f);
                    NextStage(ThrowStage.ClayHead);
                    return;
                }
                case ThrowStage.ClayHead:
                {
                    if (!_jolted) { Kick(Vector3.back, 45f); _jolted = true; Sfx.PlayHeld("ClayHead"); }
                    float u = _stageTime / Mathf.Max(0.01f, Config.MineHeadTime);
                    _clay.Head(C4Rig.BackOut(u));
                    if (u < 1f) return;
                    _clay.Head(1f);
                    NextStage(ThrowStage.ClayLenses);
                    return;
                }
                case ThrowStage.ClayLenses:
                {
                    // Left to right (Lens2 sits on the mine's left), a tick each.
                    float u = _stageTime / Mathf.Max(0.01f, Config.MineLensTime);
                    while (_ticks < 3 && u >= _ticks / 3f)
                    {
                        _clay.Lit(2 - _ticks, true);
                        Sfx.PlayHeld("ClayLens");
                        Kick(Vector3.forward, 25f);
                        _ticks++;
                    }
                    if (u < 1f) return;
                    _clay.Deployed();
                    NextStage(ThrowStage.Launch);
                    return;
                }
                case ThrowStage.Launch:
                {
                    // A beat to see the last part settle before the arm comes through.
                    if (_stageTime < Config.LaunchDelay) return;

                    // The charge leaves the hand: set down where the hologram shows now (or
                    // showed at the click, if the target was lost meanwhile), or thrown from
                    // where it is and as it is held. A grenade goes as the body alone.
                    var g = _placing
                        ? PlaceOrdnance(_heldKind, _placeValid ? _placeHit : _placeClickHit)
                        : ThrowOrdnance(_heldKind, _held.transform.position, _held.transform.rotation);
                    if (g != null)
                    {
                        var body = g.Obj != null ? g.Obj.GetComponent<Collider>() : null;
                        if (body != null)
                            foreach (var c in _looseThisThrow)
                                if (c != null) Physics.IgnoreCollision(body, c, true);
                    }
                    _looseThisThrow.Clear();
                    _stage = ThrowStage.None;
                    ConsumeHeld();
                    return;
                }
            }
        }

        /// <summary>Lets a part go: it keeps its world pose and falls as a small physics body.</summary>
        private static void ReleasePart(Transform part, float mass, Vector3 velocity, Vector3 angularVelocity)
        {
            part.SetParent(null, true);
            var go = part.gameObject;
            go.layer = IgnoreRaycastLayer;   // keeps it out of the placement ray
            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null) mr.shadowCastingMode = ShadowCastingMode.On;

            var mesh = go.GetComponent<MeshFilter>().sharedMesh;
            var box = go.AddComponent<BoxCollider>();
            box.center = mesh.bounds.center;
            box.size = Vector3.Max(mesh.bounds.size, Vector3.one * 0.004f);

            var rb = go.AddComponent(Il2CppType.Of<Rigidbody>()).TryCast<Rigidbody>();
            rb.mass = mass;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.maxAngularVelocity = 150f;   // the default 7 rad/s would strangle the spoon's spin
            rb.linearVelocity = velocity;
            rb.angularVelocity = angularVelocity;

            _looseThisThrow.Add(box);
            AddLoosePart(go, null);
        }

        private static void AddLoosePart(GameObject go, Object[] owned)
        {
            _looseParts.Add((go, Time.time + Config.PartDebrisLifetime, owned));
            while (_looseParts.Count > MaxLooseParts)
            {
                DestroyLoose(_looseParts[0]);
                _looseParts.RemoveAt(0);
            }
        }

        private static void DestroyLoose((GameObject go, float until, Object[] owned) part)
        {
            if (part.go != null) Object.Destroy(part.go);
            if (part.owned != null) foreach (var o in part.owned) if (o != null) Object.Destroy(o);
        }

        /// <summary>
        /// The spent AT-4 leaves the hand: where it is drawn this frame (up at the eye or on the
        /// shoulder), flung down and aside with a roll, a loose physics part like the grenade's
        /// pin. The next one comes up as the reload runs out.
        /// </summary>
        private static void TossSpent()
        {
            var go = _held;
            var cam = Camera.main;
            HeldPose(out Vector3 pos, out Quaternion rot);
            var owned = _label?.Orphan();
            _label = null;
            _held = null;
            DropLauncherParts();
            _ads = 0f;
            AdsBlur.Set(0f);

            go.transform.SetParent(null, true);
            go.transform.SetPositionAndRotation(pos, rot);
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = IgnoreRaycastLayer;
            foreach (var mr in go.GetComponentsInChildren<MeshRenderer>(true)) mr.shadowCastingMode = ShadowCastingMode.On;

            var b = OrdnanceModels.LocalBounds(go);
            var box = go.AddComponent<BoxCollider>();
            box.center = b.center;
            box.size = b.size;
            var rb = go.AddComponent(Il2CppType.Of<Rigidbody>()).TryCast<Rigidbody>();
            rb.mass = 4f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            if (cam != null)
            {
                Transform c = cam.transform;
                rb.linearVelocity = _camVel + c.right * 1.6f + c.up * 0.5f - c.forward * 0.3f;
                rb.angularVelocity = c.forward * -2.5f + c.up * 1.2f + Random.insideUnitSphere * 0.6f;
            }
            AddLoosePart(go, owned);
            Sfx.Play("AT4Spent", pos, null, 1f, 0.55f);   // about when it lands
            _rearmAt = Mathf.Max(Time.time, _rocketReadyAt - Config.RaiseTime - AT4ArmTime);
        }

        private static void ClearLooseParts()
        {
            foreach (var part in _looseParts) DestroyLoose(part);
            _looseParts.Clear();
            _looseThisThrow.Clear();
        }

        // ── Per frame ───────────────────────────────────────────────────────────

        private static void TickHeld()
        {
            float dt = Time.deltaTime;
            var cam = Camera.main;
            if (cam != null)
            {
                Vector3 p = cam.transform.position;
                if (dt > 0f) _camVel = (p - _camPrev) / dt;
                _camPrev = p;
            }

            for (int i = _looseParts.Count - 1; i >= 0; i--)
            {
                var part = _looseParts[i];
                if (part.go != null && Time.time < part.until) continue;
                DestroyLoose(part);
                _looseParts.RemoveAt(i);
            }

            // The game destroyed the item instance (and our model with it) without a deselect.
            if (_heldParent == null)
            {
                if (_held != null || _stage != ThrowStage.None || _rearmAt >= 0f) DetachHeld();
                return;
            }

            if (!Config.ShowHeldModels)
            {
                if (_held != null) { Object.Destroy(_held); _held = null; _heldPin = _heldSpoon = null; _rig = null; _clay = null; DropLauncherParts(); }
                _stage = ThrowStage.None;
                _rearmAt = -1f;
                return;
            }

            if (_held == null)
            {
                if (_rearmAt < 0f || Time.time >= _rearmAt)
                {
                    _rearmAt = -1f;
                    SpawnHeld(raise: true);
                }
                return;
            }

            if (_spentAt >= 0f && Time.time >= _spentAt)
            {
                _spentAt = -1f;
                TossSpent();
                return;
            }

            if (_raise < 1f) _raise = Mathf.Clamp01(_raise + dt / Mathf.Max(0.01f, Config.RaiseTime));
            ApplyRaise();   // every frame: LateUpdate may have moved it up to the eye

            // Right mouse: the Javelin's CLU display, or the AT-4's sights, up to the eye.
            bool wantAds = Input.GetMouseButton(1)
                && ((_heldKind == Ordnance.Missile && _clu != null && Holding(Ordnance.Missile))
                    || (_heldKind == Ordnance.Rocket && Holding(Ordnance.Rocket)));
            _ads = Mathf.MoveTowards(_ads, wantAds ? 1f : 0f, dt / Mathf.Max(0.01f, Config.AdsTime));

            if (_stage != ThrowStage.None) TickThrow(dt);
            if (_heldKind == Ordnance.Rocket) TickAT4(dt);
            TickKick(dt);
        }

        private static void TickKick(float dt)
        {
            if (_held == null) return;
            dt = Mathf.Min(dt, 0.05f);   // a hitch must not blow the spring up
            _kickVel += (-KickStiffness * _kick - KickDamping * _kickVel) * dt;
            _kick += _kickVel * dt;
            float deg = _kick.magnitude;
            _held.transform.localRotation = deg > 1e-3f
                ? _heldBaseRot * Quaternion.AngleAxis(deg, _kick / deg)
                : _heldBaseRot;
        }

        /// <summary>
        /// After the game has moved its item pivot: lifts the launcher to the eye by the ADS
        /// amount (display centred, AdsScreenFraction of the screen high, facing the camera,
        /// the hand's kick still on it), then updates the CLU.
        /// </summary>
        private static void LateTickHeld()
        {
            if (_held == null) { if (_ads > 0f) { _ads = 0f; AdsBlur.Set(0f); } return; }
            if (HeldPose(out Vector3 pos, out Quaternion rot)) _held.transform.SetPositionAndRotation(pos, rot);
            AdsBlur.Set(_heldKind == Ordnance.Missile ? _ads : 0f);   // iron sights: nothing to blur around
            if (_heldKind == Ordnance.Rocket) ViewmodelCamera.Sync();
            _label?.Tick(Time.deltaTime, WarheadText);

            _clu?.Tick(_ads, new CluState
            {
                Nfov = _cluNfov,
                View = _cluView,
                Attack = MissileAttackMode,
                Seeking = SlotScanning,
                Locked = _lockedTarget != null,
                LockProgress = _lockProgress,
                InFlight = _missiles.Exists(m => !m.Dead),
                Target = GateTarget(),
            });
        }

        /// <summary>
        /// Where the held model is this frame, lifted to the eye by the ADS amount: the hip pose
        /// Update left, blended toward the display centred in front of the camera. True if the
        /// lift moves it. Computed, not applied, so a missile fired during Update (before
        /// LateUpdate lifts the model) still leaves from where the tube will be drawn.
        /// </summary>
        private static bool HeldPose(out Vector3 pos, out Quaternion rot)
        {
            Transform t = _held.transform;
            pos = t.position; rot = t.rotation;
            var cam = Camera.main;
            if (_ads <= 0.001f || cam == null) return false;
            float e = _ads * _ads * (3f - 2f * _ads);
            float d;
            if (_heldKind == Ordnance.Rocket) d = Config.AT4EyeRelief;   // the root is the rear aperture
            else
            {
                float tanHalf = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
                d = (_heldDisplayH * 0.5f) / (Mathf.Clamp(Config.AdsScreenFraction, 0.2f, 1f) * tanHalf);
            }
            d = Mathf.Max(d, cam.nearClipPlane + 0.03f);
            Vector3 adsPos = cam.transform.position + cam.transform.forward * d;
            Quaternion adsRot = cam.transform.rotation * KickRotation();
            pos = Vector3.Lerp(pos, adsPos, e);
            rot = Quaternion.Slerp(rot, adsRot, e);
            return true;
        }

        private static Quaternion KickRotation()
        {
            float deg = _kick.magnitude;
            return deg > 1e-3f ? Quaternion.AngleAxis(deg, _kick / deg) : Quaternion.identity;
        }

        /// <summary>
        /// The held launcher's tube: where a missile starts (inside it, nose at the muzzle) and the
        /// way the tube points, from the Breech and Muzzle markers on its ends. The tube is angled
        /// up from the CLU's line of sight, so a launch leaves climbing (javelin_launcher_build.py).
        /// </summary>
        internal static bool HeldTube(Ordnance kind, out Vector3 pos, out Vector3 dir)
        {
            pos = dir = default;
            if (_heldKind != kind || !HeldEnds(out Vector3 muzzleW, out Vector3 breechW)) return false;
            dir = (muzzleW - breechW).normalized;
            var body = OrdnanceModels.Prefab(kind)?.transform.Find("Body");
            float half = body != null ? body.GetComponent<MeshFilter>().sharedMesh.bounds.extents.z : 0.5f;
            pos = muzzleW - dir * half;
            return true;
        }

        /// <summary>
        /// The held launcher's two ends in the world, through this frame's pose, lifted if
        /// sighted (the markers sit unrotated under the model's root, whose world scale is 1).
        /// </summary>
        private static bool HeldEnds(out Vector3 muzzleW, out Vector3 breechW)
        {
            muzzleW = breechW = default;
            if (_held == null) return false;
            var muzzle = _held.transform.Find("Muzzle");
            var breech = _held.transform.Find("Breech");
            if (muzzle == null || breech == null) return false;
            HeldPose(out Vector3 rootPos, out Quaternion rootRot);
            muzzleW = rootPos + rootRot * muzzle.localPosition;
            breechW = rootPos + rootRot * breech.localPosition;
            return true;
        }

        /// <summary>
        /// The launcher bucks as the missile leaves: muzzle up. An AT-4 also blasts out of its
        /// back end, presses its button home, and is tossed shortly after.
        /// </summary>
        internal static void HeldFired(bool rocket)
        {
            if (rocket) Backblast();
            Sfx.PlayHeld(rocket ? "AT4Fire" : "JavLaunch");
            if (!IsLauncher(_heldKind) || _held == null) return;
            Kick(Vector3.back, _heldKind == Ordnance.Rocket ? 300f : 220f);
            if (_heldKind != Ordnance.Rocket) return;
            AT4Fired();
            _spentAt = Time.time + Config.AT4SpentDelay;
        }

        /// <summary>The AT-4's backblast (Explosion.Vfx.cs), out of the held tube's back, or from behind the shoulder without one.</summary>
        private static void Backblast()
        {
            var cam = Camera.main;
            if (cam == null) return;
            Transform c = cam.transform;
            Vector3 muzzle, breech;
            if (_heldKind != Ordnance.Rocket || !HeldEnds(out muzzle, out breech))
            {
                muzzle = c.position + c.forward * 0.8f + c.right * 0.2f - c.up * 0.1f;
                breech = c.position - c.forward * 0.5f + c.right * 0.2f - c.up * 0.1f;
            }
            ExplosionVFX.SpawnBackblast(breech, (breech - muzzle).normalized, muzzle);
            CameraFX.AddKick(Config.CamFX(Config.AT4ShakeTrauma));
        }

        /// <summary>The AT-4 in hand can fire: up and not yet spent (or there is no model to wait for).</summary>
        private static bool RocketInHand =>
            !Config.ShowHeldModels || _heldParent == null || !HasHeldModel(Ordnance.Rocket)
            || (_heldKind == Ordnance.Rocket && _held != null && _raise >= 1f && _spentAt < 0f && AT4Armed);

        private static void ApplyRaise()
        {
            if (_held == null) return;
            float e = 1f - (1f - _raise) * (1f - _raise);   // ease out
            _held.transform.localPosition = Vector3.LerpUnclamped(_heldLowered, _heldHome, e);
        }

        /// <summary>The nearest rotation made only of 90 degree steps.</summary>
        private static Quaternion Snap90(Quaternion q)
        {
            Vector3 f = SnapAxis(q * Vector3.forward), u = SnapAxis(q * Vector3.up);
            return Mathf.Abs(Vector3.Dot(f, u)) > 0.5f ? q : Quaternion.LookRotation(f, u);
        }

        private static Vector3 SnapAxis(Vector3 v)
        {
            float ax = Mathf.Abs(v.x), ay = Mathf.Abs(v.y), az = Mathf.Abs(v.z);
            if (ax >= ay && ax >= az) return new Vector3(Mathf.Sign(v.x), 0f, 0f);
            if (ay >= az) return new Vector3(0f, Mathf.Sign(v.y), 0f);
            return new Vector3(0f, 0f, Mathf.Sign(v.z));
        }
    }
}
