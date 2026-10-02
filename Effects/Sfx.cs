using System.Collections.Generic;
using Il2CppAudio;
using Il2CppData.Audio;
using UnityEngine.Audio;
using MelonLoader;
using UnityEngine;
using Object = UnityEngine.Object;
using Vector3 = UnityEngine.Vector3;

namespace BombsAway
{
    /// <summary>
    /// BombsAway's sounds, all from the bundle. The prefab SFX_Library holds one child per sound
    /// key (e.g. "GrenadePin") carrying an AudioSource set up in Unity - clip, volume, pitch, how
    /// much it sits in the world (spatial blend), its distances, looping - so a sound is tuned
    /// there, not here. Several children with the same key are variants (one is picked at
    /// random); a child named key + "+" is a layer that always plays along. Explosions carry
    /// their own sounds in their effect prefabs (ExplosionFx: Sound* children).
    ///
    /// Everything goes out on the game's time-scaled bus (its slow motion reaches it), with a
    /// little pitch jitter so repeats don't sound pasted. The game's build strips PlayDelayed
    /// and the playOnAwake setter: authored sources must have Play On Awake off, and delays are
    /// run from here.
    /// </summary>
    internal static class Sfx
    {
        private const float PitchJitter = 0.05f;

        private static Dictionary<string, List<GameObject>> _variants, _layers;
        private static bool _loadTried;
        private static AudioMixerGroup _bus;
        private static bool _busTried;

        private sealed class Pending { public AudioSource Src; public float At; }
        private static readonly List<Pending> _pending = new List<Pending>();
        private sealed class Live { public GameObject Go; public float Until; }
        private static readonly List<Live> _live = new List<Live>();

        /// <summary>
        /// Plays <paramref name="key"/> at <paramref name="at"/>, riding along with
        /// <paramref name="follow"/> if given. Returns the (first) source, e.g. to stop a loop;
        /// null if the key is unknown or sound is off.
        /// </summary>
        public static AudioSource Play(string key, Vector3 at, Transform follow = null, float volume = 1f, float delay = 0f)
        {
            if (Config.SfxVolume <= 0f || !Load() || !_variants.TryGetValue(key, out var list) || list.Count == 0) return null;
            var first = Spawn(list[Random.Range(0, list.Count)], at, follow, volume, delay);
            if (_layers.TryGetValue(key, out var layers))
                foreach (var l in layers) Spawn(l, at, follow, volume, delay);
            return first;
        }

        /// <summary>At the ear: things in the hand.</summary>
        public static AudioSource PlayHeld(string key, float volume = 1f, float delay = 0f)
        {
            var cam = Camera.main;
            return cam != null ? Play(key, cam.transform.position, cam.transform, volume, delay) : null;
        }

        /// <summary>A source already in the world (an explosion's), played from here after <paramref name="delay"/>.</summary>
        public static void PlaySource(AudioSource src, float delay)
        {
            if (src == null || Config.SfxVolume <= 0f) return;
            Prepare(src, 1f);
            _pending.Add(new Pending { Src = src, At = Time.time + delay });
        }

        private static AudioSource Spawn(GameObject template, Vector3 at, Transform follow, float volume, float delay)
        {
            var go = Object.Instantiate(template);
            go.name = template.name;
            if (follow != null) go.transform.SetParent(follow, false);
            go.transform.position = at;
            var src = go.GetComponent<AudioSource>();
            if (src == null || src.clip == null) { Object.Destroy(go); return null; }
            Prepare(src, volume);
            float length = src.clip.length / Mathf.Max(0.05f, Mathf.Abs(src.pitch));
            if (delay > 0f) _pending.Add(new Pending { Src = src, At = Time.time + delay });
            else src.Play();
            // A loop lives until whatever it follows is gone (or it is stopped and destroyed).
            _live.Add(new Live { Go = go, Until = follow != null && IsLooping(template) ? float.MaxValue : Time.time + delay + length + 0.2f });
            return src;
        }

        private static bool IsLooping(GameObject template) => template.name.EndsWith("Loop") || template.name.EndsWith("Loop+");

        private static void Prepare(AudioSource src, float volume)
        {
            var bus = Bus();
            if (bus != null) src.outputAudioMixerGroup = bus;
            src.pitch *= 1f + Random.Range(-PitchJitter, PitchJitter);
            src.volume *= Mathf.Clamp(Config.SfxVolume, 0f, 2f) * volume;
        }

        /// <summary>Every frame: delayed sounds start, finished ones go.</summary>
        public static void Tick()
        {
            float now = Time.time;
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                var p = _pending[i];
                if (p.Src == null) { _pending.RemoveAt(i); continue; }
                if (now < p.At) continue;
                p.Src.Play();
                _pending.RemoveAt(i);
            }
            for (int i = _live.Count - 1; i >= 0; i--)
            {
                var l = _live[i];
                if (l.Go != null && now < l.Until) continue;
                if (l.Go != null) Object.Destroy(l.Go);
                _live.RemoveAt(i);
            }
        }

        public static void Clear()
        {
            foreach (var l in _live) if (l.Go != null) Object.Destroy(l.Go);
            _live.Clear();
            _pending.Clear();
            _bus = null; _busTried = false;   // the next scene has its own bus handler
        }

        private static bool Load()
        {
            if (_loadTried) return _variants != null;
            _loadTried = true;
            var lib = OrdnanceModels.Asset("SFX_Library");
            if (lib == null) { MelonLogger.Warning("[Sfx] no SFX_Library in the bundle: BombsAway is silent."); return false; }
            _variants = new Dictionary<string, List<GameObject>>();
            _layers = new Dictionary<string, List<GameObject>>();
            for (int i = 0; i < lib.transform.childCount; i++)
            {
                var c = lib.transform.GetChild(i).gameObject;
                string n = c.name;
                var dict = n.EndsWith("+") ? _layers : _variants;
                string key = n.TrimEnd('+');
                if (!dict.TryGetValue(key, out var l)) dict[key] = l = new List<GameObject>();
                l.Add(c);
            }
            if (Config.Dbg1) MelonLogger.Msg($"[Sfx] {_variants.Count} sounds, {lib.transform.childCount} sources");
            return true;
        }

        /// <summary>The game's time-scaled bus (slow motion slows it), or its game bus.</summary>
        private static AudioMixerGroup Bus()
        {
            if (_bus != null || _busTried) return _bus;
            _busTried = true;
            try
            {
                var handlers = Resources.FindObjectsOfTypeAll<AudioBusesHandler>();
                if (handlers != null && handlers.Length > 0)
                {
                    _bus = handlers[0].Group(AudioBus.TimeScaled) ?? handlers[0].Group(AudioBus.Game);
                    if (Config.Dbg1) MelonLogger.Msg($"[Sfx] bus '{(_bus != null ? _bus.name : "none")}'");
                }
            }
            catch (System.Exception e) { MelonLogger.Warning($"[Sfx] couldn't find the game's audio bus: {e.Message}"); }
            return _bus;
        }
    }
}
