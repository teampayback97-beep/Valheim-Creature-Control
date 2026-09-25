# Conflict audit — 85 mods vs the threat system

Method: read every plugin DLL's Harmony attribute table and resolved which
vanilla methods each one patches, then traced which ones actually write to
`BaseAI` / `MonsterAI` fields at the IL level. Not guesswork from mod
descriptions.

---

## 1. BLOCKING — CLLC "Curious" affix rewrites your ranges

`Smoothbrain-CreatureLevelAndLootControl`

`CreatureLevelControl.SetPersistedCharacterAttributes` does this:

```
offset 008c   ldfld   BaseAI::m_hearRange     <- reads current value
offset 009d   ldfld   BaseAI::m_viewRange
offset 029c   stfld   BaseAI::m_hearRange     <- writes it back, scaled
offset 02b1   stfld   BaseAI::m_viewRange
```

The Curious affix is documented as *"Curious creatures always have 360 view.
By default the hear and view ranges are increased by a factor of 1 (i.e.
doubled)."* Your config has **`Chance for Curious effect to spawn = 20`** — one
creature in five.

Callers:

| Caller | When |
|---|---|
| `AttachLevelBehaviorToCharacters::Postfix` | `Character.Awake` |
| `UpdateExtraAccordingToLevelPatch::Postfix` | on level/star change |
| `UpdateCharacterAttributesFromConfig` | on a CLLC config change |
| `creatureMovementSpeed_SettingChanged` | on a CLLC config change |

**Why it matters.** CreatureControl applies in `Start()`, which Unity runs after
every `Awake()`, so at spawn we win and the affix is silently discarded. But the
other three callers fire *later* and **re-read the current value** — so once we
have written Lox view = 8, a CLLC config change or a star-up reads 8 and writes
16. Your territorial megafauna quietly doubles its aggression radius, and a
tamed Bjorn's 15m leash becomes 30m.

**Options**

1. **Set `Chance for Curious effect to spawn = 0`.** One line, zero code, and
   the affix was already fighting the design.
2. Re-assert ranges from CreatureControl on a cheap timer (~5s) — costs a
   per-creature tick we currently do not have.
3. Have CreatureControl skip range writes on creatures carrying the affix —
   most faithful to both mods, most code.

I would take option 1 unless you like Curious.

---

## 2. BLOCKING — stars invalidate flat threat

Same mod. `Maximum stars = Five`, `Health gained per star = 100%`,
`Damage gained per star = 50%`, plus Aggressive / Quick / Regenerating /
Armored affixes.

A 3-star fuling has 4x the health of the fuling the table is calibrated on. If
threat stays flat, three 0-star fulings and three 3-star fulings make the same
decision against a lox, and the starred ones are the ones that should be
walking in confidently.

Fix is in the threat table: `effectiveThreat = base x (1 + 0.6 x stars)`.

---

## 3. IMPORTANT — `MonsterAI.UpdateAI` is already contested

`TeamKoro-BetterTames` holds **two prefixes** on it:

| Patch | Note |
|---|---|
| `PetProtection.PetProtectionPatch::KnockoutTimerPrefix` | carries `HarmonyPriority` |
| `PetProtection.StunBehaviorPatches::PreventAIUpdateWhenStunned` | |

Both exist to **return false** and skip the tick (knocked-out and stunned pets).

The fear check is planned as an `UpdateAI` prefix. Two consequences:

- It must run **after** BetterTames, or a stunned pet would still be evaluating
  whether to flee. `[HarmonyPriority(Priority.Last)]` plus
  `[HarmonyAfter("<BetterTames GUID>")]`.
- It must tolerate never running at all on a given tick. Any state the fear
  system keeps (last-evaluated timestamp, cached ally count) has to be safe
  across skipped ticks — no assuming a fixed cadence.

This is the one place where the "zero shared patch methods" result from the
earlier audit stops being true, because that audit predates the fear system.

---

## 4. NOTED — three mods on `Character.SetTamed`

`CreatureControl`, `CreatureLevelControl`, `LetMeTameYou` (x2).

All postfixes, none returns a value, so no ordering hazard. Worth recording
because it is exactly why the `TamedStateChanged()` guard earned its place —
`SetTamed` gets called far more often than a creature actually changes state.

---

## 5. NOTED — three mods on `Player.UpdateTeleport`

`BetterTames`, `Azumatt-PetPantry`, `Zenox-TeleportEverything`
(TeleportEverything also holds **five** patches on `Player.TeleportTo`).

Relevant to the pet-teleport snap-back: that path is crowded, and the
`SetHuntPlayer` ZDO write was only ever one of several things landing there.
The rigidbody + `SyncNow()` fix stands, but if the snap-back returns, this is
where to look — and the cheapest test is disabling TeleportEverything's pet
handling for one session.

---

## 6. CLEAR — checked and harmless

| Mod | Why it looked suspicious | What it actually does |
|---|---|---|
| `blacks7ar-MagicPlugin` | writes `m_faction`, `m_hearRange`, `m_alertRange` | only inside `SummonHelper::Create` — its own summons. Patches nothing on AI. |
| `HugotheDwarf-Hugos_Armory` | contains `SetHuntPlayer` | patches only `ObjectDB` and `ZNetScene`. Item mod. |
| `Azumatt-FactionAssigner` | writes `m_faction` | `Character.Awake` postfix, as expected. Our `Start()` runs after it. No change. |
| `OdinPlus-BeeQueen` | `m_faction`, `m_huntPlayer` | `Character.Awake` only, for its own creature. |
| `OdinPlus-OdinCampsite` | `m_faction` | patches only `Bed.*`. |
| `Azumatt-PetPantry` | `MonsterAI`, `Tameable` | `Tameable.IsHungry` / `OnDeath`, containers. No AI writes. |
| `Zenox-TeleportEverything` | `MonsterAI`, `BaseAI` | teleport only. No AI writes. |
| `Therzie-Monstrum` / `Warfare` / `Wizardry` | `SetAlerted` | no `m_` field writes, no `UpdateAI`, no `IsEnemy`. Their own creature scripts. |
| `Marlthon-AirAnimals` / `SeaAnimals` | `MonsterAI`, `BaseAI` | content only. |
| `Frenvius-Valharvest`, `Smoothbrain-*` (others) | — | nothing on AI. |

Nothing outside CreatureControl patches `BaseAI.IsEnemy`. The faction matrix is
ours alone.

---

## 7. Tamed creatures must be explicitly exempt

Tamed creatures keep their faction, which is what makes a tamed Bjorn read as
threat 5 to a goblin patrol — desirable. But it also means a tamed wolf would
inherit `Apex` and run its own fear check against a Bjorn and abandon you.

So the fear check needs a hard gate before anything else:

```csharp
if (Chr.IsTamed()) return;   // tamed creatures are always fearless
```

Same for player summons — `SummonedSeeker_TW` is already forced to `Players`,
and MagicPlugin's summons set their own faction in `SummonHelper::Create`.
Gating on `IsTamed() || faction == Players` covers both.
