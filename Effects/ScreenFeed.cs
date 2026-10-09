using UnityEngine;
using Object = UnityEngine.Object;
using Quaternion = UnityEngine.Quaternion;
using Vector3 = UnityEngine.Vector3;

namespace BombsAway
{
    /// <summary>
    /// The camera and RenderTexture behind a held model's screen (the Javelin's CLU, the
    /// binoculars' rangefinder), made once and handed from one equip to the next: making them
    /// on every equip was part of its hitch. Between holders the camera waits switched off,
    /// unparented (the model it was on is going) and kept over scene loads; if it went anyway
    /// (a scene took it with the model) it is made again.
    /// </summary>
    internal sealed class ScreenFeed
    {
        private readonly string _name;
        private readonly int _w, _h;
        private RenderTexture _rt;
        private GameObject _go;
        private Camera _cam;

        public ScreenFeed(string name, int w, int h)
        {
            _name = name; _w = w; _h = h;
        }

        /// <summary>The feed's camera at <paramref name="window"/>, switched off, drawing into <paramref name="rt"/>.</summary>
        public Camera Take(Transform window, out RenderTexture rt)
        {
            if (_rt == null)
            {
                _rt = new RenderTexture(_w, _h, 24) { filterMode = FilterMode.Point, hideFlags = HideFlags.DontUnloadUnusedAsset };
                _rt.Create();
            }
            if (_cam == null)
            {
                if (_go != null) Object.Destroy(_go);
                _go = new GameObject(_name);
                _cam = _go.AddComponent<Camera>();
            }
            _cam.enabled = false;
            _go.transform.SetParent(window, false);
            _go.transform.localPosition = new Vector3(0f, 0f, 0.01f);
            _go.transform.localRotation = Quaternion.identity;
            if (!_go.activeSelf) _go.SetActive(true);
            _cam.targetTexture = _rt;
            rt = _rt;
            return _cam;
        }

        /// <summary>Back to waiting: off, off the model, kept over scene loads.</summary>
        public void Give()
        {
            if (_go == null || _cam == null) return;
            _cam.enabled = false;
            _go.transform.SetParent(null, false);
            _go.SetActive(false);
            Object.DontDestroyOnLoad(_go);
        }
    }
}
