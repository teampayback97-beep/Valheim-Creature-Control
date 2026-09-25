using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace CreatureControl
{
    /// <summary>
    /// Tiny INI-style reader. Deliberately dependency-free: no YamlDotNet, no
    /// Newtonsoft, nothing that could clash with another mod's copy.
    ///
    /// Both files are parsed into fresh objects and only swapped in once BOTH
    /// succeeded, so a typo - or an editor still holding the file open - can
    /// never leave the game running on a half-empty rule set.
    /// </summary>
    public static class ConfigLoader
    {
        public const string CreaturesFile = "CreatureControl.Creatures.cfg";
        public const string FactionsFile = "CreatureControl.Factions.cfg";

        struct Line { public string Section, Key, Value; public int No; }

        static List<Line> Parse(string path)
        {
            var result = new List<Line>();
            string section = null;
            int no = 0;
            foreach (var raw in File.ReadAllLines(path))
            {
                no++;
                var s = raw.Trim();
                if (s.Length == 0 || s[0] == '#' || s[0] == ';') continue;

                // Strip trailing inline comments. Without this, a perfectly
                // reasonable line like
                //     Orca : Players = Neutral   # ignores you
                // parses its value as "Neutral   # ignores you", fails to
                // match, and the relation is silently dropped.
                int hash = s.IndexOfAny(new[] { '#', ';' });
                if (hash > 0)
                {
                    s = s.Substring(0, hash).TrimEnd();
                    if (s.Length == 0) continue;
                }

                if (s[0] == '[' && s[s.Length - 1] == ']')
                { section = s.Substring(1, s.Length - 2).Trim(); continue; }

                int eq = s.IndexOf('=');
                if (eq < 0)
                {
                    Plugin.Log.LogWarning($"[{Path.GetFileName(path)}:{no}] ignored (no '='): {s}");
                    continue;
                }
                result.Add(new Line
                {
                    Section = section,
                    Key = s.Substring(0, eq).Trim(),
                    Value = s.Substring(eq + 1).Trim(),
                    No = no
                });
            }
            return result;
        }

        static bool F(string v, out float f) =>
            float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out f);

        public static void LoadAll(string dir)
        {
            var factions = BuildFactions(dir);
            var creatures = BuildCreatures(dir, factions);

            FactionRegistry.Install(factions);
            CreatureRules.Install(creatures);

            Plugin.Log.LogInfo(
                $"Loaded {creatures.ByPrefab.Count} creature rule(s), " +
                $"{creatures.ByFaction.Count} faction rule(s)" +
                (creatures.Fallback != null ? ", + a [*] fallback" : "") +
                (factions.HasCustom ? $", {factions.Untargetable.Count} untargetable faction(s)" : ""));
        }

        // ---------------------------------------------------------------- factions

        static FactionRegistry.Store BuildFactions(string dir)
        {
            var store = new FactionRegistry.Store();
            var path = Path.Combine(dir, FactionsFile);
            if (!File.Exists(path)) WriteDefaultFactions(path);

            var lines = Parse(path);

            // Names first: later sections reference factions by name.
            foreach (var l in lines)
            {
                if (!string.Equals(l.Section, "CustomFactions", StringComparison.OrdinalIgnoreCase)) continue;
                if (!int.TryParse(l.Value, out int id))
                { Plugin.Log.LogWarning($"[{FactionsFile}:{l.No}] '{l.Value}' is not a number"); continue; }
                if (!store.Register(l.Key, id, out var err))
                    Plugin.Log.LogWarning($"[{FactionsFile}:{l.No}] {err}");
                else
                    Plugin.Log.LogInfo($"Registered faction '{l.Key}' = {id}");
            }

            foreach (var l in lines)
            {
                if (string.Equals(l.Section, "Defaults", StringComparison.OrdinalIgnoreCase))
                {
                    if (!store.ByName.TryGetValue(l.Key, out int id))
                    { Plugin.Log.LogWarning($"[{FactionsFile}:{l.No}] unknown faction '{l.Key}'"); continue; }
                    if (!TryRelation(l.Value, out var rel))
                    { Plugin.Log.LogWarning($"[{FactionsFile}:{l.No}] expected Enemy/Friendly/Neutral, got '{l.Value}'"); continue; }
                    store.Defaults[id] = rel;
                }
                else if (string.Equals(l.Section, "Untargetable", StringComparison.OrdinalIgnoreCase))
                {
                    if (!store.ByName.TryGetValue(l.Key, out int uid))
                    { Plugin.Log.LogWarning($"[{FactionsFile}:{l.No}] unknown faction '{l.Key}'"); continue; }
                    if (!bool.TryParse(l.Value, out var on))
                    { Plugin.Log.LogWarning($"[{FactionsFile}:{l.No}] expected true/false, got '{l.Value}'"); continue; }
                    if (on) store.Untargetable.Add(uid); else store.Untargetable.Remove(uid);
                }
                else if (string.Equals(l.Section, "Relations", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = l.Key.Split(':');
                    if (parts.Length != 2)
                    { Plugin.Log.LogWarning($"[{FactionsFile}:{l.No}] expected 'FactionA : FactionB = Enemy'"); continue; }

                    string an = parts[0].Trim(), bn = parts[1].Trim();
                    if (!store.ByName.TryGetValue(an, out int a))
                    { Plugin.Log.LogWarning($"[{FactionsFile}:{l.No}] unknown faction '{an}'"); continue; }
                    if (!store.ByName.TryGetValue(bn, out int b))
                    { Plugin.Log.LogWarning($"[{FactionsFile}:{l.No}] unknown faction '{bn}'"); continue; }
                    if (!TryRelation(l.Value, out var rel))
                    { Plugin.Log.LogWarning($"[{FactionsFile}:{l.No}] expected Enemy/Friendly/Neutral, got '{l.Value}'"); continue; }

                    store.Relations[FactionRegistry.Key(a, b)] = rel;
                }
            }
            return store;
        }

        static bool TryRelation(string s, out Relation r)
        {
            switch (s.Trim().ToLowerInvariant())
            {
                case "enemy": case "hostile": r = Relation.Enemy; return true;
                case "friendly": case "ally": r = Relation.Friendly; return true;
                case "neutral": r = Relation.Neutral; return true;
            }
            r = Relation.Friendly; return false;
        }

        // --------------------------------------------------------------- creatures

        static CreatureRules.Store BuildCreatures(string dir, FactionRegistry.Store factions)
        {
            var store = new CreatureRules.Store();
            var path = Path.Combine(dir, CreaturesFile);
            if (!File.Exists(path)) WriteDefaultCreatures(path);

            foreach (var l in Parse(path))
            {
                if (l.Section == null) continue;

                CreatureRule rule;
                if (l.Section == "*")
                    rule = store.Fallback ?? (store.Fallback = new CreatureRule { Source = "*" });
                else if (l.Section.Length > 1 && l.Section[0] == '@')
                {
                    var fname = l.Section.Substring(1).Trim();
                    if (!factions.ByName.ContainsKey(fname))
                    {
                        Plugin.Log.LogWarning($"[{CreaturesFile}:{l.No}] unknown faction '{fname}' in [@{fname}]");
                        continue;
                    }
                    rule = store.GetOrCreateFaction(fname);
                }
                else rule = store.GetOrCreatePrefab(l.Section);

                switch (l.Key.ToLowerInvariant())
                {
                    case "behavior":
                    case "behaviour":
                        if (BehaviorModeExt.TryParse(l.Value, out var wb)) rule.WildBehavior = wb;
                        else Warn(l, $"bad behavior '{l.Value}'");
                        break;

                    case "tamedbehavior":
                    case "tamedbehaviour":
                        if (BehaviorModeExt.TryParse(l.Value, out var tb)) rule.TamedBehavior = tb;
                        else Warn(l, $"bad tamedBehavior '{l.Value}'");
                        break;

                    case "viewrange":
                        if (F(l.Value, out var vr)) rule.WildView = vr; else WarnNum(l); break;
                    case "hearrange":
                        if (F(l.Value, out var hr)) rule.WildHear = hr; else WarnNum(l); break;
                    case "tamedviewrange":
                        if (F(l.Value, out var tvr)) rule.TamedView = tvr; else WarnNum(l); break;
                    case "tamedhearrange":
                        if (F(l.Value, out var thr)) rule.TamedHear = thr; else WarnNum(l); break;

                    case "alertrange":
                        if (F(l.Value, out var ar)) rule.AlertRange = ar; else WarnNum(l); break;
                    case "maxchasedistance":
                        if (F(l.Value, out var mc)) rule.MaxChaseDistance = mc; else WarnNum(l); break;

                    case "fleeiflowhealth":
                        if (F(l.Value, out var fl))
                        {
                            if (fl < 0f || fl > 1f)
                                Warn(l, $"fleeIfLowHealth is a fraction of max health (0-1); got {fl}");
                            else rule.FleeIfLowHealth = fl;
                        }
                        else WarnNum(l);
                        break;

                    case "fleeifnotalerted":
                        if (bool.TryParse(l.Value, out var fna)) rule.FleeIfNotAlerted = fna;
                        else Warn(l, $"expected true/false, got '{l.Value}'");
                        break;

                    case "enablehuntplayer":
                        if (bool.TryParse(l.Value, out var hp)) rule.EnableHuntPlayer = hp;
                        else Warn(l, $"expected true/false, got '{l.Value}'");
                        break;

                    case "faction":
                        if (l.Section.Length > 0 && l.Section[0] == '@')
                        { Warn(l, "a [@Faction] section cannot reassign factions; use the creature's own section"); break; }
                        if (l.Section == "*")
                        { Warn(l, "faction cannot be set on the [*] fallback; name the creature explicitly"); break; }
                        if (factions.ByName.TryGetValue(l.Value, out int fid))
                        { rule.FactionId = fid; rule.FactionName = l.Value; }
                        else Warn(l, $"unknown faction '{l.Value}' - define it in {FactionsFile} first");
                        break;

                    case "threat":
                        if (F(l.Value, out var th))
                        {
                            if (th < 0f) Warn(l, $"threat cannot be negative; got {th}");
                            else rule.Threat = th;
                        }
                        else WarnNum(l);
                        break;

                    case "rallyradius":
                        if (F(l.Value, out var rr))
                        {
                            if (rr < 0f) Warn(l, $"rallyRadius cannot be negative; got {rr}");
                            else rule.RallyRadius = rr;
                        }
                        else WarnNum(l);
                        break;

                    case "fearless":
                        if (bool.TryParse(l.Value, out var fless)) rule.Fearless = fless;
                        else Warn(l, $"expected true/false, got '{l.Value}'");
                        break;

                    // Same switch as solitary, worded for creatures that opt out
                    // for a different reason - prey that will never fight at all.
                    case "alwaysflee":
                        if (bool.TryParse(l.Value, out var af)) rule.AlwaysFlee = af;
                        else Warn(l, $"expected true/false, got '{l.Value}'");
                        break;

                    case "perception":
                    case "sight":
                    {
                        var pv = PerceptionEx.Parse(l.Value);
                        if (pv.HasValue) rule.Sight = pv.Value;
                        else Warn(l, $"expected gear/instinct/holy/none, got '{l.Value}'");
                        break;
                    }

                    case "rallies":
                        if (bool.TryParse(l.Value, out var rall)) rule.Solitary = !rall;
                        else Warn(l, $"expected true/false, got '{l.Value}'");
                        break;

                    case "solitary":
                        if (bool.TryParse(l.Value, out var solo)) rule.Solitary = solo;
                        else Warn(l, $"expected true/false, got '{l.Value}'");
                        break;

                    case "baby":
                        if (bool.TryParse(l.Value, out var bby)) rule.Baby = bby;
                        else Warn(l, $"expected true/false, got '{l.Value}'");
                        break;

                    case "avoidsfire":
                        if (bool.TryParse(l.Value, out var avf)) rule.AvoidsFire = avf;
                        else Warn(l, $"expected true/false, got '{l.Value}'");
                        break;

                    case "stancecycling":
                        if (bool.TryParse(l.Value, out var sc)) rule.StanceCycling = sc;
                        else Warn(l, $"expected true/false, got '{l.Value}'");
                        break;

                    default:
                        Warn(l, $"unknown key '{l.Key}'");
                        break;
                }
            }
            return store;
        }

        static void Warn(Line l, string msg) =>
            Plugin.Log.LogWarning($"[{CreaturesFile}:{l.No}] {msg}");
        static void WarnNum(Line l) =>
            Plugin.Log.LogWarning($"[{CreaturesFile}:{l.No}] '{l.Value}' is not a number");

        // ------------------------------------------------------------- templates

        static void WriteDefaultFactions(string path)
        {
            File.WriteAllText(path, @"# ===========================================================================
#  CreatureControl - custom factions
# ===========================================================================
#  Vanilla factions you can reference anywhere below:
#
#    Players  AnimalsVeg  ForestMonsters  Undead  Demon  MountainMonsters
#    SeaMonsters  PlainsMonsters  Boss  MistlandsMonsters  Dverger
#    PlayerSpawned  TrainingDummy  DeepNorth
#
#  Custom ids must be 100-65535. This file reloads LIVE.
# ===========================================================================

[CustomFactions]
# name = id
# Wildlife = 100

[Untargetable]
# Faction = true   ->  nothing may ever target these, not even a faction
#                      whose default stance is Enemy. For ambient insects.
# Ignored = true

[Defaults]
# Faction = Enemy | Friendly | Neutral
#   stance toward anything not named under [Relations]

[Relations]
# FactionA : FactionB = Enemy | Friendly | Neutral
#   Enemy    - hostile on sight
#   Friendly - ignores, and will NOT retaliate even if attacked
#   Neutral  - ignores, but fights back once that creature hurts it
#
#  A faction is friendly to itself unless it names itself, so
#      Feral : Feral = Enemy     -> turns on its own kind
#      (no such line)            -> pack behaviour
");
            Plugin.Log.LogInfo($"Wrote starter config: {Path.GetFileName(path)}");
        }

        static void WriteDefaultCreatures(string path)
        {
            File.WriteAllText(path, @"# ===========================================================================
#  CreatureControl - per-creature behaviour and senses
# ===========================================================================
#  Reloads LIVE. Save and the running game picks it up, including creatures
#  already spawned. No restart.
#
#  SECTIONS
#    [Wolf]        one creature, by prefab name
#    [@Grazers]    every creature in that faction
#    [*]           everything else
#  Precedence: creature -> faction -> [*]
#
#  KEYS - omit any and the game's own value is kept. Delete a line later and
#  the original is restored; you don't have to remember what it was.
#
#    behavior          Aggressive | Neutral | Passive     while wild
#    tamedBehavior     same three values, once tamed
#    viewRange         sight radius in metres, while wild
#    hearRange         hearing radius in metres, while wild
#    tamedViewRange    sight radius once tamed
#    tamedHearRange    hearing radius once tamed
#    alertRange        past this the creature drops its target outright
#    maxChaseDistance  gives up the chase past this. 0 = never gives up
#    enableHuntPlayer  false stops it actively seeking you out
#    fleeIfNotAlerted  true makes it RUN from a target instead of engaging -
#                      this is how prey works. Needed because most creatures
#                      here use MonsterAI, which attacks by default
#    fleeIfLowHealth   fraction of max health, 0-1. 0.25 = runs under 25%.
#                      0 disables fleeing entirely
#    faction           see CreatureControl.Factions.cfg (creature sections only)
#    stanceCycling     true | false - may you cycle this pet's stance in-game
#
#  A TERRITORIAL CREATURE is just a small radius plus Aggressive:
#    detection range = how far it cares, stance = what it does when it cares.
#
#  Anything NOT named here is left entirely to FactionAssigner.
# ===========================================================================

[Bjorn]
tamedBehavior  = Neutral
tamedViewRange = 15
tamedHearRange = 15
");
            Plugin.Log.LogInfo($"Wrote starter config: {Path.GetFileName(path)}");
        }
    }
}
