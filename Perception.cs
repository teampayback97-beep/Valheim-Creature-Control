namespace CreatureControl
{
    /// <summary>
    /// How a creature reads a PLAYER. Creature-versus-creature threat is not
    /// affected by any of this - a wolf still weighs a boar the same way it
    /// always did. This only decides what a creature understands when it looks
    /// at a person.
    /// </summary>
    public enum Perception
    {
        /// <summary>Sentient: reads armour and weapons for what they are, and
        /// recognises a sheathed weapon as a weapon. Goblins, dvergr,
        /// greydwarves - anything that makes and carries its own gear.</summary>
        Gear,

        /// <summary>Animal: cannot read gear at all, only whether this thing is
        /// wearing something and whether its hands are full. A blade on your
        /// back means nothing to a wolf.</summary>
        Instinct,

        /// <summary>Undead and demons: armour and steel are meaningless. Only
        /// spirit damage registers, with armour as a small bonus on top of
        /// it.</summary>
        Holy,

        /// <summary>Reads nothing, ever. The player is simply not a factor.
        /// For sea predators, which have no way to judge a person.</summary>
        None,
    }

    public static class PerceptionEx
    {
        public static Perception? Parse(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return null;
            switch (raw.Trim().ToLowerInvariant())
            {
                case "gear": case "sentient": return Perception.Gear;
                case "instinct": case "animal": return Perception.Instinct;
                case "holy": case "spirit": return Perception.Holy;
                case "none": case "ignore": return Perception.None;
                default: return null;
            }
        }

        public static string Pretty(this Perception p)
        {
            switch (p)
            {
                case Perception.Gear: return "gear";
                case Perception.Instinct: return "instinct";
                case Perception.Holy: return "holy";
                default: return "none";
            }
        }
    }
}
