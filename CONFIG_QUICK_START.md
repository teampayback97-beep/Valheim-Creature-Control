# Configuration Quick Start

## File Locations
```
BepInEx/config/CreatureControl.Creatures.cfg
BepInEx/config/CreatureControl.Factions.cfg
```

## Common Configuration Snippets

### Brave Creatures (Low Threat Threshold)
```ini
[Creature.Greydwarf]
ThreatLevel=2.0
```
Creatures flee less, attack more aggressively.

### Cowardly Creatures (High Threat Threshold)
```ini
[Creature.Boar]
ThreatLevel=6.0
```
Creatures flee quickly, avoid dangerous situations.

### Perception Types Quick Reference
| Type | Reads | Best For |
|------|-------|----------|
| `Gear` | Armor + Weapon damage | Sentient humanoids |
| `Instinct` | Presence + Movement | Animals |
| `Holy` | Spirit damage only | Undead, Demons |
| `None` | Ignores player | Sea creatures |

### Creature Roles Quick Reference
| Role | Behavior | Examples |
|------|----------|----------|
| `Fearless` | Never flee | Elites, Tamed, Constructs |
| `Solitary` | Fight alone | Bears, Sharks, Foxes |
| `Prey` | Always flee | Deer, Rabbits |
| `Babies` | Ignored by faction | Baby animals |

## Setup Templates

### Standard Humanoid Setup
```ini
[Creature.Greydwarf]
Perception=Gear
Role=Solitary
ThreatLevel=3.0

[Creature.GreydwarfElite]
Perception=Gear
Role=Fearless
ThreatLevel=4.5
```

### Wildlife Setup
```ini
[Creature.Boar]
Perception=Instinct
Role=Solitary
ThreatLevel=2.5

[Creature.Deer]
Perception=Instinct
Role=Prey
ThreatLevel=0.1
```

### Undead/Demon Setup
```ini
[Creature.Skeleton]
Perception=Holy
Role=Fearless
ThreatLevel=3.5

[Creature.Ghost]
Perception=Holy
Role=Fearless
ThreatLevel=4.0
```

### Sea Creatures Setup
```ini
[Creature.Serpent]
Perception=None
Role=Fearless
ThreatLevel=7.0

[Creature.Leviathan]
Perception=None
Role=Fearless
ThreatLevel=9.0
```

## Making Creatures Cooperate

In `Factions.cfg`:
```ini
[Faction.Humanoid]
Members=Greydwarf,GreydwarfElite,Shaman,Fuling
```
Members of the same faction coordinate and share threat verdicts.

## Making Creatures Ignore Each Other

Assign to separate factions:
```ini
[Faction.HumanoidA]
Members=Greydwarf,GreydwarfElite

[Faction.HumanoidB]
Members=Shaman,Fuling
```

## Testing Your Config

1. **Edit config file**
2. **Save** (changes apply live)
3. **Spawn creature** and test behavior
4. **Repeat** until satisfied

No Valheim restart needed!

## Troubleshooting Quick Fixes

### Creatures not fleeing
- Increase `ThreatLevel` value
- Check creature is in config file
- Verify spelling of creature name (case-sensitive)

### Creatures too cowardly
- Decrease `ThreatLevel` value
- Change `Role` from `Prey` to `Solitary`

### Creatures not cooperating
- Ensure all creatures are in same faction in `Factions.cfg`
- Check faction name matches exactly

### Everything broken
- Delete all `CreatureControl.*.cfg` files
- Restart Valheim (auto-regenerates with defaults)

## Next Steps

- Read **USAGE.md** for detailed explanations
- Read **README.md** for feature descriptions
- Check console (F5) for logs while testing
