using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace CreatureControl
{
    /// <summary>
    /// Writes two CSVs next to the configs, once per world load.
    ///
    /// The point is to stop discovering creatures by being bitten by them.
    /// Modded prefab names live inside Unity asset bundles, not in the mod DLLs,
    /// so they cannot be found by reading files - an RtD dolphin was classed as
    /// a serpent for days because nothing outside the running game knew it
    /// existed. ZNetScene.instance.m_prefabs holds every creature the whole
    /// 85-mod stack registered, which is the only complete list there is.
    /// </summary>
    public static class Catalog
    {
        public const string CreatureFile = "CreatureControl.Catalog.csv";
        public const string SpawnFile = "CreatureControl.SpawnTable.csv";
        public const string SpiritFile = "CreatureControl.SpiritItems.csv";
        public const string ArmorFile = "CreatureControl.Armor.csv";
        public const string WeaponFile = "CreatureControl.Weapons.csv";

        static bool _done;

        public static void Reset() => _done = false;

        /// <summary>Safe to call every frame; does the work once.</summary>
        public static void MaybeWrite(string dir)
        {
            if (!Plugin.WriteCatalog) return;

            // Back at the main menu: arm again so loading another world writes a
            // fresh pair of files rather than keeping the first world's.
            if (ZNetScene.instance == null) { _done = false; return; }
            if (_done) return;
            var prefabs = ZNetScene.instance.m_prefabs;
            if (prefabs == null || prefabs.Count == 0) return;

            _done = true;
            try
            {
                WriteCreatures(dir, prefabs);
                WriteSpawns(dir);
                WriteSpiritItems(dir, prefabs);
                WriteArmorItems(dir, prefabs);
                WriteWeaponItems(dir, prefabs);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Could not write the catalog: {e.Message}");
            }
        }

        // ------------------------------------------------------------ creatures

        static void WriteCreatures(string dir, List<GameObject> prefabs)
        {
            var sb = new StringBuilder();
            sb.AppendLine(
                "prefab,ai,shippedFaction,ourFaction,group,boss,tameable,health," +
                "threat,fearless,baby,rallyRadius,ruleSource," +
                "viewRange,hearRange,canBeAlerted,alertRange,maxChase," +
                "fleeIfNotAlerted,fleeIfLowHealth,fleeRange,fleeInterval,huntPlayer");

            int n = 0, unruled = 0;
            var unnamed = new List<string>();

            foreach (var go in prefabs)
            {
                if (go == null) continue;
                var chr = go.GetComponent<Character>();
                if (chr == null) continue;          // not a creature

                var ai = go.GetComponent<BaseAI>();
                var mai = ai as MonsterAI;
                var tame = go.GetComponent<Tameable>();
                string prefab = go.name;

                // NB: these are the SHIPPED values. Prefabs are never Awake'd, so
                // FactionAssigner - which works from a Character.Awake postfix -
                // has not touched them. The "ourFaction" column is what this mod
                // would move the creature to; anything blank there is left to
                // FactionAssigner and the shipped value.
                var own = CreatureRules.FindByPrefab(prefab);
                int factionId = own != null && own.FactionId.HasValue
                    ? own.FactionId.Value
                    : (int)chr.m_faction;
                var rule = CreatureRules.Resolve(prefab, factionId);

                if (own == null)
                {
                    unruled++;
                    unnamed.Add($"{prefab} <{FactionRegistry.NameOf((int)chr.m_faction)}>");
                }

                sb.Append(Q(prefab)).Append(',')
                  .Append(mai != null ? "MonsterAI" : ai != null ? "AnimalAI" : "none").Append(',')
                  .Append(Q(FactionRegistry.NameOf((int)chr.m_faction))).Append(',')
                  .Append(Q(own?.FactionName)).Append(',')
                  .Append(Q(chr.m_group)).Append(',')
                  .Append(chr.m_boss).Append(',')
                  .Append(tame != null).Append(',')
                  .Append(F(chr.m_health)).Append(',')
                  .Append(F(rule?.Threat)).Append(',')
                  .Append(rule?.Fearless == true).Append(',')
                  .Append(rule?.Baby == true).Append(',')
                  .Append(F(rule?.RallyRadius)).Append(',')
                  .Append(Q(rule?.Source)).Append(',');

                if (ai != null)
                    sb.Append(F(ai.m_viewRange)).Append(',')
                      .Append(F(ai.m_hearRange)).Append(',')
                      .Append(ai.m_canBeAlerted).Append(',');
                else
                    sb.Append(",,,");

                if (mai != null)
                    sb.Append(F(mai.m_alertRange)).Append(',')
                      .Append(F(mai.m_maxChaseDistance)).Append(',')
                      .Append(mai.m_fleeIfNotAlerted).Append(',')
                      .Append(F(mai.m_fleeIfLowHealth)).Append(',')
                      .Append(F(mai.m_fleeRange)).Append(',')
                      .Append(F(mai.m_fleeInterval)).Append(',')
                      .Append(mai.m_enableHuntPlayer);
                else
                    sb.Append(",,,,,,");

                sb.AppendLine();
                n++;
            }

            File.WriteAllText(Path.Combine(dir, CreatureFile), sb.ToString());
            Plugin.Log.LogInfo(
                $"Catalog: {n} creature prefab(s) written to {CreatureFile}; " +
                $"{unruled} have no entry of their own in the Creatures config.");

            // The ones worth looking at: no rule AND parked in a faction that
            // fights the player. That combination is how a dolphin ends up with
            // a serpent's stat line.
            if (unnamed.Count > 0 && Plugin.Verbose)
            {
                unnamed.Sort(StringComparer.OrdinalIgnoreCase);
                Plugin.Log.LogInfo("Catalog: unnamed creatures -> " + string.Join(", ", unnamed.ToArray()));
            }
        }

        // --------------------------------------------------------- spirit items

        /// <summary>
        /// Everything in the game that carries spirit damage - the only thing
        /// Valheim has that reads as holy. Weapons AND ammo, because a silver
        /// arrow carries its spirit on the arrow, never on the bow.
        ///
        /// Like the creature catalogue, this can only be read from inside the
        /// running game: item damage lives in the asset bundles, not in any
        /// file that can be read from outside.
        /// </summary>
        static void WriteSpiritItems(string dir, List<GameObject> prefabs)
        {
            var sb = new StringBuilder();
            sb.AppendLine("prefab,nameToken,itemType,ammoType,maxQuality," +
                          "spiritBase,spiritPerLevel,spiritAtMax,otherDamageAtBase");

            int n = 0;
            foreach (var go in prefabs)
            {
                if (go == null) continue;
                var drop = go.GetComponent<ItemDrop>();
                if (drop == null || drop.m_itemData == null) continue;
                var sh = drop.m_itemData.m_shared;
                if (sh == null) continue;

                float baseSpirit = sh.m_damages.m_spirit;
                float perLevel = sh.m_damagesPerLevel.m_spirit;
                if (baseSpirit <= 0f && perLevel <= 0f) continue;   // not holy

                int maxQ = sh.m_maxQuality < 1 ? 1 : sh.m_maxQuality;
                float atMax = baseSpirit + perLevel * (maxQ - 1);

                // Everything that is NOT spirit, so a weapon that is mostly
                // spirit can be told apart from one that merely has a little.
                var d = sh.m_damages;
                float other = d.m_damage + d.m_blunt + d.m_slash + d.m_pierce +
                              d.m_fire + d.m_frost + d.m_lightning + d.m_poison;

                sb.Append(Q(go.name)).Append(',')
                  .Append(Q(sh.m_name)).Append(',')
                  .Append(Q(sh.m_itemType.ToString())).Append(',')
                  .Append(Q(sh.m_ammoType)).Append(',')
                  .Append(maxQ).Append(',')
                  .Append(F(baseSpirit)).Append(',')
                  .Append(F(perLevel)).Append(',')
                  .Append(F(atMax)).Append(',')
                  .Append(F(other))
                  .AppendLine();
                n++;
            }

            File.WriteAllText(Path.Combine(dir, SpiritFile), sb.ToString());
            Plugin.Log.LogInfo(
                $"Catalog: {n} item(s) carrying spirit damage written to {SpiritFile}.");
        }

        // ---------------------------------------------------------------- armor

        /// <summary>
        /// Every wearable that carries an armor rating, so the real ceiling can
        /// be worked out by slot rather than guessed at. Armour lives in the
        /// asset bundles like everything else, so this only exists in-game.
        /// </summary>
        static void WriteArmorItems(string dir, List<GameObject> prefabs)
        {
            var sb = new StringBuilder();
            sb.AppendLine("prefab,nameToken,slot,armorMaterial,maxQuality," +
                          "armorBase,armorPerLevel,armorAtMax");

            int n = 0;
            foreach (var go in prefabs)
            {
                if (go == null) continue;
                var drop = go.GetComponent<ItemDrop>();
                if (drop == null || drop.m_itemData == null) continue;
                var sh = drop.m_itemData.m_shared;
                if (sh == null) continue;
                if (sh.m_armor <= 0f && sh.m_armorPerLevel <= 0f) continue;

                int maxQ = sh.m_maxQuality < 1 ? 1 : sh.m_maxQuality;

                sb.Append(Q(go.name)).Append(',')
                  .Append(Q(sh.m_name)).Append(',')
                  .Append(Q(sh.m_itemType.ToString())).Append(',')
                  .Append(Q(sh.m_armorMaterial != null ? sh.m_armorMaterial.name : "")).Append(',')
                  .Append(maxQ).Append(',')
                  .Append(F(sh.m_armor)).Append(',')
                  .Append(F(sh.m_armorPerLevel)).Append(',')
                  .Append(F(sh.m_armor + sh.m_armorPerLevel * (maxQ - 1)))
                  .AppendLine();
                n++;
            }

            File.WriteAllText(Path.Combine(dir, ArmorFile), sb.ToString());
            Plugin.Log.LogInfo($"Catalog: {n} armor piece(s) written to {ArmorFile}.");
        }

        // -------------------------------------------------------------- weapons

        /// <summary>
        /// Everything that deals damage, with the full breakdown. Tools come
        /// along too (they have chop and pickaxe values) - the slot column tells
        /// them apart, rather than this guessing what counts as a weapon.
        /// </summary>
        static void WriteWeaponItems(string dir, List<GameObject> prefabs)
        {
            var sb = new StringBuilder();
            sb.AppendLine("prefab,nameToken,slot,ammoType,maxQuality," +
                          "totalBase,totalAtMax,blunt,slash,pierce,fire,frost," +
                          "lightning,poison,spirit,chop,pickaxe");

            int n = 0;
            foreach (var go in prefabs)
            {
                if (go == null) continue;
                var drop = go.GetComponent<ItemDrop>();
                if (drop == null || drop.m_itemData == null) continue;
                var sh = drop.m_itemData.m_shared;
                if (sh == null) continue;

                var d = sh.m_damages;
                var per = sh.m_damagesPerLevel;

                // Combat damage only: chop and pickaxe are for wood and rock.
                float combat = d.m_damage + d.m_blunt + d.m_slash + d.m_pierce +
                               d.m_fire + d.m_frost + d.m_lightning + d.m_poison + d.m_spirit;
                if (combat <= 0f) continue;

                int maxQ = sh.m_maxQuality < 1 ? 1 : sh.m_maxQuality;
                float perCombat = per.m_damage + per.m_blunt + per.m_slash + per.m_pierce +
                                  per.m_fire + per.m_frost + per.m_lightning + per.m_poison +
                                  per.m_spirit;

                sb.Append(Q(go.name)).Append(',')
                  .Append(Q(sh.m_name)).Append(',')
                  .Append(Q(sh.m_itemType.ToString())).Append(',')
                  .Append(Q(sh.m_ammoType)).Append(',')
                  .Append(maxQ).Append(',')
                  .Append(F(combat)).Append(',')
                  .Append(F(combat + perCombat * (maxQ - 1))).Append(',')
                  .Append(F(d.m_blunt)).Append(',').Append(F(d.m_slash)).Append(',')
                  .Append(F(d.m_pierce)).Append(',').Append(F(d.m_fire)).Append(',')
                  .Append(F(d.m_frost)).Append(',').Append(F(d.m_lightning)).Append(',')
                  .Append(F(d.m_poison)).Append(',').Append(F(d.m_spirit)).Append(',')
                  .Append(F(d.m_chop)).Append(',').Append(F(d.m_pickaxe))
                  .AppendLine();
                n++;
            }

            File.WriteAllText(Path.Combine(dir, WeaponFile), sb.ToString());
            Plugin.Log.LogInfo($"Catalog: {n} damaging item(s) written to {WeaponFile}.");
        }

        // --------------------------------------------------------------- spawns

        static void WriteSpawns(string dir)
        {
            var systems = UnityEngine.Object.FindObjectsByType<SpawnSystem>(FindObjectsSortMode.None);
            if (systems == null || systems.Length == 0) return;

            var sb = new StringBuilder();
            sb.AppendLine(
                "list,name,prefab,enabled,biome,biomeArea,maxSpawned,spawnInterval," +
                "spawnChance,spawnDistance,groupMin,groupMax,groupRadius," +
                "night,day,minAlt,maxAlt,inForest,outsideForest," +
                "minOceanDepth,maxOceanDepth,requiredKey");

            int rows = 0;
            var seen = new HashSet<string>();

            foreach (var sys in systems)
            {
                if (sys == null || sys.m_spawnLists == null) continue;
                foreach (var list in sys.m_spawnLists)
                {
                    if (list == null || list.m_spawners == null) continue;
                    foreach (var s in list.m_spawners)
                    {
                        if (s == null) continue;

                        // The same spawn lists are attached to every SpawnSystem
                        // in the scene, so dedupe or the file triples.
                        string key = list.name + "|" + s.m_name + "|" +
                                     (s.m_prefab != null ? s.m_prefab.name : "?");
                        if (!seen.Add(key)) continue;

                        sb.Append(Q(list.name)).Append(',')
                          .Append(Q(s.m_name)).Append(',')
                          .Append(Q(s.m_prefab != null ? s.m_prefab.name : "")).Append(',')
                          .Append(s.m_enabled).Append(',')
                          .Append(Q(s.m_biome.ToString())).Append(',')
                          .Append(Q(s.m_biomeArea.ToString())).Append(',')
                          .Append(s.m_maxSpawned).Append(',')
                          .Append(F(s.m_spawnInterval)).Append(',')
                          .Append(F(s.m_spawnChance)).Append(',')
                          .Append(F(s.m_spawnDistance)).Append(',')
                          .Append(s.m_groupSizeMin).Append(',')
                          .Append(s.m_groupSizeMax).Append(',')
                          .Append(F(s.m_groupRadius)).Append(',')
                          .Append(s.m_spawnAtNight).Append(',')
                          .Append(s.m_spawnAtDay).Append(',')
                          .Append(F(s.m_minAltitude)).Append(',')
                          .Append(F(s.m_maxAltitude)).Append(',')
                          .Append(s.m_inForest).Append(',')
                          .Append(s.m_outsideForest).Append(',')
                          .Append(F(s.m_minOceanDepth)).Append(',')
                          .Append(F(s.m_maxOceanDepth)).Append(',')
                          .Append(Q(s.m_requiredGlobalKey))
                          .AppendLine();
                        rows++;
                    }
                }
            }

            File.WriteAllText(Path.Combine(dir, SpawnFile), sb.ToString());
            Plugin.Log.LogInfo($"Catalog: {rows} spawn entr(ies) written to {SpawnFile}.");
        }

        // ---------------------------------------------------------------- utils

        static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
        static string F(float? v) => v.HasValue ? F(v.Value) : "";

        /// <summary>Quote anything that could carry a comma into a CSV cell.</summary>
        static string Q(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.IndexOf(',') < 0 && s.IndexOf('"') < 0) return s;
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }
    }
}
