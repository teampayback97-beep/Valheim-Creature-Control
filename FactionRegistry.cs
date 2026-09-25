using System;
using System.Collections.Generic;

namespace CreatureControl
{
    /// <summary>
    /// Friendly - never an enemy, and will not retaliate even if attacked.
    /// Enemy    - hostile on sight.
    /// Neutral  - ignores, but fights back once that creature has hurt it.
    /// </summary>
    public enum Relation { Friendly = 0, Enemy = 1, Neutral = 2 }

    /// <summary>
    /// Character.Faction is an int-backed enum whose vanilla values run 0..13.
    /// Nothing stops us storing a higher number in Character.m_faction - the
    /// game accepts it, it just has no built-in opinion about it (vanilla's
    /// switch falls through to "return false" for anything it doesn't know).
    /// This owns those extra values and the table that gives them meaning.
    /// </summary>
    public static class FactionRegistry
    {
        public const int CustomBase = 100;

        /// <summary>Relations are keyed as (a &lt;&lt; 16) | b, so ids must stay
        /// inside 16 bits or distinct pairs would silently collide.</summary>
        public const int CustomMax = 65535;

        public class Store
        {
            public readonly Dictionary<string, int> ByName =
                new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<int, string> ById = new Dictionary<int, string>();
            public readonly Dictionary<int, Relation> Relations = new Dictionary<int, Relation>();
            public readonly Dictionary<int, Relation> Defaults = new Dictionary<int, Relation>();
            public readonly HashSet<int> Untargetable = new HashSet<int>();
            public bool HasCustom;

            public Store()
            {
                foreach (var name in Enum.GetNames(typeof(Character.Faction)))
                {
                    int v = (int)(Character.Faction)Enum.Parse(typeof(Character.Faction), name);
                    ByName[name] = v;
                    if (!ById.ContainsKey(v)) ById[v] = name;
                }
            }

            public bool Register(string name, int id, out string error)
            {
                error = null;
                if (string.IsNullOrEmpty(name)) { error = "empty faction name"; return false; }
                if (id < CustomBase || id > CustomMax)
                {
                    error = $"id {id} is out of range; custom faction ids must be {CustomBase}-{CustomMax} " +
                            $"(below {CustomBase} is reserved for vanilla factions)";
                    return false;
                }
                if (ByName.TryGetValue(name, out var existingId) && existingId != id)
                { error = $"name '{name}' already maps to id {existingId}"; return false; }
                if (ById.TryGetValue(id, out var existingName) &&
                    !string.Equals(existingName, name, StringComparison.OrdinalIgnoreCase))
                { error = $"id {id} already used by '{existingName}'"; return false; }

                ByName[name] = id;
                ById[id] = name;
                HasCustom = true;
                return true;
            }
        }

        static Store _active = new Store();

        public static void Install(Store s) => _active = s ?? new Store();

        public static bool HasCustomFactions => _active.HasCustom;
        public static bool AnyUntargetable => _active.Untargetable.Count > 0;

        /// <summary>Whether the config expresses ANY relationship at all.
        /// Gating on custom factions alone would silently ignore a config that
        /// only re-wires two vanilla factions against each other.</summary>
        public static bool HasAnyRelations =>
            _active.Relations.Count > 0 || _active.Defaults.Count > 0;

        public static bool IsCustom(int id) => id >= CustomBase;

        /// <summary>Creatures nothing may ever target - ambient insects and the
        /// like. Checked before any other rule so that even a faction whose
        /// default stance is Enemy cannot pick a fight with a butterfly.</summary>
        public static bool IsUntargetable(int id) => _active.Untargetable.Contains(id);

        public static bool TryGetId(string name, out int id) => _active.ByName.TryGetValue(name, out id);

        public static string NameOf(int id) =>
            _active.ById.TryGetValue(id, out var n) ? n : ("Faction" + id);

        internal static int Key(int a, int b) => a < b ? (a << 16) | b : (b << 16) | a;

        /// <summary>
        /// True only when the config spells this pair out by name. Such a line
        /// is a deliberate instruction and outranks the courtesies we would
        /// otherwise extend - notably Valheim's creature "group", which is what
        /// normally stops a troll swinging at a troll. "Feral : Feral = Enemy"
        /// is exactly that kind of instruction.
        /// </summary>
        public static bool TryGetExplicitRelation(int a, int b, out Relation r) =>
            _active.Relations.TryGetValue(Key(a, b), out r);

        public static bool TryGetRelation(int a, int b, out Relation r)
        {
            if (_active.Relations.TryGetValue(Key(a, b), out r)) return true;

            // A faction is friendly to itself unless it names itself above.
            // That single line is the whole difference between a pack hunter
            // (Orca) and something that turns on its own kind (Feral).
            if (a == b) { r = Relation.Friendly; return true; }

            if (IsCustom(a) && _active.Defaults.TryGetValue(a, out r)) return true;
            if (IsCustom(b) && _active.Defaults.TryGetValue(b, out r)) return true;
            if (IsCustom(a) || IsCustom(b)) { r = Relation.Friendly; return true; }

            r = Relation.Friendly;
            return false; // both vanilla and unlisted -> let the game decide
        }

        public static IEnumerable<KeyValuePair<string, int>> All => _active.ByName;
    }
}
