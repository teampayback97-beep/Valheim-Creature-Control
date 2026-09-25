# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.0.0] - 2026-09-25

### Added
- **Perception-Based Threat Evaluation System**
  - Gear perception: Sentient creatures (humanoids, elites) read player armor and weapons
  - Instinct perception: Animals see only presence and movement
  - Holy perception: Undead/demons read only spirit damage as threat
  - None perception: Sea creatures ignore players entirely

- **Intelligent Combat Behavior**
  - Band System: Creatures coordinate as groups, sharing fear verdicts across allies
  - Cornering Mechanics: Fleeing creatures track escape routes and fight when cornered
  - Solitary Creatures: Apex predators skip ongoing battles, only fight when freshly engaged
  - Dinner Bell: Cries for help attract both allies and predators

- **Creature Role System**
  - Fearless: Elites, constructs, undead, demons, tamed creatures
  - Solitary: Hunters that never cooperate (bears, foxes, sharks)
  - Prey: Animals that flee without fighting back
  - Babies: Worth zero threat to their faction

- **Configuration System**
  - Per-creature behavior customization via Creatures.cfg
  - Faction-wide settings via Factions.cfg
  - Live reload: Changes apply immediately without restart
  - Console logging for debugging threat evaluations

- **Mod Compatibility**
  - Designed to work with BetterFactions, BetterTames, LetMeTameYou, and Monstrum
  - Standalone functionality for users without companion mods

### Technical
- BepInEx mod framework integration
- Per-creature state tracking (CreatureState.cs)
- Group coordination system (Band.cs)
- Threat calculation engine (Threat.cs)
- Player danger evaluation (PlayerDanger.cs)
- Perception type system (Perception.cs)
- Configuration file parsing (CreatureRules.cs)

---

## Version History

### Planned for 1.1.0
- Additional creature types and presets
- Performance optimizations for large creature groups
- Extended configuration options for fine-tuning threat behavior
- Multiplayer-specific considerations

---

## How to Report Issues

If you encounter unexpected behavior:
1. Check the console (F5 in Valheim) for error messages
2. Verify your configuration files are properly formatted
3. Ensure creature names match exactly (case-sensitive)
4. Test with a clean config (delete and regenerate)

Include console logs and reproduction steps when reporting issues.
