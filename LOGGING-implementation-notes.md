# Troll Logging — implementation notes (first pass, unverified against the real assembly)

Everything in the spec artifact is implemented: `CreatureState.cs`, `Plugin.cs`,
`CreatureRules.cs`, `ConfigLoader.cs`, `Patches.cs` modified; `TrollLogging.cs`
and `TotemBind.cs` added; `CreatureControl.csproj` given a Jotunn reference.

This session has no access to `assembly_valheim.dll`, `Jotunn.dll`, or a
decompiler — only the mod's own source. Everything below is written from
general Valheim/Jotunn modding knowledge and cross-referenced against this
codebase wherever possible (e.g. the teleport fix), but **none of it has been
compiled**. Treat this file as the punch list for the first real build.

## Will almost certainly need a fix

- **`CreatureControl.csproj`** — the Jotunn `<Reference>` HintPath is a guess
  (`$(GaleProfile)\BepInEx\plugins\Jotunn\Jotunn.dll`). Gale may have extracted
  it under a different folder name (`ValheimModding-Jotunn`, etc.). The build
  will fail loudly on this one (`CheckPaths` target) rather than silently.

- **`TotemBind.cs` `SourcePrefab`** — now `"piece_ward"` (the Ward), picked
  because it's already a radius-of-influence marker in vanilla. `Register()`
  strips the cloned prefab's `PrivateArea` component so the leash doesn't also
  function as a real Ward (activation/naming interact, Eitr upkeep, its own
  hostile-warding behaviour, which would otherwise fight with the troll's own
  combat detection). **Unverified**: `PrivateArea` is my best recollection of
  the Ward's actual component type name/namespace — confirm against the
  decompile, and check whether the Ward carries any other components worth
  stripping too (its own `Piece`/icon setup should be harmless to keep).

- **Jotunn `PieceConfig`** — `PieceTable = "Hammer"` and `Category = "Misc"`
  are the values I'm most confident are stable across Jotunn versions, but the
  whole `Register()` method (`CreateClonedPrefab`, `CustomPiece`,
  `RequirementConfig`, `PieceManager.Instance.AddPiece`) needs a real compile
  against whatever Jotunn version is actually installed.

## Vanilla API calls that need checking against the decompile

None of these are used elsewhere in this codebase, so I have no existing
call site here to confirm the exact signature against:

- `Piece.m_allPieces` (`TotemBind.cs`) — assumed to be vanilla's own public
  static list of every placed piece.
- `Container.GetInventory()` / `Inventory.AddItem(string, int, int, int, long, string)`
  (`TrollLogging.Deposit`).
- `ItemDrop.ItemData.m_dropPrefab`, `.m_stack`, `.m_shared.m_maxStackSize`
  (`TrollLogging.TryPickUp` / `CarryIsFull`).
- `ZNetView.Destroy()` (`TrollLogging.TryPickUp`, removing a fully-collected
  world item).
- `HitData.SetAttacker(Character)` (`TrollLogging.Chop`) — Patches.cs only
  ever uses the getter (`hit.GetAttacker()`), so the setter's exact signature
  (it may take a second `string` parameter) is unconfirmed.
- `TreeBase.Damage(HitData)` / `TreeLog.Damage(HitData)` — reasonably
  confident these exist and match the `WearNTear.Damage(HitData)` /
  `IDestructible` shape, since `StructureGuard.cs`'s own comment says
  "Trees, rocks and the other world destructibles use TreeBase / MineRock /
  Destructible instead [of WearNTear]" — but the exact method name/signature
  isn't verified here.
- `ZDO.Set(string, bool)` / `ZDO.GetBool(string, bool)` — `CreatureState.cs`'s
  existing `ZdoModeKey` pattern stores an `int`, not a `bool`; I used the bool
  overload for `CC_logging` on the assumption ZDO has one (matching the
  spec artifact's own pseudocode, which wrote `Set("CC_logging", loggingOn)`
  with a bool). If it doesn't exist, switch to the same `int` pattern
  `ZdoModeKey` uses.
- `ZSyncAnimation.SetTrigger(string)` (`TrollLogging.Chop`, cosmetic swing) —
  wrapped in try/catch already since it's purely cosmetic; the "attack"
  trigger name is a guess and may not exist on every creature's Animator.
- `Character.Teleport(...)` was **deliberately avoided** — `TeleportFix.cs`
  documents that a plain transform/position write on a `Character` gets
  overwritten by its own Rigidbody on the next FixedUpdate. `TrollLogging.cs`'s
  `TeleportHome()` reuses that exact proven fix (move the Rigidbody, zero its
  velocity, `ZSyncTransform.SyncNow()`) instead of guessing at a `Teleport()`
  method.

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
