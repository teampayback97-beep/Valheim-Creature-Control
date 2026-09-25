# Usage Guide

## Configuration Files

CreatureControl generates configuration files automatically in `BepInEx/config/` on first launch. Edit these files to customize creature behavior without needing to recompile.

### Creatures.cfg

Defines per-creature behavior, perception type, and threat levels.

**Available Perception Types:**
- `Gear` - Sentient creatures (humanoids, elites) read player armor and weapons
- `Instinct` - Animals sense only presence and movement
- `Holy` - Undead/demons read only spirit damage as threat
- `None` - Sea creatures ignore players entirely

**Available Creature Roles:**
- `Fearless` - Elites, constructs, undead, demons, tamed creatures (never flee)
- `Solitary` - Hunters that never cooperate (bears, foxes, sharks)
- `Prey` - Animals that flee without fighting back
- `Babies` - Worth zero threat to faction, never called for backup

**Example Configuration:**
```
[Creature.Troll]
Perception=Gear
Role=Fearless
ThreatLevel=8.0

[Creature.Deer]
Perception=Instinct
Role=Prey
ThreatLevel=0.2
```

### Factions.cfg

Defines faction-wide settings and creature relationships (which creatures cooperate or hunt each other).

**Example:**
```
[Faction.Humanoid]
Members=Greydwarf,GreydwarfElite,Shaman,Fuling
Perception=Gear
DefaultRole=Solitary

[Faction.Beast]
Members=Boar,Wolf,Troll
Perception=Instinct
DefaultRole=Fearless
```

## How It Works

### Threat Assessment

Creatures evaluate your threat based on:
1. **Equipped Gear**: (Gear perception only) Armor rating and weapon damage
2. **Spirit Damage**: (Holy perception only) Equipped spear type and enchantments
3. **Presence**: (Instinct perception) Your proximity and movement
4. **None**: (None perception) You don't exist as a threat

### Band Coordination

When creatures encounter danger, they signal nearby allies:
- **Band Formation**: Allies share fear verdicts and act as a coordinated group
- **Dinner Bell**: Fleeing creatures attract both allies (who help) and predators (who hunt the caller)
- **Pack Behavior**: Groups decide collectively whether to fight or flee

### Cornering Mechanics

Fleeing creatures don't flee forever:
- Creatures track available escape routes (gaps, water, cliffs)
- If cornered (no escape available), they turn and fight
- This creates dynamic, realistic combat rather than endless kiting

### Solitary Creatures

Creatures with the **Solitary** role skip ongoing battles:
- They only enter combat when freshly engaged
- They ignore distant fights and "Dinner Bell" calls
- Realistic for apex predators (bears, foxes, sharks)

## Adjusting Behavior

### Make Creatures Braver
```
Increase ThreatLevel value in Creatures.cfg
Decrease player equipment armor value in assessment
Change Perception=Instinct to Perception=None for specific creatures
```

### Make Creatures More Cautious
```
Decrease ThreatLevel value
Increase armor rating evaluation
Add creatures to Prey role
```

### Create Custom Factions
Edit Factions.cfg to:
- Add new faction groupings
- Assign creatures to different factions (they won't cooperate with outside factions)
- Set default perception and behavior for faction members

## Live Reload

**Config changes apply immediately without restarting Valheim.** The mod watches configuration files and reloads them when saved. Test settings in real-time while playing.

## Troubleshooting Config Issues

**Config changes don't apply:**
- Verify you're editing files in `BepInEx/config/`
- Ensure file names match exactly (case-sensitive on Linux)
- Check console (F5) for parse errors

**Creatures not behaving as expected:**
- Verify the creature name matches exactly in config (check Valheim console for exact prefab names)
- Check that the creature is assigned to the correct faction
- Ensure Perception type matches creature archetype

**Performance issues:**
- Reduce Band size or remove unnecessary factions
- Disable "Dinner Bell" coordinate system if performance drops
- Check console for spammy logs indicating parsing errors

## Advanced: Console Commands

Check the console (F5) during gameplay for detailed logs:
- `[CreatureControl]` messages show threat evaluations
- `[Band]` messages show pack coordination
- `[Threat]` messages show danger scoring

Use these logs to debug why specific creatures behave unexpectedly.

## Mod Compatibility

This mod is designed to work with:
- **BetterFactions** - Enhances faction management
- **BetterTames** - Improves taming mechanics
- **LetMeTameYou** - Alternative taming system
- **Monstrum** - Adds new creatures that integrate with the threat system

Install any or all of these alongside CreatureControl for enhanced gameplay.

## Feedback & Issues

If creatures behave unexpectedly or you find configuration options don't work as expected, check:
1. The console logs (F5)
2. Configuration file formatting
3. Creature name accuracy
4. Whether the creature's role/faction is correctly assigned

Report issues at the GitHub repository with console logs and reproduction steps.
