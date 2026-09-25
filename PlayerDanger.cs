using HarmonyLib;
using UnityEngine;

namespace CreatureControl
{
    /// <summary>
    /// What a player looks like to something deciding whether to attack them.
    ///
    /// The governing idea is that a creature can only fear what it can SEE.
    /// Armour is visible. A weapon in your hands is visible. A weapon on your
    /// back is visible to anything that understands weapons, and meaningless to
    /// anything that does not. What is in your pack is invisible to everything,
    /// and so is worth nothing here.
    /// </summary>
    public static class PlayerDanger
    {
        // ItemData and SharedData are nested inside ItemDrop; DamageTypes is
        // nested inside HitData. Naming them flat does not compile.
        static AccessTools.FieldRef<Humanoid, ItemDrop.ItemData> _hiddenRight;
        static AccessTools.FieldRef<Humanoid, ItemDrop.ItemData> _hiddenLeft;
        static bool _bound;

        /// <summary>The sheathed slots are private, so they need reflection.
        /// Without them a player who put their sword away would read as unarmed
        /// and get swarmed every time they stopped to walk somewhere.</summary>
        static void Bind()
        {
            if (_bound) return;
            _bound = true;
            try
            {
                _hiddenRight = AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData>("m_hiddenRightItem");
                _hiddenLeft = AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData>("m_hiddenLeftItem");
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning(
                    "Could not reach the sheathed weapon slots; a holstered weapon " +
                    $"will read as unarmed to sentient creatures: {e.Message}");
            }
        }

        // ------------------------------------------------------------ reading

        static float Armor(Player p)
        {
            float a = p.GetBodyArmor();
            return a > 0f ? a : 0f;
        }

        /// <summary>Total combat damage of an item, quality included. Chop and
        /// pickaxe are left out: they are for wood and stone, not for you.</summary>
        static float Combat(ItemDrop.ItemData it)
        {
            if (it == null) return 0f;
            HitData.DamageTypes d;
            try { d = it.GetDamage(); } catch { return 0f; }
            return d.m_damage + d.m_blunt + d.m_slash + d.m_pierce +
                   d.m_fire + d.m_frost + d.m_lightning + d.m_poison + d.m_spirit;
        }

        static float Spirit(ItemDrop.ItemData it)
        {
            if (it == null) return 0f;
            try { return it.GetDamage().m_spirit; } catch { return 0f; }
        }

        /// <summary>The weapon actually in hand, or null when empty-handed.
        /// GetCurrentWeapon hands back the built-in fists when nothing is held,
        /// so that case has to be filtered out or bare hands read as a weapon.</summary>
        static ItemDrop.ItemData Held(Player p)
        {
            var w = p.GetCurrentWeapon();
            if (w == null) return null;
            var fists = p.m_unarmedWeapon != null ? p.m_unarmedWeapon.m_itemData : null;
            if (fists != null && w == fists) return null;
            return w;
        }

        static ItemDrop.ItemData Sheathed(Player p)
        {
            Bind();
            if (_hiddenRight == null) return null;
            try
            {
                var r = _hiddenRight(p);
                if (r != null) return r;
                return _hiddenLeft != null ? _hiddenLeft(p) : null;
            }
            catch { return null; }
        }

        // ------------------------------------------------------------- lenses

        /// <summary>Sentient. Reads the real numbers, and counts a sheathed
        /// weapon - a goblin knows what a sword on your back is.</summary>
        static float Gear(Player p)
        {
            float dmg = Combat(Held(p));
            if (dmg <= 0f) dmg = Combat(Sheathed(p));
            if (dmg > Plugin.WeaponDamageInputCap) dmg = Plugin.WeaponDamageInputCap;

            return Armor(p) / Plugin.ArmorDivisor + dmg / Plugin.WeaponDivisor;
        }

        /// <summary>
        /// Animal. Cannot read quality, only presence: is it wearing something,
        /// are its hands full. A sheathed weapon is deliberately ignored - a
        /// wolf sees empty hands.
        /// </summary>
        static float Instinct(Player p)
        {
            float v = Plugin.InstinctBase;
            if (Armor(p) > 0f) v += Plugin.InstinctStep;
            if (Combat(Held(p)) > 0f) v += Plugin.InstinctStep;
            return v;
        }

        /// <summary>
        /// Undead and demons. Steel and armour mean nothing; only spirit damage
        /// registers. Armour rides along as a small multiplier - it decides how
        /// long you live to keep swinging, never whether you are a threat at
        /// all. No spirit damage means no fear, whatever you are wearing.
        /// </summary>
        static float Holy(Player p)
        {
            float s = Spirit(Held(p));
            if (s <= 0f) s = Spirit(Sheathed(p));
            if (s <= 0f) return 0f;

            float baseHoly = s / Plugin.SpiritDivisor;
            float armorBonus = 1f + Armor(p) / Plugin.HolyArmorDivisor;
            return baseHoly * armorBonus;
        }

        // -------------------------------------------------------------- entry

        /// <summary>What this player is worth to something looking through the
        /// given lens. Always passed through the shared scale and cap.</summary>
        public static float For(Player p, Perception mode)
        {
            if (p == null || p.IsDead()) return 0f;
            if (mode == Perception.None) return 0f;

            float v;
            switch (mode)
            {
                case Perception.Gear: v = Gear(p); break;
                case Perception.Instinct: v = Instinct(p); break;
                case Perception.Holy: v = Holy(p); break;
                default: return 0f;
            }

            v *= Plugin.PlayerThreatScale;
            if (v > Plugin.DangerCap) v = Plugin.DangerCap;
            return v < 0f ? 0f : v;
        }

        /// <summary>For the verbose log, so the numbers can be checked against
        /// the tables rather than taken on trust.</summary>
        public static string Describe(Player p)
        {
            if (p == null) return "(no player)";
            var held = Held(p);
            var sheathed = Sheathed(p);
            return $"armor={Armor(p):0.#} held={Combat(held):0.#} " +
                   $"sheathed={Combat(sheathed):0.#} spirit={Spirit(held ?? sheathed):0.#} " +
                   $"| gear={For(p, Perception.Gear):0.00} " +
                   $"instinct={For(p, Perception.Instinct):0.00} " +
                   $"holy={For(p, Perception.Holy):0.00}";
        }
    }
}
