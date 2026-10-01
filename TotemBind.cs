using System;
using System.Collections.Generic;
using UnityEngine;

namespace CreatureControl
{
    /// <summary>
    /// STATUS: WORK IN PROGRESS - not yet build-verified. See
    /// LOGGING-implementation-notes.md in the repo root before relying on
    /// this in a real game; remove this notice once that file's open items
    /// are resolved and it's been run and tested.
    ///
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
        /// easily-swapped constant, exactly per spec. The Ward's real prefab
        /// name is "guard_stone" (confirmed in the build menu and against
        /// valheimcheats.com - "piece_ward" was a wrong guess and is why
        /// registration failed at startup with "can not find base prefab").
        /// The Ward already IS a radius-of-influence marker in vanilla,
        /// which is exactly what a leash is, so it's the thematic fit as
        /// well as the practical one.</summary>
        const string SourcePrefab = "guard_stone";

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
                // Grab the Ward's own ground-ring radius indicator (a
                // CircleProjector child - the same one vanilla uses to show
                // a Ward's edge) before stripping PrivateArea; it lives on
                // its own child GameObject, so removing the PrivateArea
                // component doesn't take it down too. Keep it always
                // visible and sized to the configured leash radius -
                // Rebuild() below keeps every already-placed leash's ring
                // in sync if the radius setting changes later.
                var ward = prefab.GetComponent<PrivateArea>();
                CircleProjector marker = ward != null ? ward.m_areaMarker : null;
                if (ward != null) UnityEngine.Object.DestroyImmediate(ward);
                else if (Plugin.Verbose)
                    Plugin.Log.LogWarning(
                        $"'{SourcePrefab}' had no PrivateArea component to strip - " +
                        "double-check it isn't carrying its own ward behaviour into the leash.");

                if (marker != null)
                {
                    marker.m_radius = Plugin.LoggingLeashRadius;
                    marker.gameObject.SetActive(true);
                }
                else if (Plugin.Verbose)
                    Plugin.Log.LogWarning(
                        $"'{SourcePrefab}' had no area-marker ring to reuse - the leash will bind " +
                        "and work normally, it just won't show its radius on the ground.");

                var piece = new Jotunn.Entities.CustomPiece(prefab, fixReference: true, config);
                if (!Jotunn.Managers.PieceManager.Instance.AddPiece(piece))
                {
                    Plugin.Log.LogError(
                        $"PieceManager rejected '{LeashPrefabName}' (invalid piece, or the name " +
                        "is already taken) - logging will have nothing to bind to.");
                    return;
                }

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
        // FireAversion's Rebuild(). Piece.m_allPieces is private (confirmed
        // via dump.py against the real assembly - the public surface only
        // exposes Piece.GetAllPiecesInRadius(center, radius, list), which
        // needs a center point we don't have for a world-wide leash scan), so
        // this falls back to a timer-gated FindObjectsByType sweep instead -
        // the same pattern TrollLogging.cs already uses for felled-log and
        // item-drop scans.
        static GameObject[] _snap = new GameObject[8];
        static int _n;
        static float _nextScan;

        // Same idea, same timer, for real placed chests - every chest lookup
        // below used to call FindObjectsByType<Container>() itself, which was
        // fine while those calls were rare (an occasional deposit-trip pick),
        // but became the same class of bug as the Piece scan above the moment
        // PassiveDepositWood needed to check on every logging tick instead of
        // only once wood hit its threshold. One shared, timer-gated scan for
        // every logging troll instead of one raw scan per troll per tick.
        static Container[] _chestSnap = new Container[8];
        static int _chestN;

        public static void Reset() { _n = 0; _chestN = 0; _nextScan = 0f; }

        public static void Tick()
        {
            if (Time.time < _nextScan) return;
            _nextScan = Time.time + Plugin.LoggingLeashScanInterval;

            // FindObjectsByType<Piece> walks EVERY placed building piece in
            // the loaded world - a real base can easily have thousands, and
            // until this check existed this ran unconditionally every
            // interval as long as the LOGGING FEATURE was merely enabled,
            // whether or not anything was actually logging. Skipped entirely
            // unless at least one tracked creature is currently in logging
            // mode - see the identical reasoning in TrollLogging.Tick.
            bool anyoneLogging = false;
            foreach (var st in CreatureState.AllTracked)
                if (st.IsLogging) { anyoneLogging = true; break; }
            if (!anyoneLogging) return;

            Rebuild();
        }

        static void Rebuild()
        {
            _n = 0;
            var all = UnityEngine.Object.FindObjectsByType<Piece>(FindObjectsSortMode.None);
            float radius = Plugin.LoggingLeashRadius;

            for (int i = 0; i < all.Length; i++)
            {
                var p = all[i];
                if (p == null) continue;
                var go = p.gameObject;
                if (go == null) continue;
                if (!string.Equals(CreatureRules.CleanName(go.name), LeashPrefabName,
                        StringComparison.OrdinalIgnoreCase)) continue;

                // Keeps every already-placed leash's ring matched to the
                // current config value - picks up a live radius change
                // (Configuration Manager) within one scan interval, same as
                // every other logging tunable.
                var marker = go.GetComponentInChildren<CircleProjector>(true);
                if (marker != null && !Mathf.Approximately(marker.m_radius, radius))
                    marker.m_radius = radius;

                if (_n >= _snap.Length) Array.Resize(ref _snap, _snap.Length * 2);
                _snap[_n++] = go;

                // Remembered independently of the piece itself: once this
                // zone unloads the leash GameObject goes with it, so there
                // would be nothing left to find and nothing to hold the area
                // open. See ZoneKeepAlive.
                ZoneKeepAlive.Remember(go.transform.position);
            }

            _chestN = 0;
            var allContainers = UnityEngine.Object.FindObjectsByType<Container>(FindObjectsSortMode.None);
            for (int i = 0; i < allContainers.Length; i++)
            {
                var c = allContainers[i];
                if (c == null || !IsRealChest(c)) continue;
                if (_chestN >= _chestSnap.Length) Array.Resize(ref _chestSnap, _chestSnap.Length * 2);
                _chestSnap[_chestN++] = c;
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

        /// <summary>True for a real, placed-in-the-world chest - false for a
        /// live Container another mod spawns for its own purposes (a
        /// backpack mod's proxy container representing a player's worn
        /// backpack, confirmed live in the log: "AB_BackpackProxy" was the
        /// nearest unnamed Container in range and silently absorbed every
        /// deposit that didn't match a named chest). Every real placed piece
        /// carries a Piece component; a dynamically-spawned proxy does not.</summary>
        ///
        /// Also must be PLACED BY A PLAYER: world-generated containers (the
        /// buried treasure chests, dungeon loot chests) are real pieces too,
        /// and were receiving deposits - confirmed live as Wood, Stone and
        /// Resin going into 'TreasureChest_meadows_buried'.
        static bool IsRealChest(Container c)
        {
            var p = c.GetComponent<Piece>();
            return p != null && p.IsPlacedByPlayer();
        }

        /// <summary>True if any named chest inside this leash's radius
        /// accepts <paramref name="item"/>. That is what makes an item
        /// "catalogued": it has a dedicated home, so it must never be
        /// dropped into an unnamed catch-all chest just because one happened
        /// to be closer.</summary>
        public static bool HasNamedChestFor(Vector3 leashPos, string item)
        {
            float rSq = Plugin.LoggingLeashRadius * Plugin.LoggingLeashRadius;
            for (int i = 0; i < _chestN; i++)
            {
                var c = _chestSnap[i];
                if (c == null) continue;
                if ((c.transform.position - leashPos).sqrMagnitude > rSq) continue;
                string name = StorageNaming.GetName(c);
                if (!string.IsNullOrEmpty(name) && StorageNaming.Accepts(name, item)) return true;
            }
            return false;
        }

        /// <summary>Nearest vanilla Container within the SAME leash's radius -
        /// measured from the leash, not the troll, so a chest at the far edge
        /// of the radius still counts even while the troll is elsewhere
        /// inside it.</summary>
        public static GameObject FindNearestChest(Vector3 leashPos)
        {
            GameObject best = null;
            float bestSq = float.MaxValue;
            float rSq = Plugin.LoggingLeashRadius * Plugin.LoggingLeashRadius;

            for (int i = 0; i < _chestN; i++)
            {
                var c = _chestSnap[i];
                if (c == null) continue;
                var go = c.gameObject;
                if (go == null) continue;

                float sq = (go.transform.position - leashPos).sqrMagnitude;
                if (sq > rSq) continue;
                if (sq < bestSq) { bestSq = sq; best = go; }
            }
            return best;
        }

        /// <summary>Same search, but a chest the player named (see
        /// StorageNaming) that matches one of the troll's carried items wins
        /// over any distance comparison against an unnamed one - a dedicated
        /// destination beats "merely closer". Only when no named match exists
        /// at all does this fall back to the nearest unnamed (catch-all)
        /// chest, exactly like FindNearestChest above.</summary>
        public static GameObject FindDepositChest(Vector3 leashPos, IEnumerable<string> carriedItems)
        {
            float rSq = Plugin.LoggingLeashRadius * Plugin.LoggingLeashRadius;

            GameObject bestNamed = null; float bestNamedSq = float.MaxValue;
            GameObject bestUnnamed = null; float bestUnnamedSq = float.MaxValue;

            for (int i = 0; i < _chestN; i++)
            {
                var c = _chestSnap[i];
                if (c == null) continue;
                var go = c.gameObject;
                if (go == null) continue;

                float sq = (go.transform.position - leashPos).sqrMagnitude;
                if (sq > rSq) continue;

                string storageName = StorageNaming.GetName(c);
                if (string.IsNullOrEmpty(storageName))
                {
                    if (sq < bestUnnamedSq) { bestUnnamedSq = sq; bestUnnamed = go; }
                    continue;
                }

                foreach (var item in carriedItems)
                {
                    if (StorageNaming.Accepts(storageName, item))
                    {
                        if (sq < bestNamedSq) { bestNamedSq = sq; bestNamed = go; }
                        break;
                    }
                }
            }

            return bestNamed != null ? bestNamed : bestUnnamed;
        }

        /// <summary>A REAL chest already within <paramref name="range"/> of
        /// <paramref name="pos"/> right now that accepts at least one of
        /// <paramref name="items"/> (named match, or unnamed catch-all) -
        /// used for a passive "walked past a chest" deposit, never as a
        /// navigation target. Unlike FindDepositChest, this is NOT measured
        /// from the leash and does not fall back to "nearest regardless of
        /// distance" - null simply means no chest happens to be close by
        /// right now.</summary>
        public static GameObject FindChestWithinRange(Vector3 pos, float range, IEnumerable<string> items)
        {
            float rSq = range * range;

            // Named-first, exactly like FindDepositChest - tracked separately
            // rather than taking whichever accepting chest happens to be
            // nearest. An unnamed chest accepts EVERYTHING, so a nearest-wins
            // search handed it the entire load whenever it sat closer than
            // the sorted chests, and since this runs every tick on walk-by it
            // emptied the troll into the catch-all before the deliberate
            // named-chest trip ever had a chance to fire. Confirmed live as
            // "deposits strictly into the unnamed chest".
            GameObject bestNamed = null; float bestNamedSq = float.MaxValue;
            GameObject bestUnnamed = null; float bestUnnamedSq = float.MaxValue;

            for (int i = 0; i < _chestN; i++)
            {
                var c = _chestSnap[i];
                if (c == null) continue;
                var go = c.gameObject;
                if (go == null) continue;

                float sq = (go.transform.position - pos).sqrMagnitude;
                if (sq > rSq) continue;

                string storageName = StorageNaming.GetName(c);
                if (string.IsNullOrEmpty(storageName))
                {
                    if (sq < bestUnnamedSq) { bestUnnamedSq = sq; bestUnnamed = go; }
                    continue;
                }

                foreach (var item in items)
                {
                    if (StorageNaming.Accepts(storageName, item))
                    {
                        if (sq < bestNamedSq) { bestNamedSq = sq; bestNamed = go; }
                        break;
                    }
                }
            }

            // Deposit() itself only ever moves what the chosen chest actually
            // accepts, so picking the named one leaves everything else in
            // carry for the next chest rather than stranding it.
            return bestNamed != null ? bestNamed : bestUnnamed;
        }
    }
}
