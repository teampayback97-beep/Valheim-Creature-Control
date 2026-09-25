using System.Collections.Generic;
using UnityEngine;

namespace CreatureControl
{
    /// <summary>
    /// Per-creature runtime state. Lives as a component on the creature's own
    /// GameObject so it dies with the creature - no static dictionaries to leak.
    /// </summary>
    public class CreatureState : MonoBehaviour
    {
        public const string ZdoModeKey = "CC_mode";

        /// <summary>IsEnemy runs on a very hot path, so we keep an O(1) lookup
        /// rather than calling GetComponent per check.</summary>
        static readonly Dictionary<Character, CreatureState> _registry =
            new Dictionary<Character, CreatureState>();

        public static CreatureState For(Character c)
        {
            if (c == null) return null;
            return _registry.TryGetValue(c, out var s) ? s : null;
        }

        public static int Tracked => _registry.Count;
        public static void ClearRegistry() => _registry.Clear();

        public static IEnumerable<CreatureState> AllTracked => _registry.Values;

        public CreatureRule Rule;
        public Character Chr;
        public BaseAI Ai;
        public MonsterAI Mai;               // null for AnimalAI creatures
        public ZNetView Nview;
        public string Prefab;

        // --- pristine values, captured once, so deleting a config line puts
        //     the creature back exactly the way the game (and other mods) had it.
        bool _captured;
        float _origView, _origHear;
        bool _origCanBeAlerted;
        Character.Faction _origFaction;
        bool _origAttackPlayerObjects, _origHuntPlayer;
        float _origAlertRange, _origMaxChase, _origFleeLowHealth;
        bool _origFleeIfNotAlerted;
        bool _factionApplied;
        float _origRegenAllHPTime;

        /// <summary>Same reasoning as _huntApplied: we only own m_regenAllHPTime
        /// while the config actually asks us to scale it, so that at the default
        /// multiplier this mod's footprint on that field is exactly nothing.</summary>
        bool _regenApplied;

        /// <summary>SetHuntPlayer writes to the ZDO, so we only ever call it
        /// when the config actually asks us to. Writing a value back "just to
        /// be safe" produces network churn - and a ZDO write landing while
        /// another mod is repositioning the creature will snap it back.</summary>
        bool _huntApplied;

        bool _lastTamed, _tamedKnown;

        /// <summary>
        /// Character.SetTamed early-returns when the value hasn't changed, but a
        /// Harmony postfix still runs. Anything calling MakeTame() on an already
        /// tamed creature - BetterTames does this on every teleport tick - would
        /// otherwise drag a full AI re-apply along with it, several times a second.
        /// </summary>
        public bool TamedStateChanged()
        {
            bool now = Chr != null && Chr.IsTamed();
            if (_tamedKnown && now == _lastTamed) return false;
            _tamedKnown = true;
            _lastTamed = now;
            return true;
        }

        public bool ModeOverridden;
        BehaviorMode _mode = BehaviorMode.Aggressive;

        readonly Dictionary<Character, float> _grudges = new Dictionary<Character, float>();
        float _nextPrune;

        // ---- fear ---------------------------------------------------------------
        // The verdict is cached: re-deciding every frame would be both expensive
        // and twitchy, and a creature that flickers in and out of a rout looks
        // broken rather than frightened.
        float _nextFearEval;
        float _fleeUntil;
        bool _scared;
        Character _scaredOf;

        /// <summary>True for creatures the fear check may apply to at all.
        /// Tamed creatures and player summons are always fearless - a tamed wolf
        /// inherits Apex, and without this gate it would weigh itself against a
        /// Bjorn and abandon you.</summary>
        public bool FearApplies
        {
            get
            {
                if (!Plugin.FearEnabled || Chr == null || Mai == null) return false;
                if (Chr.IsTamed()) return false;
                if (Chr.m_faction == Character.Faction.Players) return false;

                // Bosses are fearless, always, whatever the config says and
                // whatever faction they sit in. Checked before every other rule
                // so nothing added later can ever make a boss back off.
                if (Chr.m_boss) return false;

                if (Rule != null && Rule.Baby == true) return true;   // babies always
                if (Rule != null && Rule.AlwaysFlee == true) return true;  // and pure prey
                if (Rule != null && Rule.Fearless == true) return false;
                return true;
            }
        }

        /// <summary>
        /// Decides - on a timer - whether this creature backs off from its
        /// current target, and for how long. Returns the position to run from,
        /// or null when it should fight.
        /// </summary>
        public bool WantsToFlee(Character target, out Vector3 from)
        {
            from = Vector3.zero;
            if (target == null) { _scared = false; _scaredOf = null; return false; }

            bool baby = Rule != null && Rule.Baby == true;
            bool bolts = baby || (Rule != null && Rule.AlwaysFlee == true);

            // Already cornered and committed: no re-deciding mid-stand.
            if (!bolts && Time.time < _corneredUntil) { _scared = false; return false; }

            // No retaliation special-case. Being attacked already gives a
            // creature a target, so it weighs the fight like any other: it
            // fights back when it can win, and routs when it plainly cannot.
            // The rout is no longer an escape hatch either - run it down and
            // the cornered rule below turns it around. Forcing retaliation on
            // top of that would only stop outmatched creatures ever retreating.

            // On the way to an ally that called: keep going. A creature that
            // re-decides mid-answer never actually reaches the fight. Babies
            // are checked first and are never held to this - nothing drags a
            // calf into a fight, and CallForHelp refuses to summon one anyway.
            if (!baby && IsRallying) { _scared = false; return false; }

            if (Time.time >= _nextFearEval || target != _scaredOf)
            {
                _nextFearEval = Time.time + Plugin.FearInterval;
                _scaredOf = target;
                _scared = bolts
                          || (IsSolitary && SolitaryYields(target))
                          || Band.ShouldBackOff(this, target);

                // A bounded window. While we are returning false from UpdateAI
                // the game never refreshes m_targetCreature, so the flee has to
                // expire on its own or a creature could run forever from a
                // target that has long since wandered off.
                if (_scared) _fleeUntil = Time.time + Plugin.FearInterval * 4f;
            }

            if (!_scared) { _chased = false; return false; }

            // It wants to run. Whether it CAN is a different question, and
            // distance alone cannot answer it: melee fighting happens close up,
            // so "the enemy is near" describes a brawl just as well as a chase.
            // The honest test is whether the gap is actually opening. Let it go
            // and it escapes; stay on it and it runs out of options and turns.
            if (!bolts && Plugin.CorneredRadius > 0f && Chr != null)
            {
                float gap = Vector3.Distance(Chr.transform.position,
                                             target.transform.position);
                if (!_chased)
                {
                    _chased = true; _chaseGap = gap; _chaseSince = Time.time;
                }
                else if (gap > _chaseGap + 1f)
                {
                    // Making ground. Reset the clock and keep running.
                    _chaseGap = gap; _chaseSince = Time.time;
                }
                else if (gap <= Plugin.CorneredRadius &&
                         Time.time - _chaseSince >= Plugin.CorneredAfter)
                {
                    _corneredUntil = Time.time + Plugin.CorneredCommit;
                    _chased = false;
                    _scared = false;
                    if (Plugin.Verbose)
                        Plugin.Log.LogInfo(
                            $"[CC fear] {Prefab} cannot break away - turning to fight.");
                    return false;
                }
            }

            // Window closed, or it has put real distance between them: hand the
            // tick back to vanilla so UpdateTarget can re-acquire or drop the
            // target properly.
            if (Time.time >= _fleeUntil)
            {
                _scared = false;
                _nextFearEval = 0f;     // reconsider immediately next tick
                return false;
            }

            from = target.transform.position;
            return true;
        }

        // ---- fire avoidance -------------------------------------------------------
        // Separate cache from the fear system: no target is needed, and it is
        // not gated by FearApplies/Fearless - a Deathsquito opts out of the
        // combat fear check but still needs to dodge a bonfire.
        float _nextFireEval;
        bool _avoidingFire;
        Vector3 _fireFrom;

        public bool AvoidsFire => Rule != null && Rule.AvoidsFire == true;

        /// <summary>
        /// On a timer, checks for a nearby fire source. Returns the position
        /// to steer away from, or false if there is nothing close enough to
        /// react to right now.
        /// </summary>
        public bool WantsToAvoidFire(out Vector3 from)
        {
            from = Vector3.zero;
            if (!Plugin.FireAvoidEnabled || !AvoidsFire || Chr == null) return false;

            if (Time.time >= _nextFireEval)
            {
                _nextFireEval = Time.time + Plugin.FearInterval;
                _avoidingFire = FireAversion.NearFire(
                    Chr.transform.position, Plugin.FireAvoidRadius, out _fireFrom);
            }

            if (!_avoidingFire) return false;
            from = _fireFrom;
            return true;
        }

        // ---- band membership ----------------------------------------------------
        // The verdict is reached once per band per interval and published to
        // every member, so a camp of twelve costs one evaluation.
        public int BandHop;
        float _bandUntil;
        Character _bandTarget;
        bool _bandBackOff;

        public bool BandBackOff => _bandBackOff;

        public bool BandVerdictFresh(Character target) =>
            _bandTarget == target && Time.time < _bandUntil;

        public void SetBandVerdict(Character target, bool backOff, float until)
        {
            _bandTarget = target;
            _bandBackOff = backOff;
            _bandUntil = until;

            // Keep the member's own timer in step, or it would re-evaluate on its
            // own schedule and undo the whole point of deciding together.
            _scaredOf = target;
            _scared = backOff;
            _nextFearEval = until;
            if (backOff) _fleeUntil = Time.time + Plugin.FearInterval * 4f;
        }

        /// <summary>How far this creature looks for FRIENDS. Deliberately
        /// separate from viewRange: widening it must never make anything spot an
        /// enemy sooner.</summary>
        public float RallyRadius =>
            Rule != null && Rule.RallyRadius.HasValue
                ? Rule.RallyRadius.Value
                : Plugin.DefaultRallyRadius;

        // ---- answering a call ---------------------------------------------------
        float _rallyUntil;

        // Cornered tracking: a creature that tried to run and could not.
        float _corneredUntil;
        bool _chased;
        float _chaseGap, _chaseSince;

        /// <summary>True while this creature is on its way to an ally that called
        /// for help. It holds its nerve for the trip rather than re-deciding alone
        /// halfway there - which is what stops a rally dissolving before it lands.
        /// The window is short, so if the fight really is hopeless it can still
        /// break once it arrives and sees for itself.</summary>
        public bool IsRallying => Time.time < _rallyUntil;

        /// <summary>Hunts alone: neither calls for help nor answers a call.
        /// It DOES still answer a dinner bell - refusing to cooperate is not
        /// the same as refusing a free meal.</summary>
        public bool IsSolitary => Rule != null && Rule.Solitary == true;

        /// <summary>How this creature reads a PLAYER. Anything with no rule of
        /// its own falls back to animal instinct, which is the safe default: it
        /// reads presence rather than gear, so an unclassified creature can
        /// never accidentally start judging armour it should not understand.</summary>
        public Perception Sight =>
            Rule != null && Rule.Sight.HasValue ? Rule.Sight.Value : Perception.Instinct;

        /// <summary>
        /// A solitary hunter wants an easy meal, alone. It stays out of fights
        /// already in progress, and if something stronger turns up - before or
        /// during - it gives up the kill rather than contest it.
        ///
        /// Deliberately blind to the player: being outgunned by a PERSON is the
        /// ordinary fear check's business, and retaliation is handled above.
        /// This is only ever about rival creatures.
        /// </summary>
        bool SolitaryYields(Character target)
        {
            if (Chr == null || target == null) return false;

            float mine = Threat.OfCreature(Chr);
            Vector3 at = target.transform.position;
            float r = Plugin.FearRadius;
            float rSq = r * r;

            _rivalScan.Clear();
            Character.GetCharactersInRange(at, r, _rivalScan);

            for (int i = 0; i < _rivalScan.Count; i++)
            {
                var c = _rivalScan[i];
                if (c == null || c == Chr || c == target || c.IsDead()) continue;
                if ((c.transform.position - at).sqrMagnitude > rSq) continue;

                var st = For(c);
                if (st != null && st.Rule != null && st.Rule.Baby == true) continue;

                // Someone else already has this kill.
                if (st != null && st.Mai != null && st.Mai.GetTargetCreature() == target)
                    return true;

                // Something bigger than me has turned up and wants a word.
                if (BaseAI.IsEnemy(c, Chr) && Threat.OfCreature(c) > mine)
                    return true;
            }
            return false;
        }

        static readonly List<Character> _rivalScan = new List<Character>();

        /// <summary>
        /// Whether this creature takes part in the call-for-help system at all,
        /// in either direction. Calling and answering are the same sociability,
        /// so one gate governs both.
        ///
        /// Out entirely:
        ///   - solitary / rallies=false: set in the config, per creature or per
        ///     faction. Loners, mindless things, and prey that never fights.
        ///   - babies: they only ever run, and nothing drags them into a fight.
        ///   - tamed: they answer to the player, not to the wild.
        ///
        /// Fearless is deliberately NOT a bar. A fearless creature never calls
        /// on its own - it is never frightened enough to reach that point - but
        /// it can still answer, and a fearless pack (goblins, ulv, lox) is
        /// exactly the kind that should come running.
        /// </summary>
        public bool RalliesWithOthers
        {
            get
            {
                if (Chr == null || Mai == null) return false;
                if (Chr.IsTamed()) return false;
                if (Rule == null) return true;
                if (Rule.Solitary == true) return false;
                if (Rule.Baby == true) return false;
                return true;
            }
        }

        public void MarkRallying() => _rallyUntil = Time.time + Plugin.RallySeconds;

        /// <summary>Set when a flee was attempted and the creature had nowhere to
        /// go. While this holds we hand every tick straight back to vanilla, so a
        /// creature the fear system cannot move behaves exactly as it did before
        /// the mod rather than standing still.</summary>
        float _fearBrokenUntil;
        public bool FearBroken => Time.time < _fearBrokenUntil;
        public void MarkFearUnusable()
        {
            _fearBrokenUntil = Time.time + 10f;
            ForgetFear();
        }

        // ---- stuck detection ----------------------------------------------------
        Vector3 _lastFleePos;
        float _fleeProgressAt;
        bool _trackingFlee;

        /// <summary>
        /// Called every tick we drive a flee. If the creature has not actually
        /// moved for a few seconds it is wedged - against geometry, or with no
        /// path anywhere - so we stop taking its ticks and let vanilla have it
        /// back. Measuring real movement is the only honest signal: Flee's return
        /// value means "stopped", which is true both on arrival and on failure.
        /// </summary>
        public void NoteFleeProgress(Vector3 pos)
        {
            if (!_trackingFlee)
            {
                _trackingFlee = true;
                _lastFleePos = pos;
                _fleeProgressAt = Time.time;
                return;
            }

            if ((pos - _lastFleePos).sqrMagnitude > 0.25f)   // half a metre
            {
                _lastFleePos = pos;
                _fleeProgressAt = Time.time;
                return;
            }

            if (Time.time - _fleeProgressAt > 3f)
            {
                if (Plugin.Verbose)
                    Plugin.Log.LogWarning(
                        $"[CC fear] {Prefab} has been trying to flee for 3s without moving - " +
                        "handing it back to vanilla.");
                MarkFearUnusable();
            }
        }

        public void ForgetFear()
        {
            _trackingFlee = false;
            _chased = false;
            _scared = false;
            _scaredOf = null;
            _nextFearEval = 0f;
            _bandTarget = null;
            _bandUntil = 0f;
        }

        public BehaviorMode Mode => _mode;

        // Unity overloads == so a destroyed object compares equal to null.
        // OnDestroy therefore can't use `Chr != null` to find its own key - by
        // then it reads as null and the entry would leak for the whole session.
        // Keep the reference and compare as a plain object instead.
        Character _key;

        public void Register()
        {
            _key = Chr;
            if ((object)_key != null) _registry[_key] = this;
        }

        void OnDestroy()
        {
            if ((object)_key != null) _registry.Remove(_key);
        }

        /// <summary>
        /// Unity runs every Awake before any Start. FactionAssigner assigns
        /// m_faction from a Character.Awake postfix, so waiting until Start is
        /// what lets us read the creature's *settled* faction and resolve
        /// [@Faction] rules against it - no Harmony ordering games required.
        /// </summary>
        bool _ran;
        void Start() { if (!_ran) ResolveAndApply(); }

        public void ResolveAndApply()
        {
            if (Chr == null || Ai == null) { return; }
            _ran = true;

            CaptureOriginals();

            // Read the saved stance here rather than in Awake: the ZDO isn't
            // reliably attached that early, and a missed read would silently
            // lose a stance the player had chosen.
            if (!ModeOverridden) LoadMode();

            // A by-name rule may move the creature into another faction, and
            // that has to happen before we resolve faction-keyed rules.
            var byName = CreatureRules.FindByPrefab(Prefab);
            if (byName != null && byName.FactionId.HasValue)
            {
                Chr.m_faction = (Character.Faction)byName.FactionId.Value;
                _factionApplied = true;
            }
            else if (_factionApplied)
            {
                // The config used to move it and no longer does.
                Chr.m_faction = _origFaction;
                _factionApplied = false;
            }

            // Keep the tamed-change guard in step with reality.
            TamedStateChanged();

            Rule = CreatureRules.Resolve(Prefab, (int)Chr.m_faction);

            if (Rule == null)
            {
                // The rule that used to cover this creature is gone. Undo the
                // senses AND the stance - leaving _mode on Passive would keep
                // the IsEnemy hook neutering a creature with no rule at all.
                RestoreOriginals();
                if (!ModeOverridden) _mode = BehaviorMode.Aggressive;
                return;
            }

            if (!ModeOverridden) RefreshFromRule();
            Apply();

            if (Plugin.Verbose)
                Plugin.Log.LogInfo(
                    $"[CC] {Prefab} <{FactionRegistry.NameOf((int)Chr.m_faction)}> " +
                    $"ai={(Mai != null ? "MonsterAI" : "AnimalAI")} " +
                    $"stance={_mode.Pretty()} view={Ai.m_viewRange:0.#} hear={Ai.m_hearRange:0.#} " +
                    $"threat={Threat.OfCreature(Chr):0.##} " +
                    $"fearless={(Rule.Fearless == true)} baby={(Rule.Baby == true)} " +
                    $"canBeAlerted={Ai.m_canBeAlerted} rally={RallyRadius:0.#} " +
                    $"solitary={IsSolitary} sees={Sight.Pretty()}" +
                    (Mai != null ? $" chase={Mai.m_maxChaseDistance:0.#} flee={Mai.m_fleeIfLowHealth:0.##}" +
                     $" fleeNotAlerted={Mai.m_fleeIfNotAlerted}" : "") +
                    $" (from {Rule.Source})");
        }

        public void CaptureOriginals()
        {
            if (_captured || Ai == null) return;
            _captured = true;
            _origView = Ai.m_viewRange;
            _origHear = Ai.m_hearRange;
            _origCanBeAlerted = Ai.m_canBeAlerted;
            if (Chr != null)
            {
                _origFaction = Chr.m_faction;
                _origRegenAllHPTime = Chr.m_regenAllHPTime;
            }
            if (Mai != null)
            {
                _origAttackPlayerObjects = Mai.m_attackPlayerObjects;
                _origHuntPlayer = Mai.m_enableHuntPlayer;
                _origAlertRange = Mai.m_alertRange;
                _origMaxChase = Mai.m_maxChaseDistance;
                _origFleeLowHealth = Mai.m_fleeIfLowHealth;
                _origFleeIfNotAlerted = Mai.m_fleeIfNotAlerted;
            }
        }

        /// <summary>Put everything back exactly as it was found.</summary>
        public void RestoreOriginals()
        {
            if (!_captured || Ai == null) return;
            Ai.m_viewRange = _origView;
            Ai.m_hearRange = _origHear;
            Ai.m_canBeAlerted = _origCanBeAlerted;
            if (Mai != null)
            {
                Mai.m_attackPlayerObjects = _origAttackPlayerObjects;
                Mai.m_alertRange = _origAlertRange;
                Mai.m_maxChaseDistance = _origMaxChase;
                Mai.m_fleeIfLowHealth = _origFleeLowHealth;
                Mai.m_fleeIfNotAlerted = _origFleeIfNotAlerted;
                // Only undo the hunt flag if we were the ones who set it.
                if (_huntApplied)
                {
                    Mai.m_enableHuntPlayer = _origHuntPlayer;
                    Ai.SetHuntPlayer(_origHuntPlayer);
                    _huntApplied = false;
                }
            }
            if (_factionApplied && Chr != null)
            {
                Chr.m_faction = _origFaction;
                _factionApplied = false;
            }
            // Only undo the regen scaling if we were the ones who set it.
            if (_regenApplied && Chr != null)
            {
                Chr.m_regenAllHPTime = _origRegenAllHPTime;
                _regenApplied = false;
            }
        }

        public void SetMode(BehaviorMode m, bool persist)
        {
            _mode = m;
            ModeOverridden = true;
            if (persist) SaveMode();
        }

        public void RefreshFromRule()
        {
            if (ModeOverridden) return;
            if (Rule == null) { _mode = BehaviorMode.Aggressive; return; }
            bool tamed = Chr != null && Chr.IsTamed();
            var pick = tamed ? (Rule.TamedBehavior ?? Rule.WildBehavior) : Rule.WildBehavior;
            _mode = pick ?? BehaviorMode.Aggressive;
        }

        /// <summary>Write the rule onto the creature. Always starts from the
        /// captured originals, so removing a line genuinely reverts it.</summary>
        public void Apply()
        {
            if (Ai == null || Rule == null) return;
            CaptureOriginals();

            bool tamed = Chr != null && Chr.IsTamed();

            float view = _origView, hear = _origHear;
            var v = tamed ? (Rule.TamedView ?? Rule.WildView) : Rule.WildView;
            var h = tamed ? (Rule.TamedHear ?? Rule.WildHear) : Rule.WildHear;
            if (v.HasValue) view = v.Value;
            if (h.HasValue) hear = h.Value;
            Ai.m_viewRange = view;
            Ai.m_hearRange = hear;

            // IsEnemy governs who a creature fights, but structures are decided
            // separately by m_attackPlayerObjects - without this a "passive"
            // greydwarf still happily demolishes your walls.
            if (Mai != null)
            {
                Mai.m_attackPlayerObjects =
                    _mode == BehaviorMode.Aggressive && _origAttackPlayerObjects;

                Mai.m_alertRange = Rule.AlertRange ?? _origAlertRange;
                Mai.m_maxChaseDistance = Rule.MaxChaseDistance ?? _origMaxChase;
                Mai.m_fleeIfLowHealth = Rule.FleeIfLowHealth ?? _origFleeLowHealth;
                Mai.m_fleeIfNotAlerted = Rule.FleeIfNotAlerted ?? _origFleeIfNotAlerted;

                // m_enableHuntPlayer is only read in MonsterAI.Awake, so the
                // flag alone would do nothing to a creature already spawned;
                // SetHuntPlayer is the live switch. It also writes to the ZDO,
                // so it is called ONLY when the config has an opinion - and once
                // more to undo ourselves if that opinion is later removed.
                if (Rule.EnableHuntPlayer.HasValue)
                {
                    Mai.m_enableHuntPlayer = Rule.EnableHuntPlayer.Value;
                    Ai.SetHuntPlayer(Rule.EnableHuntPlayer.Value);
                    _huntApplied = true;
                }
                else if (_huntApplied)
                {
                    Mai.m_enableHuntPlayer = _origHuntPlayer;
                    Ai.SetHuntPlayer(_origHuntPlayer);
                    _huntApplied = false;
                }
            }

            // A neutral creature has to be able to wake up when something hits it.
            bool alertable = _mode == BehaviorMode.Neutral ? true : _origCanBeAlerted;

            // Fear needs an alerted state to express itself. BaseAI.Flee ends with
            //     MoveTo(dt, m_fleeTarget, 1f, IsAlerted());
            // and MoveTo's last argument is `run`. SetAlerted opens with
            //     if (!m_canBeAlerted) return;
            // so on a creature shipped with that flag off, IsAlerted() can never
            // become true and the thing AMBLES away from what it fears instead of
            // bolting. Anything the fear system drives has to be alertable.
            // _origCanBeAlerted is untouched, so removing the rule restores it.
            if (Plugin.FearEnabled && Mai != null && Chr != null &&
                !Chr.IsTamed() && Chr.m_faction != Character.Faction.Players &&
                !(Rule != null && Rule.Fearless == true))
                alertable = true;

            Ai.m_canBeAlerted = alertable;

            // BaseAI.UpdateRegeneration heals maxHealth / m_regenAllHPTime worth
            // of HP per second - that field is "seconds for a full heal", so a
            // HIGHER multiplier needs a LOWER value here.
            //
            // Written ONLY while we actually want it scaled. Writing the captured
            // value back every Apply() "just to be safe" would mean silently
            // owning a field we have no opinion about, and stomping anything else
            // that ever sets it - the same reason SetHuntPlayer above is only
            // called when the config asks. At the default multiplier of 1 this
            // block now touches nothing at all.
            //
            // A prefab shipping 0 is left alone deliberately: vanilla divides by
            // this, so 0 there means "heal instantly", not "never heal", and
            // scaling it would be meaningless anyway.
            if (Chr != null)
            {
                float mult = Plugin.TamedRegenMultiplier;
                bool wantScaled = tamed && mult != 1f && _origRegenAllHPTime > 0f;

                if (wantScaled)
                {
                    Chr.m_regenAllHPTime = _origRegenAllHPTime / mult;
                    _regenApplied = true;
                }
                else if (_regenApplied)
                {
                    // We scaled it before and no longer want to: put it back once.
                    Chr.m_regenAllHPTime = _origRegenAllHPTime;
                    _regenApplied = false;
                }
            }
        }

        // ---- retaliation memory -------------------------------------------------

        public void Remember(Character attacker)
        {
            if (attacker == null || attacker == Chr) return;
            _grudges[attacker] = Time.time + Plugin.GrudgeSeconds;
        }

        public bool HoldsGrudge(Character other)
        {
            if (other == null || _grudges.Count == 0) return false;
            Prune();
            return _grudges.TryGetValue(other, out float until) && Time.time < until;
        }

        public void ForgetAll() => _grudges.Clear();

        void Prune()
        {
            if (Time.time < _nextPrune) return;
            _nextPrune = Time.time + 5f;
            List<Character> dead = null;
            foreach (var kv in _grudges)
                if (Time.time >= kv.Value || kv.Key == null)
                    (dead ?? (dead = new List<Character>())).Add(kv.Key);
            if (dead != null) foreach (var d in dead) _grudges.Remove(d);
        }

        // ---- persistence --------------------------------------------------------

        public void SaveMode()
        {
            if (Nview == null || !Nview.IsValid()) return;
            if (!Nview.IsOwner()) Nview.ClaimOwnership();
            if (!Nview.IsOwner()) return;
            Nview.GetZDO().Set(ZdoModeKey, (int)_mode);
        }

        public bool LoadMode()
        {
            if (Nview == null || !Nview.IsValid()) return false;
            var zdo = Nview.GetZDO();
            if (zdo == null) return false;

            int stored = zdo.GetInt(ZdoModeKey, -1);
            if (stored < (int)BehaviorMode.Aggressive || stored > (int)BehaviorMode.Passive)
                return false;

            _mode = (BehaviorMode)stored;
            ModeOverridden = true;
            return true;
        }
    }
}
