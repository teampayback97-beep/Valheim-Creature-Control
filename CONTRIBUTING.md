# Contributing to CreatureControl

Thank you for your interest in contributing! This document outlines how to help improve the mod.

## Ways to Contribute

### Reporting Issues

Found a bug? Creatures behaving unexpectedly? Please open an issue with:
1. **Description**: What behavior did you observe? What did you expect?
2. **Reproduction Steps**: How can we reproduce the issue?
3. **Console Logs**: Include any `[CreatureControl]` or error messages from F5 console
4. **Configuration**: Share relevant config excerpts (Creatures.cfg, Factions.cfg)
5. **Environment**: Valheim version, BepInEx version, other mods installed

### Suggesting Features

Have an idea to improve creature behavior? Ideas welcome:
- New perception types for specific creature archetypes
- Additional creature roles or faction mechanics
- Configuration improvements
- Performance optimizations

### Submitting Code

Want to contribute code? Great! Here's how:

#### Setup for Development

1. **Install .NET SDK 8**: Required for building
   - Windows: `winget install Microsoft.DotNet.SDK.8`
   - macOS: `brew install dotnet`
   - Linux: Follow your package manager's instructions

2. **Clone the repository**:
   ```bash
   git clone https://github.com/teampayback97-beep/Valheim-Creature-Control.git
   cd Valheim-Creature-Control
   ```

3. **Update paths in the project file**:
   - Set your Valheim installation path
   - Set your BepInEx installation path

4. **Build**:
   ```bash
   dotnet publish
   ```

#### Code Style

- Follow existing code style in the project
- Use clear, descriptive variable and method names
- Comment complex logic, especially in threat calculation
- Keep methods focused and reasonably sized

#### Key Files to Understand

- **Plugin.cs**: Main entry point, configuration loading, mod lifecycle
- **CreatureState.cs**: Per-creature state, fear/flee logic
- **Band.cs**: Group coordination and communication between creatures
- **Threat.cs**: Danger scoring and fear calculations
- **PlayerDanger.cs**: Threat evaluation based on perception type
- **Perception.cs**: Perception type definitions and parsing
- **CreatureRules.cs**: Configuration file representation

#### Making Changes

1. **Create a branch**:
   ```bash
   git checkout -b feature/your-feature-name
   ```

2. **Make your changes**:
   - Write clean, focused commits
   - Test thoroughly in Valheim
   - Update documentation if behavior changes

3. **Test your changes**:
   - Build the mod locally
   - Test with various creature types
   - Verify config reload works
   - Check console logs for any new errors

4. **Submit a pull request**:
   - Describe what your change does
   - Explain why the change is needed
   - Reference any related issues

## Development Tips

### Testing Your Changes

1. **Quick Test**:
   - Build with `dotnet publish`
   - Reload Valheim (mod reloads immediately)
   - Spawn creatures and test behavior

2. **Console Debugging**:
   - Press F5 in Valheim to open console
   - Look for `[CreatureControl]` logs from your changes
   - Use `[Threat]`, `[Band]` tags for specific systems

3. **Config Testing**:
   - Edit config files while Valheim is running
   - Changes apply live without restart
   - No recompile needed for config-only changes

### Common Modifications

**Adding a new creature type**:
1. Add to Creatures.cfg with perception type and role
2. Assign to appropriate faction in Factions.cfg
3. Reload and test behavior

**Adjusting threat evaluation**:
- Modify threat calculation in Threat.cs
- Adjust ThreatLevel values in Creatures.cfg
- Test with different gear loadouts

**Changing pack behavior**:
- Band.cs handles group coordination
- Modify how creatures share fear verdicts
- Test with groups of enemies

## Pull Request Process

1. Ensure all changes are tested
2. Update documentation if behavior changes
3. Make sure code builds without warnings
4. Submit PR with clear description

## Code of Conduct

- Be respectful and constructive
- Focus on the code and ideas, not people
- Help others learn and improve
- Assume good intent

## Questions?

If you have questions about the codebase:
- Check existing issues for similar questions
- Review code comments and documentation
- Open a discussion issue

We're happy to help new contributors get started!

## Recognition

Contributors will be recognized in:
- CHANGELOG.md
- GitHub contributors page
- Mod listing (if published)

Thank you for helping make CreatureControl better!
