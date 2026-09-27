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

        public static void Reset() { _n = 0; _nextScan = 0f; }

        public static void Tick()
        {
            if (Time.time < _nextScan) return;
            _nextScan = Time.time + Plugin.LoggingTreeScanInterval;
            Rebuild();
        }

        static void Rebuild()
        {
            _n = 0;
            AddAll(UnityEngine.Object.FindObjectsByType<TreeBase>(FindObjectsSortMode.None));
            AddAll(UnityEngine.Object.FindObjectsByType<TreeLog>(FindObjectsSortMode.None));
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
                if (TreeSources.TryMatch(name, out var forced) && !forced) continue;

                if (_n >= _snap.Length) Array.Resize(ref _snap, _snap.Length * 2);
                _snap[_n++] = go;
            }
        }

        /// <summary>Nearest scanned tree/log inside the leash's radius -
        /// deliberately measured from the LEASH, not the troll, so a tree at
        /// the far edge of the radius is never skipped just because the troll
        /// itself happens to be standing near the boundary.</summary>
        static GameObject FindNearestTree(Vector3 pos, Vector3 leashPos)
        {
            GameObject best = null;
            float bestSq = float.MaxValue;
            float leashRSq = Plugin.LoggingLeashRadius * Plugin.LoggingLeashRadius;

            for (int i = 0; i < _n; i++)
            {
                var go = _snap[i];
                if (go == null) continue;

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
            const float R = 4f;
            var logs = UnityEngine.Object.FindObjectsByType<TreeLog>(FindObjectsSortMode.None);

            GameObject best = null;
            float bestSq = R * R;
            for (int i = 0; i < logs.Length; i++)
            {
                var go = logs[i] != null ? logs[i].gameObject : null;
                if (go == null) continue;
                float sq = (go.transform.position - nearPos).sqrMagnitude;
                if (sq <= bestSq) { bestSq = sq; best = go; }
            }
            return best;
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
            bool inCombat = ai.GetTargetCreature() != null || ai.IsAlerted();
            if (inCombat)
            {
                st.MarkInCombat();
                st.ForgetLoggingWork();
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

        /// <summary>The actual job: deposit if full, otherwise keep (or pick)
        /// a tree and chop it. Calls Threat.BaseTick itself - the only call
        /// site for a tick that stays inside the leash.</summary>
        static bool DriveWork(MonsterAI ai, CreatureState st, float dt, Vector3 leashPos, ref bool __result)
        {
            if (!Threat.BaseTick(ai, dt)) { __result = false; return false; }
            if (ai.IsSleeping()) { __result = true; return false; }

            if (st.HasCarry && CarryIsFull(st))
                return DriveDeposit(ai, st, dt, leashPos, ref __result);

            // The tree we were working vanished (felled down to nothing, or
            // chopped/despawned by something else) since our last look.
            // ReferenceEquals is deliberate: Unity's own == overload fake-nulls
            // a destroyed GameObject, which is exactly the case being tested
            // for here, so the plain == cannot be used to detect it.
            if (!ReferenceEquals(st.LogTargetTree, null) && st.LogTargetTree == null)
            {
                var successor = FindSuccessorLog(st.LogTargetPos);
                if (successor != null)
                {
                    st.LogTargetTree = successor;
                    st.LogTargetPos = successor.transform.position;
                    st.ChopTimer = 0f;
                }
                else
                {
                    CollectDrops(st);
                    st.ForgetLoggingWork();
                }
            }

            if (st.LogTargetTree == null)
            {
                var tree = FindNearestTree(ai.transform.position, leashPos);
                if (tree == null) { __result = true; return false; }   // nothing left to chop right now

                st.LogTargetTree = tree;
                st.LogTargetPos = tree.transform.position;
                st.ChopTimer = 0f;
            }

            return DriveChop(ai, st, dt, ref __result);
        }

        const float ChopRange = 3f;

        static bool DriveChop(MonsterAI ai, CreatureState st, float dt, ref bool __result)
        {
            float dist = Vector3.Distance(ai.transform.position, st.LogTargetPos);

            if (dist > ChopRange)
            {
                AiMotion.MoveTo(ai, dt, st.LogTargetPos, ChopRange - 0.5f, run: true);
                __result = true;
                return false;
            }

            ai.LookTowards(st.LogTargetPos);

            st.ChopTimer -= dt;
            if (st.ChopTimer <= 0f)
            {
                st.ChopTimer = Plugin.LoggingChopInterval;
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
            if (tree == null && log == null) return;

            var hit = new HitData();
            hit.m_damage.m_chop = Plugin.LoggingChopDamage;
            hit.m_point = st.LogTargetPos;
            hit.m_dir = Vector3.up;
            hit.SetAttacker(st.Chr);

            if (tree != null) tree.Damage(hit);
            else log.Damage(hit);

            // Cosmetic only - the real hit already landed above. Best-effort:
            // not every creature's Animator necessarily exposes an "attack"
            // trigger, so a miss here should never take the real chop with it.
            try { ai.GetComponent<ZSyncAnimation>()?.SetTrigger("attack"); }
            catch { /* cosmetic; the chop already happened */ }
        }

        const float DropPickupRadius = 4f;
        static readonly List<ItemDrop> _dropScan = new List<ItemDrop>();

        /// <summary>Sweeps whatever landed near a just-felled tree into the
        /// carry slot. Single-type, single-slot: the first item found sets
        /// what the slot holds, anything else is left on the ground exactly
        /// as spec'd, rather than silently discarded.</summary>
        static void CollectDrops(CreatureState st)
        {
            _dropScan.Clear();
            var all = UnityEngine.Object.FindObjectsByType<ItemDrop>(FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++)
            {
                var d = all[i];
                if (d == null) continue;
                if ((d.transform.position - st.LogTargetPos).sqrMagnitude >
                    DropPickupRadius * DropPickupRadius) continue;
                _dropScan.Add(d);
            }

            for (int i = 0; i < _dropScan.Count; i++)
                TryPickUp(st, _dropScan[i]);
        }

        static void TryPickUp(CreatureState st, ItemDrop drop)
        {
            if (drop == null || drop.m_itemData?.m_shared == null) return;

            string name = drop.m_itemData.m_dropPrefab != null
                ? drop.m_itemData.m_dropPrefab.name
                : CreatureRules.CleanName(drop.gameObject.name);

            if (st.HasCarry && !string.Equals(st.CarryItem, name, StringComparison.OrdinalIgnoreCase))
                return;   // holding something else already - leave this on the ground

            int max = drop.m_itemData.m_shared.m_maxStackSize > 0
                ? drop.m_itemData.m_shared.m_maxStackSize : 1;
            int already = st.HasCarry ? st.CarryCount : 0;
            int room = max - already;
            if (room <= 0) return;

            int have = drop.m_itemData.m_stack > 0 ? drop.m_itemData.m_stack : 1;
            int take = Mathf.Min(have, room);
            if (take <= 0) return;

            st.CarryItem = name;
            st.CarryCount = already + take;

            if (drop.m_nview == null || !drop.m_nview.IsValid()) return;
            if (!drop.m_nview.IsOwner()) drop.m_nview.ClaimOwnership();
            if (!drop.m_nview.IsOwner()) return;

            if (take >= have) drop.m_nview.Destroy();
            else
            {
                drop.m_itemData.m_stack -= take;
                drop.Save();
            }
        }

        static bool CarryIsFull(CreatureState st)
        {
            if (!st.HasCarry) return false;
            var prefab = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(st.CarryItem) : null;
            var idrop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
            int max = idrop != null && idrop.m_itemData?.m_shared != null
                ? idrop.m_itemData.m_shared.m_maxStackSize : st.CarryCount;
            return st.CarryCount >= max;
        }

        const float DepositRange = 2.5f;

        static bool DriveDeposit(MonsterAI ai, CreatureState st, float dt, Vector3 leashPos, ref bool __result)
        {
            var chestGo = TotemBind.FindNearestChest(leashPos);
            if (chestGo == null)
            {
                // No chest inside the radius: hold the stack and wait, rather
                // than wasting further chops on drops it cannot carry.
                __result = true;
                return false;
            }

            var container = chestGo.GetComponent<Container>();
            Vector3 chestPos = chestGo.transform.position;

            float dist = Vector3.Distance(ai.transform.position, chestPos);
            if (dist > DepositRange)
            {
                AiMotion.MoveTo(ai, dt, chestPos, DepositRange - 0.5f, run: true);
                __result = true;
                return false;
            }

            Deposit(st, container);
            __result = true;
            return false;
        }

        static void Deposit(CreatureState st, Container container)
        {
            if (container == null || !st.HasCarry) return;

            var prefab = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(st.CarryItem) : null;
            if (prefab == null)
            {
                if (Plugin.Verbose)
                    Plugin.Log.LogWarning(
                        $"[logging] {st.Prefab} could not resolve '{st.CarryItem}' to deposit it.");
                return;
            }

            var inv = container.GetInventory();
            if (inv == null) return;

            bool added = inv.AddItem(prefab.name, st.CarryCount, 1, 0, 0L, "");
            if (!added) return;   // chest is full; keep the stack, retry next tick

            if (Plugin.Verbose)
                Plugin.Log.LogInfo($"[logging] {st.Prefab} deposited {st.CarryCount}x {st.CarryItem}.");

            st.CarryItem = null;
            st.CarryCount = 0;
        }
    }
}
