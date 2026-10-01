using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace CreatureControl
{
    /// <summary>
    /// Which world objects the logging troll may chop, on top of whatever
    /// already carries a TreeBase/TreeLog component (every vanilla tree and,
    /// once felled, its log). Mirrors FireSources - config-driven rather than
    /// a hard-coded prefab list - but EXCLUDE-only: TreeBase/TreeLog is
    /// already the real signal, so there is nothing to force in that doesn't
    /// carry one of those components.
    /// </summary>
    public static class TreeSources
    {
        public class Store
        {
            public readonly Dictionary<string, bool> ByName =
                new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            public int Count => ByName.Count;
        }

        static Store _active = new Store();
        public static int Count => _active.ByName.Count;

        public static void Install(Store s) => _active = s ?? new Store();

        /// <summary>Exact match only - unlike FireSources' prefix walk, a
        /// tree's own GameObject name already IS the prefab name; nothing
        /// carries a tree the way a torch is carried by its holder.</summary>
        public static bool TryMatch(string name, out bool loggable)
        {
            loggable = false;
            if (string.IsNullOrEmpty(name)) return false;
            return _active.ByName.TryGetValue(name, out loggable);
        }
    }

    /// <summary>
    /// STATUS: WORK IN PROGRESS - not yet build-verified. See
    /// LOGGING-implementation-notes.md in the repo root before relying on
    /// this in a real game; remove this notice once that file's open items
    /// are resolved and it's been run and tested.
    ///
    /// Drives a logging troll: which tree it works, when it hits it, what it
    /// carries, and when it hands the stack off to a chest. TotemBind.cs owns
    /// WHERE (the leash bind, the radius, the nearest chest); this owns WHAT
    /// THE TROLL DOES, and is the sole caller of Threat.BaseTick for the tick
    /// it takes over - see Patches.cs, which folds this in right after the
    /// existing fear/flee check so the two can never both drive a tick.
    /// </summary>
    public static class TrollLogging
    {
        // -------------------------------------------------------- world scan
        // One shared scan for every logging troll, on a timer - mirrors
        // FireAversion's Rebuild(). Sourced from FindObjectsByType rather than
        // a global static list: unlike EffectArea, neither TreeBase nor
        // TreeLog keeps one of its own, and this only runs a few times a
        // minute, not per frame.
        static GameObject[] _snap = new GameObject[128];
        static int _n;
        static float _nextScan;

        // Shared with the same cadence as the tree/rock scan above, rather
        // than each logging troll running its own FindObjectsByType<ItemDrop>
        // every tick - see PassivePickup, which just filters this cached
        // list by distance from wherever a given troll happens to be.
        static ItemDrop[] _itemSnap = Array.Empty<ItemDrop>();

        // Staging buffer for an in-progress scan. The live _snap stays
        // untouched and fully queryable until a scan completes and swaps,
        // so spreading the work over several frames never exposes a
        // half-built world to FindNearestTree.
        static GameObject[] _build = new GameObject[128];
        static int _buildN;
        static int _phase;
        static int _pTrees, _pLogs, _pStumps, _pRocks;

        // Self-tuning for the WearNTear (modded stump) scan - see phase 3.
        static int _moddedStumpMisses;
        static int _scanCount;
        const int ModdedStumpProbeAfter = 3;    // empty scans before backing off
        const int ModdedStumpProbeEvery = 20;   // then re-check this often

        public static void Reset()
        {
            _n = 0;
            _buildN = 0;
            _phase = 0;
            _nextScan = 0f;
            _itemSnap = Array.Empty<ItemDrop>();
            _oreItems = null;   // re-derive against the new world's prefabs
        }

        public static void Tick()
        {
            // Mid-scan: keep going, one step per frame, no interval wait.
            if (_phase == 0 && Time.time < _nextScan) return;

            // Each FindObjectsByType walks every object of that type in every
            // loaded zone. Doing all of them in one frame was a visible
            // stall once Keep Leash Zones Loaded started holding extra zones
            // open - the loaded world roughly tripled (7000+ objects against
            // ~2400), and the whole scan landed on a single frame as a
            // 10-20fps drop every interval. Skipped entirely unless at least
            // one tracked creature is actually in logging mode.
            bool anyoneLogging = false;
            foreach (var st in CreatureState.AllTracked)
                if (st.IsLogging) { anyoneLogging = true; break; }
            if (!anyoneLogging) { _phase = 0; return; }

            RebuildStep();
        }

        /// <summary>One slice of the world scan per call. Splitting it this
        /// way turns a single large stall into several small ones spread
        /// across consecutive frames - the same total work, just never all at
        /// once. The results land in a staging buffer and only replace the
        /// live snapshot on the final step.</summary>
        static void RebuildStep()
        {
            switch (_phase)
            {
                case 0:
                    _buildN = 0;
                    _saplingExclusions.Clear();
                    AddAll(UnityEngine.Object.FindObjectsByType<TreeBase>(FindObjectsSortMode.None));
                    _pTrees = _buildN;
                    break;

                case 1:
                    AddAll(UnityEngine.Object.FindObjectsByType<TreeLog>(FindObjectsSortMode.None));
                    _pLogs = _buildN;
                    break;

                case 2:
                    AddStumps(UnityEngine.Object.FindObjectsByType<Destructible>(FindObjectsSortMode.None));
                    break;

                case 3:
                    // The single most expensive scan here: WearNTear is on
                    // EVERY building piece in every loaded zone, so this walks
                    // the player's whole base. It exists only to catch mods
                    // that build stumps on WearNTear instead of Destructible,
                    // which may well be nothing in a given world - vanilla
                    // stumps are Destructible and handled in phase 2.
                    //
                    // So it self-tunes: once it has produced nothing several
                    // scans running, it drops to an occasional re-check
                    // instead of paying full price every time. A mod that
                    // does use WearNTear stumps still gets picked up, just on
                    // the re-check rather than instantly.
                    if (_moddedStumpMisses < ModdedStumpProbeAfter || (_scanCount % ModdedStumpProbeEvery) == 0)
                    {
                        int before = _buildN;
                        AddModdedStumps(UnityEngine.Object.FindObjectsByType<WearNTear>(FindObjectsSortMode.None));
                        if (_buildN == before)
                        {
                            if (_moddedStumpMisses < int.MaxValue) _moddedStumpMisses++;
                        }
                        else _moddedStumpMisses = 0;
                    }
                    _pStumps = _buildN;
                    break;

                case 4:
                    AddAll(UnityEngine.Object.FindObjectsByType<MineRock>(FindObjectsSortMode.None));
                    break;

                case 5:
                    AddAll(UnityEngine.Object.FindObjectsByType<MineRock5>(FindObjectsSortMode.None));
                    _pRocks = _buildN;
                    break;

                default:
                    _itemSnap = UnityEngine.Object.FindObjectsByType<ItemDrop>(FindObjectsSortMode.None);

                    // Swap the completed scan in as the live one.
                    var tmp = _snap; _snap = _build; _build = tmp;
                    _n = _buildN;

                    _phase = 0;
                    _scanCount++;
                    _nextScan = Time.time + Plugin.LoggingTreeScanInterval;

                    if (Plugin.Verbose && Plugin.LoggingDiagEnabled)
                    {
                        Plugin.Log.LogInfo(
                            $"[logging] rebuild: {_pTrees} standing tree(s), " +
                            $"{_pLogs - _pTrees} log(s), {_pStumps - _pLogs} stump(s), " +
                            $"{_pRocks - _pStumps} rock/ore(s) - {_n} total in range of any leash.");
                        if (_saplingExclusions.Count > 0)
                            Plugin.Log.LogInfo(
                                "[logging] excluded as saplings (never targeted, by design - but check these ARE " +
                                "actually regrowing young trees and not a permanently-small variant that just " +
                                "happens to share the naming convention): " + string.Join(", ", _saplingExclusions));
                    }
                    return;
            }

            _phase++;
        }

        // A felled stump is a Destructible + DropOnDestroyed - NOT a
        // WearNTear, and not a TreeBase/TreeLog either. This was wrong for
        // this mod's entire history and is the single root cause behind
        // "0 stump(s)" in every rebuild ever logged, stumps never being
        // cleared, replanting therefore never firing, AND small always-small
        // tree variants reading as "No target": the scan was looking for a
        // component these objects simply do not have. Confirmed by dumping
        // the real prefabs out of the game files - every "stub" container
        // holds 12 Destructible + 12 DropOnDestroyed and ZERO WearNTear, and
        // the small beech is the same shape:
        //   stump       -> Destructible, m_destructibleType = 2, health 100
        //   small beech -> Destructible, m_destructibleType = 2, health 20
        //
        // DestructibleType.Tree (== 2) is what makes this safe to scan
        // wholesale: Destructible on its own is a very broad component (every
        // barrel, crate and bit of breakable clutter has one, as Default),
        // but filtering to Tree keeps it to exactly the natural wood-family
        // objects a logging troll should be working - no name matching, no
        // guessing at prefab naming conventions.
        static bool IsTreeDestructible(Destructible d) =>
            d != null && d.m_destructibleType == DestructibleType.Tree;

        /// <summary>A natural destructible built on WearNTear instead of
        /// Destructible. Vanilla stumps are Destructible, but that is NOT
        /// universal - a tree mod is free to build its stumps on WearNTear,
        /// and switching the scan wholesale to Destructible would silently
        /// drop exactly those (confirmed live: modded tree stumps stopped
        /// being targeted). Both paths are scanned now rather than one.
        ///
        /// Told apart from ordinary building pieces the same way
        /// TotemBind.IsRealChest tells a real chest from a modded proxy:
        /// every player-placeable piece (wall, floor, fence, chest) carries a
        /// Piece component and nothing natural does, and a genuine
        /// DropOnDestroyed table means it actually yields something.</summary>
        static bool IsNaturalWearNTear(GameObject go) =>
            go != null &&
            go.GetComponent<Piece>() == null &&
            go.GetComponent<DropOnDestroyed>() != null;

        /// <summary>A boulder/rock built on plain Destructible rather than
        /// MineRock/MineRock5. Confirmed from the real prefabs: under
        /// Props/Rocks, Destructible is the MOST common component (62 of
        /// them, more than MineRock5's 34) - and every single one is
        /// m_destructibleType = Default, never Tree. Scanning only Tree-type
        /// Destructibles therefore made most of the world's rocks invisible
        /// to the troll, which is exactly "he isn't mining every rock in his
        /// radius".
        ///
        /// Default is the catch-all type though - barrels, crates and
        /// scenery are Default too - so accepting it wholesale would have a
        /// troll punching furniture. The precise discriminator is what the
        /// thing actually YIELDS: a real rock drops stone or ore, a barrel
        /// drops coins and food. That keeps this self-limiting, and picks up
        /// modded rocks for free as long as they drop something recognisable.
        /// Anything player-built (a Piece) is excluded outright.</summary>
        static bool IsMineableRock(Destructible d)
        {
            if (d == null || d.m_destructibleType != DestructibleType.Default) return false;

            var go = d.gameObject;
            if (go == null || go.GetComponent<Piece>() != null) return false;

            var drop = go.GetComponent<DropOnDestroyed>();
            var table = drop != null ? drop.m_dropWhenDestroyed : null;
            if (table == null || table.m_drops == null) return false;

            var ore = OreItems;
            foreach (var entry in table.m_drops)
            {
                if (entry.m_item == null) continue;
                if (ore.Contains(entry.m_item.name)) return true;
            }
            return false;
        }

        static HashSet<string> _oreItems;

        /// <summary>Every item that counts as "stone or ore" for the purpose
        /// of deciding whether a plain Destructible boulder is worth mining.
        ///
        /// Derived rather than hand-listed: MineRock and MineRock5 ARE the
        /// game's mineable nodes by definition, so whatever their drop tables
        /// yield is exactly the stone/ore item set - for vanilla and for any
        /// mod's ore alike, with nothing to maintain per-item. The hardcoded
        /// StoneOreItemNames seeds it so the deliberate-deposit-trip rules
        /// stay stable even before a scene exists.
        ///
        /// This is what widens boulder detection past the handful of names
        /// that were listed by hand: any rock dropping anything a real ore
        /// node also drops now qualifies. Player-built pieces are still
        /// excluded outright by the Piece check above, so this can never
        /// pull in a wall or a chest no matter what it would drop.</summary>
        static HashSet<string> OreItems
        {
            get
            {
                if (_oreItems != null) return _oreItems;

                var set = new HashSet<string>(StoneOreItemNames, StringComparer.OrdinalIgnoreCase);

                var scene = ZNetScene.instance;
                var prefabs = scene != null ? scene.m_prefabs : null;
                if (prefabs == null) return set;   // no scene yet - don't cache a partial answer

                foreach (var go in prefabs)
                {
                    if (go == null) continue;
                    var mr = go.GetComponent<MineRock>();
                    if (mr != null) AddOreDrops(set, mr.m_dropItems);
                    var mr5 = go.GetComponent<MineRock5>();
                    if (mr5 != null) AddOreDrops(set, mr5.m_dropItems);
                }

                _oreItems = set;
                if (Plugin.Verbose)
                    Plugin.Log.LogInfo(
                        $"[logging] mineable-drop catalog: {set.Count} item(s) counted as stone/ore - " +
                        string.Join(", ", set.OrderBy(s => s, StringComparer.OrdinalIgnoreCase)));
                return set;
            }
        }

        static void AddOreDrops(HashSet<string> set, DropTable table)
        {
            if (table == null || table.m_drops == null) return;
            foreach (var d in table.m_drops)
                if (d.m_item != null) set.Add(d.m_item.name);
        }

        // A freshly (re)planted tree - whether it grew there naturally or a
        // logging troll just put it there - carries "sapling" in its name in
        // every case this mod has catalogued (vanilla and RtDBiomes alike).
        // Excluded from every scan below on sight: a troll should never
        // damage what it, or the world, is still growing back.
        static bool IsSapling(string name) => name.IndexOf("sapling", StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>True if <paramref name="go"/> is something a logging
        /// troll could actually log or mine - the same categories Chop()
        /// itself knows how to hit (TreeBase/TreeLog/MineRock/MineRock5, or a
        /// name-matched stump), checked the same way. Walks up from whatever
        /// collider a raycast happened to land on rather than assuming the
        /// hit object IS the root, since that's how Plugin's Force Target
        /// hotkey needs to check a fresh raycast hit rather than an
        /// already-resolved LogTargetTree. Used by that hotkey's dual-use
        /// mode so a player can only ever force a chop/mine job onto
        /// something that could actually accept one - never onto a wall, a
        /// boat, or anything else that merely happens to have a collider.</summary>
        public static bool IsLoggableOrMineable(GameObject go)
        {
            return ResolveChopRoot(go) != null;
        }

        /// <summary>Resolves the actual root GameObject a chop/mine order
        /// should target for <paramref name="go"/> - whichever of
        /// TreeBase/TreeLog/MineRock/MineRock5/stump-WearNTear its collider
        /// belongs to. Null if none apply (including a sapling, which is
        /// deliberately never a valid target here even though a young tree
        /// still carries a real TreeBase).</summary>
        public static GameObject ResolveChopRoot(GameObject go)
        {
            if (go == null) return null;
            var name = CreatureRules.CleanName(go.name);
            if (IsSapling(name)) return null;

            var tree = go.GetComponentInParent<TreeBase>();
            if (tree != null) return tree.gameObject;
            var log = go.GetComponentInParent<TreeLog>();
            if (log != null) return log.gameObject;
            var rock = go.GetComponentInParent<MineRock>();
            if (rock != null) return rock.gameObject;
            var rock5 = go.GetComponentInParent<MineRock5>();
            if (rock5 != null) return rock5.gameObject;

            var dest = go.GetComponentInParent<Destructible>();
            if (IsTreeDestructible(dest) || IsMineableRock(dest)) return dest.gameObject;

            // Modded stumps may be WearNTear-based instead - see
            // IsNaturalWearNTear.
            var wnt = go.GetComponentInParent<WearNTear>();
            if (wnt != null && IsNaturalWearNTear(wnt.gameObject)) return wnt.gameObject;

            return null;
        }

        // Diagnostic only: distinct names excluded by IsSapling this Rebuild -
        // tests a specific live hypothesis (small always-small tree variants,
        // "the little ones you can break with your fist", never getting
        // targeted at all) against a specific cause: IsSapling excludes ANY
        // name containing "sapling" from every scan, on purpose, so a
        // regrowing young tree is never damaged - but if a permanently-small
        // decorative variant's real prefab name also happens to contain that
        // substring (a naming coincidence, not an actual regrowing sapling),
        // it would be excluded forever too, which looks identical to "never
        // detected" from in-game. Capped and deduplicated so a forest full of
        // real saplings can't flood the log.
        static readonly HashSet<string> _saplingExclusions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        static void AddAll<T>(T[] items) where T : Component
        {
            bool diag = Plugin.Verbose && Plugin.LoggingDiagEnabled;

            for (int i = 0; i < items.Length; i++)
            {
                var c = items[i];
                if (c == null) continue;
                var go = c.gameObject;
                if (go == null) continue;

                var name = CreatureRules.CleanName(go.name);
                if (IsSapling(name))
                {
                    if (diag && _saplingExclusions.Count < 25) _saplingExclusions.Add(name);
                    continue;
                }
                if (TreeSources.TryMatch(name, out var forced) && !forced) continue;

                if (_buildN >= _build.Length) Array.Resize(ref _build, _build.Length * 2);
                _build[_buildN++] = go;
            }
        }

        /// <summary>Stumps AND permanently-small tree variants - everything
        /// wood-family that carries neither TreeBase nor TreeLog. See
        /// IsTreeDestructible for why this scans Destructible rather than the
        /// WearNTear this used to (wrongly) look for.</summary>
        /// <summary>The WearNTear half of stump scanning - see
        /// IsNaturalWearNTear for why both component families are covered
        /// rather than just the vanilla one.</summary>
        static void AddModdedStumps(WearNTear[] items)
        {
            for (int i = 0; i < items.Length; i++)
            {
                var c = items[i];
                if (c == null) continue;
                var go = c.gameObject;
                if (go == null) continue;
                if (!IsNaturalWearNTear(go)) continue;

                var name = CreatureRules.CleanName(go.name);
                if (IsSapling(name)) continue;
                if (TreeSources.TryMatch(name, out var forced) && !forced) continue;

                if (_buildN >= _build.Length) Array.Resize(ref _build, _build.Length * 2);
                _build[_buildN++] = go;
            }
        }

        static void AddStumps(Destructible[] items)
        {
            for (int i = 0; i < items.Length; i++)
            {
                var c = items[i];
                if (c == null) continue;
                // Tree-type: stumps and permanently-small trees.
                // Default-type that yields stone/ore: plain boulders, which
                // are the bulk of the world's rocks - see IsMineableRock.
                if (!IsTreeDestructible(c) && !IsMineableRock(c)) continue;
                var go = c.gameObject;
                if (go == null) continue;

                var name = CreatureRules.CleanName(go.name);
                if (IsSapling(name)) continue;
                if (TreeSources.TryMatch(name, out var forced) && !forced) continue;

                if (_buildN >= _build.Length) Array.Resize(ref _build, _build.Length * 2);
                _build[_buildN++] = go;
            }
        }

        /// <summary>Nearest scanned tree/log/stump/rock/ore inside the leash's
        /// radius - deliberately measured from the LEASH, not the troll, so
        /// one at the far edge of the radius is never skipped just because
        /// the troll itself happens to be standing near the boundary. Kept
        /// the pre-existing "tree" name despite now covering rock/ore too,
        /// to avoid a mechanical rename across every call site.</summary>
        static GameObject FindNearestTree(Vector3 pos, Vector3 leashPos, GameObject exclude)
        {
            GameObject best = null;
            float bestSq = float.MaxValue;
            float leashRSq = Plugin.LoggingLeashRadius * Plugin.LoggingLeashRadius;

            for (int i = 0; i < _n; i++)
            {
                var go = _snap[i];
                if (go == null || go == exclude) continue;

                var gp = go.transform.position;
                if ((gp - leashPos).sqrMagnitude > leashRSq) continue;

                float sq = (gp - pos).sqrMagnitude;
                if (sq < bestSq) { bestSq = sq; best = go; }
            }
            return best;
        }

        /// <summary>Nearest known-catalog item drop anywhere inside the leash -
        /// not just within PassivePickup's short walk-by radius. Used only
        /// when there's nothing left to chop: rather than a troll standing
        /// there idle while a pile of wood it dropped earlier (chest was full,
        /// slot was capped, whatever) sits fifteen metres away in plain
        /// sight, it deliberately walks over. PassivePickup itself still does
        /// the actual pickup the moment it's in range - this only supplies
        /// the missing navigation.</summary>
        static GameObject FindNearestItem(Vector3 pos, Vector3 leashPos)
        {
            GameObject best = null;
            float bestSq = float.MaxValue;
            float leashRSq = Plugin.LoggingLeashRadius * Plugin.LoggingLeashRadius;

            for (int i = 0; i < _itemSnap.Length; i++)
            {
                var d = _itemSnap[i];
                if (d == null) continue;
                var go = d.gameObject;
                if (go == null) continue;

                var gp = go.transform.position;
                if ((gp - leashPos).sqrMagnitude > leashRSq) continue;

                string name = d.m_itemData?.m_dropPrefab != null
                    ? d.m_itemData.m_dropPrefab.name
                    : CreatureRules.CleanName(go.name);
                if (!TreeDropCatalog.Contains(name)) continue;

                float sq = (gp - pos).sqrMagnitude;
                if (sq < bestSq) { bestSq = sq; best = go; }
            }
            return best;
        }

        /// <summary>A felled standing tree usually leaves a TreeLog at (near)
        /// the same spot - still choppable, into wood proper. Not sourced from
        /// the shared snapshot (which only refreshes every few seconds) since
        /// this has to see a log that appeared moments ago.</summary>
        static GameObject FindSuccessorLog(Vector3 nearPos)
        {
            float R = Plugin.LoggingSuccessorRadius;
            var logs = UnityEngine.Object.FindObjectsByType<TreeLog>(FindObjectsSortMode.None);

            GameObject best = null;
            float bestSq = R * R;

            // Diagnostic only: tracks the closest TreeLog regardless of R, so
            // a miss can be told apart from "nothing exists" - a tall felled
            // tree's log can easily land further than R from the trunk it
            // fell from.
            GameObject nearestAny = null;
            float nearestAnySq = float.MaxValue;

            for (int i = 0; i < logs.Length; i++)
            {
                var go = logs[i] != null ? logs[i].gameObject : null;
                if (go == null) continue;
                float sq = (go.transform.position - nearPos).sqrMagnitude;
                if (sq <= bestSq) { bestSq = sq; best = go; }
                if (sq < nearestAnySq) { nearestAnySq = sq; nearestAny = go; }
            }

            if (Plugin.Verbose && Plugin.LoggingDiagEnabled)
            {
                if (best != null)
                    Plugin.Log.LogInfo($"[logging] successor: found '{best.name}' at {Mathf.Sqrt(bestSq):0.0}m (R={R}).");
                else if (nearestAny != null)
                    Plugin.Log.LogInfo(
                        $"[logging] successor: none within R={R} - nearest real TreeLog is " +
                        $"'{nearestAny.name}' at {Mathf.Sqrt(nearestAnySq):0.0}m. Checking for a stump next.");
                else
                    Plugin.Log.LogInfo("[logging] successor: no TreeLog exists anywhere in the loaded area. Checking for a stump next.");
            }

            return best;
        }

        /// <summary>Same idea as FindSuccessorLog, once the log chain is
        /// fully exhausted: a felled tree's stump is left standing at (near)
        /// the trunk's own position, and per the fell -> log -> stump ->
        /// replant sequence this class follows, it's the last thing chopped
        /// before the space is clear to plant back into.</summary>
        static GameObject FindSuccessorStump(Vector3 nearPos)
        {
            float R = Plugin.LoggingSuccessorRadius;
            var all = UnityEngine.Object.FindObjectsByType<Destructible>(FindObjectsSortMode.None);

            GameObject best = null;
            float bestSq = R * R;

            for (int i = 0; i < all.Length; i++)
            {
                if (!IsTreeDestructible(all[i])) continue;
                var go = all[i].gameObject;
                if (go == null) continue;

                var name = CreatureRules.CleanName(go.name);
                if (IsSapling(name)) continue;

                float sq = (go.transform.position - nearPos).sqrMagnitude;
                if (sq <= bestSq) { bestSq = sq; best = go; }
            }

            if (Plugin.Verbose && Plugin.LoggingDiagEnabled)
                Plugin.Log.LogInfo(best != null
                    ? $"[logging] successor: found stump '{best.name}' at {Mathf.Sqrt(bestSq):0.0}m (R={R})."
                    : "[logging] successor: no stump found either - this tree is fully cleared.");

            return best;
        }

        /// <summary>The next thing to chop after whatever LogTargetTree just
        /// was: the log chain first, then the stump once that's exhausted.
        /// Null means both are gone - the tree is fully cleared.</summary>
        static GameObject FindSuccessor(Vector3 nearPos)
        {
            var log = FindSuccessorLog(nearPos);
            return log != null ? log : FindSuccessorStump(nearPos);
        }

        // ------------------------------------------------------------- tick

        /// <summary>
        /// Entry point from Patches.cs. Returns true to hand the tick to
        /// vanilla (in combat, unbound, or nothing to do); false with __result
        /// set means we took it - see the [HarmonyPriority(Priority.Last)]
        /// UpdateAI prefix for the calling convention.
        /// </summary>
        public static bool DriveTick(MonsterAI ai, CreatureState st, float dt, ref bool __result)
        {
            // A tame that isn't logging normally hands the tick straight back
            // to vanilla - unless it's carrying a player-issued chop/mine
            // order, which is the "come help me work this vein" case and
            // deliberately needs no leash behind it.
            if (!st.IsLogging)
            {
                // CanLog is the hard gate: only a creature configured with
                // loggingMode = true (a troll) can ever be driven by this at
                // all, so a bear or a wolf can never be sent at a tree by a
                // mis-aimed Force Target press.
                if (!st.CanLog || st.ForcedChopTarget == null || !Plugin.ForceChopWithoutLeash) return true;
                return DriveForcedChopNoLeash(ai, st, dt, ref __result);
            }

            // Combat always wins, and is read directly off vanilla's own
            // state rather than tracked separately - OnDamaged (Patches.cs)
            // sets both independently of whichever tick system is driving,
            // so this is live even on a tick we never took.
            //
            // The raw signal is NOT trusted on its own: vanilla's own target
            // re-acquisition (MonsterAI.FindEnemy) only runs every ~2-6s, not
            // every tick, so GetTargetCreature()/IsAlerted() can both read
            // false for a tick or two while something is still actively
            // attacking. Reacting to that instantly - handing the tick back
            // to logging mid-fight, then back to combat next tick - is
            // confirmed live as "bounces between aggro and logging" while a
            // Brute wailed on it. A short grace window after the last time
            // combat was genuinely seen absorbs that flicker without
            // meaningfully delaying the return to work once a fight is truly
            // over (LoggingReturnTimeout's own much longer window is what
            // actually governs walking back afterward).
            bool rawCombat = ai.GetTargetCreature() != null || ai.IsAlerted();
            if (rawCombat) st.LastCombatSignalAt = Time.time;
            bool inCombat = st.LastCombatSignalAt >= 0f &&
                            Time.time - st.LastCombatSignalAt < Plugin.LoggingCombatGraceSeconds;

            if (inCombat)
            {
                st.MarkInCombat();
                // A temporary distraction, not abandonment - the target,
                // its position, and which species it is are all kept, so
                // the exact same job resumes once combat clears instead of
                // the troll losing track and picking something else.
                st.PauseLoggingWork();
                return true;
            }

            Vector3 pos = ai.transform.position;

            if (!TotemBind.ResolveBind(st, pos))
            {
                // No leash in range (or none placed at all). Nothing to do;
                // leave the troll to vanilla's own idle/follow behaviour.
                st.MarkCombatClearedIfNew();
                return true;
            }

            var leash = st.BoundLeash;
            Vector3 leashPos = leash.transform.position;

            if (!TotemBind.IsInsideLeash(leash, pos))
            {
                // Combat (or a stray push) put it outside its own radius -
                // but so does simply walking around the far side of a tree
                // that happens to sit on the boundary. Reacting the instant
                // the line is crossed meant abandoning a job half-done and
                // marching back to the middle over a step it was about to
                // take back on its own, so it gets a grace period first and
                // keeps working through it.
                if (st.OutsideLeashSince < 0f) st.OutsideLeashSince = Time.time;

                if (Time.time - st.OutsideLeashSince >= Plugin.LoggingLeashGraceSeconds)
                {
                    st.MarkCombatClearedIfNew();
                    return DriveReturn(ai, st, dt, leashPos, ref __result);
                }

                // Still inside the grace window - carry on with the job.
                st.MarkCombatClearedIfNew();
                return DriveWork(ai, st, dt, leashPos, ref __result);
            }

            st.OutsideLeashSince = -1f;
            st.MarkCombatClearedIfNew();
            return DriveWork(ai, st, dt, leashPos, ref __result);
        }

        /// <summary>A player-issued chop/mine order carried out by a tame
        /// that isn't in logging mode and has no leash - "come help me work
        /// this vein". Deliberately much smaller than the full logging
        /// drive: no leash, no deposit trips, no replanting, no successor
        /// chain. It walks to the one thing it was pointed at, works it until
        /// it's gone, then stands down and hands the tick back to vanilla.
        ///
        /// Combat still wins outright, same as the leashed path - an order to
        /// mine is not an order to ignore something hitting you.</summary>
        static bool DriveForcedChopNoLeash(MonsterAI ai, CreatureState st, float dt, ref bool __result)
        {
            bool rawCombat = ai.GetTargetCreature() != null || ai.IsAlerted();
            if (rawCombat) st.LastCombatSignalAt = Time.time;
            if (st.LastCombatSignalAt >= 0f &&
                Time.time - st.LastCombatSignalAt < Plugin.LoggingCombatGraceSeconds)
            {
                st.MarkInCombat();
                return true;   // vanilla drives the fight
            }
            st.MarkCombatClearedIfNew();

            var forced = st.ForcedChopTarget;
            if (forced == null || ResolveChopRoot(forced) != forced)
            {
                // Destroyed, or no longer something that can be worked - the
                // order is finished either way.
                ReleaseChopIgnore(ai, st);
                st.ClearForcedChopTarget();
                st.ForgetLoggingWork();
                return true;
            }

            if (st.LogTargetTree != forced)
            {
                if (Plugin.Verbose)
                    Plugin.Log.LogInfo($"[logging] {st.Prefab}: working '{forced.name}' on command (no leash).");
                SetChopTarget(ai, st, forced);
                st.OriginalTreePos = forced.transform.position;
                st.OriginalTreeName = null;   // no replanting on an ad-hoc order
                st.ChopApproachSince = -1f;
                st.IdleSince = -1f;
            }

            // Its own position stands in for the leash, so DriveChop's
            // stuck-teleport failsafe degrades to a harmless no-op rather
            // than flinging it to some unrelated point.
            return DriveChop(ai, st, dt, ai.transform.position, ref __result);
        }

        /// <summary>
        /// Walks back inside the leash. Not a flee - passes run explicitly
        /// rather than going through Alert()/IsAlerted(), so the troll gets no
        /// alerted roar or stance change just for coming back to work.
        /// Teleports it the rest of the way if it hasn't managed on its own
        /// within Plugin.LoggingReturnTimeout of combat clearing.
        /// </summary>
        static bool DriveReturn(MonsterAI ai, CreatureState st, float dt, Vector3 leashPos, ref bool __result)
        {
            if (!Threat.BaseTick(ai, dt)) { __result = false; return false; }
            if (ai.IsSleeping()) { __result = true; return false; }

            if (st.SecondsSinceCombatCleared >= Plugin.LoggingReturnTimeout)
            {
                TeleportHome(st.Chr, leashPos);
                if (Plugin.Verbose)
                    Plugin.Log.LogInfo(
                        $"[logging] {st.Prefab} teleported back inside its leash after " +
                        $"{Plugin.LoggingReturnTimeout:0}s trying to walk there.");
                __result = true;
                return false;
            }

            AiMotion.MoveTo(ai, dt, leashPos, 1f, run: true);
            __result = true;
            return false;
        }

        /// <summary>
        /// Snaps a character straight to a position. Plain transform writes
        /// don't hold - a Character is driven by a non-kinematic Rigidbody,
        /// and Unity overwrites the transform from it on the next FixedUpdate
        /// (see TeleportFix.cs, which exists precisely because BetterTames
        /// found this the hard way). Moving the rigidbody too, killing its
        /// momentum, and syncing right away is the only combination that
        /// actually sticks.
        /// </summary>
        static void TeleportHome(Character chr, Vector3 pos)
        {
            if (chr == null) return;
            var tf = chr.transform;
            if (tf == null) return;

            Quaternion rot = tf.rotation;
            tf.position = pos;

            var body = chr.GetComponent<Rigidbody>();
            if (body != null)
            {
                body.position = pos;
                body.rotation = rot;
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }

            var sync = chr.GetComponent<ZSyncTransform>();
            if (sync != null) sync.SyncNow();
        }

        // The wood-tier items this mod's own tree-drop investigation has
        // catalogued across vanilla and RtDBiomes. Wood gets special
        // treatment below (an automatic, proximity-only deposit once any one
        // of these hits a flat threshold) precisely because it's the actual
        // point of a LOGGING troll - everything else keeps going through the
        // ordinary between-jobs deposit trip untouched.
        static readonly HashSet<string> WoodItemNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Wood", "FineWood", "RoundLog", "ElderBark", "Blackwood", "Frostwood", "YggdrasilWood"
        };

        // Same deal, same threshold, same trigger for the mining half of the
        // job - stone/ore now abides by the exact rules wood already did:
        // Deposit() itself already empties whatever a chest accepts
        // regardless of item type (see PassiveDepositWood, which despite the
        // name checks the whole Carry dict, not just wood), so the only thing
        // wood had that stone/ore didn't was the ability to interrupt an
        // in-progress chop and force a deliberate trip once a stack got full.
        static readonly HashSet<string> StoneOreItemNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Stone", "Flint", "Copper", "Tin", "Iron", "ScrapIron", "SilverOre", "Obsidian"
        };

        /// <summary>Automatic offload: no navigation, no dedicated trip - this
        /// only fires when a REAL chest already happens to be within ordinary
        /// deposit range of wherever the troll currently is (working a tree,
        /// walking between them, anything), the same "just passing by" idea
        /// PassivePickup already uses for items on the ground. Checks
        /// EVERYTHING currently carried, at any amount - not gated behind
        /// Plugin.LoggingWoodDepositThreshold, which is only the bar for the
        /// separate DELIBERATE out-of-the-way trip in DriveWork. Gating this
        /// passive check on that same threshold was the actual bug behind
        /// "he only ever seems to have wood and nothing shows up in chests I
        /// didn't put there myself" - a troll carrying 12 wood and 3 resin
        /// would walk right past a chest and drop off nothing, because
        /// neither had reached 50 yet. This deposits whatever a nearby chest
        /// will accept regardless of quantity; Deposit() itself already only
        /// removes what that specific chest actually takes, leaving the rest
        /// untouched in Carry for the next chest or the next trip.</summary>
        static void PassiveDepositWood(MonsterAI ai, CreatureState st)
        {
            if (st.Carry.Count == 0) return;

            var chestGo = TotemBind.FindChestWithinRange(ai.transform.position, DepositRange, st.Carry.Keys);
            if (chestGo == null) return;

            var container = chestGo.GetComponent<Container>();
            int moved = Deposit(st, container);   // only removes what this chest actually accepts - other carried items are untouched

            if (moved > 0 && Plugin.Verbose)
                Plugin.Log.LogInfo($"[logging] {st.Prefab}: passed a chest while carrying goods - topped it off automatically.");
        }

        /// <summary>Picks up anything the troll happens to be near, on top of
        /// whatever it deliberately went to collect - the same idea as a
        /// player's own walk-by auto-pickup (vanilla's own
        /// Player.m_autoPickupRange is 2m), just at 3x the distance for a
        /// creature this size. Runs every logging tick regardless of what
        /// else that tick does, off the shared, throttled item snapshot
        /// Rebuild() already refreshes - never its own world scan.</summary>
        static void PassivePickup(MonsterAI ai, CreatureState st)
        {
            float rSq = Plugin.LoggingPassivePickupRadius * Plugin.LoggingPassivePickupRadius;
            Vector3 pos = ai.transform.position;

            for (int i = 0; i < _itemSnap.Length; i++)
            {
                var d = _itemSnap[i];
                if (d == null) continue;
                if ((d.transform.position - pos).sqrMagnitude > rSq) continue;
                TryPickUp(st, d);
            }
        }

        /// <summary>The actual job: keep (or pick) a tree/rock and chop it.
        /// Wood hitting its deposit threshold is the one thing that can
        /// interrupt an in-progress chop to go deposit (see the woodFull
        /// check below); every other material only ever rides along on a
        /// trip wood already triggered, or leaves its overflow on the ground
        /// (see TryPickUp) until room opens up - no trip is ever made for
        /// them on their own. Calls Threat.BaseTick itself - the only call
        /// site for a tick that stays inside the leash.</summary>
        static bool DriveWork(MonsterAI ai, CreatureState st, float dt, Vector3 leashPos, ref bool __result)
        {
            if (!Threat.BaseTick(ai, dt)) { __result = false; return false; }
            if (ai.IsSleeping()) { __result = true; return false; }

            PassivePickup(ai, st);
            PassiveDepositWood(ai, st);   // opportunistic walk-by offload; may already have cleared the threshold below without any trip

            // A deposit trip already under way takes priority over anything
            // else - it doesn't get re-evaluated mid-walk.
            if (st.DepositTarget != null)
                return DriveDeposit(ai, st, dt, leashPos, ref __result);

            // A player-forced chop/mine order (Plugin.HandleForceTargetKey's
            // dual-use mode) outranks whatever the troll picked on its own,
            // same as ForcedTarget already outranks stance for combat - an
            // explicit command beats an automatic choice. Stale/destroyed/out
            // of leash orders are dropped here rather than left to error out
            // later, the same "clear it and move on" handling SensesForcedTarget
            // gives a combat order that's gone bad.
            if (st.ForcedChopTarget != null)
            {
                var forced = st.ForcedChopTarget;
                // Same anchor SetChopTarget itself uses below - NOT the raw
                // prefab pivot. A MineRock5 cluster's pivot can sit well
                // outside the leash radius even while its visible ore is well
                // inside it, which silently failed this check and cleared
                // the order every single tick before ever walking there -
                // confirmed live as "accepted the command, never acts on it."
                Vector3 forcedAnchor = ChopAnchorPoint(forced, leashPos);
                bool stillValid = forced != null
                    && ResolveChopRoot(forced) == forced
                    && (forcedAnchor - leashPos).sqrMagnitude <= Plugin.LoggingLeashRadius * Plugin.LoggingLeashRadius;

                if (!stillValid)
                {
                    st.ClearForcedChopTarget();
                }
                else if (st.LogTargetTree != forced)
                {
                    if (Plugin.Verbose)
                        Plugin.Log.LogInfo($"[logging] {st.Prefab}: forced onto '{forced.name}' by player command.");
                    SetChopTarget(ai, st, forced);
                    st.OriginalTreePos = forced.transform.position;
                    st.ChopApproachSince = -1f;
                    st.OriginalTreeName = forced.GetComponent<TreeBase>() != null
                        ? CreatureRules.CleanName(forced.name) : null;
                    st.IdleSince = -1f;
                }

                if (st.ForcedChopTarget != null)
                    return DriveChop(ai, st, dt, leashPos, ref __result);
            }

            // Wood and stone/ore are the only things that ever start a
            // DELIBERATE trip - collecting either is the actual job, so a
            // full stack of one is urgent enough to pause an in-progress
            // chop and go deposit right now, rather than waiting for
            // "between jobs" the way other loot does. Everything else the
            // troll is carrying rides along for free once a trip is already
            // happening (Deposit() itself empties whatever the chosen chest
            // will accept, not just whichever item triggered the trip), but
            // nothing outside these two sets ever TRIGGERS one.
            bool woodFull = false;
            foreach (var kv in st.Carry)
                if ((WoodItemNames.Contains(kv.Key) || StoneOreItemNames.Contains(kv.Key)) &&
                    kv.Value >= Plugin.LoggingWoodDepositThreshold)
                { woodFull = true; break; }

            if (woodFull)
            {
                var woodChest = TotemBind.FindDepositChest(leashPos, st.Carry.Keys);
                if (woodChest != null)
                {
                    st.IdleSince = -1f;
                    st.DepositTarget = woodChest;
                    st.DepositApproachSince = -1f;
                    // A temporary interruption, not abandonment - same
                    // treatment as a combat pause: keep the current target
                    // and species memory so the exact same tree resumes
                    // once this trip is done.
                    st.PauseLoggingWork();
                    return DriveDeposit(ai, st, dt, leashPos, ref __result);
                }
            }

            // The tree we were working vanished (felled down to nothing, or
            // chopped/despawned by something else) since our last look.
            // ReferenceEquals is deliberate: Unity's own == overload fake-nulls
            // a destroyed GameObject, which is exactly the case being tested
            // for here, so the plain == cannot be used to detect it.
            if (!ReferenceEquals(st.LogTargetTree, null) && st.LogTargetTree == null)
            {
                // Log chain first, then the stump once that's exhausted - see
                // FindSuccessor. Anchored to OriginalTreePos (fixed at this
                // chain's start), NOT LogTargetPos (wherever the last piece
                // ended up) - confirmed live that anchoring to the drifting
                // position eventually picks up an entirely different tree's
                // remains in a dense forest, hopping between them forever
                // and never actually finishing either one.
                var successor = FindSuccessor(st.OriginalTreePos);
                if (successor != null)
                {
                    SetChopTarget(ai, st, successor);
                    st.ChopApproachSince = -1f;
                }
                else
                {
                    // Log AND stump both gone - the space is clear. Replant
                    // before collecting drops, so the sapling lands exactly
                    // where the stump (and the tree before it) actually stood.
                    TryReplant(st, st.OriginalTreeName, st.OriginalTreePos);
                    CollectDrops(st);
                    ReleaseChopIgnore(ai, st);
                    st.ForgetLoggingWork();
                }
            }

            if (st.LogTargetTree == null)
            {
                // A target that just stranded the troll long enough to force
                // the stuck-teleport failsafe (DriveChop) is excluded here
                // while its blacklist is active - without this, teleporting
                // to the leash and immediately re-picking the SAME
                // unreachable object as "nearest" was confirmed live,
                // producing an endless walk -> stuck -> teleport loop.
                var exclude = st.StuckUntil > Time.time ? st.StuckTarget : null;
                var tree = FindNearestTree(ai.transform.position, leashPos, exclude);
                if (tree == null)
                {
                    // Genuinely nothing to do. Rather than hoard whatever's
                    // in Carry forever, once this has gone on long enough,
                    // dump into whatever chest is nearest at all - ignoring
                    // naming - so it isn't sitting on resources it can't use
                    // just because the "right" chest wasn't found or is full.
                    if (st.HasCarry)
                    {
                        if (st.IdleSince < 0f) st.IdleSince = Time.time;
                        if (Time.time - st.IdleSince >= Plugin.LoggingStuckTimeout)
                        {
                            var anyChest = TotemBind.FindNearestChest(leashPos);
                            if (anyChest != null)
                            {
                                if (Plugin.Verbose)
                                    Plugin.Log.LogInfo(
                                        $"[logging] {st.Prefab}: idle with nothing to chop for " +
                                        $"{Plugin.LoggingStuckTimeout:0}s - dumping inventory into the " +
                                        "nearest chest regardless of naming.");
                                st.IdleSince = -1f;
                                st.DepositTarget = anyChest;
                                st.DepositApproachSince = -1f;
                                st.ForceDeposit = true;
                                return DriveDeposit(ai, st, dt, leashPos, ref __result);
                            }
                        }
                    }

                    // Nothing to chop, but a known-catalog drop might still be
                    // sitting on the ground further out than PassivePickup's
                    // short walk-by radius reaches - the troll's own leftover
                    // wood from a chest that was full, an overflowed slot,
                    // whatever. Walk to it deliberately rather than standing
                    // there idle in plain sight of it; PassivePickup (already
                    // called every tick above) does the actual pickup the
                    // moment it's close enough.
                    var item = FindNearestItem(ai.transform.position, leashPos);
                    if (item != null)
                    {
                        st.IdleSince = -1f;
                        if (Plugin.Verbose && Plugin.LoggingDiagEnabled)
                        {
                            float id = Vector3.Distance(ai.transform.position, item.transform.position);
                            Plugin.Log.LogInfo($"[logging] {st.Prefab}: no tree to chop - walking to leftover '{item.name}' at {id:0.0}m.");
                        }
                        AiMotion.MoveTo(ai, dt, item.transform.position, Plugin.LoggingPassivePickupRadius * 0.5f, run: false);
                        __result = true;
                        return false;
                    }

                    if (Plugin.Verbose && Plugin.LoggingDiagEnabled)
                    {
                        // The "rebuild:" line's own total is EVERY leash in the
                        // world combined ("in range of any leash") - a troll
                        // idling right next to a forest full of trees can
                        // still see that count sitting non-zero if most of it
                        // actually belongs to a DIFFERENT placed leash. This
                        // counts what's inside THIS troll's own bound leash
                        // specifically, so "genuinely nothing here" and "bound
                        // to the wrong totem" don't look identical in the log.
                        int mineCount = 0;
                        float leashRSq2 = Plugin.LoggingLeashRadius * Plugin.LoggingLeashRadius;
                        for (int si = 0; si < _n; si++)
                        {
                            var sgo = _snap[si];
                            if (sgo == null) continue;
                            if ((sgo.transform.position - leashPos).sqrMagnitude <= leashRSq2) mineCount++;
                        }
                        Plugin.Log.LogInfo(
                            $"[logging] {st.Prefab}: no tree/log found inside the leash - idle. " +
                            $"(this troll's own leash at {leashPos}, {mineCount} scanned object(s) actually within " +
                            $"its {Plugin.LoggingLeashRadius:0}m radius, troll is {Vector3.Distance(ai.transform.position, leashPos):0.0}m from it)");
                    }
                    __result = true;
                    return false;   // nothing left to chop right now
                }

                st.IdleSince = -1f;

                if (Plugin.Verbose && Plugin.LoggingDiagEnabled)
                {
                    float d = Vector3.Distance(ai.transform.position, tree.transform.position);
                    // Category tag so a log can settle "is he actually ever
                    // choosing rocks/stumps, or only ever standing trees" -
                    // same components Chop() itself checks, in the same order.
                    string category =
                        tree.GetComponent<TreeBase>() != null ? "tree" :
                        tree.GetComponent<TreeLog>() != null ? "log" :
                        tree.GetComponent<MineRock>() != null ? "rock" :
                        tree.GetComponent<MineRock5>() != null ? "rock5" : "stump";
                    Plugin.Log.LogInfo($"[logging] {st.Prefab}: picked {category} target '{tree.name}' at {d:0.0}m.");
                }

                SetChopTarget(ai, st, tree);
                st.OriginalTreePos = tree.transform.position;
                st.ChopApproachSince = -1f;
                // A standing tree names its own species directly. A stump or
                // small tree variant picked up on its own (no felling chain
                // behind it) carries ITS name instead - that won't match a
                // grown-tree key on its own, but it lets a [Replant] config
                // line map an orphan stump deliberately, where before the
                // species was simply thrown away and replanting could never
                // fire for one. A rock names nothing, and TryReplant no-ops
                // on null.
                st.OriginalTreeName =
                    tree.GetComponent<TreeBase>() != null ? CreatureRules.CleanName(tree.name)
                    : IsTreeDestructible(tree.GetComponent<Destructible>()) ? CreatureRules.CleanName(tree.name)
                    : null;
            }

            return DriveChop(ai, st, dt, leashPos, ref __result);
        }

        const float ChopRange = 3f;

        /// <summary>How long before the hit lands the swing animation starts.
        /// See DriveChop - the damage one-shots most targets, so the
        /// animation needs a head start or it's never seen.</summary>
        const float SwingLeadSeconds = 0.6f;

        /// <summary>Fires the swing/slam animation only - no damage. Split out
        /// of Chop() so the swing can be telegraphed ahead of the hit.
        /// Real trigger names, confirmed by dumping the Troll's own weapon
        /// items out of the game files (Assets/Characters/Troll/misc):
        ///   "LOG" item (log in hand) -> "swing_logv" / "swing_logh"
        ///   "slap" item (bare-handed) -> "punch", "swing_l", "swing_r",
        ///                                "attack", "stomp_l", "stomp_r"
        /// A bare-handed troll gets swing_l/swing_r for standing trees and
        /// stomp_l/stomp_r for anything on the ground or made of stone, both
        /// from the "slap" set that's actually bound while unarmed.
        /// swing_logv/swing_logh only bind while a log is really equipped, so
        /// they're deliberately not used here yet.</summary>
        /// <summary>Returns true only if a REAL attack actually started.
        /// The caller uses that to keep the hit in step with the swing:
        /// vanilla declines an attack while the weapon's own
        /// m_aiAttackInterval is still running, which is longer than the
        /// configured chop interval, so roughly two thirds of swings were
        /// falling through to the raw-trigger path that renders nothing -
        /// confirmed live at 11 fallbacks to 5 real attacks, and it's why
        /// rock mining in particular looked animation-less.</summary>
        static bool PlayChopAnimation(MonsterAI ai, CreatureState st)
        {
            var treeGo = st.LogTargetTree;
            if (treeGo == null) return false;

            // "Upright" decides swing vs slam, and a real TreeBase is only
            // half of it: a permanently-small tree (Beech_small1/2) is a
            // Destructible, not a TreeBase, so keying purely off TreeBase
            // sent every small standing tree down the slam branch - confirmed
            // live as "swing 'stomp_r' on 'Beech_small2'", a ground slam
            // against something still standing up.
            //
            // Name matching is deliberate HERE and only here: this is purely
            // cosmetic, so the worst case of a miss is a slightly odd-looking
            // swing rather than anything functional, and nothing else
            // distinguishes a standing small tree from a felled stump - both
            // are tree-type Destructibles with the same components.
            var treeName = CreatureRules.CleanName(treeGo.name);
            bool looksFelled =
                treeName.IndexOf("stub", StringComparison.OrdinalIgnoreCase) >= 0 ||
                treeName.IndexOf("stump", StringComparison.OrdinalIgnoreCase) >= 0;

            bool isStandingTree =
                treeGo.GetComponent<TreeBase>() != null ||
                (!looksFelled && IsTreeDestructible(treeGo.GetComponent<Destructible>()));

            bool altSwing = (st.ChopHitCount++ & 1) == 0;
            string animTrigger = isStandingTree
                ? (altSwing ? "swing_l" : "swing_r")
                : (altSwing ? "stomp_l" : "stomp_r");

            // Vanilla's OWN attack path first. A raw ZSyncAnimation.SetTrigger
            // was confirmed live to fire cleanly - 7 successful calls, no
            // exception, no missing component - and still play nothing at
            // all, because a trigger on its own is only one part of an
            // attack: Attack.Start() also picks the weapon, sets InAttack
            // state, wires the animation events and drives the hit box, and
            // without that surrounding setup the trigger lands in an animator
            // that has no reason to leave its current state.
            //
            // MonsterAI itself calls DoAttack(null, ...) -> StartAttack(null,
            // charge: false) for exactly this case (attacking a static
            // object rather than a creature), so this is the same call
            // vanilla uses on a tree, not a reconstruction of it. It also
            // resolves the armed/unarmed split for free: GetCurrentWeapon()
            // picks the log moveset when a log is held and the bare-handed
            // one when it isn't, so no weapon-specific trigger names are
            // needed here at all.
            if (st.Chr != null && !st.Chr.InAttack())
            {
                try
                {
                    if (st.Chr.StartAttack(null, false))
                    {
                        if (Plugin.Verbose && Plugin.LoggingDiagEnabled)
                            Plugin.Log.LogInfo($"[logging] {st.Prefab}: real attack on '{treeGo.name}'.");
                        return true;
                    }
                }
                catch (Exception e)
                {
                    if (Plugin.Verbose)
                        Plugin.Log.LogWarning($"[logging] {st.Prefab}: StartAttack failed - {e.Message}");
                }
            }

            // No raw-trigger fallback any more. It was doubly harmful: a bare
            // ZSyncAnimation.SetTrigger is already proven not to render
            // anything on its own (a trigger is only one part of an attack -
            // Attack.Start also picks the weapon, sets InAttack state and
            // wires the animation events), AND firing it kept the character
            // sitting in an attack state, so InAttack() read true forever and
            // StartAttack stopped being attempted at all. Declining quietly
            // and letting the next cycle try again is strictly better than a
            // "swing" that does nothing except jam the next real one.
            if (Plugin.Verbose && Plugin.LoggingDiagEnabled)
                Plugin.Log.LogInfo(
                    $"[logging] {st.Prefab}: swing declined by vanilla on '{treeGo.name}' " +
                    $"(weapon interval still running) - hit still lands, next cycle retries.");
            return false;
        }

        /// <summary>Turns collision back on between the troll and whatever it
        /// was chopping, if anything - the counterpart to SetChopTarget's
        /// ignore. Idempotent: safe to call whenever a chop session might be
        /// ending, whether or not one was actually active.</summary>
        static void ReleaseChopIgnore(MonsterAI ai, CreatureState st)
        {
            if (st.ChopIgnoredColliders == null) return;
            var trollCol = ai.GetComponent<Collider>();
            if (trollCol != null)
            {
                foreach (var c in st.ChopIgnoredColliders)
                {
                    if (c == null) continue;
                    Physics.IgnoreCollision(trollCol, c, false);
                }
            }
            st.ChopIgnoredColliders = null;
        }

        /// <summary>Assigns a new chop target and, in the same step, turns
        /// collision off between the troll and it for as long as it stays the
        /// target - this is what stops the troll shoving a log/rock around as
        /// it walks up to and stands against it, which zeroing velocity alone
        /// (see DriveChop) never addressed since that only ever ran once
        /// already in range; the push happens during the approach itself.
        /// Releases whatever the PREVIOUS target's ignore-pair was first, so
        /// a target swap (successor chain, retargeting) never leaves a stale
        /// ignore behind on an object the troll has moved on from.</summary>
        /// <summary>The point DriveChop's approach-range check (and any other
        /// caller needing "is this thing actually near X") should measure
        /// against - a tree/log/stump's own transform.position already sits
        /// right on the trunk, fine as-is, but a rock/ore formation is a
        /// different shape entirely: MineRock5 in particular is a
        /// multi-chunk cluster that can span several metres, with its prefab
        /// pivot not necessarily anywhere near any individual chunk.
        /// Anchoring to that raw pivot could report "not in range" while
        /// standing right against real ore, or - just as bad - report a
        /// forced order as outside the leash radius while its visible ore is
        /// well inside it (confirmed live: exactly why a forced rock target
        /// was accepted but silently dropped every tick and never acted on).
        /// Using the actual collider's closest surface point to
        /// <paramref name="fromPos"/> gives an anchor that's actually near
        /// real rock, consistently, everywhere this is used.</summary>
        static Vector3 ChopAnchorPoint(GameObject go, Vector3 fromPos)
        {
            if (go == null) return fromPos;

            // Applies to EVERY target with a collider, not just rock. A big
            // felled log (MagicLog2Half, confirmed live) has its pivot buried
            // inside or behind its own mesh, so a 3m range check measured to
            // that pivot is simply unreachable - the troll walks up, is
            // stopped by the log itself, never "arrives", and the 30s
            // approach failsafe drags it home having done nothing. The log
            // then sits there forever, rediscovered as a successor every few
            // seconds and never cleared. A surface point fixes that the same
            // way it fixed multi-chunk rock formations. For a small tree the
            // pivot sits at the base anyway, so this changes nothing there.
            {
                // bounds.ClosestPoint, NOT Collider.ClosestPoint: the latter
                // only works on Box/Sphere/Capsule/convex-Mesh colliders, and
                // a rock formation's chunks are non-convex MeshColliders. On
                // those it logs "Physics.ClosestPoint can only be used with..."
                // and - far worse - RETURNS THE INPUT POINT UNCHANGED, which
                // silently set LogTargetPos to the troll's own position:
                // distance to target read 0.0m, so it never walked anywhere,
                // and hit.m_point landed nowhere near the rock, so every swing
                // logged "Minerock hit has no collider or invalid hit area"
                // and dealt nothing. Confirmed live in the log, both warnings
                // spamming together. Bounds are axis-aligned rather than exact,
                // which is plenty for an approach anchor.
                var col = go.GetComponentInChildren<Collider>();
                if (col != null) return col.bounds.ClosestPoint(fromPos);
            }
            return go.transform.position;
        }

        /// <summary>Assigns a new chop target and, in the same step, turns
        /// collision off between the troll and it for as long as it stays the
        /// target - this is what stops the troll shoving a log/rock around as
        /// it walks up to and stands against it, which zeroing velocity alone
        /// (see DriveChop) never addressed since that only ever ran once
        /// already in range; the push happens during the approach itself.
        /// Releases whatever the PREVIOUS target's ignore-pair was first, so
        /// a target swap (successor chain, retargeting) never leaves a stale
        /// ignore behind on an object the troll has moved on from.</summary>
        static void SetChopTarget(MonsterAI ai, CreatureState st, GameObject newTarget)
        {
            ReleaseChopIgnore(ai, st);
            st.LogTargetTree = newTarget;
            st.ChopActiveSince = -1f;
            // A full interval of wind-up before the FIRST hit, not zero.
            // Starting at zero meant the very first tick inside ChopRange
            // fired Chop() instantly on the arrival frame - a 20 HP small
            // beech died before the troll ever stopped moving or the swing
            // could be seen, which in-game reads exactly as "he's doing
            // drive-bys". Arriving, planting, winding up and then swinging
            // is both the correct look and what gives the animation time to
            // actually play.
            st.ChopTimer = Plugin.LoggingChopInterval;
            if (newTarget == null) return;

            st.LogTargetPos = ChopAnchorPoint(newTarget, ai.transform.position);

            var trollCol = ai.GetComponent<Collider>();
            if (trollCol == null) return;

            // EVERY collider on the target, not just the first. A big log or
            // a multi-part rock has several, and exempting only one still
            // left the troll physically blocked by the rest - which is what
            // stopped it ever closing the last few metres onto a felled
            // MagicLog and finishing the job.
            var cols = newTarget.GetComponentsInChildren<Collider>();
            if (cols == null || cols.Length == 0) return;

            foreach (var c in cols)
            {
                if (c == null) continue;
                Physics.IgnoreCollision(trollCol, c, true);
            }
            st.ChopIgnoredColliders = cols;
        }

        static bool DriveChop(MonsterAI ai, CreatureState st, float dt, Vector3 leashPos, ref bool __result)
        {
            float dist = Vector3.Distance(ai.transform.position, st.LogTargetPos);

            if (dist > ChopRange)
            {
                if (st.ChopApproachSince < 0f) st.ChopApproachSince = Time.time;
                if (Time.time - st.ChopApproachSince >= Plugin.LoggingStuckTimeout)
                {
                    if (Plugin.Verbose)
                        Plugin.Log.LogInfo(
                            $"[logging] {st.Prefab}: couldn't reach '{st.LogTargetTree?.name}' for " +
                            $"{Plugin.LoggingStuckTimeout:0}s - teleporting back to the leash.");
                    TeleportHome(st.Chr, leashPos);
                    // The target that stranded it is blacklisted, not just
                    // dropped - "dropped" alone left it sitting in the shared
                    // scan, so FindNearestTree immediately re-picked the SAME
                    // unreachable object as "nearest" from the leash and the
                    // troll walked straight back into whatever blocked it,
                    // forever: confirmed live as a walk -> stuck -> teleport
                    // loop. The blacklist forces a genuinely different pick
                    // for a while.
                    st.StuckTarget = st.LogTargetTree;
                    st.StuckUntil = Time.time + Plugin.LoggingStuckBlacklistSeconds;
                    ReleaseChopIgnore(ai, st);
                    st.ForgetLoggingWork();
                    __result = true;
                    return false;
                }

                AiMotion.MoveTo(ai, dt, st.LogTargetPos, ChopRange - 0.5f, run: true);
                __result = true;
                return false;
            }

            st.ChopApproachSince = -1f;
            if (st.ChopActiveSince < 0f) st.ChopActiveSince = Time.time;
            if (Time.time - st.ChopActiveSince >= Plugin.LoggingHarvestStuckTimeout)
            {
                if (Plugin.Verbose)
                    Plugin.Log.LogInfo(
                        $"[logging] {st.Prefab}: '{st.LogTargetTree?.name}' hasn't gone down after " +
                        $"{Plugin.LoggingHarvestStuckTimeout:0}s of real swings - force-finishing it.");
                ForceFinishTarget(ai, st);
                __result = true;
                return false;
            }

            // AiMotion.MoveTo drives the real Rigidbody (non-kinematic - see
            // TeleportHome's own comment on this), and stops being CALLED the
            // instant we're in range, but momentum it already imparted keeps
            // carrying the body regardless - nothing ever told physics to
            // actually stop. That's the "sliding during the attack" - killing
            // velocity here the moment it arrives ends the slide immediately
            // instead of letting it bleed off on its own.
            var body = ai.GetComponent<Rigidbody>();
            if (body != null)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }

            ai.LookTowards(st.LogTargetPos);

            st.ChopTimer -= dt;

            // Telegraph the swing BEFORE the hit lands. Chop damage one-shots
            // nearly everything (50 vs a 20 HP small tree / 100 HP stump), so
            // firing the animation at the same instant the target is deleted
            // meant the troll snapped straight back to walking and the swing
            // was never visibly played - the trigger fired into a frame that
            // immediately transitioned away from it. Leading the hit by
            // SwingLeadSeconds gives a real wind-up -> impact, which both
            // looks right and is what makes the animation actually visible.
            // Exactly ONE swing attempt per chop cycle, and the hit lands on
            // schedule whether or not vanilla granted it.
            //
            // An earlier version held the hit back until a real swing
            // started, and pinned ChopTimer to SwingLeadSeconds when it
            // didn't. That deadlocked outright: the telegraph fires on
            // ChopTimer <= SwingLeadSeconds, so pinning it there re-fired
            // every tick and the timer never reached 0, meaning Chop() was
            // never called at all - confirmed live as 1067 swing attempts,
            // 0 real attacks and 0 damage dealt. The retry storm also fed
            // itself: hammering the animation every tick kept the character
            // in an attack state, so InAttack() stayed true and StartAttack
            // was never even attempted.
            if (!st.ChopSwingTelegraphed && st.ChopTimer <= SwingLeadSeconds)
            {
                st.ChopSwingTelegraphed = true;   // set regardless - one try per cycle, never a storm
                PlayChopAnimation(ai, st);
            }

            if (st.ChopTimer <= 0f)
            {
                st.ChopTimer = Plugin.LoggingChopInterval;
                st.ChopSwingTelegraphed = false;
                if (Plugin.Verbose && Plugin.LoggingDiagEnabled)
                    Plugin.Log.LogInfo($"[logging] {st.Prefab}: chopping '{st.LogTargetTree?.name}' at {dist:0.0}m.");
                Chop(ai, st);
            }

            __result = true;
            return false;
        }

        static void Chop(MonsterAI ai, CreatureState st)
        {
            var treeGo = st.LogTargetTree;
            if (treeGo == null) return;

            var tree = treeGo.GetComponent<TreeBase>();
            var log = tree == null ? treeGo.GetComponent<TreeLog>() : null;
            // A stump (and a permanently-small tree variant) is neither -
            // it's a Destructible + DropOnDestroyed, see IsTreeDestructible.
            var stump = (tree == null && log == null) ? treeGo.GetComponent<Destructible>() : null;
            var rock = (tree == null && log == null && stump == null) ? treeGo.GetComponent<MineRock>() : null;
            var rock5 = (rock == null && tree == null && log == null && stump == null) ? treeGo.GetComponent<MineRock5>() : null;
            // ...and a MODDED stump may be WearNTear-based instead. Without
            // this it would be scanned and walked to, then never actually
            // damaged, because nothing here knew how to hit it.
            var wnt = (tree == null && log == null && stump == null && rock == null && rock5 == null)
                ? treeGo.GetComponent<WearNTear>() : null;
            if (tree == null && log == null && stump == null && rock == null && rock5 == null && wnt == null)
            {
                if (Plugin.Verbose && Plugin.LoggingDiagEnabled)
                    Plugin.Log.LogInfo($"[logging] {st.Prefab}: chop target '{treeGo.name}' has none of the components this troll knows how to hit - skipping.");
                return;
            }

            var hit = new HitData();
            // Wood takes chop damage, rock/ore takes pickaxe damage - both
            // set on every hit rather than branching the field, since each
            // target's own DamageModifiers already ignores whichever one
            // doesn't apply to it.
            hit.m_damage.m_chop = Plugin.LoggingChopDamage;
            hit.m_damage.m_pickaxe = Plugin.LoggingChopDamage;
            hit.m_point = st.LogTargetPos;
            hit.m_dir = Vector3.up;
            hit.SetAttacker(st.Chr);
            // MineRock5.Damage (decompiled and checked directly, not guessed)
            // has two paths: hit.m_hitCollider == null falls back to an
            // OverlapSphere at m_point, then REQUIRES every candidate
            // collider's parent (or grandparent) transform to be exactly this
            // MineRock5's own GameObject - a strict hierarchy check that
            // silently rejects a perfectly real collider sitting one level
            // deeper than that, which is exactly what confirmed live as
            // endless "Minerock hit has no collider or invalid hit area"
            // spam with zero damage ever landing. Passing a REAL collider
            // reference via m_hitCollider skips that whole fragile path
            // entirely and resolves the hit area directly by reference - the
            // same collider MineRock5's own Awake() already indexed via
            // GetComponentsInChildren, so it's always one of its real hit
            // areas regardless of exactly how deep it sits.
            // m_radius MUST stay 0 whenever a real collider is supplied.
            // MineRock5.Damage picks its path with
            //     if (hit.m_hitCollider == null || hit.m_radius > 0f)
            // so a non-zero radius sends it down the OverlapSphere branch
            // EVEN WITH a valid collider - and that branch damages every
            // chunk inside the radius at once, which is why a 2m radius took
            // an entire boulder down in a single swing. With the collider set
            // and radius left at 0 it resolves exactly one hit area, chunk by
            // chunk, the way a real pickaxe swing does.
            bool haveCollider = false;
            if (rock5 != null)
            {
                var rock5Col = rock5.GetComponentInChildren<Collider>();
                if (rock5Col != null) { hit.m_hitCollider = rock5Col; haveCollider = true; }
            }
            else if (rock != null)
            {
                var rockCol = rock.GetComponentInChildren<Collider>();
                if (rockCol != null) { hit.m_hitCollider = rockCol; haveCollider = true; }
            }
            // Only used as the fallback sphere size when no collider was
            // found at all - see above for why it can't be set otherwise.
            hit.m_radius = haveCollider ? 0f : 2f;
            // Every one of TreeBase/TreeLog/WearNTear/MineRock/MineRock5 gates
            // damage behind hit.CheckToolTier(m_minToolTier) and applies
            // ZERO damage (silently - a "Too Hard" text, no exception, no
            // log line) below it. A synthetic hit with no tool at all
            // defaults m_toolTier to 0, so it could only ever break the
            // lowest tier of tree or rock - exactly why Birch (tier 1) never
            // budged while Beech (tier 0) worked fine. A troll should be
            // able to brute-force anything regardless of what any axe or
            // pickaxe would normally require.
            hit.m_toolTier = short.MaxValue;

            // WearNTear reads plain m_damage rather than chop/pickaxe, so a
            // modded stump needs that set too or it takes the hit for zero.
            hit.m_damage.m_damage = Plugin.LoggingChopDamage;

            if (tree != null) tree.Damage(hit);
            else if (log != null) log.Damage(hit);
            else if (stump != null) stump.Damage(hit);
            else if (rock != null) rock.Damage(hit);
            else if (rock5 != null) rock5.Damage(hit);
            else wnt.Damage(hit);

            // The swing animation is NOT fired here - PlayChopAnimation has
            // already played it, led in ahead of this hit by
            // SwingLeadSeconds. See DriveChop for why they're separated.
        }

        /// <summary>The Plugin.LoggingHarvestStuckTimeout failsafe: rather
        /// than let a troll swing forever at something that just won't die
        /// (a tougher-than-expected rock, a hit-registration edge case,
        /// whatever the exact cause), this ends it outright - one massive hit
        /// guaranteed to drop ANY target regardless of remaining health, then
        /// an immediate CollectDrops sweep so whatever it spawns lands in
        /// carry right away instead of waiting for a passer-by pickup. Uses
        /// the exact same Damage() call per target type Chop() does - only
        /// the damage number differs - so this is never a different code path
        /// that could destroy something Chop() itself wouldn't have, just a
        /// bigger number on the identical call.</summary>
        static void ForceFinishTarget(MonsterAI ai, CreatureState st)
        {
            var treeGo = st.LogTargetTree;
            if (treeGo == null) return;

            var tree = treeGo.GetComponent<TreeBase>();
            var log = tree == null ? treeGo.GetComponent<TreeLog>() : null;
            var stump = (tree == null && log == null) ? treeGo.GetComponent<Destructible>() : null;
            var rock = (tree == null && log == null && stump == null) ? treeGo.GetComponent<MineRock>() : null;
            var rock5 = (rock == null && tree == null && log == null && stump == null) ? treeGo.GetComponent<MineRock5>() : null;
            var wnt = (tree == null && log == null && stump == null && rock == null && rock5 == null)
                ? treeGo.GetComponent<WearNTear>() : null;
            if (tree == null && log == null && stump == null && rock == null && rock5 == null && wnt == null) return;

            var hit = new HitData();
            const float overwhelming = 999999f;
            hit.m_damage.m_chop = overwhelming;
            hit.m_damage.m_pickaxe = overwhelming;
            hit.m_damage.m_damage = overwhelming;   // stumps (WearNTear) read plain m_damage, not chop/pickaxe
            hit.m_point = st.LogTargetPos;
            hit.m_dir = Vector3.up;
            hit.SetAttacker(st.Chr);
            // Same real-collider fix as Chop() - see its comment for why.
            if (rock5 != null)
            {
                var rock5Col = rock5.GetComponentInChildren<Collider>();
                if (rock5Col != null) hit.m_hitCollider = rock5Col;
            }
            else if (rock != null)
            {
                var rockCol = rock.GetComponentInChildren<Collider>();
                if (rockCol != null) hit.m_hitCollider = rockCol;
            }
            hit.m_radius = 2f;
            hit.m_toolTier = short.MaxValue;

            if (tree != null) tree.Damage(hit);
            else if (log != null) log.Damage(hit);
            else if (stump != null) stump.Damage(hit);
            else if (rock != null) rock.Damage(hit);
            else if (rock5 != null) rock5.Damage(hit);
            else wnt.Damage(hit);

            CollectDrops(st);
        }

        /// <summary>Plants the sapling ReplantMapping says goes with
        /// <paramref name="originalTreeName"/> at <paramref name="pos"/> - no
        /// seed consumed, no Piece placement cost check, just a direct spawn
        /// the same way the game itself creates any networked object. A null
        /// name (nothing to map, or the chain never started from a standing
        /// tree) or an unmapped species is a silent no-op, not a warning -
        /// most species aren't in the table yet by design (see
        /// ReplantMapping).</summary>
        static void TryReplant(CreatureState st, string originalTreeName, Vector3 pos)
        {
            if (!Plugin.LoggingReplantEnabled) return;
            if (string.IsNullOrEmpty(originalTreeName))
            {
                if (Plugin.Verbose && Plugin.LoggingDiagEnabled)
                    Plugin.Log.LogInfo($"[logging] {st.Prefab}: nothing to replant - this chain didn't start from a standing tree.");
                return;
            }
            if (!ReplantMapping.TryGetSapling(originalTreeName, out var saplingName))
            {
                if (Plugin.Verbose && Plugin.LoggingDiagEnabled)
                    Plugin.Log.LogInfo(
                        $"[logging] {st.Prefab}: '{originalTreeName}' has no [Replant] mapping - " +
                        "not replanted. Add one in CreatureControl.Creatures.cfg to cover it.");
                return;
            }

            var prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(saplingName) : null;
            if (prefab == null)
            {
                if (Plugin.Verbose)
                    Plugin.Log.LogWarning(
                        $"[logging] {st.Prefab}: replant mapping points '{originalTreeName}' -> " +
                        $"'{saplingName}', but no such prefab is registered.");
                return;
            }

            UnityEngine.Object.Instantiate(prefab, pos, Quaternion.identity);

            if (Plugin.Verbose)
                Plugin.Log.LogInfo($"[logging] {st.Prefab}: replanted '{saplingName}' where '{originalTreeName}' stood.");
        }

        static readonly List<ItemDrop> _dropScan = new List<ItemDrop>();

        /// <summary>Sweeps whatever landed near a just-felled tree into carry.
        /// Every distinct item on the ground gets its own reserved slot (see
        /// CreatureState.Carry / TreeDropCatalog) - nothing here picks one
        /// item type and ignores the rest anymore.</summary>
        static void CollectDrops(CreatureState st)
        {
            float r = Plugin.LoggingDropPickupRadius;
            _dropScan.Clear();
            var all = UnityEngine.Object.FindObjectsByType<ItemDrop>(FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++)
            {
                var d = all[i];
                if (d == null) continue;
                if ((d.transform.position - st.LogTargetPos).sqrMagnitude > r * r) continue;
                _dropScan.Add(d);
            }

            if (Plugin.Verbose && Plugin.LoggingDiagEnabled)
                Plugin.Log.LogInfo(
                    $"[logging] {st.Prefab}: collecting drops near {st.LogTargetPos} " +
                    $"(R={r}) - {_dropScan.Count} ItemDrop(s) in range.");

            for (int i = 0; i < _dropScan.Count; i++)
                TryPickUp(st, _dropScan[i]);
        }

        static void TryPickUp(CreatureState st, ItemDrop drop)
        {
            bool diag = Plugin.Verbose && Plugin.LoggingDiagEnabled;

            if (drop == null || drop.m_itemData?.m_shared == null) return;

            string name = drop.m_itemData.m_dropPrefab != null
                ? drop.m_itemData.m_dropPrefab.name
                : CreatureRules.CleanName(drop.gameObject.name);

            // No item-type restriction at all. This used to accept only what
            // TreeDropCatalog listed as a tree/stump/rock drop, which meant
            // anything else the troll walked over was ignored - the catalog
            // had to keep pace with every mod's drop tables just to let a
            // troll pick up what was lying at its feet. It now works like a
            // plain container: whatever it passes goes in, and sorting is
            // left to the chests on deposit, where a named chest takes its
            // item and an unnamed one catches the rest.
            int max = drop.m_itemData.m_shared.m_maxStackSize > 0
                ? drop.m_itemData.m_shared.m_maxStackSize : 1;
            st.Carry.TryGetValue(name, out int already);

            // Slot limit, not a type limit: a kind already being carried
            // tops up its existing slot freely, a NEW kind needs a free one.
            if (already <= 0 && st.Carry.Count >= Plugin.LoggingCarrySlots)
            {
                if (diag)
                    Plugin.Log.LogInfo(
                        $"[logging] {st.Prefab}: leaving '{name}' - all " +
                        $"{Plugin.LoggingCarrySlots} carry slots are in use.");
                return;
            }

            int room = max - already;
            if (room <= 0)
            {
                if (diag) Plugin.Log.LogInfo($"[logging] {st.Prefab}: leaving '{name}' - its slot is already full ({already}/{max}).");
                return;
            }

            int have = drop.m_itemData.m_stack > 0 ? drop.m_itemData.m_stack : 1;
            int take = Mathf.Min(have, room);
            if (take <= 0) return;

            st.Carry[name] = already + take;
            st.SaveCarry();   // survives unload - see CreatureState.SaveCarry
            if (diag) Plugin.Log.LogInfo($"[logging] {st.Prefab}: picked up {take}x '{name}' ({st.Carry[name]}/{max} carried).");

            // ItemDrop's own m_nview/Save() are private (confirmed via
            // dump.py against the real assembly) - ZNetView is just a
            // MonoBehaviour sitting on the same GameObject, so fetch it as a
            // component instead of reaching for the private field.
            var nview = drop.GetComponent<ZNetView>();
            if (nview == null || !nview.IsValid()) return;
            if (!nview.IsOwner()) nview.ClaimOwnership();
            if (!nview.IsOwner()) return;

            if (take >= have) nview.Destroy();
            else
            {
                // SetStack() is the public equivalent of "set m_stack then
                // Save()" - it re-checks ownership itself and persists.
                drop.SetStack(have - take);
            }
        }

        const float DepositRange = 10f;   // a troll is a big target - a tight radius just means fussy pathing for no reason

        /// <summary>Walks an already-chosen deposit trip (st.DepositTarget,
        /// set by DriveWork when it decided this was worth a detour) to
        /// completion. If the chest becomes unreachable partway - destroyed,
        /// walled off, a hole opened under the path - it doesn't hang
        /// forever: past LoggingStuckTimeout seconds of failing to close the
        /// distance, it deposits remotely (the items travel, the troll
        /// doesn't) so a pathing failure can never strand a full inventory.</summary>
        static bool DriveDeposit(MonsterAI ai, CreatureState st, float dt, Vector3 leashPos, ref bool __result)
        {
            var chestGo = st.DepositTarget;
            if (chestGo == null)
            {
                st.DepositApproachSince = -1f;
                __result = true;
                return false;
            }

            var container = chestGo.GetComponent<Container>();
            Vector3 chestPos = chestGo.transform.position;
            float dist = Vector3.Distance(ai.transform.position, chestPos);

            if (dist > DepositRange)
            {
                if (st.DepositApproachSince < 0f) st.DepositApproachSince = Time.time;
                if (Time.time - st.DepositApproachSince >= Plugin.LoggingStuckTimeout)
                {
                    if (Plugin.Verbose)
                        Plugin.Log.LogInfo(
                            $"[logging] {st.Prefab}: couldn't reach its deposit chest for " +
                            $"{Plugin.LoggingStuckTimeout:0}s - depositing remotely instead.");
                    Deposit(st, container);
                    st.DepositTarget = null;
                    st.DepositApproachSince = -1f;
                    __result = true;
                    return false;
                }

                AiMotion.MoveTo(ai, dt, chestPos, DepositRange - 0.5f, run: true);
                __result = true;
                return false;
            }

            Deposit(st, container);
            st.DepositTarget = null;
            st.DepositApproachSince = -1f;
            __result = true;
            return false;
        }

        static readonly List<string> _depositScratch = new List<string>();

        /// <summary>Deposits every carried item this particular chest will
        /// accept (see StorageNaming.Accepts - everything, if it's unnamed;
        /// only the one matching item, if it's named) and leaves the rest
        /// untouched in carry for the next trip.</summary>
        static int Deposit(CreatureState st, Container container)
        {
            if (container == null || st.Carry.Count == 0) return 0;

            // One-shot override for the idle-dump failsafe (DriveWork) -
            // empties into the nearest chest regardless of what it's named,
            // rather than hoarding a full inventory with nothing left to cut.
            bool force = st.ForceDeposit;
            st.ForceDeposit = false;

            string storageName = StorageNaming.GetName(container);
            var inv = container.GetInventory();
            if (inv == null) return 0;

            // Named chests first, always. An unnamed chest is the catch-all
            // for UNCATALOGUED items only - anything a named chest in this
            // leash's area accepts goes there and nowhere else, however close
            // the unnamed one happens to be.
            bool unnamed = string.IsNullOrEmpty(storageName);
            Vector3 leashPos = st.BoundLeash != null
                ? st.BoundLeash.transform.position : container.transform.position;

            _depositScratch.Clear();
            foreach (var kv in st.Carry)
            {
                string item = kv.Key;
                int count = kv.Value;

                if (!force && !StorageNaming.Accepts(storageName, item)) continue;
                if (!force && unnamed && TotemBind.HasNamedChestFor(leashPos, item)) continue;

                var prefab = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(item) : null;
                if (prefab == null)
                {
                    if (Plugin.Verbose)
                        Plugin.Log.LogWarning($"[logging] {st.Prefab} could not resolve '{item}' to deposit it.");
                    continue;
                }

                // Real overload (confirmed via pnames.py) is 8 args and
                // returns the created ItemData, not a bool - null means it
                // didn't fit.
                var added = inv.AddItem(prefab.name, count, 1, 0, 0L, "", false, false);
                if (added == null) continue;   // chest is full; keep this one, retry next trip

                _depositScratch.Add(item);
                if (Plugin.Verbose)
                    Plugin.Log.LogInfo(
                        $"[logging] {st.Prefab} deposited {count}x {item} into " +
                        $"'{(string.IsNullOrEmpty(storageName) ? container.name : storageName)}'.");
            }

            if (_depositScratch.Count == 0) return 0;

            foreach (var item in _depositScratch) st.Carry.Remove(item);
            st.SaveCarry();   // what's left must survive unload too

            // Container.Save() is private; Container wires its own Save into
            // Inventory.m_onChanged (confirmed via IL of Container.Awake), and
            // that Action field is public, so invoking it triggers the same
            // save without reaching for the private method.
            inv.m_onChanged?.Invoke();
            return _depositScratch.Count;
        }
    }

    /// <summary>
    /// Shows a logging troll's current carry contents on its own hover text
    /// (the same place its tamed stance/status already shows) - a live view
    /// of CreatureState.Carry with no separate UI or hotkey needed. Only
    /// appears once it's actually holding something; an empty-handed tame
    /// (logging or not) shows exactly what it always did.
    /// </summary>
    [HarmonyPatch(typeof(Tameable), nameof(Tameable.GetHoverText))]
    static class Patch_Tameable_GetHoverText_Carry
    {
        static void Postfix(Tameable __instance, ref string __result)
        {
            var chr = __instance.GetComponent<Character>();
            var st = chr != null ? CreatureState.For(chr) : null;
            if (st == null || st.Carry.Count == 0) return;

            var parts = new List<string>();
            foreach (var kv in st.Carry)
                parts.Add($"{kv.Value}x {kv.Key}");

            __result += "\nCarrying: " + string.Join(", ", parts);
        }
    }
}
