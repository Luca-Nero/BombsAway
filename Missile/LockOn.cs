using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Color = UnityEngine.Color;
using Vector3 = UnityEngine.Vector3;

namespace BombsAway
{
    internal class LockBracket
    {
        private readonly string _name;
        private readonly bool _dynamicColor; 

        private GameObject _root;
        private LineRenderer[] _lines;
        private Material _mat;
        private Rigidbody _cachedOwner;
        private Renderer[] _cachedRenderers;

        public LockBracket(string name, bool dynamicColor)
        {
            _name = name;
            _dynamicColor = dynamicColor;
        }

        private void EnsureBuilt()
        {
            if (_root != null) return;

            _root = new GameObject(_name + "Brackets");
            GameObject.DontDestroyOnLoad(_root);

            _mat = new Material(Config.FindSpriteShader());
            _mat.SetInt("_ZTest", -1);  
            if (!_dynamicColor) _mat.color = new Color(0.15f, 1f, 0.15f, 0.65f);

            _lines = new LineRenderer[4];
            for (int i = 0; i < 4; i++)
            {
                var go = new GameObject($"{_name}Bracket_{i}");
                go.transform.SetParent(_root.transform);
                var lr = go.AddComponent<LineRenderer>();
                lr.material = _mat;
                lr.startWidth = 0.1f;
                lr.endWidth = 0.1f;
                lr.positionCount = 3;           
                lr.useWorldSpace = true;
                lr.shadowCastingMode = ShadowCastingMode.Off;
                lr.receiveShadows = false;
                lr.numCornerVertices = 0;
                _lines[i] = lr;
            }
        }

        private Renderer[] GetCachedRenderers(Rigidbody rb)
        {
            if (_cachedOwner == rb && _cachedRenderers != null) return _cachedRenderers;

            var raw = rb.GetComponentsInChildren<Renderer>();
            var arr = new Renderer[raw.Count];
            for (int i = 0; i < raw.Count; i++) arr[i] = raw[i];

            _cachedOwner = rb;
            _cachedRenderers = arr;
            return arr;
        }

        private Bounds GetAggregateBounds(Rigidbody rb)
        {
            var renderers = GetCachedRenderers(rb);
            if (renderers == null || renderers.Length == 0)
                return new Bounds(rb.transform.position, Vector3.one * 0.5f);

            Bounds b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                b.Encapsulate(renderers[i].bounds);
            return b;
        }

        public void Update(Rigidbody rb, bool locked)
        {
            EnsureBuilt();
            _root.SetActive(true);

            Bounds bounds;
            try { bounds = GetAggregateBounds(rb); }
            catch { Hide(); return; }

            var cam = Camera.main;
            if (cam == null) return;

            Vector3 min3 = bounds.min - Vector3.one * 0.05f;
            Vector3 max3 = bounds.max + Vector3.one * 0.05f;

            float sMinX = float.MaxValue, sMinY = float.MaxValue;
            float sMaxX = float.MinValue, sMaxY = float.MinValue;

            for (int i = 0; i < 8; i++)
            {
                Vector3 wc = new Vector3(
                    (i & 1) == 0 ? min3.x : max3.x,
                    (i & 2) == 0 ? min3.y : max3.y,
                    (i & 4) == 0 ? min3.z : max3.z);
                Vector3 sp = cam.WorldToScreenPoint(wc);
                if (sp.z < 0f) continue;  // behind camera
                if (sp.x < sMinX) sMinX = sp.x;
                if (sp.x > sMaxX) sMaxX = sp.x;
                if (sp.y < sMinY) sMinY = sp.y;
                if (sp.y > sMaxY) sMaxY = sp.y;
            }

            Vector3 c = bounds.center;
            float distToCam = Vector3.Distance(c, cam.transform.position);
            Vector3 camRight = cam.transform.right;
            Vector3 camUp = cam.transform.up;

            Vector3 screenC = cam.WorldToScreenPoint(c);

            Vector3 TL = cam.ScreenToWorldPoint(new Vector3(sMinX, sMaxY, screenC.z));
            Vector3 TR = cam.ScreenToWorldPoint(new Vector3(sMaxX, sMaxY, screenC.z));
            Vector3 BL = cam.ScreenToWorldPoint(new Vector3(sMinX, sMinY, screenC.z));
            Vector3 BR = cam.ScreenToWorldPoint(new Vector3(sMaxX, sMinY, screenC.z));

            float armWorld = Mathf.Clamp(Vector3.Distance(TL, TR) * 0.25f, 0.04f, 0.6f);

            Vector3 armR = camRight * armWorld;
            Vector3 armU = camUp * armWorld;

            _lines[0].SetPosition(0, TL + armR);
            _lines[0].SetPosition(1, TL);
            _lines[0].SetPosition(2, TL - armU);

            _lines[1].SetPosition(0, TR - armR);
            _lines[1].SetPosition(1, TR);
            _lines[1].SetPosition(2, TR - armU);

            _lines[2].SetPosition(0, BR - armR);
            _lines[2].SetPosition(1, BR);
            _lines[2].SetPosition(2, BR + armU);

            _lines[3].SetPosition(0, BL + armR);
            _lines[3].SetPosition(1, BL);
            _lines[3].SetPosition(2, BL + armU);

            float width = Mathf.Clamp(distToCam * 0.003f, 0.008f, 0.04f);
            for (int b = 0; b < 4; b++)
            {
                _lines[b].startWidth = width;
                _lines[b].endWidth = width;
            }

            if (_dynamicColor)
                _mat.color = locked
                    ? new Color(1f, 0.15f, 0.15f, 0.85f)
                    : new Color(0.15f, 1f, 0.15f, 0.65f);
        }

        public void Hide()
        {
            if (_root != null) _root.SetActive(false);
        }
    }

    public partial class Core
    {
        private static readonly LockBracket _lockBracket = new LockBracket("Lock", dynamicColor: true);
        private static readonly LockBracket _focusBracket = new LockBracket("Focus", dynamicColor: false);

        private static void UpdateLockIndicator(Rigidbody rb, bool locked) => _lockBracket.Update(rb, locked);
        private static void HideLockIndicator() => _lockBracket.Hide();
        private static void UpdateFocusIndicator(Rigidbody rb) => _focusBracket.Update(rb, false);
        private static void HideFocusIndicator() => _focusBracket.Hide();
        private static readonly HashSet<Rigidbody> _scanSeen = new HashSet<Rigidbody>();

        private static Rigidbody ScanForTarget()
        {
            var cam = Camera.main;
            if (cam == null) return null;

            Vector3 camPos = cam.transform.position;
            Vector3 camFwd = cam.transform.forward;
            float cosThreshold = Mathf.Cos(Config.MissileLockAngle * Mathf.Deg2Rad);

            Rigidbody best = null;
            float bestDot = cosThreshold;
            _scanSeen.Clear();

            var overlaps = ExplosionSystem.OverlapSphereShared(camPos, Config.MissileLockRange,
                Config.FragLayerMask, QueryTriggerInteraction.Ignore, out int count);
            for (int i = 0; i < count; i++)
            {
                var col = overlaps[i];
                if (col == null) continue;
                var rb = col.attachedRigidbody;
                if (rb == null || rb.isKinematic) continue;
                if (!_scanSeen.Add(rb)) continue;

                Vector3 toTarget = (rb.transform.position - camPos).normalized;
                float dot = Vector3.Dot(toTarget, camFwd);
                if (dot > bestDot)
                {
                    bestDot = dot;
                    best = rb;
                }
            }

            return best;
        }
    }
}
