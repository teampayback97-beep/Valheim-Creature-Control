using System;
using System.Collections.Generic;
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

        public static void Reset() { _n = 0; _nextScan = 0f; _itemSnap = Array.Empty<ItemDrop>(); }

        public static void Tick()
        {
            if (Time.time < _nextScan) return;
            _nextScan = Time.time + Plugin.LoggingTreeScanInterval;
            Rebuild();
        }

        // A felled stump is a plain WearNTear + DropOnDestroyed - never a
        // TreeBase or TreeLog (confirmed against both RtDBiomes' asset bundle
        // and vanilla: TreeBase.m_stubPrefab points at exactly this kind of
        // object). FindObjectsByType<WearNTear> would also return every
        // placed building piece in the world, so stumps are picked out by
        // name instead, the same way FireSources matches fire prefabes by
        // name rather than by a shared component.
        static readonly string[] StumpNameHints = { "stump", "stub" };

        // A freshly (re)planted tree - whether it grew there naturally or a
        // logging troll just put it there - carries "sapling" in its name in
        // every case this mod has catalogued (vanilla and RtDBiomes alike).
        // Excluded from every scan below on sight: a troll should never
        // damage what it, or the world, is still growing back.
        static bool IsSapling(string name) => name.IndexOf("sapling", StringComparison.OrdinalIgnoreCase) >= 0;

        static void Rebuild()
        {
            _n = 0;
            int beforeTrees = _n;
            AddAll(UnityEngine.Object.FindObjectsByType<TreeBase>(FindObjectsSortMode.None));
            int beforeLogs = _n;
            AddAll(UnityEngine.Object.FindObjectsByType<TreeLog>(FindObjectsSortMode.None));
            int beforeStumps = _n;
            AddStumps(UnityEngine.Object.FindObjectsByType<WearNTear>(FindObjectsSortMode.None));
            int beforeRocks = _n;
            // MineRock (single-piece ore veins/rocks) and MineRock5 (the
            // multi-chunk rock formations) are both IDestructible with the
            // same real-signal-not-name-list shape TreeBase/TreeLog already
            // get - see Chop() for how each is actually damaged.
            AddAll(UnityEngine.Object.FindObjectsByType<MineRock>(FindObjectsSortMode.None));
            AddAll(UnityEngine.Object.FindObjectsByType<MineRock5>(FindObjectsSortMode.None));

            _itemSnap = UnityEngine.Object.FindObjectsByType<ItemDrop>(FindObjectsSortMode.None);

            if (Plugin.Verbose && Plugin.LoggingDiagEnabled)
                Plugin.Log.LogInfo(
                    $"[logging] rebuild: {beforeLogs - beforeTrees} standing tree(s), " +
                    $"{beforeStumps - beforeLogs} log(s), {beforeRocks - beforeStumps} stump(s), " +
                    $"{_n - beforeRocks} rock/ore(s) - {_n} total in range of any leash.");
        }

        static void AddAll<T>(T[] items) where T : Component
        {
            for (int i = 0; i < items.Length; i++)
            {
                var c = items[i];
                if (c == null) continue;
                var go = c.gameObject;
                if (go == null) continue;

                var name = CreatureRules.CleanName(go.name);
                if (IsSapling(name)) continue;
                if (TreeSources.TryMatch(name, out var forced) && !forced) continue;

                if (_n >= _snap.Length) Array.Resize(ref _snap, _snap.Length * 2);
                _snap[_n++] = go;
            }
        }

        static void AddStumps(WearNTear[] items)
        {
            for (int i = 0; i < items.Length; i++)
            {
                var c = items[i];
                if (c == null) continue;
                var go = c.gameObject;
                if (go == null) continue;

                var name = CreatureRules.CleanName(go.name);
                if (IsSapling(name)) continue;

                bool looksLikeStump = false;
                for (int h = 0; h < StumpNameHints.Length; h++)
                    if (name.IndexOf(StumpNameHints[h], StringComparison.OrdinalIgnoreCase) >= 0) { looksLikeStump = true; break; }
                if (!looksLikeStump) continue;

                if (TreeSources.TryMatch(name, out var forced) && !forced) continue;

                if (_n >= _snap.Length) Array.Resize(ref _snap, _snap.Length * 2);
                _snap[_n++] = go;
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
            var all = UnityEngine.Object.FindObjectsByType<WearNTear>(FindObjectsSortMode.None);

            GameObject best = null;
            float bestSq = R * R;

            for (int i = 0; i < all.Length; i++)
            {
                var go = all[i] != null ? all[i].gameObject : null;
                if (go == null) continue;

                var name = CreatureRules.CleanName(go.name);
                bool looksLikeStump = false;
                for (int h = 0; h < StumpNameHints.Length; h++)
                    if (name.IndexOf(StumpNameHints[h], StringComparison.OrdinalIgnoreCase) >= 0) { looksLikeStump = true; break; }
                if (!looksLikeStump) continue;

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
            if (!st.IsLogging) return true;

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
                // Combat (or a stray push) put it outside its own radius.
                st.MarkCombatClearedIfNew();
                return DriveReturn(ai, st, dt, leashPos, ref __result);
            }

            st.MarkCombatClearedIfNew();
            return DriveWork(ai, st, dt, leashPos, ref __result);
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

        static readonly List<string> _woodOverThreshold = new List<string>();

        /// <summary>Automatic wood offload: no navigation, no dedicated trip -
        /// this only fires when a REAL chest already happens to be within
        /// ordinary deposit range of wherever the troll currently is (working
        /// a tree, walking between them, anything), the same "just passing
        /// by" idea PassivePickup already uses for items on the ground.
        /// Triggers per wood item once it reaches Plugin.LoggingWoodDepositThreshold
        /// (a flat number, not each item's own max stack - the point is
        /// clearing wood regularly, not waiting to top out at 999 on
        /// something like Blackwood). Non-wood materials are entirely
        /// untouched here; they still go through DriveDeposit's normal
        /// between-jobs trip.</summary>
        static void PassiveDepositWood(MonsterAI ai, CreatureState st)
        {
            _woodOverThreshold.Clear();
            foreach (var kv in st.Carry)
                if (WoodItemNames.Contains(kv.Key) && kv.Value >= Plugin.LoggingWoodDepositThreshold)
                    _woodOverThreshold.Add(kv.Key);
            if (_woodOverThreshold.Count == 0) return;

            var chestGo = TotemBind.FindChestWithinRange(ai.transform.position, DepositRange, _woodOverThreshold);
            if (chestGo == null) return;

            var container = chestGo.GetComponent<Container>();
            Deposit(st, container);   // only removes what this chest actually accepts - other carried items are untouched

            if (Plugin.Verbose)
                Plugin.Log.LogInfo($"[logging] {st.Prefab}: passed a chest while carrying full wood - topped it off automatically.");
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

            // Wood is the only thing that ever starts a DELIBERATE trip -
            // collecting it is the actual job, so a full stack is urgent
            // enough to pause an in-progress chop and go deposit right now,
            // rather than waiting for "between jobs" the way other loot
            // does. Everything else the troll is carrying rides along for
            // free once a trip is already happening (Deposit() itself
            // empties whatever the chosen chest will accept, not just
            // wood), but nothing but wood ever TRIGGERS one.
            bool woodFull = false;
            foreach (var kv in st.Carry)
                if (WoodItemNames.Contains(kv.Key) && kv.Value >= Plugin.LoggingWoodDepositThreshold)
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
                    st.LogTargetTree = successor;
                    st.LogTargetPos = successor.transform.position;
                    st.ChopTimer = 0f;
                    st.ChopApproachSince = -1f;
                }
                else
                {
                    // Log AND stump both gone - the space is clear. Replant
                    // before collecting drops, so the sapling lands exactly
                    // where the stump (and the tree before it) actually stood.
                    TryReplant(st, st.OriginalTreeName, st.OriginalTreePos);
                    CollectDrops(st);
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

                    if (Plugin.Verbose && Plugin.LoggingDiagEnabled)
                        Plugin.Log.LogInfo($"[logging] {st.Prefab}: no tree/log found inside the leash - idle.");
                    __result = true;
                    return false;   // nothing left to chop right now
                }

                st.IdleSince = -1f;

                if (Plugin.Verbose && Plugin.LoggingDiagEnabled)
                {
                    float d = Vector3.Distance(ai.transform.position, tree.transform.position);
                    Plugin.Log.LogInfo($"[logging] {st.Prefab}: picked target '{tree.name}' at {d:0.0}m.");
                }

                st.LogTargetTree = tree;
                st.LogTargetPos = tree.transform.position;
                st.OriginalTreePos = tree.transform.position;
                st.ChopTimer = 0f;
                st.ChopApproachSince = -1f;
                // Only a genuine standing tree has a species worth replanting
                // - picking up mid-chain (an orphaned log/stump) or a rock
                // leaves this null, and TryReplant no-ops on null.
                st.OriginalTreeName = tree.GetComponent<TreeBase>() != null
                    ? CreatureRules.CleanName(tree.name) : null;
            }

            return DriveChop(ai, st, dt, leashPos, ref __result);
        }

        const float ChopRange = 3f;

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
                    st.ForgetLoggingWork();
                    __result = true;
                    return false;
                }

                AiMotion.MoveTo(ai, dt, st.LogTargetPos, ChopRange - 0.5f, run: true);
                __result = true;
                return false;
            }

            st.ChopApproachSince = -1f;

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
            if (st.ChopTimer <= 0f)
            {
                st.ChopTimer = Plugin.LoggingChopInterval;
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
            // A stump is neither - just a WearNTear + DropOnDestroyed (see
            // AddStumps above).
            var stump = (tree == null && log == null) ? treeGo.GetComponent<WearNTear>() : null;
            var rock = (tree == null && log == null && stump == null) ? treeGo.GetComponent<MineRock>() : null;
            var rock5 = (rock == null && tree == null && log == null && stump == null) ? treeGo.GetComponent<MineRock5>() : null;
            if (tree == null && log == null && stump == null && rock == null && rock5 == null)
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
            // MineRock5 (multi-chunk rock formations) needs m_radius to find
            // which chunk got hit at all: with m_hitCollider null (we have no
            // real collider reference to give it) it falls back to an
            // OverlapSphere at m_point, but only a 0.05m pinprick if m_radius
            // is left at its default 0 - confirmed live in the log spamming
            // "Minerock hit has no collider or invalid hit area" on every
            // single swing. A generous radius here catches the actual
            // hit-area collider regardless of exactly where LogTargetPos
            // landed relative to it. Harmless for every other target type,
            // none of which read m_radius at all.
            hit.m_radius = 2f;
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

            if (tree != null) tree.Damage(hit);
            else if (log != null) log.Damage(hit);
            else if (stump != null) stump.Damage(hit);
            else if (rock != null) rock.Damage(hit);
            else rock5.Damage(hit);

            // Cosmetic only - the real hit already landed above. Best-effort:
            // not every creature's Animator necessarily exposes an "attack"
            // trigger, so a miss here should never take the real chop with it.
            try { ai.GetComponent<ZSyncAnimation>()?.SetTrigger("attack"); }
            catch { /* cosmetic; the chop already happened */ }
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

            // Anything a tree/stump/rock can drop gets its own reserved slot,
            // built live from the game's own drop tables (TreeDropCatalog) -
            // an item the troll happens to walk past that ISN'T one of those
            // (loot from something else entirely) is left alone rather than
            // hoarded.
            if (!TreeDropCatalog.Contains(name))
            {
                if (diag) Plugin.Log.LogInfo($"[logging] {st.Prefab}: leaving '{name}' - not a known tree/ore drop.");
                return;
            }

            int max = drop.m_itemData.m_shared.m_maxStackSize > 0
                ? drop.m_itemData.m_shared.m_maxStackSize : 1;
            st.Carry.TryGetValue(name, out int already);
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
        static void Deposit(CreatureState st, Container container)
        {
            if (container == null || st.Carry.Count == 0) return;

            // One-shot override for the idle-dump failsafe (DriveWork) -
            // empties into the nearest chest regardless of what it's named,
            // rather than hoarding a full inventory with nothing left to cut.
            bool force = st.ForceDeposit;
            st.ForceDeposit = false;

            string storageName = StorageNaming.GetName(container);
            var inv = container.GetInventory();
            if (inv == null) return;

            _depositScratch.Clear();
            foreach (var kv in st.Carry)
            {
                string item = kv.Key;
                int count = kv.Value;

                if (!force && !StorageNaming.Accepts(storageName, item)) continue;

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

            if (_depositScratch.Count == 0) return;

            foreach (var item in _depositScratch) st.Carry.Remove(item);

            // Container.Save() is private; Container wires its own Save into
            // Inventory.m_onChanged (confirmed via IL of Container.Awake), and
            // that Action field is public, so invoking it triggers the same
            // save without reaching for the private method.
            inv.m_onChanged?.Invoke();
        }
    }
}
