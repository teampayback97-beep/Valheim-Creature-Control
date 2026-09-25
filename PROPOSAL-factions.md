# CreatureControl — faction & behaviour audit

Cross-examined all 243 entries in `Azumatt.FactionAssigner.yml` against the
behaviour system. **Nothing here is applied yet.** Decisions from review are now locked in (see §7);
this is the build spec.

---

## 1. How this stays compatible with FactionAssigner

They layer, they don't compete:

- **FactionAssigner stays the base layer.** Its yaml is not edited at all.
- **CreatureControl is the override layer.** It only names creatures whose
  faction or behaviour we're actually changing.
- Where both name a creature, CreatureControl wins deterministically
  (`[HarmonyAfter("Azumatt.FactionAssigner")]` on the same `Character.Awake`
  postfix it uses).
- Disable CreatureControl and everything reverts to FactionAssigner exactly
  as it is today. No migration, no lost work.

**Hard constraint:** FactionAssigner can only emit the 14 vanilla enum values.
Any creature we move into a custom faction *must* be assigned by
CreatureControl. That's the dividing line between the two files.

---

## 2. The structural problem

Vanilla `IsEnemy` returns **false whenever both creatures share a faction** —
before any other logic. So two creatures in the same faction can *never*
interact, no matter what else we configure.

That silently blocks predation in two big piles:

| Pile | Predators stuck in it | Prey stuck with them |
|---|---|---|
| `AnimalsVeg` (41) | BlackBear_TW, BlackBearCub_TW, GrizzlyBear_TW, GrizzlyBearCub_TW, Fox_TW, FoxCub_TW | Deer, Deer_White, Hare, Sheep_TW, Lamb_TW, Chicken, Hen |
| `SeaMonsters` (17) | SA_WhiteShark, SA_TigerShark, SA_BlueShark, SA_HammerHeadShark, SA_Crocodile, SA_HumboldtSquid, SA_Orca, Shark_TW, Serpent | SA_*Turtle (5), SA_Dolphin, SA_RightWhale, SA_WhaleShark |

A grizzly cannot touch a deer. A white shark cannot touch a turtle. Splitting
these two piles is the single change that makes a food chain possible.

---

## 3. Inconsistencies found

Independent of the tier design — these look like plain mistakes:

| # | Creature | Issue |
|---|---|---|
| 1 | `Razorback_TW`, `RazorbackPiggy_TW` | `ForestMonsters`, while `Boar`/`Boar_piggy` are `AnimalsVeg`. Same animal type, opposite teams — they currently treat each other as enemies. |
| 2 | `Asksvin_hatchling` | `Undead`, while adult `Asksvin` is `Demon`. Parent and young are hostile to each other. |
| 3 | `SummonedSeeker_TW` | `MistlandsMonsters`, while every other player summon (`SummonedGolem_TW`, `SummonedSurtling_TW`, the `*_spiritcaller` line) is `Players`. If the player summons it, it is currently hostile to the player. |
| 4 | `FrostWisp`, `Frysling` | `TrainingDummy`, which is hostile **only** to Players and ignored by everything else. Fine if they're meant to attack you; wrong if they're ambient. **Needs your call.** |
| 5 | Sleeping variants | `Bjorn_sleeping`, `Troll_sleeping`, `Draugr_sleeping`, `Draugr_Elite_sleeping`, `Draugr_Ranged_sleeping`, `Ghost_sleeping`, `Morgen_NonSleeping` must carry identical rules to their awake forms or behaviour flips on wake. |
| 6 | Young/cubs | `BlackBearCub_TW`, `GrizzlyBearCub_TW`, `FoxCub_TW`, `Wolf_cub`, `ProwlerCub_TW`, `Lox_Calf`, `Moose_calf`, `Boar_piggy`, `RazorbackPiggy_TW`, `Seal_Pup`, `Asksvin_hatchling` — each must match its parent's faction, with shorter ranges. |

---

## 4. Proposed factions

Eight new, all in the 100+ custom range.

| id | Faction | Members |
|---|---|---|
| 100 | **Ignored** | ButterFly1-6, Beetle1-4, all 10 AA_* birds, **FrostWisp**, **Frysling** |
| 101 | **Grazers** | Deer, Deer_White, Hare, Sheep_TW, Sheep_Testing, Lamb_TW, Chicken, Hen, Seal, Seal_Pup |
| 102 | **Swine** | Boar, Boar_piggy, Razorback_TW, RazorbackPiggy_TW |
| 103 | **Megafauna** | Moose, Moose_calf, Lox, Lox_Calf |
| 104 | **Predator** | Fox_TW, FoxCub_TW, **Neck** |
| 105 | **Apex** | BlackBear_TW, BlackBearCub_TW, GrizzlyBear_TW, GrizzlyBearCub_TW, Wolf, Wolf_cub, **Prowler_TW**, **ProwlerCub_TW** |
| 106 | **Feral** | Bjorn, Bjorn_sleeping, Troll, Troll_sleeping, TrollFrost, Unbjorn |
| 107 | **SeaLife** | SA_BlueTurtle, SA_GreenTurtle, SA_RedTurtle, SA_YellowTurtle, SA_LeatherbackSeaTurtle, SA_Dolphin, SA_RightWhale, SA_WhaleShark |
| 108 | **SeaHunters** | SA_WhiteShark, SA_TigerShark, SA_BlueShark, SA_HammerHeadShark, SA_Crocodile, SA_HumboldtSquid, Shark_TW |
| 109 | **Orca** | SA_Orca - apex pack hunter, eats everything in the sea but never its own |

`Serpent` stays `SeaMonsters` — it's a boss-tier encounter, not part of the
food chain. `SeaHunters : SeaMonsters = Friendly` keeps them from brawling.

Everything else — all 41 Undead, 30 Boss, 16 Demon, Dverger, Mistlands,
DeepNorth, the Goblin line, Greydwarves, Skeletons, Mimics, player summons —
**stays exactly where FactionAssigner has it.** Roughly 160 of 243 creatures
are untouched.

---

## 5. Relations

```ini
[Defaults]
Ignored    = Friendly
Grazers    = Friendly
Swine      = Friendly
Megafauna  = Friendly
Predator   = Friendly
Apex       = Friendly
Feral      = Enemy
SeaLife    = Friendly
SeaHunters = Friendly
Orca       = Enemy        # hunts all sea life; pod stays intact via the
                          # absence of an "Orca : Orca" line below

[Relations]
# --- nothing ever targets the small stuff -------------------------------
# (also enforced in code, so a Feral default of Enemy can't override it)
Ignored : Players   = Friendly
Ignored : Feral     = Friendly

# --- land food chain ----------------------------------------------------
Predator : Grazers  = Enemy      # fox hunts deer, hare, chickens
Predator : Swine    = Enemy      # and piglets
Apex     : Grazers  = Enemy
Apex     : Swine    = Enemy
Apex     : Predator = Enemy      # bears run foxes off
Apex     : Players  = Enemy

# --- ferals fight everything, including their own kind ------------------
Feral : Feral       = Enemy      # needs the explicit-relation code change
Feral : Players     = Enemy

# --- megafauna: hunted by nobody, hunts nobody, wrecks anything close ---
Megafauna : Players        = Friendly   # territorial via range, not faction
Megafauna : PlainsMonsters = Enemy      # goblin camps do fight lox
Megafauna : Feral          = Enemy

# --- swine defend themselves but start nothing --------------------------
Swine : Players = Friendly

# --- ocean --------------------------------------------------------------
SeaHunters : SeaLife     = Enemy
SeaHunters : Players     = Enemy
SeaHunters : SeaMonsters = Friendly
SeaLife    : Players     = Friendly

# --- orcas: feral toward the sea, loyal to the pod ----------------------
Orca : SeaLife     = Enemy
Orca : SeaHunters  = Enemy      # real orcas do hunt great whites
Orca : Players     = Enemy
Orca : SeaMonsters = Enemy      # includes Serpent - flag if too much
Orca : Ignored     = Friendly
# deliberately NO "Orca : Orca" line - same-faction defaults to Friendly,
# which is exactly pack behaviour. Contrast with Feral, which DOES name
# itself and therefore turns on its own kind.
```

---

## 6. Behaviour & senses

Faction-keyed, so the rule reads as the design rather than 200 copied lines.

```ini
# --- animals value their lives -----------------------------------------
[@Grazers]
fleeIfLowHealth = 0.25
behavior        = Passive

[@Swine]
fleeIfLowHealth = 0.25
behavior        = Neutral

[@Predator]
fleeIfLowHealth = 0.25

# --- apex predators and ferals do not ----------------------------------
[@Apex]
fleeIfLowHealth = 0
[@Feral]
fleeIfLowHealth = 0

# --- monsters never break off ------------------------------------------
[@ForestMonsters]
fleeIfLowHealth = 0
[@Undead]
fleeIfLowHealth = 0
[@Demon]
fleeIfLowHealth = 0
[@PlainsMonsters]
fleeIfLowHealth = 0
[@MountainMonsters]
fleeIfLowHealth = 0
[@MistlandsMonsters]
fleeIfLowHealth = 0
[@DeepNorth]
fleeIfLowHealth = 0
[@Dverger]
fleeIfLowHealth = 0
[@SeaMonsters]
fleeIfLowHealth = 0
[@SeaHunters]
fleeIfLowHealth = 0

# --- territorial megafauna: Moose is the baseline ----------------------
[@Megafauna]
behavior         = Aggressive
viewRange        = 8
hearRange        = 6
alertRange       = 20
enableHuntPlayer = false
fleeIfLowHealth  = 0        # decided: too big to rout
maxChaseDistance = 15

[Lox]
maxChaseDistance = 6        # hulking herbivore, commits to almost nothing
[Lox_Calf]
maxChaseDistance = 6
```

### Suggested per-creature overrides

```ini
# Ambushers: can't see you coming, but you're in trouble once adjacent.
[Mimic_BF_TW]
viewRange = 4
hearRange = 3
[Mimic_Swamp_TW]
viewRange = 4
hearRange = 3
[Mimic_Mountain_TW]
viewRange = 4
hearRange = 3
[Mimic_Plains_TW]
viewRange = 4
hearRange = 3
[Mimic_Mistlands_TW]
viewRange = 4
hearRange = 3

# Young animals are skittish and shouldn't lead fights.
[BlackBearCub_TW]
maxChaseDistance = 8
fleeIfLowHealth  = 0.5
[GrizzlyBearCub_TW]
maxChaseDistance = 8
fleeIfLowHealth  = 0.5
[FoxCub_TW]
fleeIfLowHealth  = 0.6
[ProwlerCub_TW]
fleeIfLowHealth  = 0.6
[Wolf_cub]
fleeIfLowHealth  = 0.5
```

---

## 7. Decisions (locked)

| # | Decision |
|---|---|
| 1 | `FrostWisp`, `Frysling` -> **Ignored** |
| 2 | `Neck` -> **Predator** (with Fox). `Prowler_TW` + `ProwlerCub_TW` -> **Apex** |
| 3 | `Seal`, `Seal_Pup` -> **Grazers** (prey) |
| 4 | `Ulv` stays `MountainMonsters` with Fenring - **already correct, no change**. Dropped from Apex; it's cave-bound and outside the food chain |
| 5 | Megafauna does **not** flee. `fleeIfLowHealth = 0` for Moose and Lox both |
| 6 | Anything tamed or summoned is allied to Players, never hostile. `SummonedSeeker_TW` -> `Players` |
| 7 | Every creature **fears** Boss - see the caveat below |

### The #7 caveat

Fear is not a faction concept, so this splits by AI component:

- **AnimalAI creatures** (Grazers, and prey generally) - marking Boss an enemy
  makes them run. Works with factions alone, today.
- **MonsterAI creatures** (everything else) - marking Boss an enemy makes them
  *attack* it. Greydwarves would swarm Bonemass.

So #7 only half-works without the **Fears** system, which converts "this is an
enemy" into "flee from it" for MonsterAI. That moves Fears from optional polish
to required scope.

```ini
# Works now - prey scatter from bosses
Grazers   : Boss = Enemy
Swine     : Boss = Enemy
Ignored   : Boss = Friendly     # bugs stay untargetable, even by bosses

# Needs the Fears system, or these creatures attack instead of fleeing
[Fears]
Predator   : Boss = true
Apex       : Boss = true
Feral      : Boss = true
Megafauna  : Boss = true
SeaLife    : Boss = true
SeaHunters : Boss = true
# ...and the vanilla monster factions likewise
```

## 7b. Tamed creatures vs their wild counterparts

**Requirement:** every tamed creature can fight its own wild kind - tamed wolf
vs wild wolf, tamed bear vs wild bear - for all tameable creatures.

**What blocks it:** vanilla `IsEnemy` checks the shared creature "group" and
returns false *before* it reaches its tamed logic:

```
groups match            -> return false     // runs first, vetoes everything
either tamed, not both,
other isn't Player/Dvergr -> return true     // never reached
```

`Character.SetTamed` does not modify `m_group` (verified in IL), so a tamed
wolf keeps the wild wolves' group string. The group guard is the only blocker.

**Rule:** when exactly one of the pair is tamed and they share a non-empty
group, the group veto does not apply - a tamed creature has left its old pack.
Then vanilla's own tamed outcome stands.

Generic, so it covers every tameable creature including ones added later.
No per-creature config, and it replaces the need for `Feral : Feral = Enemy`
to carry this case.

| Pair | Result |
|---|---|
| tamed wolf vs wild wolf | enemy |
| tamed wolf vs tamed wolf | friendly (both-tamed check precedes it) |
| tamed wolf vs player | friendly (unconditional) |
| wild wolf vs wild wolf | friendly - packs stay intact |

Rejected alternative: clearing `m_group` on tamed creatures. `m_group` also
drives `HaveFriendsInRange`, so blanking it would stop tamed wolves
coordinating with each other.

---

## 8. Code still required

| Change | Size | Needed for |
|---|---|---|
| Explicit relation overrides the group guard | ~6 lines, drafted | `Feral : Feral = Enemy` |
| New config keys: `enableHuntPlayer`, `maxChaseDistance`, `alertRange`, `fleeIfLowHealth` | small | Lox/Moose/morale |
| `[@Faction]` sections + precedence | moderate | keeping this file readable |
| `Ignored` hard short-circuit in `IsEnemy` | ~10 lines | bugs untargetable even by Ferals |
| Faction-keyed lookup resolved after FactionAssigner | small | correctness |
| Group veto ignored when exactly one of the pair is tamed | ~8 lines | §7b, tamed vs wild counterparts |
| **Fears system** - `[Fears]` table + force `BaseAI.Flee()` when the target's faction is feared | ~40-60 lines | decision #7 for every MonsterAI creature |

### Suggested build order

1. Config keys + `[@Faction]` sections + explicit-relation fix + `Ignored`
   short-circuit. Delivers the whole tier map, Lox/Moose, and animal morale.
2. Fears system. Delivers #7 properly, and the Prowler's nerve afterwards.
3. Diagnostic logging (spawn table + AI type per creature) before any spawn work.
