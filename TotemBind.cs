using System;
using UnityEngine;

namespace CreatureControl
{
    /// <summary>
    /// The "Troll Logging Leash" piece: registration, world binding, and the
    /// two lookups TrollLogging.cs needs - whether a point is inside a
    /// leash's radius, and where the nearest chest is.
    ///
    /// Deliberately holds no movement code and never calls Threat.BaseTick:
    /// TrollLogging.cs is the sole owner of the MonsterAI.UpdateAI tick it
    /// takes over, and BaseAI's per-tick housekeeping has to be called
    /// exactly once per tick taken. Splitting movement across two files risks
    /// calling it twice, or not at all, so this only ever answers questions -
    /// it never drives anything itself.
    /// </summary>
    public static class TotemBind
    {
        // ------------------------------------------------------------- piece

        /// <summary>Which vanilla piece the leash is cloned from - a single,
        /// easily-swapped constant, exactly per spec. The Ward ("piece_ward")
        /// already IS a radius-of-influence marker in vanilla, which is
        /// exactly what a leash is, so it's the thematic fit as well as the
        /// practical one.</summary>
        const string SourcePrefab = "piece_ward";

        public const string LeashPrefabName = "CC_TrollLoggingLeash";

        static bool _hooked;

        /// <summary>
        /// Registers the Jotunn hook. Jotunn itself defers the actual prefab
        /// work until vanilla prefabs exist, so this is safe to call straight
        /// from Plugin.Awake - nothing here touches ZNetScene/ObjectDB early.
        /// </summary>
        public static void Init()
        {
            if (_hooked) return;
            _hooked = true;
            Jotunn.Managers.PrefabManager.OnVanillaPrefabsAvailable += Register;
        }

        static void Register()
        {
            Jotunn.Managers.PrefabManager.OnVanillaPrefabsAvailable -= Register;

            try
            {
                var prefab = Jotunn.Managers.PrefabManager.Instance
                    .CreateClonedPrefab(LeashPrefabName, SourcePrefab);
                if (prefab == null)
                {
                    Plugin.Log.LogError(
                        $"Could not clone '{SourcePrefab}' for the Troll Logging Leash - " +
                        "logging will have nothing to bind to until this is fixed.");
                    return;
                }

                var config = new Jotunn.Configs.PieceConfig
                {
                    Name = "Troll Logging Leash",
                    Description = "A tamed troll with logging on binds to the nearest one of " +
                                  "these and works the loggable trees inside its radius.",
                    PieceTable = "Hammer",
                    Category = "Misc",
                    Requirements = new[]
                    {
                        new Jotunn.Configs.RequirementConfig { Item = "Wood", Amount = 10 },
                        new Jotunn.Configs.RequirementConfig { Item = "Stone", Amount = 5 },
                    }
                };

                // The Ward is picked for its radius-of-influence THEME, not its
                // actual function - cloning it also clones its real PrivateArea
                // component (activation/naming interact, Eitr upkeep, its own
                // hostile-ward-off behaviour), which would fight with the
                // troll's own combat detection and give players an interact
                // prompt that does the wrong thing. Strip it so the leash is a
                // pure marker; TrollLogging/TotemBind supply all the actual
                // radius behaviour themselves.
                var ward = prefab.GetComponent<PrivateArea>();
                if (ward != null) UnityEngine.Object.DestroyImmediate(ward);
                else if (Plugin.Verbose)
                    Plugin.Log.LogWarning(
                        $"'{SourcePrefab}' had no PrivateArea component to strip - " +
                        "double-check it isn't carrying its own ward behaviour into the leash.");

                var piece = new Jotunn.Entities.CustomPiece(prefab, fixReference: true, config);
                Jotunn.Managers.PieceManager.Instance.AddPiece(piece);

                // Tames damaging player-built pieces is already blocked
                // generically by StructureGuard.cs (Plugin.TamesSpareStructures
                // + Piece.IsPlacedByPlayer()) - the leash needs no special case
                // there, it is protected the same way a placed wall is.
                Plugin.Log.LogInfo($"Registered '{LeashPrefabName}' (cloned from '{SourcePrefab}').");
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Troll Logging Leash registration failed: {e}");
            }
        }

        // -------------------------------------------------------- world scan
        // One shared scan for every logging troll, on a timer - mirrors
        // FireAversion's Rebuild(), sourced from Piece.m_allPieces (vanilla's
        // own live list of every placed piece) rather than a fresh
        // FindObjectsByType sweep, since pieces already maintain one.
        static GameObject[] _snap = new GameObject[8];
        static int _n;
        static float _nextScan;

        public static void Reset() { _n = 0; _nextScan = 0f; }

        public static void Tick()
        {
            if (Time.time < _nextScan) return;
            _nextScan = Time.time + Plugin.LoggingLeashScanInterval;
            Rebuild();
        }

        static void Rebuild()
        {
            _n = 0;
            var all = Piece.m_allPieces;
            if (all == null) return;

            for (int i = 0; i < all.Count; i++)
            {
                var p = all[i];
                if (p == null) continue;
                var go = p.gameObject;
                if (go == null) continue;
                if (!string.Equals(CreatureRules.CleanName(go.name), LeashPrefabName,
                        StringComparison.OrdinalIgnoreCase)) continue;

                if (_n >= _snap.Length) Array.Resize(ref _snap, _snap.Length * 2);
                _snap[_n++] = go;
            }
        }

        // ------------------------------------------------------------- bind

        public static bool IsInsideLeash(GameObject leash, Vector3 pos)
        {
            if (leash == null) return false;
            return Vector3.Distance(leash.transform.position, pos) <= Plugin.LoggingLeashRadius;
        }

        /// <summary>
        /// Keeps st.BoundLeash current. Drops it only if the totem itself was
        /// destroyed - a troll pulled outside its radius by combat comes back
        /// to the SAME leash, never whichever happens to be nearest at that
        /// moment. A fresh bind only ever forms while standing inside a
        /// leash's radius: proximity-based and continuous, never a one-time
        /// assignment at spawn, per spec.
        /// </summary>
        public static bool ResolveBind(CreatureState st, Vector3 pos)
        {
            // st.BoundLeash == null is also true for a destroyed (Unity
            // fake-null) totem, which is exactly the "unbind" case here.
            if (st.BoundLeash != null) return true;

            GameObject best = null;
            float bestSq = float.MaxValue;
            float r = Plugin.LoggingLeashRadius;

            for (int i = 0; i < _n; i++)
            {
                var go = _snap[i];
                if (go == null) continue;
                float sq = (go.transform.position - pos).sqrMagnitude;
                if (sq > r * r) continue;
                if (sq < bestSq) { bestSq = sq; best = go; }
            }

            st.BoundLeash = best;
            return best != null;
        }

        // ----------------------------------------------------------- chests

        /// <summary>Nearest vanilla Container within the SAME leash's radius -
        /// measured from the leash, not the troll, so a chest at the far edge
        /// of the radius still counts even while the troll is elsewhere
        /// inside it.</summary>
        public static GameObject FindNearestChest(Vector3 leashPos)
        {
            var all = Piece.m_allPieces;
            if (all == null) return null;

            GameObject best = null;
            float bestSq = float.MaxValue;
            float rSq = Plugin.LoggingLeashRadius * Plugin.LoggingLeashRadius;

            for (int i = 0; i < all.Count; i++)
            {
                var p = all[i];
                if (p == null) continue;
                var go = p.gameObject;
                if (go == null) continue;
                var c = go.GetComponent<Container>();
                if (c == null) continue;

                float sq = (go.transform.position - leashPos).sqrMagnitude;
                if (sq > rSq) continue;
                if (sq < bestSq) { bestSq = sq; best = go; }
            }
            return best;
        }
    }
}
