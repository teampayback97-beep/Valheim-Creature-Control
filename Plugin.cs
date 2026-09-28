using System;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace CreatureControl
{
    // Deliberately no [BepInProcess]: restricting to valheim.exe would stop the
    // mod loading on a dedicated server, which is exactly where creature AI
    // runs for most of the world.
    [BepInPlugin(GUID, NAME, VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        public const string GUID = "koro.creaturecontrol";
        public const string NAME = "CreatureControl";
        public const string VERSION = "1.0.0";

        public static ManualLogSource Log;
        internal static Plugin Instance;

        static ConfigEntry<bool> _verbose;
        static ConfigEntry<bool> _cycling;
        static ConfigEntry<float> _grudge;
        static ConfigEntry<KeyboardShortcut> _cycleKey;
        static ConfigEntry<KeyboardShortcut> _forceTargetKey;
        static ConfigEntry<float> _forceTargetRange;

        static ConfigEntry<bool> _enrageOn;

        static ConfigEntry<bool> _fearOn;
        static ConfigEntry<float> _fearRadius;
        static ConfigEntry<float> _fearInterval;
        static ConfigEntry<float> _starScale;
        static ConfigEntry<float> _petWeight;
        static ConfigEntry<float> _playerScale;
        static ConfigEntry<float> _defaultThreat;
        static ConfigEntry<bool> _chainAggro;
        static ConfigEntry<float> _rally;
        static ConfigEntry<bool> _callHelp;
        static ConfigEntry<float> _helpRadius;
        static ConfigEntry<float> _rallySeconds;
        static ConfigEntry<float> _fearAlertRadius;
        static ConfigEntry<float> _fearAlertDelay;
        static ConfigEntry<bool> _dinnerBell;
        static ConfigEntry<float> _armorDiv;
        static ConfigEntry<float> _weaponDiv;
        static ConfigEntry<float> _weaponInputCap;
        static ConfigEntry<float> _dangerCap;
        static ConfigEntry<float> _spiritDiv;
        static ConfigEntry<float> _holyArmorDiv;
        static ConfigEntry<float> _instinctBase;
        static ConfigEntry<float> _instinctStep;
        static ConfigEntry<float> _corneredRadius;
        static ConfigEntry<float> _corneredAfter;
        static ConfigEntry<float> _corneredCommit;
        static ConfigEntry<int> _bandSize;
        static ConfigEntry<int> _bandHops;
        static ConfigEntry<float> _bandSpread;
        static ConfigEntry<bool> _catalog;
        static ConfigEntry<bool> _squelchLogs;
        static ConfigEntry<bool> _birchFine;
        static ConfigEntry<bool> _birchSeeds;
        static ConfigEntry<float> _tamedRegen;
        static ConfigEntry<bool> _tameStructures;
        static ConfigEntry<bool> _offlineTaming;

        static ConfigEntry<bool> _phaseOn;
        static ConfigEntry<bool> _stalkOn;

        static ConfigEntry<bool> _fireAvoidOn;
        static ConfigEntry<float> _fireTorch, _fireCampfire, _fireBonfire;
        static ConfigEntry<float> _fireMaxRadius;
        static ConfigEntry<float> _fireInterval, _fireCommit, _fireScan;
        static ConfigEntry<FireTier> _fireUnknown;
        static ConfigEntry<float> _fireGuessTorch, _fireGuessCampfire;

        static ConfigEntry<bool> _loggingOn;
        static ConfigEntry<KeyboardShortcut> _loggingKey;
        static ConfigEntry<float> _leashRadius;
        static ConfigEntry<float> _chopInterval;
        static ConfigEntry<float> _chopDamage;
        static ConfigEntry<float> _treeScanInterval;
        static ConfigEntry<float> _leashScanInterval;
        static ConfigEntry<float> _loggingReturnTimeout;

        public static bool Verbose => _verbose != null && _verbose.Value;
        public static bool AllowStanceCycling => _cycling == null || _cycling.Value;
        public static float GrudgeSeconds => _grudge == null ? 30f : _grudge.Value;
        public static string CycleKeyLabel =>
            _cycleKey == null ? "L.Alt + X" : _cycleKey.Value.ToString();
        public static string ForceTargetKeyLabel =>
            _forceTargetKey == null ? "L.Alt + T" : _forceTargetKey.Value.ToString();
        public static float ForceTargetRange => _forceTargetRange == null ? 50f : _forceTargetRange.Value;

        /// <summary>Master switch for the enrage mechanic. Reuses
        /// FearInterval for its own re-check cadence rather than adding a
        /// second timer - it's the same "how often does a creature
        /// reconsider" question the fear system already answers.</summary>
        public static bool EnrageEnabled => _enrageOn == null || _enrageOn.Value;

        public static bool FearEnabled => _fearOn == null || _fearOn.Value;
        public static float FearRadius => _fearRadius == null ? 20f : _fearRadius.Value;
        public static float FearInterval => _fearInterval == null ? 2f : _fearInterval.Value;

        /// <summary>Instinctive smoke/fire avoidance - separate from the
        /// danger-score fear system. Applies only to creatures with
        /// avoidsFire = true in their rule (Deathsquito by default), and
        /// applies even to creatures the fear system treats as fearless.</summary>
        public static bool FireAvoidEnabled => _fireAvoidOn == null || _fireAvoidOn.Value;
        public static bool PhaseEnabled => _phaseOn == null || _phaseOn.Value;
        public static bool StalkEnabled => _stalkOn == null || _stalkOn.Value;
        public static float FireRadiusTorch => _fireTorch == null ? 5f : _fireTorch.Value;
        public static float FireRadiusCampfire => _fireCampfire == null ? 9f : _fireCampfire.Value;
        public static float FireRadiusBonfire => _fireBonfire == null ? 15f : _fireBonfire.Value;
        public static float FireMaxRadius => _fireMaxRadius == null ? 40f : _fireMaxRadius.Value;
        public static float FireInterval => _fireInterval == null ? 1f : _fireInterval.Value;
        public static float FireCommitSeconds => _fireCommit == null ? 5f : _fireCommit.Value;
        public static float FireScanInterval => _fireScan == null ? 0.5f : _fireScan.Value;
        public static FireTier FireUnknownTier => _fireUnknown == null ? FireTier.Campfire : _fireUnknown.Value;
        public static float FireRadiusGuessTorch => _fireGuessTorch == null ? 1.5f : _fireGuessTorch.Value;
        public static float FireRadiusGuessCampfire => _fireGuessCampfire == null ? 3.5f : _fireGuessCampfire.Value;

        public static bool LoggingEnabled => _loggingOn == null || _loggingOn.Value;
        public static string LoggingKeyLabel =>
            _loggingKey == null ? "L.Ctrl + X" : _loggingKey.Value.ToString();
        public static float LoggingLeashRadius => _leashRadius == null ? 20f : _leashRadius.Value;
        public static float LoggingChopInterval => _chopInterval == null ? 2f : _chopInterval.Value;
        /// <summary>Flat "chop" HitData damage per hit, independent of the
        /// troll's own real attack stats - simplicity over fidelity, so the
        /// felling rate is tunable in one number rather than tied to
        /// whatever a troll's fists happen to deal.</summary>
        public static float LoggingChopDamage => _chopDamage == null ? 50f : _chopDamage.Value;
        public static float LoggingTreeScanInterval => _treeScanInterval == null ? 3f : _treeScanInterval.Value;
        public static float LoggingLeashScanInterval => _leashScanInterval == null ? 5f : _leashScanInterval.Value;
        public static float LoggingReturnTimeout => _loggingReturnTimeout == null ? 30f : _loggingReturnTimeout.Value;
        public static float StarThreatScale => _starScale == null ? 0.6f : _starScale.Value;
        public static float PetThreatWeight => _petWeight == null ? 0.5f : _petWeight.Value;
        public static float PlayerThreatScale => _playerScale == null ? 1f : _playerScale.Value;
        public static float DefaultThreat => _defaultThreat == null ? 1f : _defaultThreat.Value;
        public static bool ChainAggro => _chainAggro == null || _chainAggro.Value;
        public static float DefaultRallyRadius => _rally == null ? 12f : _rally.Value;
        public static bool DinnerBell => _dinnerBell == null || _dinnerBell.Value;
        public static float ArmorDivisor => _armorDiv == null || _armorDiv.Value <= 0f ? 100f : _armorDiv.Value;
        public static float WeaponDivisor => _weaponDiv == null || _weaponDiv.Value <= 0f ? 150f : _weaponDiv.Value;
        public static float WeaponDamageInputCap => _weaponInputCap == null ? 400f : _weaponInputCap.Value;
        public static float DangerCap => _dangerCap == null ? 5f : _dangerCap.Value;
        public static float SpiritDivisor => _spiritDiv == null || _spiritDiv.Value <= 0f ? 30f : _spiritDiv.Value;
        public static float HolyArmorDivisor => _holyArmorDiv == null || _holyArmorDiv.Value <= 0f ? 300f : _holyArmorDiv.Value;
        public static float InstinctBase => _instinctBase == null ? 0.2f : _instinctBase.Value;
        public static float InstinctStep => _instinctStep == null ? 0.5f : _instinctStep.Value;
        public static float CorneredRadius => _corneredRadius == null ? 5f : _corneredRadius.Value;
        public static float CorneredAfter => _corneredAfter == null ? 2f : _corneredAfter.Value;
        public static float CorneredCommit => _corneredCommit == null ? 6f : _corneredCommit.Value;

        public static bool CallForHelpEnabled => _callHelp == null || _callHelp.Value;
        public static float HelpRadius => _helpRadius == null ? 25f : _helpRadius.Value;
        public static float RallySeconds => _rallySeconds == null ? 5f : _rallySeconds.Value;

        /// <summary>How far a panicking creature's alert reaches. Only creatures
        /// given sharesFear = true in the config send or receive one.</summary>
        public static float FearAlertRadius => _fearAlertRadius == null ? 15f : _fearAlertRadius.Value;
        /// <summary>The reaction-time delay before a nearby creature acts on
        /// someone else's fear instead of instantly knowing about it.</summary>
        public static float FearAlertDelay => _fearAlertDelay == null ? 0.5f : _fearAlertDelay.Value;
        public static int BandMaxSize => _bandSize == null ? 16 : _bandSize.Value;
        public static int BandMaxHops => _bandHops == null ? 3 : _bandHops.Value;
        public static float BandMaxSpread => _bandSpread == null ? 60f : _bandSpread.Value;
        public static bool WriteCatalog => _catalog != null && _catalog.Value;
        public static bool SquelchNoisyLogs => _squelchLogs == null || _squelchLogs.Value;
        public static bool BirchFineWoodOnly => _birchFine != null && _birchFine.Value;
        public static bool BirchKeepSeeds => _birchSeeds != null && _birchSeeds.Value;
        public static float TamedRegenMultiplier => _tamedRegen == null ? 1f : _tamedRegen.Value;
        public static bool TamesSpareStructures => _tameStructures == null || _tameStructures.Value;
        public static bool OfflineTamingEnabled => _offlineTaming == null || _offlineTaming.Value;

        // The old single player score (base + sqrt(armor), capped at 2.8) is
        // gone deliberately. It could only ever produce ONE number, and the
        // whole point now is that a goblin, a wolf and a skeleton look at the
        // same person and see three different things. Everything that decides
        // how a player reads lives in [Danger] below and in PlayerDanger.cs.

        string _cfgDir;
        FileSystemWatcher _watcher;
        Harmony _harmony;

        // FileSystemWatcher raises its events on a worker thread, where touching
        // any Unity API is illegal. The callback only flips this flag; the real
        // work happens in Update(), on the main thread.
        volatile bool _dirty;
        float _reloadAt = -1f;

        void Awake()
        {
            Instance = this;
            Log = Logger;
            _cfgDir = Paths.ConfigPath;

            _verbose = Config.Bind("General", "Verbose Logging", false,
                "Log every creature as it spawns, with the stance and senses applied. Noisy; for tuning only.");
            _cycling = Config.Bind("General", "Allow Stance Cycling", true,
                "Lets you cycle a tamed creature's stance in-game by looking at it and pressing the hotkey.");
            _cycleKey = Config.Bind("General", "Stance Cycle Key",
                new KeyboardShortcut(KeyCode.X, KeyCode.LeftAlt),
                "Look at one of your tames and press this to cycle Passive / Neutral / Aggressive.");
            _grudge = Config.Bind("General", "Retaliation Memory", 30f,
                new ConfigDescription(
                    "How many seconds a Neutral creature stays angry at whatever hurt it before settling down.",
                    new AcceptableValueRange<float>(1f, 600f)));
            _forceTargetKey = Config.Bind("General", "Force Target Key",
                new KeyboardShortcut(KeyCode.T, KeyCode.LeftAlt),
                "Look at anything and press this to send every tame you have after it, overriding " +
                "stance, Guard and fear alike. Each tame keeps fighting until the target dies or it " +
                "would have to leave its own leash (alertRange) to reach it - never further than that.");
            _forceTargetRange = Config.Bind("General", "Force Target Range", 50f,
                new ConfigDescription(
                    "How far the Force Target hotkey can pick out a target you are looking at.",
                    new AcceptableValueRange<float>(10f, 200f)));

            _enrageOn = Config.Bind("Enrage", "Enable Enrage", true,
                "Master switch for the enrage mechanic - a creature reads the same " +
                "outnumbered/not-outnumbered verdict the fear system would flee on, whether or " +
                "not fear ever applies to it (Fearless creatures included). Never changes " +
                "whether or how anything flees; see enrageWhenOutnumbered per creature.");

            _fearOn = Config.Bind("Fear", "Enable Fear", true,
                "Creatures weigh their side against yours before starting a fight. Never blocks " +
                "retaliation, camp defence, joining a fight already running, or bosses.");
            _fearRadius = Config.Bind("Fear", "Awareness Radius", 20f,
                new ConfigDescription(
                    "How far a creature looks when counting who is on each side.",
                    new AcceptableValueRange<float>(5f, 60f)));
            _fearInterval = Config.Bind("Fear", "Re-check Seconds", 2f,
                new ConfigDescription(
                    "How often a creature reconsiders. Lower reacts faster and costs more.",
                    new AcceptableValueRange<float>(0.25f, 10f)));
            _starScale = Config.Bind("Fear", "Threat Per Star", 0.6f,
                new ConfigDescription(
                    "Extra threat per star, so a 2-star creature is worth (1 + 0.6*2) of its base. " +
                    "Set 0 if you are not running a level mod.",
                    new AcceptableValueRange<float>(0f, 3f)));
            _petWeight = Config.Bind("Fear", "Tamed Threat Weight", 0.5f,
                new ConfigDescription(
                    "How much of its threat a tamed creature contributes to your side. " +
                    "1 makes a single tamed Bjorn clear the area around you.",
                    new AcceptableValueRange<float>(0f, 1f)));
            _playerScale = Config.Bind("Fear", "Player Threat Scale", 1f,
                new ConfigDescription(
                    "Multiplies how dangerous you look. Turn down if the late game goes quiet.",
                    new AcceptableValueRange<float>(0f, 3f)));
            _defaultThreat = Config.Bind("Fear", "Default Threat", 1f,
                new ConfigDescription(
                    "Threat for any creature the config does not give a value - one Fuling.",
                    new AcceptableValueRange<float>(0.01f, 50f)));

            _chainAggro = Config.Bind("Fear", "Chain Aggro", true,
                "When a band commits, members that had not noticed the enemy are handed the " +
                "target, so the reinforcements it counted actually arrive. Turn off and a band " +
                "still decides together but nobody gets pulled in.");
            _rally = Config.Bind("Fear", "Default Rally Radius", 12f,
                new ConfigDescription(
                    "How far a creature looks for FRIENDS when banding up. Never affects how " +
                    "far it can see an enemy. Overridable per creature with rallyRadius.",
                    new AcceptableValueRange<float>(0f, 50f)));
            _callHelp = Config.Bind("Fear", "Call For Help", true,
                "A creature that finds itself outmatched calls its allies instead of just " +
                "running. They are pointed at the enemy and come, so next time the odds are " +
                "counted the pack may be strong enough to fight. Applies to every creature " +
                "the fear system drives.");
            _helpRadius = Config.Bind("Fear", "Help Radius", 25f,
                new ConfigDescription(
                    "How far a cry for help carries. Deliberately wider than the rally radius - " +
                    "a creature can be heard from further than its friends can be seen. " +
                    "Never affects how far anything can see an ENEMY.",
                    new AcceptableValueRange<float>(0f, 100f)));
            _rallySeconds = Config.Bind("Fear", "Rally Nerve Seconds", 5f,
                new ConfigDescription(
                    "How long a creature answering a call holds its nerve on the way over. " +
                    "Without it a responder re-decides alone halfway there and turns back, and " +
                    "the rally falls apart before it arrives.",
                    new AcceptableValueRange<float>(0f, 30f)));
            _dinnerBell = Config.Bind("Fear", "Cry For Help Draws Predators", true,
                "A cry for help carries to everything, not just friends. Predators that hunt " +
                "the caller hear it too and come for the caller instead of coming to its aid. " +
                "Solitary hunters answer this even though they never join a rally.");
            _fearAlertRadius = Config.Bind("Fear", "Fear Alert Radius", 15f,
                new ConfigDescription(
                    "How far a panicking creature's alert reaches. Only creatures given " +
                    "sharesFear = true in the config send or receive one - currently just Deer, " +
                    "as a test case.",
                    new AcceptableValueRange<float>(1f, 60f)));
            _fearAlertDelay = Config.Bind("Fear", "Fear Alert Delay", 0.5f,
                new ConfigDescription(
                    "How long a nearby creature takes to react to someone else's panic. A real " +
                    "animal notices its herd bolting and reacts a beat later, rather than " +
                    "instantly knowing there is danger.",
                    new AcceptableValueRange<float>(0f, 5f)));

            _armorDiv = Config.Bind("Danger", "Armor Divisor", 100f,
                new ConfigDescription(
                    "Armour rating divided by this becomes part of your danger, for creatures " +
                    "that can read gear. 100 means a full suit of the best armour in your game " +
                    "is worth about 2.4 - roughly two and a half fulings.",
                    new AcceptableValueRange<float>(10f, 500f)));
            _weaponDiv = Config.Bind("Danger", "Weapon Divisor", 150f,
                new ConfigDescription(
                    "Weapon damage divided by this becomes the other part of your danger. 150 " +
                    "means the strongest realistic weapon is worth about 2.2. Lower this to make " +
                    "the weapon matter more than the armour.",
                    new AcceptableValueRange<float>(10f, 1000f)));
            _weaponInputCap = Config.Bind("Danger", "Weapon Damage Ignored Above", 400f,
                new ConfigDescription(
                    "Damage above this is ignored when judging you. Guards against debug and " +
                    "admin items - there are weapons in the loaded mods listing six-figure " +
                    "damage, and without this one of them would terrify the entire map.",
                    new AcceptableValueRange<float>(50f, 100000f)));
            _dangerCap = Config.Bind("Danger", "Danger Cap", 5f,
                new ConfigDescription(
                    "The most frightening a player can ever read, to anything. Without a ceiling " +
                    "a fully geared player eventually outscales every creature and the world goes " +
                    "quiet. 5 means the scariest possible player is worth five fulings.",
                    new AcceptableValueRange<float>(1f, 20f)));

            _spiritDiv = Config.Bind("Danger", "Spirit Divisor", 30f,
                new ConfigDescription(
                    "Spirit damage divided by this is how frightening you are to the undead and " +
                    "demons. 30 is the median spirit weapon in your game, so an average silver " +
                    "weapon makes you as frightening to the dead as one fuling is to the living.",
                    new AcceptableValueRange<float>(1f, 500f)));
            _holyArmorDiv = Config.Bind("Danger", "Holy Armor Divisor", 300f,
                new ConfigDescription(
                    "Armour adds a small bonus on top of spirit damage: 1 + armour/this. 300 is " +
                    "near the maximum armour possible, so armour can never more than double your " +
                    "holy threat. With no spirit damage equipped this does nothing at all.",
                    new AcceptableValueRange<float>(50f, 2000f)));

            _instinctBase = Config.Bind("Danger", "Instinct Base", 0.2f,
                new ConfigDescription(
                    "What a naked, empty-handed person is worth to an ANIMAL. Animals cannot " +
                    "read gear; they only see whether you are dressed and whether your hands " +
                    "are full.",
                    new AcceptableValueRange<float>(0f, 5f)));
            _instinctStep = Config.Bind("Danger", "Instinct Step", 0.5f,
                new ConfigDescription(
                    "Added once for wearing anything at all, and again for holding anything in " +
                    "your hands. A sheathed weapon is ignored - a wolf sees empty hands. At the " +
                    "default a dressed and armed person reads 1.2, just above a lone wolf.",
                    new AcceptableValueRange<float>(0f, 5f)));

            _corneredRadius = Config.Bind("Danger", "Cornered Radius", 5f,
                new ConfigDescription(
                    "Inside this range a frightened creature stops running and turns to fight - " +
                    "it has run out of room. Chase something down and it rounds on you, so " +
                    "hunting for drops does not become a footrace. 0 disables.",
                    new AcceptableValueRange<float>(0f, 30f)));
            _corneredAfter = Config.Bind("Danger", "Cornered After Seconds", 2f,
                new ConfigDescription(
                    "How long a fleeing creature gets to open the gap before it decides it " +
                    "cannot escape. Distance alone cannot judge this - melee fighting happens " +
                    "close up, so what matters is whether it is actually getting away.",
                    new AcceptableValueRange<float>(0.5f, 15f)));
            _corneredCommit = Config.Bind("Danger", "Cornered Commit Seconds", 6f,
                new ConfigDescription(
                    "Once cornered, how long it fights before reconsidering. Stops a trapped " +
                    "creature flickering between running and turning. Back off during this and " +
                    "it breaks away again once the window closes.",
                    new AcceptableValueRange<float>(0f, 30f)));
            _bandSize = Config.Bind("Fear", "Band Max Size", 16,
                new ConfigDescription(
                    "Hard cap on how many creatures one band may contain.",
                    new AcceptableValueRange<int>(1, 64)));
            _bandHops = Config.Bind("Fear", "Band Max Links", 3,
                new ConfigDescription(
                    "How many times the chain may jump from one creature to the next.",
                    new AcceptableValueRange<int>(1, 10)));
            _bandSpread = Config.Bind("Fear", "Band Max Spread", 60f,
                new ConfigDescription(
                    "No band member may be further than this from the creature that started it, " +
                    "so an unbroken line of creatures cannot chain across the map.",
                    new AcceptableValueRange<float>(10f, 200f)));

            _phaseOn = Config.Bind("DayNight", "Enable Day/Night Profiles", true,
                "Creatures with a [day] or [night] block in the creature config swap their senses, " +
                "threat, reach, speed, damage and how easily they are noticed as the sun moves. " +
                "Off leaves every creature on its round-the-clock numbers.");
            _stalkOn = Config.Bind("DayNight", "Enable Stalking", true,
                "Creatures given stalkSeconds circle a fresh target before charging it, rather than " +
                "walking straight in. Off makes every creature close immediately, as they do now.");

            _fireAvoidOn = Config.Bind("Fire", "Enable Fire Avoidance", true,
                "Creatures given an avoidsFire tier keep their distance from flames. Instinct, not " +
                "the danger-score fear check: it needs no target and applies even to fearless " +
                "creatures. Tamed creatures always ignore fire, as they do in vanilla.");

            _fireTorch = Config.Bind("Fire", "Reach - Torch", 5f,
                new ConfigDescription(
                    "How far a torch holds off a creature that fears torches.",
                    new AcceptableValueRange<float>(1f, 40f)));
            _fireCampfire = Config.Bind("Fire", "Reach - Campfire", 9f,
                new ConfigDescription(
                    "How far a campfire, hearth or forge holds off a creature that fears them.",
                    new AcceptableValueRange<float>(1f, 60f)));
            _fireBonfire = Config.Bind("Fire", "Reach - Bonfire", 15f,
                new ConfigDescription(
                    "How far a bonfire holds off a creature. This is the one that turns a lox.",
                    new AcceptableValueRange<float>(1f, 80f)));
            _fireMaxRadius = Config.Bind("Fire", "Reach Cap", 40f,
                new ConfigDescription(
                    "Hard ceiling after a creature's own fireBuffer multiplier is applied, so a " +
                    "twitchy creature at a big bonfire cannot end up with a no-go zone the size " +
                    "of a village. 0 removes the cap.",
                    new AcceptableValueRange<float>(0f, 200f)));

            _fireInterval = Config.Bind("Fire", "Re-check Seconds", 1f,
                new ConfigDescription(
                    "How often one creature reconsiders the fire near it.",
                    new AcceptableValueRange<float>(0.1f, 5f)));
            _fireCommit = Config.Bind("Fire", "Commitment Seconds", 5f,
                new ConfigDescription(
                    "Once it has decided to keep away it holds that for this long. Stops a " +
                    "creature sitting exactly on the edge of the radius twitching in and out. " +
                    "Vanilla does the same thing with a 6 second memory.",
                    new AcceptableValueRange<float>(0f, 20f)));
            _fireScan = Config.Bind("Fire", "Scan Seconds", 0.5f,
                new ConfigDescription(
                    "How often the list of burning things in the world is refreshed. This is ONE " +
                    "scan shared by every creature, not one each; positions are read live, so a " +
                    "carried torch is never stale regardless of this value.",
                    new AcceptableValueRange<float>(0.1f, 5f)));

            _fireUnknown = Config.Bind("Fire", "Unlisted Source Tier", FireTier.Campfire,
                "What to treat a burning thing as when it is not named under [FireSources] and " +
                "its flame is too small to guess from. Turn on verbose logging to have anything " +
                "unlisted reported once, with the name to paste into the config.");
            _fireGuessTorch = Config.Bind("Fire", "Guess - Torch Below", 1.5f,
                new ConfigDescription(
                    "An unlisted flame smaller than this reads as a torch. Only ever used for " +
                    "sources missing from [FireSources]; a name there always wins.",
                    new AcceptableValueRange<float>(0.1f, 10f)));
            _fireGuessCampfire = Config.Bind("Fire", "Guess - Campfire Below", 3.5f,
                new ConfigDescription(
                    "An unlisted flame smaller than this reads as a campfire; bigger reads as a " +
                    "bonfire.",
                    new AcceptableValueRange<float>(0.1f, 20f)));

            _catalog = Config.Bind("Diagnostics", "Write Creature Catalog", true,
                "Once per world load, write every registered creature prefab and the live spawn " +
                "table to CSVs next to this file. The only complete list of what your mods " +
                "actually add - modded prefab names live in Unity asset bundles and cannot be " +
                "read from outside the running game.");
            _squelchLogs = Config.Bind("Diagnostics", "Silence Noisy Mod Logs", true,
                "Drops known log spam from other mods (currently: No Rain Damage's per-piece " +
                "'I'm wet!' Info line) before it reaches the console or disk log. Leaves this " +
                "mod's own Verbose logging and the global BepInEx log level untouched.");

            _birchFine = Config.Bind("Trees", "Birch Drops Only Fine Wood", true,
                "Strips everything except FineWood from every birch drop table. Applied to the " +
                "prefabs at load, so it costs nothing while you are chopping.");
            _birchSeeds = Config.Bind("Trees", "Birch Still Drops Seeds", false,
                "Turn on to spare BirchSeeds from the strip. Off means birch really does drop " +
                "fine wood and nothing else - which also means no seeds to replant with.");

            _tamedRegen = Config.Bind("Taming", "Tamed HP Regen Multiplier", 1f,
                new ConfigDescription(
                    "Multiplies how fast TAMED creatures heal over time. Vanilla fills the whole " +
                    "health bar over a fixed number of seconds; 2 here halves that time (twice " +
                    "the regen rate), 0.5 halves it. Wild creatures are never touched. Applied and " +
                    "removed live as a creature is tamed/untamed or the config reloads.",
                    new AcceptableValueRange<float>(0.1f, 10f)));
            _tameStructures = Config.Bind("Taming", "Tames Cannot Damage Structures", true,
                "Stops your tamed creatures damaging anything YOU built - no more stray swings " +
                "knocking holes in your walls. They can still hit trees, rocks and everything " +
                "else in the world exactly as before; this only shields player-built pieces.");
            _offlineTaming = Config.Bind("Taming", "Credit Taming Progress While Away", true,
                "Vanilla's own taming timer AND its 'find and eat nearby food' behaviour both only " +
                "run while the creature is actually loaded - leave the zone and both just stop, " +
                "food pile or not. This replays that behaviour retroactively when you come back: it " +
                "chains through however many matching food items were left nearby (actually eating " +
                "them, same as it would have live) to stay fed the whole time you were gone. A big " +
                "enough supply covers the entire trip; running out partway just leaves it hungry " +
                "exactly where it would have gone hungry live, never further ahead than a player " +
                "standing there feeding it by hand would have gotten it. MonsterAI-driven tames " +
                "only (that's what has the consume-food behaviour at all) - vanilla or modded.");

            _loggingOn = Config.Bind("Logging", "Enable Troll Logging", true,
                "Master switch for the Troll Logging Leash feature. Off leaves the hotkey and " +
                "the leash piece inert, whatever loggingMode says per creature.");
            _loggingKey = Config.Bind("Logging", "Logging Toggle Key",
                new KeyboardShortcut(KeyCode.X, KeyCode.LeftControl),
                "Look at a tame with loggingMode = true and press this to toggle its logging " +
                "work loop on or off. Separate from the stance-cycle key - the two are " +
                "independent axes on the same creature.");
            _leashRadius = Config.Bind("Logging", "Leash Radius", 20f,
                new ConfigDescription(
                    "How far a Troll Logging Leash's binding and work area reaches. A logging " +
                    "troll never paths outside this while bound. Every placed leash shows its " +
                    "radius on the ground as a ring, same as a vanilla Ward's edge marker.",
                    new AcceptableValueRange<float>(10f, 50f)));
            _chopInterval = Config.Bind("Logging", "Chop Interval", 2f,
                new ConfigDescription(
                    "Seconds between hits once a logging troll is in range of its target tree.",
                    new AcceptableValueRange<float>(0.5f, 10f)));
            _chopDamage = Config.Bind("Logging", "Chop Damage", 50f,
                new ConfigDescription(
                    "Flat chop damage per hit, independent of the troll's own attack stats.",
                    new AcceptableValueRange<float>(1f, 1000f)));
            _treeScanInterval = Config.Bind("Logging", "Tree Scan Seconds", 3f,
                new ConfigDescription(
                    "How often the world's trees are re-surveyed. One shared scan for every " +
                    "logging troll, not one each.",
                    new AcceptableValueRange<float>(0.5f, 15f)));
            _leashScanInterval = Config.Bind("Logging", "Leash Scan Seconds", 5f,
                new ConfigDescription(
                    "How often placed Troll Logging Leash pieces are re-surveyed.",
                    new AcceptableValueRange<float>(0.5f, 20f)));
            _loggingReturnTimeout = Config.Bind("Logging", "Return Timeout Seconds", 30f,
                new ConfigDescription(
                    "How long a logging troll gets to walk back inside its leash radius after " +
                    "combat clears before it is simply teleported the rest of the way there.",
                    new AcceptableValueRange<float>(5f, 180f)));

            LoadConfigs();

            _harmony = new Harmony(GUID);
            try
            {
                _harmony.PatchAll();
            }
            catch (Exception e)
            {
                Log.LogError($"Patching failed - the mod is inert this session: {e}");
                return;
            }

            // Optional, resolved by name: fixes BetterTames' pet teleport being
            // undone by physics. No-op if BetterTames isn't installed.
            TeleportFix.TryPatch(_harmony);

            // Registers the "Troll Logging Leash" piece via Jotunn. Deferred to
            // Jotunn's own OnVanillaPrefabsAvailable event internally, so this
            // is safe to call before ZNetScene/ObjectDB exist.
            TotemBind.Init();

            StartWatching();
            Log.LogInfo($"{NAME} v{VERSION} ready.");

            // Unconditional - not gated by Verbose - because this is a build-time
            // status flag, not tuning noise. Remove this call (and update
            // LOGGING-implementation-notes.md) once the feature has been run in
            // a real game and the open items in that file are resolved.
            if (LoggingEnabled)
                Log.LogWarning(
                    "[WIP] Troll Logging Leash has not been build-verified yet - see " +
                    "LOGGING-implementation-notes.md in the repo root for the open punch list " +
                    "before relying on it.");
        }

        void LoadConfigs()
        {
            try
            {
                ConfigLoader.LoadAll(_cfgDir);
            }
            catch (Exception e)
            {
                // LoadAll only publishes once both files parsed, so the rules
                // currently in force are genuinely untouched.
                Log.LogError($"Could not read configs, keeping the previous rules: {e.Message}");
            }
        }

        void StartWatching()
        {
            try
            {
                _watcher = new FileSystemWatcher(_cfgDir, "CreatureControl.*.cfg")
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName
                };
                FileSystemEventHandler bump = (s, e) => _dirty = true;
                _watcher.Changed += bump;
                _watcher.Created += bump;
                _watcher.Renamed += (s, e) => _dirty = true;
                // Subscribe first, then start raising, or an edit that lands in
                // between is dropped.
                _watcher.EnableRaisingEvents = true;
            }
            catch (Exception e)
            {
                Log.LogWarning($"Live config reload unavailable: {e.Message}");
            }
        }

        void Update()
        {
            HandleReload();
            HandleStanceKey();
            HandleLoggingKey();
            HandleForceTargetKey();
            TargetMarker.Tick();

            // One clock read for the whole mod. Only does real work on the two
            // ticks a day when it actually turns over.
            if (PhaseEnabled) Phase.Tick();

            // Delivers any queued herd panic alerts whose reaction delay has
            // elapsed. A handful of entries at most, so this costs nothing.
            FearAlert.Tick();

            // World-scan halves of the logging feature - trees and leash
            // pieces - each on their own timer, shared by every logging troll.
            if (LoggingEnabled)
            {
                TrollLogging.Tick();
                TotemBind.Tick();
            }

            // Cheap: returns immediately once written, and before that it only
            // tests whether ZNetScene has finished registering prefabs.
            Catalog.MaybeWrite(_cfgDir);
        }

        void HandleReload()
        {
            // Editors often save in several bursts; wait for it to settle.
            if (_dirty)
            {
                _dirty = false;
                _reloadAt = Time.realtimeSinceStartup + 0.75f;
                return;
            }
            if (_reloadAt < 0f || Time.realtimeSinceStartup < _reloadAt) return;
            _reloadAt = -1f;

            Log.LogInfo("Config changed on disk, reloading.");
            _fearOn = Config.Bind("Fear", "Enable Fear", true,
                "Creatures weigh their side against yours before starting a fight. Never blocks " +
                "retaliation, camp defence, joining a fight already running, or bosses.");
            _fearRadius = Config.Bind("Fear", "Awareness Radius", 20f,
                new ConfigDescription(
                    "How far a creature looks when counting who is on each side.",
                    new AcceptableValueRange<float>(5f, 60f)));
            _fearInterval = Config.Bind("Fear", "Re-check Seconds", 2f,
                new ConfigDescription(
                    "How often a creature reconsiders. Lower reacts faster and costs more.",
                    new AcceptableValueRange<float>(0.25f, 10f)));
            _starScale = Config.Bind("Fear", "Threat Per Star", 0.6f,
                new ConfigDescription(
                    "Extra threat per star, so a 2-star creature is worth (1 + 0.6*2) of its base. " +
                    "Set 0 if you are not running a level mod.",
                    new AcceptableValueRange<float>(0f, 3f)));
            _petWeight = Config.Bind("Fear", "Tamed Threat Weight", 0.5f,
                new ConfigDescription(
                    "How much of its threat a tamed creature contributes to your side. " +
                    "1 makes a single tamed Bjorn clear the area around you.",
                    new AcceptableValueRange<float>(0f, 1f)));
            _playerScale = Config.Bind("Fear", "Player Threat Scale", 1f,
                new ConfigDescription(
                    "Multiplies how dangerous you look. Turn down if the late game goes quiet.",
                    new AcceptableValueRange<float>(0f, 3f)));
            _defaultThreat = Config.Bind("Fear", "Default Threat", 1f,
                new ConfigDescription(
                    "Threat for any creature the config does not give a value - one Fuling.",
                    new AcceptableValueRange<float>(0.01f, 50f)));

            _chainAggro = Config.Bind("Fear", "Chain Aggro", true,
                "When a band commits, members that had not noticed the enemy are handed the " +
                "target, so the reinforcements it counted actually arrive. Turn off and a band " +
                "still decides together but nobody gets pulled in.");
            _rally = Config.Bind("Fear", "Default Rally Radius", 12f,
                new ConfigDescription(
                    "How far a creature looks for FRIENDS when banding up. Never affects how " +
                    "far it can see an enemy. Overridable per creature with rallyRadius.",
                    new AcceptableValueRange<float>(0f, 50f)));
            _callHelp = Config.Bind("Fear", "Call For Help", true,
                "A creature that finds itself outmatched calls its allies instead of just " +
                "running. They are pointed at the enemy and come, so next time the odds are " +
                "counted the pack may be strong enough to fight. Applies to every creature " +
                "the fear system drives.");
            _helpRadius = Config.Bind("Fear", "Help Radius", 25f,
                new ConfigDescription(
                    "How far a cry for help carries. Deliberately wider than the rally radius - " +
                    "a creature can be heard from further than its friends can be seen. " +
                    "Never affects how far anything can see an ENEMY.",
                    new AcceptableValueRange<float>(0f, 100f)));
            _rallySeconds = Config.Bind("Fear", "Rally Nerve Seconds", 5f,
                new ConfigDescription(
                    "How long a creature answering a call holds its nerve on the way over. " +
                    "Without it a responder re-decides alone halfway there and turns back, and " +
                    "the rally falls apart before it arrives.",
                    new AcceptableValueRange<float>(0f, 30f)));
            _dinnerBell = Config.Bind("Fear", "Cry For Help Draws Predators", true,
                "A cry for help carries to everything, not just friends. Predators that hunt " +
                "the caller hear it too and come for the caller instead of coming to its aid. " +
                "Solitary hunters answer this even though they never join a rally.");
            _fearAlertRadius = Config.Bind("Fear", "Fear Alert Radius", 15f,
                new ConfigDescription(
                    "How far a panicking creature's alert reaches. Only creatures given " +
                    "sharesFear = true in the config send or receive one - currently just Deer, " +
                    "as a test case.",
                    new AcceptableValueRange<float>(1f, 60f)));
            _fearAlertDelay = Config.Bind("Fear", "Fear Alert Delay", 0.5f,
                new ConfigDescription(
                    "How long a nearby creature takes to react to someone else's panic. A real " +
                    "animal notices its herd bolting and reacts a beat later, rather than " +
                    "instantly knowing there is danger.",
                    new AcceptableValueRange<float>(0f, 5f)));

            _armorDiv = Config.Bind("Danger", "Armor Divisor", 100f,
                new ConfigDescription(
                    "Armour rating divided by this becomes part of your danger, for creatures " +
                    "that can read gear. 100 means a full suit of the best armour in your game " +
                    "is worth about 2.4 - roughly two and a half fulings.",
                    new AcceptableValueRange<float>(10f, 500f)));
            _weaponDiv = Config.Bind("Danger", "Weapon Divisor", 150f,
                new ConfigDescription(
                    "Weapon damage divided by this becomes the other part of your danger. 150 " +
                    "means the strongest realistic weapon is worth about 2.2. Lower this to make " +
                    "the weapon matter more than the armour.",
                    new AcceptableValueRange<float>(10f, 1000f)));
            _weaponInputCap = Config.Bind("Danger", "Weapon Damage Ignored Above", 400f,
                new ConfigDescription(
                    "Damage above this is ignored when judging you. Guards against debug and " +
                    "admin items - there are weapons in the loaded mods listing six-figure " +
                    "damage, and without this one of them would terrify the entire map.",
                    new AcceptableValueRange<float>(50f, 100000f)));
            _dangerCap = Config.Bind("Danger", "Danger Cap", 5f,
                new ConfigDescription(
                    "The most frightening a player can ever read, to anything. Without a ceiling " +
                    "a fully geared player eventually outscales every creature and the world goes " +
                    "quiet. 5 means the scariest possible player is worth five fulings.",
                    new AcceptableValueRange<float>(1f, 20f)));

            _spiritDiv = Config.Bind("Danger", "Spirit Divisor", 30f,
                new ConfigDescription(
                    "Spirit damage divided by this is how frightening you are to the undead and " +
                    "demons. 30 is the median spirit weapon in your game, so an average silver " +
                    "weapon makes you as frightening to the dead as one fuling is to the living.",
                    new AcceptableValueRange<float>(1f, 500f)));
            _holyArmorDiv = Config.Bind("Danger", "Holy Armor Divisor", 300f,
                new ConfigDescription(
                    "Armour adds a small bonus on top of spirit damage: 1 + armour/this. 300 is " +
                    "near the maximum armour possible, so armour can never more than double your " +
                    "holy threat. With no spirit damage equipped this does nothing at all.",
                    new AcceptableValueRange<float>(50f, 2000f)));

            _instinctBase = Config.Bind("Danger", "Instinct Base", 0.2f,
                new ConfigDescription(
                    "What a naked, empty-handed person is worth to an ANIMAL. Animals cannot " +
                    "read gear; they only see whether you are dressed and whether your hands " +
                    "are full.",
                    new AcceptableValueRange<float>(0f, 5f)));
            _instinctStep = Config.Bind("Danger", "Instinct Step", 0.5f,
                new ConfigDescription(
                    "Added once for wearing anything at all, and again for holding anything in " +
                    "your hands. A sheathed weapon is ignored - a wolf sees empty hands. At the " +
                    "default a dressed and armed person reads 1.2, just above a lone wolf.",
                    new AcceptableValueRange<float>(0f, 5f)));

            _corneredRadius = Config.Bind("Danger", "Cornered Radius", 5f,
                new ConfigDescription(
                    "Inside this range a frightened creature stops running and turns to fight - " +
                    "it has run out of room. Chase something down and it rounds on you, so " +
                    "hunting for drops does not become a footrace. 0 disables.",
                    new AcceptableValueRange<float>(0f, 30f)));
            _bandSize = Config.Bind("Fear", "Band Max Size", 16,
                new ConfigDescription(
                    "Hard cap on how many creatures one band may contain.",
                    new AcceptableValueRange<int>(1, 64)));
            _bandHops = Config.Bind("Fear", "Band Max Links", 3,
                new ConfigDescription(
                    "How many times the chain may jump from one creature to the next.",
                    new AcceptableValueRange<int>(1, 10)));
            _bandSpread = Config.Bind("Fear", "Band Max Spread", 60f,
                new ConfigDescription(
                    "No band member may be further than this from the creature that started it, " +
                    "so an unbroken line of creatures cannot chain across the map.",
                    new AcceptableValueRange<float>(10f, 200f)));


            LoadConfigs();
            ReapplyToLoadedCreatures();
        }

        static bool IsTyping()
        {
            // Valheim's Console type sits in the global namespace and would
            // otherwise collide with System.Console.
            try { return global::Console.IsVisible() || TextInput.IsVisible(); }
            catch { return false; }
        }

        /// <summary>
        /// Stance cycling is a hotkey rather than an interaction because vanilla
        /// already owns both interaction slots on a tame: alt-interact renames
        /// it, and hold-interact repeats every frame.
        /// </summary>
        void HandleStanceKey()
        {
            if (!AllowStanceCycling || _cycleKey == null) return;
            if (!_cycleKey.Value.IsDown()) return;
            if (IsTyping()) return;

            var player = Player.m_localPlayer;
            if (player == null) return;

            var chr = player.GetHoverCreature();
            if (chr == null || !chr.IsTamed()) return;

            var st = CreatureState.For(chr);
            if (st == null) return;
            if (st.Rule != null && st.Rule.StanceCycling == false) return;

            var next = st.Mode.Next();
            st.SetMode(next, persist: true);
            st.Apply();

            // Calming down should also drop whatever it was angry at.
            if (next != BehaviorMode.Aggressive)
            {
                st.ForgetAll();
                var mai = chr.gameObject.GetComponent<MonsterAI>();
                if (mai != null) mai.MakeTame();   // public; clears target and alert
            }

            var name = chr.m_name;
            var tameable = chr.gameObject.GetComponent<Tameable>();
            if (tameable != null) name = tameable.GetHoverName();

            player.Message(MessageHud.MessageType.Center,
                           $"{name}: {next.Pretty()}", 0, null, false);
        }

        /// <summary>
        /// Logging is a second, independent hotkey rather than folded into the
        /// stance cycle - a troll can be Neutral-and-logging or
        /// Aggressive-and-logging, so the two have to be toggled separately.
        /// Same interaction-slot reasoning as HandleStanceKey: both of a tame's
        /// interact slots are already spoken for by vanilla.
        /// </summary>
        void HandleLoggingKey()
        {
            if (!LoggingEnabled || _loggingKey == null) return;
            if (!_loggingKey.Value.IsDown()) return;
            if (IsTyping()) return;

            var player = Player.m_localPlayer;
            if (player == null) return;

            var chr = player.GetHoverCreature();
            if (chr == null || !chr.IsTamed()) return;

            var st = CreatureState.For(chr);
            if (st == null || !st.CanLog) return;

            bool next = !st.IsLogging;
            st.SetLogging(next, persist: true);

            var name = chr.m_name;
            var tameable = chr.gameObject.GetComponent<Tameable>();
            if (tameable != null) name = tameable.GetHoverName();

            player.Message(MessageHud.MessageType.Center,
                           $"{name}: logging {(next ? "ON" : "OFF")}", 0, null, false);
        }

        /// <summary>
        /// "Attack this" - look at anything, wild or hostile, and press the
        /// hotkey to send every tame you have after it. Deliberately not
        /// restricted to a hover-interact range like the stance/logging keys:
        /// those are commands given TO a tame, this one is aimed AT a target,
        /// which is routinely further away than interact distance.
        /// </summary>
        void HandleForceTargetKey()
        {
            if (_forceTargetKey == null || !_forceTargetKey.Value.IsDown()) return;
            if (IsTyping())
            {
                Log.LogInfo("[CC target-cmd] key seen but IsTyping() blocked it.");
                return;
            }

            var player = Player.m_localPlayer;
            if (player == null) return;

            Log.LogInfo("[CC target-cmd] key pressed, raycasting...");

            var target = RaycastTarget(player);
            if (target == null || target.IsDead())
            {
                player.Message(MessageHud.MessageType.Center, "No target.", 0, null, false);
                return;
            }
            if (target.IsTamed() || target is Player)
            {
                Log.LogInfo($"[CC target-cmd] raycast hit {target.name}, refused (tamed or player).");
                player.Message(MessageHud.MessageType.Center, "Can't target a tame or a player.", 0, null, false);
                return;
            }

            int n = ForcedTarget.Command(target);
            Log.LogInfo($"[CC target-cmd] {target.name} marked - {n} tame(s) commanded.");

            var name = target.m_name;
            var tameable = target.gameObject.GetComponent<Tameable>();
            if (tameable != null) name = tameable.GetHoverName();

            player.Message(MessageHud.MessageType.Center,
                n > 0 ? $"Attack {name}!" : "No tames to command.", 0, null, false);
        }

        // Same layer set vanilla's own melee attack raycast uses (Player.s_attackMask) -
        // every character-related layer plus solid terrain/pieces, so a wall or a
        // mountain still blocks the shot, but decorative clutter with no gameplay
        // collider never can. A bare, unmasked Physics.Raycast (the first cut of
        // this feature) had no such guarantee and could plausibly stop on
        // anything in the world with ANY collider, character or not - the leading
        // suspect for "the hotkey did nothing" reports with no error in the log.
        static int _targetRayMask = -1;
        static int TargetRayMask
        {
            get
            {
                if (_targetRayMask < 0)
                    _targetRayMask = LayerMask.GetMask(
                        "character", "character_net", "character_ghost", "character_noenv", "hitbox",
                        "Default", "static_solid", "Default_small", "terrain", "piece", "piece_nonsolid", "vehicle");
                return _targetRayMask;
            }
        }

        static Character RaycastTarget(Player player)
        {
            if (GameCamera.instance == null) return null;
            var cam = GameCamera.instance.transform;
            if (!Physics.Raycast(cam.position, cam.forward, out var hit, ForceTargetRange, TargetRayMask))
            {
                Log.LogInfo("[CC target-cmd] raycast hit nothing at all.");
                return null;
            }

            var chr = hit.collider.GetComponentInParent<Character>();
            if (chr == null)
                Log.LogInfo($"[CC target-cmd] raycast hit '{hit.collider.name}' " +
                            $"(layer {LayerMask.LayerToName(hit.collider.gameObject.layer)}), no Character there.");
            return chr;
        }

        /// <summary>Push freshly-loaded rules onto creatures already in the world,
        /// so tuning a number doesn't mean reloading the save.</summary>
        void ReapplyToLoadedCreatures()
        {
            int n = 0;
            foreach (var ai in UnityEngine.Object.FindObjectsByType<BaseAI>(FindObjectsSortMode.None))
            {
                // Attach covers creatures that had no state before (the config
                // may have been empty); ResolveAndApply re-runs the merge for
                // everything, since Start() only ever fires once.
                var st = Setup.Attach(ai);
                if (st == null) continue;

                if (!CreatureRules.AnyRules && !FactionRegistry.HasAnyRelations)
                {
                    // The config was emptied. Hand every creature we touched
                    // back to the game rather than leaving our edits baked in.
                    st.RestoreOriginals();
                    UnityEngine.Object.Destroy(st);
                }
                else st.ResolveAndApply();
                n++;
            }
            Log.LogInfo($"Reapplied to {n} creature(s) in the loaded world.");
        }

        void OnDestroy()
        {
            try { if (_watcher != null) _watcher.Dispose(); } catch { }
            try { if (_harmony != null) _harmony.UnpatchSelf(); } catch { }
            CreatureState.ClearRegistry();
            FireAversion.Reset();
            FearAlert.Reset();
            Phase.Forget();
            TrollLogging.Reset();
            TotemBind.Reset();
            TargetMarker.Reset();
        }
    }
}
