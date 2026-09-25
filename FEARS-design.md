# Fear & courage — design for review

Nothing implemented. This is the map to argue with before any code.

---

## 1. These are two different mechanics

Worth keeping apart, because they need different machinery:

| | **Fear** | **Courage** |
|---|---|---|
| Question | "do I run from *that kind of thing*?" | "do I run because it's *winning*?" |
| Keyed on | faction pair, **directional** | situation — how many, how hurt |
| Your example | goblin flees a bear | prowler won't touch a lox with two loxes nearby |
| Cost | small | needs ally-counting + caching |

Fear is the cheaper, bigger win. Courage is the one that makes a prowler look
like it's thinking. I'd build fear first and courage second, but the design
below covers both.

**Fear is directional and relations are not.** `[Relations]` deliberately
normalises `A : B` and `B : A` into one entry, because hostility is mutual.
Fear isn't: the goblin fears the bear, the bear does not fear the goblin. So
`[Fears]` needs its own un-normalised table.

**A feared pair must still be `Enemy`.** A creature has to acquire the target
before it can decide to run from it. Fear changes the *response*, not the
targeting.

---

## 2. The granularity problem

Every goblin you have sits in `PlainsMonsters`:

```
Deathsquito  Goblin  Goblin_Gem  GoblinArcher  GoblinBrute
GoblinBrute_Hildir  GoblinBruteBros  GoblinBruteBros_nochest
GoblinDeepNorth  GoblinMage_TW  GoblinShaman  GoblinShaman_Hildir
GoblinShaman_Hildir_nochest  Mimic_Plains_TW
```

You want the rabble to break and the brutes to hold. A faction-level rule
can't say that. Two options:

**(a) Split the faction** — `GoblinRabble` + `GoblinElite`. Clean, but it's a
new faction for every such case, and there will be more (Greydwarf vs
Greydwarf_Elite, Draugr vs Draugr_Elite, Fenring vs Fenring_Cultist...).

**(b) A per-creature `fearless = true` override.** Faction sets the rule, the
creature opts out. Four lines covers the brutes, and it reuses the precedence
we already have: creature beats faction.

**Recommending (b).** It also handles the cases where fear is simply
nonsensical — a Mimic is a chest that ambushes you, it should never flee; a
Deathsquito is an insect with no self-preservation.

---

## 3. The threat ladder

Rather than enumerating every pair, rank things and fear what's above you.

| Tier | Who | Fears |
|---|---|---|
| 0 | `Ignored` — insects, small birds | *(untargetable; moot)* |
| 1 | `Grazers` — deer, hare, sheep, seals | everything (already flee via faction) |
| 2 | `Swine` — boar, razorback | predators and up |
| 3 | `Predator` — fox, neck | Apex, Feral, Players, Boss |
| 4 | **Rabble** — greydwarf, greyling, skeleton, small goblins, fenring, dvergr | Apex, Feral, Boss |
| 5 | `Apex` — bears, wolves, prowlers | Feral, Boss |
| 6 | `Feral` — Bjorn, Unbjorn, Troll, TrollFrost | Boss only |
| 7 | `Boss` | nothing |

Rule of thumb: **fear what is two or more tiers above you.** Tier 4 fearing
tier 5 is the one deliberate exception — a greydwarf breaking from a bear is
exactly the flavour you asked for.

Your "bears" span two tiers on purpose: `BlackBear_TW` / `GrizzlyBear_TW` are
`Apex`, while `Bjorn` and `Unbjorn` are `Feral`. Fearing **both** is what makes
"goblins flee bears" mean what you meant. And because a tamed creature keeps
its faction, **a tamed Bjorn is feared exactly like a wild one** — no extra
rule needed.

---

## 4. Proposed `[Fears]` table

```ini
[Fears]
# A : B = true   ->  A runs from B instead of fighting it.
# Directional. The pair must also be Enemy under [Relations].

# --- small predators know their place ---------------------------------
Predator : Apex       = true      # fox scatters from a bear or wolf
Predator : Feral      = true
Predator : Players    = true      # a fox bolts when you round a corner

# --- livestock ---------------------------------------------------------
Swine : Predator      = true
Swine : Apex          = true
Swine : Feral         = true

# --- the rabble break when something big turns up ----------------------
PlainsMonsters    : Apex  = true  # goblins vs bears  <-- your ask
PlainsMonsters    : Feral = true  # goblins vs bjorns and trolls
ForestMonsters    : Apex  = true  # greydwarves vs bears
ForestMonsters    : Feral = true
MountainMonsters  : Feral = true  # fenrings vs trolls/bjorns
Dverger           : Feral = true

# --- apex predators answer to almost nothing ---------------------------
Apex : Feral          = true      # even a bear gives a troll room

# --- everything fears a boss (your #7) ---------------------------------
* : Boss              = true
```

`* : Boss` is a wildcard — one line instead of fourteen. It's also the only
way #7 works at all: prey already flee bosses through the faction table, but
monsters would otherwise *swarm* Bonemass.

### Deliberately NOT afraid

- **`Undead`** — skeletons and draugr have no survival instinct. Thematically
  they should walk into a bear.
- **`Demon`** — same; charred and surtlings are not scared of wildlife.
- **`MistlandsMonsters`** — seekers are hive insects, they don't rout.
- **`SeaHunters` / `Orca`** — apex in their own domain.
- **`Megafauna`** — Moose and Lox don't run from anything.

---

## 5. Per-creature `fearless = true`

```ini
# Goblin elites hold the line while the rabble breaks.
[GoblinBrute]
fearless = true
[GoblinBrute_Hildir]
fearless = true
[GoblinBruteBros]
fearless = true
[GoblinBruteBros_nochest]
fearless = true

# Mindless: an insect and a chest with teeth.
[Deathsquito]
fearless = true
[Mimic_Plains_TW]
fearless = true
[Mimic_BF_TW]
fearless = true
[Mimic_Swamp_TW]
fearless = true
[Mimic_Mountain_TW]
fearless = true
[Mimic_Mistlands_TW]
fearless = true

# Elites and constructs don't break.
[Greydwarf_Elite]
fearless = true
[ObsidianGolem_TW]
fearless = true
[StoneGolem]
fearless = true
[TentaRoot_wild]
fearless = true      # it is a plant, it cannot run
[BeeQueen]
fearless = true
```

---

## 6. Courage — the prowler

Separate mechanic, same `UpdateAI` hook:

> Before committing to a target, weigh it. If the target has **N or more
> allies** within a radius, or is **already fighting something else**, look for
> an easier meal instead.

Your refinement was better than my first pass: "is this target *busy*" reads
as smarter than counting species, and it's cheaper. A lox already tangling
with a goblin camp is visibly occupied.

```ini
[Courage]
# Faction = allies-nearby threshold that makes it back off. 0 = fearless.
Predator = 2
Apex     = 3        # a prowler will take a lone lox, not a herd
```

Needs ally-counting via `Character.GetAllCharacters()` cached on a ~1s timer —
that's the bulk of the work, which is why it's phase two.

---

## 7. Open questions

1. **`Ulv`** — currently `MountainMonsters`, so it'd fear Feral. It's a
   pack wolf; fearless, or let it break?
2. **`Fenring_Cultist`** — cultists are fanatics. Fearless while plain
   Fenrings flee?
3. **`Dverger`** — armed and organised. Should they really break from a
   troll, or hold?
4. **`Greydwarf_Shaman` / `GoblinShaman`** — support casters. Flee (they're
   squishy) or hold (they're committed)?
5. **`Hatchling`** — young drake, `MountainMonsters`. Fear Feral?
6. **`Neck`** — in `Predator`, so it'd fear Players. A neck fleeing you is
   arguably correct, but they're famously suicidal. Fearless?
7. **Boss wildcard** — should `Ignored` be exempt? They're untargetable
   anyway, so it's moot, but worth confirming you want no exceptions.
