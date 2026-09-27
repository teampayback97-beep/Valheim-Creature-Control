using System;
using System.Collections.Generic;
using UnityEngine;

namespace CreatureControl
{
    /// <summary>How strong a fire is. A creature is deterred by any fire at or
    /// above its own threshold, so Torch means "even a torch turns it" and
    /// Bonfire means "only a bonfire will do".</summary>
    public enum FireTier { None = 0, Torch = 1, Campfire = 2, Bonfire = 3 }

    /// <summary>What a deterred creature does about it. Circle keeps its target
    /// and waits at the edge; Flee drops the target and bolts.</summary>
    public enum FireReaction { Circle = 0, Flee = 1 }

    /// <summary>
    /// Which world objects count as which tier of fire. Config-driven rather
    /// than a hard-coded prefab list, because a mod pack can add fire pieces we
    /// have never heard of - and because the radius fallback below is a guess,
    /// while a name in the config is not.
    /// </summary>
    public static class FireSources
    {
        public class Store
        {
            public readonly Dictionary<string, FireTier> ByName =
                new Dictionary<string, FireTier>(StringComparer.OrdinalIgnoreCase);
            public int Count => ByName.Count;
        }

        static Store _active = new Store();
        public static int Count => _active.ByName.Count;

        public static void Install(Store s)
        {
            _active = s ?? new Store();
            // Tier verdicts are cached per name; a reload may have changed them.
            FireAversion.ForgetClassifications();
        }

        /// <summary>Exact match first, then the longest name in the table that
        /// this object's name starts with - so one entry for
        /// "piece_groundtorch" covers _wood, _green, _blue and _mist.</summary>
        public static bool TryMatch(string name, out FireTier tier)
        {
            tier = FireTier.None;
            if (string.IsNullOrEmpty(name)) return false;
            if (_active.ByName.TryGetValue(name, out tier)) return true;

            int bestLen = 0;
            bool found = false;
            foreach (var kv in _active.ByName)
            {
                if (kv.Key.Length <= bestLen) continue;
                if (name.StartsWith(kv.Key, StringComparison.OrdinalIgnoreCase))
                { bestLen = kv.Key.Length; tier = kv.Value; found = true; }
            }
            return found;
        }
    }

    /// <summary>
    /// Fire detection for the avoidance behaviour.
    ///
    /// Vanilla already owns the BEHAVIOUR - BaseAI.AvoidFire flees or circles,
    /// and MonsterAI drops the target when it flees. What vanilla cannot do is
    /// tell a torch from a bonfire: it asks
    ///     EffectArea.IsPointInsideArea(pos, Type.Fire, 3f)
    /// which is a flat 3 m margin for every creature and every flame. This
    /// class supplies the missing half - which fires count for this creature,
    /// and from how far - and Patches drives vanilla's own movement with it.
    ///
    /// Reads EffectArea.GetAllAreas(), the game's own public list, so anything
    /// that genuinely counts as fire is included (placed pieces, a torch in a
    /// player's hand, a lit smelter) with no prefab list to maintain. The list
    /// is snapshotted on a timer and filtered down to fire, but POSITIONS are
    /// read live off each area every check, so a carried torch is never stale.
    /// </summary>
    public static class FireAversion
    {
        struct Src
        {
            public EffectArea Area;
            public FireTier Tier;
            public float Radius;    // the flame's own collider radius
        }

        static Src[] _snap = new Src[32];
        static int _n;
        static float _nextScan;

        // Name -> tier, so the transform walk and string work happen once per
        // distinct prefab rather than once per area per check.
        static readonly Dictionary<string, FireTier> _byName =
            new Dictionary<string, FireTier>(StringComparer.OrdinalIgnoreCase);

        // Whatever we could not name, reported once each so the log stays
        // readable and the player has a list to paste into [FireSources].
        static readonly HashSet<string> _reported = new HashSet<string>();

        public static int KnownSources => _n;

        public static void ForgetClassifications()
        {
            _byName.Clear();
            _reported.Clear();
            _nextScan = 0f;     // force a rebuild with the new table
        }

        public static void Reset()
        {
            ForgetClassifications();
            _n = 0;
        }

        /// <summary>The standoff distance a fire of this tier earns, before the
        /// creature's own buffer is applied.</summary>
        public static float TierRadius(FireTier t)
        {
            switch (t)
            {
                case FireTier.Torch: return Plugin.FireRadiusTorch;
                case FireTier.Campfire: return Plugin.FireRadiusCampfire;
                case FireTier.Bonfire: return Plugin.FireRadiusBonfire;
                default: return 0f;
            }
        }

        static void Rebuild()
        {
            _n = 0;
            var all = EffectArea.GetAllAreas();
            if (all == null) return;

            for (int i = 0; i < all.Count; i++)
            {
                var a = all[i];
                if (a == null) continue;
                if ((a.m_type & EffectArea.Type.Fire) == 0) continue;

                float radius;
                try { radius = a.GetRadius(); }
                catch { radius = 0f; }

                if (_n >= _snap.Length) Array.Resize(ref _snap, _snap.Length * 2);
                _snap[_n].Area = a;
                _snap[_n].Tier = Classify(a, radius);
                _snap[_n].Radius = radius;
                _n++;
            }
        }

        /// <summary>
        /// Walks from the flame up towards its root, asking the config about
        /// each name on the way. Nearest match wins, so a torch carried by a
        /// player is a torch and not a player. Falls back to the flame's own
        /// collider size, which is a real signal - a bonfire's fire area is
        /// genuinely bigger than a torch's - but a guess all the same, so the
        /// name table always overrules it.
        /// </summary>
        static FireTier Classify(EffectArea a, float radius)
        {
            var t = a.transform;
            Character creatureHolder = null;
            for (int depth = 0; depth < 6 && t != null; depth++, t = t.parent)
            {
                // A fire held by ANY creature - player, goblin, NPC - is a hand
                // torch regardless of what the prop is named. Checked once per
                // ancestor rather than short-circuiting the walk immediately, so
                // a name-table hit (which can still win outright, e.g. a
                // creature standing next to a bonfire prop parented oddly) is
                // never shadowed by a coincidental Character further up.
                if (creatureHolder == null)
                    creatureHolder = t.gameObject.GetComponent<Character>();

                var raw = t.gameObject.name;
                if (string.IsNullOrEmpty(raw)) continue;

                if (_byName.TryGetValue(raw, out var cached))
                {
                    if (cached != FireTier.None) return cached;
                    continue;               // known-uninteresting name, keep walking
                }

                var clean = CreatureRules.CleanName(raw);
                if (FireSources.TryMatch(clean, out var tier))
                {
                    _byName[raw] = tier;
                    return tier;
                }
                _byName[raw] = FireTier.None;
            }

            if (creatureHolder != null)
            {
                if (Plugin.Verbose)
                    Plugin.Log.LogInfo(
                        $"[fire] {CreatureRules.CleanName(creatureHolder.name)} is holding a " +
                        "fire source - treated as a torch.");
                return FireTier.Torch;
            }

            var guess = ByRadius(radius);

            if (Plugin.Verbose)
            {
                var name = CreatureRules.CleanName(a.transform.root.gameObject.name);
                if (_reported.Add(name))
                    Plugin.Log.LogInfo(
                        $"[fire] unlisted source '{name}' (flame radius {radius:0.##}) " +
                        $"treated as {guess}. Add '{name} = torch|campfire|bonfire' " +
                        $"under [FireSources] to set it explicitly.");
            }

            return guess;
        }

        static FireTier ByRadius(float r)
        {
            if (r <= 0f) return Plugin.FireUnknownTier;
            if (r < Plugin.FireRadiusGuessTorch) return FireTier.Torch;
            if (r < Plugin.FireRadiusGuessCampfire) return FireTier.Campfire;
            return FireTier.Bonfire;
        }

        /// <summary>
        /// The fire this creature should react to, or false if none does.
        ///
        /// When several qualify the winner is the one it is DEEPEST inside -
        /// smallest distance as a fraction of that fire's reach - so a bonfire
        /// 20 m off correctly outranks a torch at 8 m instead of the nearest
        /// flame always winning.
        /// </summary>
        /// <param name="threshold">weakest tier this creature reacts to</param>
        /// <param name="buffer">per-creature multiplier on the tier's reach</param>
        /// <param name="firePos">where the offending flame is</param>
        /// <param name="reach">how far from it this creature wants to stay</param>
        public static bool NearFire(Vector3 pos, FireTier threshold, float buffer,
                                    out Vector3 firePos, out float reach)
        {
            firePos = Vector3.zero;
            reach = 0f;
            if (threshold == FireTier.None) return false;

            if (Time.time >= _nextScan)
            {
                _nextScan = Time.time + Plugin.FireScanInterval;
                Rebuild();
            }

            float bestDepth = 1f;       // only interested in "inside", i.e. <= 1
            bool found = false;

            for (int i = 0; i < _n; i++)
            {
                var s = _snap[i];
                if (s.Area == null) continue;
                if (s.Tier < threshold) continue;

                // Never let the standoff sit inside the flame's own collider.
                float r = Mathf.Max(TierRadius(s.Tier), s.Radius + 1f) * buffer;
                if (Plugin.FireMaxRadius > 0f) r = Mathf.Min(r, Plugin.FireMaxRadius);
                if (r <= 0f) continue;

                float d = Vector3.Distance(s.Area.transform.position, pos);
                float depth = d / r;
                if (depth <= bestDepth)
                {
                    bestDepth = depth;
                    firePos = s.Area.transform.position;
                    reach = r;
                    found = true;
                }
            }

            return found;
        }
    }
}
