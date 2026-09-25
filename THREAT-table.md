# Threat table — baseline: Fuling = 1.0

## Why Fuling

- Sits at the median of the whole 243-creature roster — roughly as much above a
  greydwarf as it is below a lox.
- Humanoid melee with no gimmick: no ranged, no heal, no charge, no poison. A
  gimmick creature as the unit means every other number carries its distortion.
- It is the creature you already reason about in groups ("3 fulings per lox"),
  so the number matches your intuition instead of fighting it.
- It makes your two anchors whole numbers: Lox = 3, Bjorn = 5.

Read every value below as **"how many fulings is this worth in a fight."**

---

## The maths is flat

Each side sums its threat. A creature commits when its side matches the other.

```
commit if  myThreat >= theirThreat
```

That is linear, so ratios hold at any scale:

| Scene | Maths | Fulings needed |
|---|---|---:|
| 1 lox | 3 | 3 |
| 2 loxes | 6 | 6 |
| 3 loxes | 9 | 9 |
| 1 bjorn | 5 | 5 |
| 2 bjorns | 10 | 10 |

The earlier "4 for two loxes" was a calibration error, not a flaw in the model —
fuling was set to 1.5 against lox 3, which is 2:1. With fuling at 1.0 the ratio
you asked for falls straight out, and mixed groups still resolve sensibly
(a lox + a boar = 3.25, so four fulings).

`fearless = true` skips the check entirely. That is the only other knob for
creatures — no boldness multipliers, no per-pair entries.

---

## Threat values

### Forest

| Creature | Threat |
|---|---:|
| `Greyling` | 0.15 |
| `Greydwarf` / `Greydwarf_Frozen` | 0.35 |
| `Greydwarf_Shaman` (+ `_Frozen`) | 0.5 |
| `GreydwarfMage_TW` | 0.5 |
| `Greydwarf_Elite` | **1.0** · fearless |

### Plains

| Creature | Threat |
|---|---:|
| `Goblin` / `Goblin_Gem` / `GoblinDeepNorth` | **1.0** |
| `GoblinArcher` / `GoblinMage_TW` | 1.0 |
| `GoblinShaman` (+ Hildir variants) | 1.0 |
| `GoblinBrute` (+ Hildir, Bros, Bros_nochest) | 2.5 · fearless |
| `Deathsquito` | 0.6 · fearless |
| `Tick` | 0.5 |

### Mountain

| Creature | Threat |
|---|---:|
| `Fenring` | 1.0 |
| `FenringMage_TW` | 1.0 |
| `Fenring_Cultist` (+ Hildir variants) | 2.0 · fearless |
| `Ulv` | 0.75 · fearless |
| `Hatchling` | 0.8 |
| `StoneGolem` | 3.0 · fearless |
| `Wolf` | 1.0 |

### Swamp / Undead / Demon — all fearless

| Creature | Threat |
|---|---:|
| `Skeleton` | 0.35 |
| `Draugr` | 0.6 |
| `Draugr_Elite` | 1.5 |
| `Wraith` | 1.0 |
| `BlobMork` | 1.2 |
| `Abomination` | 4.0 |
| `Charred_Twitcher` | 0.8 |
| `Charred_Archer` | 1.5 |
| `Charred_Melee` | 2.0 |
| `Asksvin` | 1.5 |
| `Asksvin_hatchling` | 0.5 |
| `Morgen` | 5.0 |

### Mistlands — all fearless

| Creature | Threat |
|---|---:|
| `Seeker` | 1.0 |
| `SeekerBrute` | 3.5 |
| `Gjall` | 3.5 |
| `Dverger` | 1.0 |
| `Tick` | 0.5 |

### Animals

| Creature | Threat |
|---|---:|
| `Neck` | 0.15 |
| `Deer` / `Deer_White` / `Hare` / `Sheep_TW` | 0.2 |
| `Boar` / `Razorback_TW` | 0.25 |
| `Fox_TW` | 0.3 |
| `Volture` | 1.2 |
| `BlackBear_TW` / `GrizzlyBear_TW` | 1.5 |
| `Prowler_TW` | **3.0** |
| `Moose` | 3.0 · fearless |
| `Lox` | **3.0** · fearless |
| `Bjorn` / `Unbjorn` | **5.0** · fearless |
| `Troll` | 5.0 · fearless |
| `TrollFrost` | 6.0 · fearless |
| `JotunWarrior` | 4.0 |
| `Elaking` | 3.5 |
| `ShadowPerson` | 2.5 |

### Sea

| Creature | Threat |
|---|---:|
| `SA_Dolphin` / turtles | 0.3 |
| `SA_RightWhale` / `SA_WhaleShark` | 1.5 |
| `SA_BlueShark` / `SA_HammerHeadShark` | 1.0 |
| `SA_TigerShark` / `SA_WhiteShark` / `Shark_TW` | 1.5 |
| `SA_Crocodile` / `SA_HumboldtSquid` | 1.2 |
| `SA_Orca` | 3.0 · fearless |
| `Serpent` | 3.5 · fearless |

### Fixed

| Creature | Threat |
|---|---:|
| `Boss` faction (all) | 50 · fearless |
| `Ignored` faction (all) | 0 |
| All `Mimic_*` | 1.0 · fearless |
| `ObsidianGolem_TW` | 3.0 · fearless |
| `TentaRoot_wild` / `BeeQueen` | — · fearless |

**Young:** see the Babies section — threat 0, always flee.

---

## Faction defaults

For anything not named above, the faction supplies the number.

| Faction | Threat | Fearless |
|---|---:|---|
| `Ignored` | 0 | — |
| `Grazers` | 0.2 | no |
| `SeaLife` | 0.4 | no |
| `Swine` | 0.25 | no |
| `Predator` | 0.3 | no |
| `ForestMonsters` | 0.35 | no |
| `Undead` | 0.6 | **yes** |
| `Demon` | 1.0 | **yes** |
| `PlainsMonsters` | 1.0 | no |
| `MountainMonsters` | 1.0 | no |
| `MistlandsMonsters` | 1.0 | **yes** |
| `Dverger` | 1.0 | no |
| `SeaHunters` | 1.2 | no |
| `Apex` | 1.5 | no |
| `DeepNorth` | 1.5 | no |
| `Megafauna` | 3.0 | **yes** |
| `Orca` | 3.0 | **yes** |
| `SeaMonsters` | 3.5 | **yes** |
| `Feral` | 5.0 | **yes** |
| `Boss` | 50 | **yes** |

---

## Spot-checks

| Scene | Maths | Result |
|---|---|---|
| Fulings vs 1 lox | need 3.0 | **3 fulings** |
| Fulings vs 2 loxes | need 6.0 | **6 fulings** |
| Fulings vs 1 bjorn | need 5.0 | **5 fulings** |
| Greylings vs 1 bjorn | need 5.0 / 0.15 | 34 — never happens |
| Greydwarfs vs 1 bjorn | need 5.0 / 0.35 | 15 — a full camp |
| 1 fox vs 1 deer | 0.3 vs 0.2 | hunts |
| 1 fox vs 1 boar | 0.3 vs 0.25 | hunts, barely |
| 1 fox vs boar + piggy | 0.3 vs 0.375 | leaves it |
| 3 wolves vs 1 bjorn | 3.0 vs 5.0 | keeps distance |
| 5 wolves vs 1 bjorn | 5.0 vs 5.0 | commits |
| 1 prowler vs 1 lox | 3.0 vs 3.0 | commits |
| 1 prowler vs 2 loxes | 3.0 vs 6.0 | declines |
| 1 prowler vs lox + calf | 3.0 vs 3.0 | commits — the calf adds nothing |
| 1 prowler vs lone calf | 3.0 vs 0 | commits, calf bolts |
| 2 prowlers vs 2 loxes | 6.0 vs 6.0 | commits |
| Anything vs a boss | vs 50 | flees |

---

## Babies

A baby is **threat 0** and **always flees**. It contributes nothing to its
herd's side and it never commits to anything, so the fear check exits before it
runs:

```csharp
if (rule.Baby) { Flee(); return; }
```

Threat 0 is the interesting half. A lox with a calf is worth 3.0, not 4.5, so a
prowler that would take a lone lox still takes it — and predators preferring the
young is the correct outcome, not a bug. A calf on its own is worth nothing at
all, so everything hunts it.

"Except for their mother" needs no rule. Same-faction pairs are Friendly in
`[Relations]`, so a calf never registers its own kind as an enemy and has
nothing to flee from. The flee is only ever aimed at things it already sees as
hostile.

**The explicit list** — matched by name, not by suffix:

```
Lox_Calf            Moose_calf          Boar_piggy          RazorbackPiggy_TW
Lamb_TW             Chicken             Seal_Pup            Wolf_cub
FoxCub_TW           ProwlerCub_TW       BlackBearCub_TW     GrizzlyBearCub_TW
Asksvin_hatchling
```

Two traps this list avoids:

- **`Hatchling` is not a baby.** It is the vanilla mountain drake, a fully
  grown enemy. Suffix matching on `hatchling` would have made every drake in
  the Mountains flee on sight.
- **`Asksvin_hatchling` overrides its faction.** It is `Demon`, and Demon is
  fearless — but baby wins. Its parents already ignore it (same faction), which
  was the point of moving it to Demon in the first place.

The per-creature `fleeIfLowHealth` values already set for cubs (0.5-0.6) become
redundant and can go; a baby that always flees never gets to a health check.

---

## Stars change everything (CLLC)

Creature Level & Loot Control is running with **Maximum stars = Five**, +100%
health and +50% damage per star. A 2-star fuling is not worth 1.0 against a
0-star lox, so threat has to scale with level or the whole table lies.

```
effectiveThreat = baseThreat x (1 + 0.6 x stars)
```

| Stars | Multiplier | Fuling | Lox |
|---:|---:|---:|---:|
| 0 | 1.0 | 1.0 | 3.0 |
| 1 | 1.6 | 1.6 | 4.8 |
| 2 | 2.2 | 2.2 | 6.6 |
| 3 | 2.8 | 2.8 | 8.4 |
| 5 | 4.0 | 4.0 | 12.0 |

`Character.GetLevel()` returns 1 for a no-star creature, so stars = level - 1.
0.6 is the knob — lower it if starred creatures end up too timid.

---

## The player

Same currency. You are a creature with a threat value; the difference is that
yours moves as you gear up.

```
playerThreat = 0.3 + sqrt(bodyArmor) x 0.25      capped at 2.8
```

`Character.GetBodyArmor()` is public and virtual, so modded armor — Southsil,
Hugo's Armory, EpicValheims — feeds in automatically with no per-item table.

| Gear | Armor | Threat | Greydwarfs to commit | Fulings |
|---|---:|---:|---:|---:|
| Naked | 0 | 0.30 | **1** | 1 |
| Rags | 6 | 0.91 | 3 | 1 |
| Leather | 7 | 0.96 | 3 | 1 |
| Bronze | 20 | 1.42 | 5 | 2 |
| Iron | 32 | 1.71 | 5 | 2 |
| Silver | 46 | 2.00 | 6 | 2 |
| Padded | 70 | 2.39 | 7 | 3 |
| Modded end-game | 120+ | 2.80 (cap) | **8** | 3 |

The two numbers you asked for land exactly: **naked in the Black Forest, one
greydwarf comes anyway**, and **the cap is 8 greydwarfs**, which is a full camp
and more than ever spawns loose.

### Why the curve is a square root

Armor climbs roughly linearly through the tiers, so linear threat would make
the late game empty. `sqrt` flattens it: bronze to iron is +0.29, silver to
padded is +0.39, and everything past padded is worth less than a tenth of a
greydwarf. You get a visible reward for the first real armor set and almost
nothing for the last one, which is the falloff you asked for.

### Why this does not make the late game quiet

Fear only gates **starting a fight**. It never stops:

- **Retaliation.** Hit anything and it fights, whatever the numbers.
- **Camp and structure defence.** `m_attackPlayerObjects` is separate.
- **Joining a fight already running.** The first commit opens the door.
- **Bosses**, which are fearless at 50.

So in padded armour a lone fuling patrol gives you room, and a fuling camp
still comes at you — because a camp is four or more. That is the behaviour you
described wanting, with the annoyance removed rather than the danger.

If it still reads as too quiet, `playerThreatScale` (default 1.0) turns the
whole curve down without touching a single creature value.

### Following tames

A pet at your side adds to your side, at **50%**:

```
sideThreat = playerThreat + 0.5 x sum(threat of following tames)
```

| Escort | Added | Total in padded | Fulings to commit |
|---|---:|---:|---:|
| none | 0 | 2.39 | 3 |
| 2 tamed wolves | 1.0 | 3.39 | 4 |
| 1 tamed Bjorn | 2.5 | 4.89 | 5 |
| 2 tamed Bjorns | 5.0 | 7.39 | 8 |

At 100% a single Bjorn would put you at 7.39 and clear the Plains around you,
which is the failure mode you were worried about. 50% keeps the escort
meaningful — walking with a bear visibly thins the small stuff — without
turning it off. It is a config knob either way.

Tames themselves stay **fearless** and never run this check; the gate is the
first line of the routine:

```csharp
if (Chr.IsTamed() || Chr.m_faction == Character.Faction.Players) return;
```

### Counting radius

20m, matching the default `alertRange`, re-evaluated about every 2s. Sides are
built from `Character.GetAllCharacters()` filtered by distance and by whether
each one is an enemy of the other side — cached per tick so a camp of twelve
does not run 144 comparisons.
