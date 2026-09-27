# Troll Logging — implementation notes

**STATUS: WORK IN PROGRESS — not yet build-verified in a real game.** A
startup log warning in `Plugin.cs` and header notices in `TrollLogging.cs`/
`TotemBind.cs` point back to this file. Once everything below is resolved and
the feature has actually been run and tested in-game, remove all three of
those markers (and this line) to flag it complete.

Everything in the spec artifact is implemented: `CreatureState.cs`, `Plugin.cs`,
`CreatureRules.cs`, `ConfigLoader.cs`, `Patches.cs` modified; `TrollLogging.cs`
and `TotemBind.cs` added; `CreatureControl.csproj` given a Jotunn reference.

This session has no access to `assembly_valheim.dll` or `Jotunn.dll` to
compile against directly, but the Jotunn API calls and most of the vanilla API
calls below have since been **cross-checked against real, compiling source**:
[Valheim-Modding/Jotunn](https://github.com/Valheim-Modding/Jotunn) itself for
the Jotunn side, and [RandyKnapp/ValheimMods](https://github.com/RandyKnapp/valheimmods)
(EpicLoot, EquipmentAndQuickSlots, etc. - a large, actively maintained
open-source collection) for the vanilla side. Still not the same as an actual
compile, but real signatures beat guesses. What's still open is listed below.

## Verified against Jotunn's own source (Valheim-Modding/Jotunn)

- `PrefabManager.OnVanillaPrefabsAvailable` (event) and
  `PrefabManager.Instance.CreateClonedPrefab(string name, string baseName)` -
  exact match.
- `new CustomPiece(GameObject piecePrefab, bool fixReference, PieceConfig pieceConfig)`
  - exact overload match (there's also an older 2-arg obsolete one; this isn't it).
  Note: `CustomPiece`'s constructor already calls `pieceConfig.Apply(prefab)`
  internally, so `TotemBind.cs` correctly doesn't call it a second time.
- `PieceManager.Instance.AddPiece(CustomPiece)` returns `bool` - `Register()`
  didn't check it before; now logs an error if it comes back false (piece
  invalid, or a name collision).
- `PieceConfig.PieceTable = "Hammer"` - confirmed via Jotunn's own
  `PieceTables.NamesMap`, which maps the literal string `"Hammer"` to the real
  internal name `"_HammerPieceTable"`.
- `PieceConfig.Category = "Misc"` - confirmed via `PieceCategories.Misc =>
  nameof(Piece.PieceCategory.Misc)`, i.e. this literally has to be `"Misc"`.
- **New finding, not previously flagged**: `CustomPiece.IsValid()` requires
  `Piece.m_icon` to be non-null or `AddPiece` rejects it outright. Since the
  Ward clone inherits the vanilla Ward's own icon (nothing in `PieceConfig`
  overrides `Icon`), this should pass registration - but the leash will show
  the *Ward's* icon in the build menu, which could read as "this is the real
  Ward" to a player. Worth giving it its own icon at some point; not fixed here.

## Verified against real vanilla-API call sites (RandyKnapp/ValheimMods)

All of these now have a confirmed, compiling example to match against:

- `PrivateArea` - confirmed as the Ward/Guard Stone's real component type
  (`EpicLoot/src/Adventure/AdventureWardCheck.cs` references
  `PrivateArea.m_allAreas`, `.IsEnabled`, `.CheckAccess` directly, and its own
  comment calls the Guard Stone "a deactivated guard stone" in the same
  breath - confirming guard_stone and piece_ward are both `PrivateArea`-based,
  so the strip logic in `TotemBind.Register()` would have worked either way).
- `Container.GetInventory()` and `Inventory.AddItem(string prefabName, int stack, int quality, int variant, long crafterID, string crafterName)`
  - exact match (`EpicLoot/src/Adventure/TreasureMapChest.cs`:
  `container.m_inventory.AddItem("ForestToken", amount, 1, 0, 0, string.Empty, cheated: false)`).
- `Container.Save()` after mutating its inventory - confirmed as the real
  pattern (same file, right after the `AddItem` calls). **Fixed**:
  `TrollLogging.Deposit` was missing this call; added it back.
- `ItemDrop.ItemData.m_dropPrefab`, `.m_stack`, `.m_shared.m_maxStackSize` -
  all confirmed, exact field names, across several files.
- `ItemDrop.Save()` after mutating `m_itemData` directly - confirmed
  (`EpicLoot/src/Loot/LootRoller.cs`).
- `ZNetView.Destroy()` - confirmed exact (`EpicLoot/src/Adventure/BountyTarget.cs`:
  `_character.m_nview.Destroy();`).
- `HitData.SetAttacker(Character)` - confirmed exact, single-argument overload,
  used identically in several `MagicItemEffects` files.
- `TreeBase.Damage(HitData)` / `TreeLog.Damage(HitData)` - confirmed exact via
  Harmony patches on both (`EpicLoot/src/Magic/MagicItemEffects/IncreaseTreeDrop.cs`:
  `[HarmonyPatch(typeof(TreeBase), nameof(TreeBase.Damage))]` /
  `[HarmonyPatch(typeof(TreeLog), nameof(TreeLog.Damage))]`, both taking `HitData`).
- `ZDO.Set(string, bool)` / `ZDO.GetBool(string, bool)` - confirmed exact
  (`DvergerColor/src/VisEquipment_Patch.cs`: `zdo.Set(key, On); ... On =
  zdo.GetBool(key, true);`). `CC_logging` storing a plain bool (rather than the
  int `CC_mode` uses) is fine as-is.

## Still open / genuinely unverified

- **`CreatureControl.csproj`** Jotunn `<Reference>` HintPath is still a guess
  at the local Gale profile's folder layout
  (`$(GaleProfile)\BepInEx\plugins\Jotunn\Jotunn.dll`) - that's a local-machine
  fact no GitHub source can confirm. The build's `CheckPaths` target will fail
  loudly and name the problem if it's wrong.
- **`Piece.m_allPieces`** (`TotemBind.cs`) - not found in either reference repo
  I checked, so still unconfirmed by source, though it's a very well
  established fact in the wider Valheim modding community (the standard way
  mods enumerate every placed piece). Worth a quick decompile check before
  relying on it.
- **`ZSyncAnimation.SetTrigger("attack")`** (`TrollLogging.Chop`, cosmetic
  swing only) - `ZSyncAnimation` itself is confirmed as a real component type
  (Jotunn's `CustomCreature.cs` requires one on every custom creature), but no
  source with a `SetTrigger` call turned up, and the `"attack"` trigger name
  is still a guess. Already wrapped in try/catch since it's cosmetic-only -
  worst case it silently does nothing.
- **`Character.Teleport(...)` was deliberately avoided** - `TeleportFix.cs`
  documents that a plain transform/position write on a `Character` gets
  overwritten by its own Rigidbody on the next FixedUpdate. `TrollLogging.cs`'s
  `TeleportHome()` reuses that exact proven in-repo fix (move the Rigidbody,
  zero its velocity, `ZSyncTransform.SyncNow()`) instead of guessing at a
  `Teleport()` method.

## Design decisions made where the spec left something open

- **`loggingMode` defaults to off, not on** (`CreatureRules.cs` /
  `CreatureState.CanLog`). The spec says it "mirrors stanceCycling", but
  stanceCycling defaults permissive (null = allowed) because it only gates a
  stance the player already controls. Logging drives a tame into an unattended
  work loop, so I made it opt-in per creature — a config with no `loggingMode`
  line at all logs nothing, even for a troll. Default template now ships
  `[Troll]\nloggingMode = true` so trolls work out of the box; flip the
  default polarity in `CreatureState.CanLog` if you want the stanceCycling
  behaviour instead.
- **Logging hotkey defaults to L.Ctrl+X** (separate from the L.Alt+X stance
  key). Arbitrary — spec left it TBD.
- **Numeric defaults** (leash radius 20m, chop interval 2s, chop damage 50,
  tree/leash scan intervals 3s/5s, return timeout 30s) are all `ConfigEntry`s,
  tunable without a recompile.
- **Totem durability**: not special-cased. The leash is a normal player-placed
  piece, so it's already covered by the existing `StructureGuard.cs` /
  `Plugin.TamesSpareStructures` check (tamed creatures can't damage it) but
  **wild enemies can destroy it like any other building** — the spec's "if the
  totem is destroyed, troll unbinds" bullet implies destructibility is
  intended, so this was left as vanilla `WearNTear` behaviour rather than
  making it indestructible or admin-only. Flag if you wanted the latter.
- **Multiple logging trolls can target the same tree.** No reservation/claim
  system was built — two trolls bound to the same leash may walk to and chop
  the same tree simultaneously. Noted as an acceptable simplification, not
  fixed.
- **"Aggroed by proximity" isn't fully covered.** Combat-interruption checks
  `ai.GetTargetCreature() != null || ai.IsAlerted()` each tick. Vanilla's own
  proximity-based target *acquisition* normally happens inside
  `MonsterAI.UpdateAI` itself, which a logging troll's tick never calls (we
  take the tick over completely, same as the existing fear system does).
  So a wild creature that notices the troll but hasn't landed a hit yet won't
  interrupt logging until either it actually hits the troll (`OnDamaged` /
  `ApplyDamage`, patched independently in `Patches.cs` and unaffected by which
  system owns the tick) or the troll's own owner/nearby ally combat pulls it
  in via the existing grudge system. In practice this should cover the large
  majority of real encounters; a pure "something is sneaking up but hasn't
  attacked yet" case is the one gap.
- **Tree "successor" search** (`TrollLogging.FindSuccessorLog`) runs a fresh
  `FindObjectsByType<TreeLog>` every time a troll's target tree disappears,
  rather than waiting for the shared timer-based scan — needed so a freshly
  felled log is seen immediately rather than up to `Tree Scan Seconds` later.
  This is a full scene sweep and only fires on a felling event (not every
  tick), so it should be cheap in practice, but it's worth watching if you
  have many logging trolls running at once.

## Everything else

Config plumbing (`loggingMode` key, `[LoggingTrees]` override table,
`[*]`/`@Faction`/creature precedence via `FillFrom`/`IsEmpty`), the
`CreatureControl.Creatures.cfg` starter template, live-reload support, hover
text, persistence (`CC_logging` on the ZDO, loaded the same way `CC_mode` is),
and the hotkey handler all follow the exact existing patterns in this codebase
one-for-one and should need no changes beyond whatever the API-signature fixes
above require.
