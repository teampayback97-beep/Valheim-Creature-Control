using System.Collections.Generic;
using UnityEngine;

namespace CreatureControl
{
    /// <summary>
    /// The visible half of Force Target - a translucent lime-green bubble
    /// around whatever is currently under a forced-target order, so the
    /// command is confirmed on screen even before a tame arrives to act on
    /// it. One marker per targeted creature, shared by every tame ordered
    /// onto it; it disappears the instant the order does, for any reason
    /// (target dead, or every tame that had it left its own leash).
    /// </summary>
    static class TargetMarker
    {
        static readonly Dictionary<Character, GameObject> _markers = new Dictionary<Character, GameObject>();
        static readonly HashSet<Character> _liveTargets = new HashSet<Character>();
        static List<Character> _stale;

        static Mesh _sphereMesh;
        static Material _material;

        static Mesh SphereMesh()
        {
            if (_sphereMesh == null)
            {
                var tmp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                _sphereMesh = tmp.GetComponent<MeshFilter>().sharedMesh;
                UnityEngine.Object.Destroy(tmp);
            }
            return _sphereMesh;
        }

        static Material BubbleMaterial()
        {
            if (_material == null)
            {
                // Sprites/Default is unlit and alpha-blended out of the box -
                // no render-queue/keyword juggling needed to get a translucent
                // colour to actually show as translucent, unlike Standard.
                var shader = Shader.Find("Sprites/Default");
                _material = new Material(shader) { color = new Color(0.35f, 1f, 0.25f, 0.30f) };
            }
            return _material;
        }

        static GameObject CreateMarker(Character target)
        {
            var go = new GameObject("CC_TargetMarker");
            go.transform.SetParent(target.transform, worldPositionStays: false);

            float r = Mathf.Max(target.GetRadius(), 0.5f);
            go.transform.localPosition = Vector3.up * r;
            go.transform.localScale = Vector3.one * (r * 2.6f);

            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = SphereMesh();

            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = BubbleMaterial();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            // No collider - this must never be able to catch the Force Target
            // raycast itself, or looking at a marked creature a second time
            // would hit its own bubble instead of it.
            return go;
        }

        /// <summary>Called once a frame from Plugin.Update(). Cheap: bails
        /// immediately whenever nothing in the world currently has a forced
        /// target, which is true on the overwhelming majority of frames.</summary>
        public static void Tick()
        {
            if (CreatureState.ForcedTargetCount == 0 && _markers.Count == 0) return;

            _liveTargets.Clear();
            foreach (var st in CreatureState.AllTracked)
            {
                if (st != null && st.HasForcedTarget && st.ForcedTarget != null && !st.ForcedTarget.IsDead())
                    _liveTargets.Add(st.ForcedTarget);
            }

            _stale?.Clear();
            foreach (var kv in _markers)
            {
                if (kv.Key == null || !_liveTargets.Contains(kv.Key))
                    (_stale ?? (_stale = new List<Character>())).Add(kv.Key);
            }
            if (_stale != null)
                foreach (var c in _stale)
                {
                    if (_markers.TryGetValue(c, out var go) && go != null) UnityEngine.Object.Destroy(go);
                    _markers.Remove(c);
                }

            foreach (var c in _liveTargets)
                if (!_markers.ContainsKey(c))
                    _markers[c] = CreateMarker(c);
        }

        /// <summary>World unload / mod teardown - the markers are parented to
        /// creature transforms and would die with them anyway, but a fresh
        /// world load must not start from stale dictionary entries.</summary>
        public static void Reset()
        {
            foreach (var kv in _markers)
                if (kv.Value != null) UnityEngine.Object.Destroy(kv.Value);
            _markers.Clear();
        }
    }
}
