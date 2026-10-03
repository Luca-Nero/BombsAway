using UnityEngine;
using Color = UnityEngine.Color;
using Object = UnityEngine.Object;
using Quaternion = UnityEngine.Quaternion;
using Vector3 = UnityEngine.Vector3;

namespace BombsAway
{
    /// <summary>
    /// The MOAB's carrier (5.22.0): an MC-130J, HERC 71, running in slow (MoabCarrierSpeed) and
    /// level at the JDAMs' height, the way the real one was delivered. The bundle's <c>C130</c>
    /// (5f, 1.2x real) has its four props spinning, and its ramp and upper door open
    /// HercRampLead seconds before the drop. The bomb lies in the bay on its extraction cradle.
    /// HercStreamTime before the pull a drogue streams out behind the ramp on its riser, then it
    /// pulls the bomb and cradle aft along the floor (an even acceleration over
    /// HercExtractTime) and off the ramp lip, where the bomb's own fall begins at the aircraft's
    /// speed less the pull's. The cradle and the drogue go their own way, braked hard and
    /// sinking (HercDebrisLife seconds, then gone), and the ramp closes as the aircraft turns off.
    /// Numbers marked "model" are read off c130_build.py's printout (Unity m: right, up, forward).
    /// </summary>
    internal static partial class AirStrike
    {
        private const string HercSign = "HERC", HercCallsign = "Herc 71";
        // Since 5.24.0 the MOAB is a 1.2x model like the C-130, its cradle full size and stowed further forward.
        private static readonly Vector3 HercStow = new Vector3(0f, -1.182f, 0.84f);    // model: the bomb's middle on its cradle
        private const float HercLipZ = -9.50f;          // model: the open ramp's aft edge, level with the floor
        private const float HercExtractTime = 1.4f;     // the drogue's pull, from rest to off the lip
        private const float HercExtractDist = 10.34f;   // HercStow.z - HercLipZ: the bomb's middle reaches the lip
        private const float HercExitSpeed = 2f * HercExtractDist / HercExtractTime;   // ~14.8 m/s slower than the aircraft
        private const float HercStreamTime = 0.7f;      // the drogue streaming out to its riser's length
        private const float HercRiser = 16f;
        private const float HercDrogueY = -1.7f;        // the drogue flies a little under the floor line
        private const float HercCradleHalf = 5.64f;     // model: the cradle runs 5.64 m fore and aft of its middle
        private const float HercRampLead = 12f, HercRampTime = 4f, HercRampHold = 5f;
        private const float HercRampOpen = 18.9f, HercDoorOpen = 24f;   // degrees about local X, negative opens (checked in the editor)
        private const float HercTurnAfter = 8f;         // seconds after the drop before it turns off
        private const float HercPropSpin = 900f;        // deg/s: readable against the blades' 6-fold symmetry
        private const float HercDebrisLife = 25f;
        private const float HercDrogueK = 0.0157f;      // drag over mass of cradle and drogue: sinks at ~25 m/s

        /// <summary>The C-130's moving parts and, after the drop, its cradle and drogue on their own.</summary>
        private sealed class Hercules
        {
            public Transform Ramp, Door, Cradle, Drogue, Bomb;
            public Transform[] Props;
            public Quaternion RampRest, DoorRest;
            public Quaternion[] PropRest;
            public Vector3 CradleRest;
            public float Spin;
            public LineRenderer Riser;
            public bool Detached;
            public float DetachedAt, Roll;
            public Vector3 Pos, Vel;
        }

        /// <summary>The bundle's C130 with the MOAB in its bay; the stand-in F-15E without it.</summary>
        private static void BuildC130(Strike s)
        {
            var root = SpawnBare("C130");
            if (root == null) { BuildF15(s); return; }
            var t = root.transform;
            var h = s.H ?? (s.H = new Hercules());
            h.Ramp = t.Find("Ramp");
            h.Door = t.Find("Door");
            if (h.Ramp != null) h.RampRest = h.Ramp.localRotation;
            if (h.Door != null) h.DoorRest = h.Door.localRotation;
            var props = new System.Collections.Generic.List<Transform>();
            for (int i = 0; i < t.childCount; i++) if (t.GetChild(i).name.StartsWith("Prop")) props.Add(t.GetChild(i));
            h.Props = props.ToArray();
            h.PropRest = new Quaternion[h.Props.Length];
            for (int i = 0; i < h.Props.Length; i++) h.PropRest[i] = h.Props[i].localRotation;

            var cradle = t.Find("Cradle");
            var drogue = t.Find("Drogue");
            if (drogue != null) drogue.gameObject.SetActive(false);
            h.Bomb = BuildBomb(s.B.K, t).transform;
            h.Bomb.localPosition = HercStow;
            // The stand-in's grid fins ride folded against the body (the model folds its own in BuildBomb).
            for (int i = 0; i < h.Bomb.childCount; i++)
                if (h.Bomb.GetChild(i).name == "GridFin") h.Bomb.GetChild(i).localScale = new Vector3(1f, 0.45f, 1f);
            if (s.B.Released)
            {
                // Rebuilt after the drop: its cradle and drogue are already out (or gone).
                h.Bomb.gameObject.SetActive(false);
                if (cradle != null) cradle.gameObject.SetActive(false);
            }
            else
            {
                h.Cradle = cradle;
                h.Drogue = drogue;
                if (cradle != null) h.CradleRest = cradle.localPosition;
            }
            if (h.Riser == null)
            {
                EnsureMats();
                var go = new GameObject("BA_HercRiser");
                go.layer = 2;
                var lr = go.AddComponent<LineRenderer>();
                lr.useWorldSpace = true;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.receiveShadows = false;
                lr.positionCount = 2;
                lr.material = _smokeMat;
                lr.startWidth = lr.endWidth = 0.07f;
                lr.startColor = lr.endColor = new Color(0.09f, 0.092f, 0.098f, 1f);   // dark
                lr.enabled = false;
                h.Riser = lr;
            }
            s.Craft = root;
        }

        /// <summary>Each frame while the C-130 flies: props, ramp and door, and the extraction up to the drop.</summary>
        private static void TickHerc(Strike s, float now)
        {
            var h = s.H;
            var b = s.B;
            float dt = Time.deltaTime;
            h.Spin = (h.Spin + HercPropSpin * dt) % 360f;
            for (int i = 0; i < h.Props.Length; i++)
                if (h.Props[i] != null) h.Props[i].localRotation = h.PropRest[i] * Quaternion.AngleAxis(h.Spin + 17f * i, Vector3.forward);

            float open = Mathf.Clamp01(Mathf.Min((now - (b.ReleaseAt - HercRampLead)) / HercRampTime,
                                                 (b.ReleaseAt + HercRampHold + HercRampTime - now) / HercRampTime));
            open = open * open * (3f - 2f * open);
            if (h.Ramp != null) h.Ramp.localRotation = h.RampRest * Quaternion.AngleAxis(-HercRampOpen * open, Vector3.right);
            if (h.Door != null) h.Door.localRotation = h.DoorRest * Quaternion.AngleAxis(-HercDoorOpen * open, Vector3.right);

            if (b.Released || h.Detached) return;
            float pull = b.ReleaseAt - HercExtractTime;
            float tp = Mathf.Clamp(now - pull, 0f, HercExtractTime);
            float slide = HercExtractDist * (tp * tp) / (HercExtractTime * HercExtractTime);
            if (h.Bomb != null) h.Bomb.localPosition = HercStow + Vector3.back * slide;
            if (h.Cradle != null) h.Cradle.localPosition = h.CradleRest + Vector3.back * slide;

            float deploy = pull - HercStreamTime;
            if (h.Drogue == null || now < deploy) return;
            if (!h.Drogue.gameObject.activeSelf) h.Drogue.gameObject.SetActive(true);
            float u = Mathf.Clamp01((now - deploy) / HercStreamTime);
            h.Drogue.localPosition = new Vector3(0f, HercDrogueY, HercLipZ - HercRiser * u);
            h.Drogue.localRotation = Quaternion.identity;
            h.Drogue.localScale = Vector3.one * Mathf.Lerp(0.15f, 1f, u * u);
            DrawRiser(h);
        }

        /// <summary>At the drop: the cradle and drogue leave the aircraft at the bomb's speed and fly on as one.</summary>
        private static void DetachHerc(Strike s, float now)
        {
            var h = s.H;
            if (h.Detached) return;
            h.Detached = true;
            h.DetachedAt = now;
            Vector3 p0 = s.B.Path.PosAt(s.B.ReleaseAt), p1 = s.B.Path.PosAt(s.B.ReleaseAt + SampleDt);
            h.Vel = (p1 - p0) / SampleDt;
            if (h.Cradle != null)
            {
                h.Cradle.SetParent(null, true);
                h.Pos = h.Cradle.position;
            }
            else h.Pos = p0;
            if (h.Drogue != null)
            {
                h.Drogue.SetParent(null, true);
                h.Drogue.gameObject.SetActive(true);
                h.Drogue.localScale = Vector3.one;
            }
        }

        /// <summary>The cradle and drogue after the drop: braked toward a slow sinking fall, the drogue trailing on its riser.</summary>
        private static void TickHercDebris(Strike s, float now)
        {
            var h = s.H;
            if (h == null || !h.Detached || (h.Cradle == null && h.Drogue == null)) return;
            if (now > h.DetachedAt + HercDebrisLife) { DropHercDebris(h); return; }
            float dt = Time.deltaTime;
            Vector3 v = h.Vel;
            v += (Physics.gravity - HercDrogueK * v.magnitude * v) * dt;
            h.Vel = v;
            h.Pos += v * dt;
            Vector3 dir = v.sqrMagnitude > 1e-4f ? v.normalized : Vector3.down;
            Vector3 upHint = Mathf.Abs(Vector3.Dot(dir, Vector3.up)) > 0.95f ? s.Heading : Vector3.up;
            h.Roll += 25f * dt;
            if (h.Cradle != null) h.Cradle.SetPositionAndRotation(h.Pos, Quaternion.LookRotation(dir, upHint) * Quaternion.AngleAxis(h.Roll, Vector3.forward));
            if (h.Drogue != null)
                h.Drogue.SetPositionAndRotation(h.Pos - dir * (HercCradleHalf + HercRiser), Quaternion.LookRotation(dir, upHint));
            DrawRiser(h);
        }

        private static void DrawRiser(Hercules h)
        {
            if (h.Riser == null) return;
            if (h.Cradle == null || h.Drogue == null || !h.Drogue.gameObject.activeSelf) { h.Riser.enabled = false; return; }
            h.Riser.enabled = true;
            h.Riser.SetPosition(0, h.Cradle.TransformPoint(new Vector3(0f, 0.15f, -HercCradleHalf)));
            h.Riser.SetPosition(1, h.Drogue.position);
        }

        private static void DropHercDebris(Hercules h)
        {
            if (h.Detached)
            {
                // Only the detached ones are ours to destroy; before the drop they go with the aircraft.
                if (h.Cradle != null) Object.Destroy(h.Cradle.gameObject);
                if (h.Drogue != null) Object.Destroy(h.Drogue.gameObject);
            }
            h.Cradle = null;
            h.Drogue = null;
            if (h.Riser != null) h.Riser.enabled = false;
        }

        private static void DropHerc(Hercules h)
        {
            DropHercDebris(h);
            if (h.Riser != null) Object.Destroy(h.Riser.gameObject);
            h.Riser = null;
        }
    }
}
