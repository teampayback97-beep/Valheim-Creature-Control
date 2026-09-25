namespace CreatureControl
{
    /// <summary>How a creature decides whether to pick a fight.</summary>
    public enum BehaviorMode
    {
        /// <summary>Vanilla behaviour - attacks anything its faction dislikes, on sight.</summary>
        Aggressive = 0,

        /// <summary>Will not start a fight, but fights back once something hurts it.</summary>
        Neutral = 1,

        /// <summary>Never treats anything as an enemy. Docile.</summary>
        Passive = 2,
    }

    public static class BehaviorModeExt
    {
        public static BehaviorMode Next(this BehaviorMode m)
        {
            switch (m)
            {
                case BehaviorMode.Passive: return BehaviorMode.Neutral;
                case BehaviorMode.Neutral: return BehaviorMode.Aggressive;
                default: return BehaviorMode.Passive;
            }
        }

        public static string Pretty(this BehaviorMode m)
        {
            switch (m)
            {
                case BehaviorMode.Passive: return "Passive";
                case BehaviorMode.Neutral: return "Neutral";
                default: return "Aggressive";
            }
        }

        public static bool TryParse(string s, out BehaviorMode m)
        {
            m = BehaviorMode.Aggressive;
            if (string.IsNullOrEmpty(s)) return false;
            switch (s.Trim().ToLowerInvariant())
            {
                case "passive": case "docile": m = BehaviorMode.Passive; return true;
                case "neutral": case "defensive": m = BehaviorMode.Neutral; return true;
                case "aggressive": case "hostile": m = BehaviorMode.Aggressive; return true;
            }
            return false;
        }
    }
}
