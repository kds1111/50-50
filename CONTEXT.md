# 50-50 — domain context

An arena soccer game on skateboards. Two players compete in a concrete skatepark-stadium; tricks build a **pending bank** of points, and a **goal** is what cements that bank into your score. Trick without scoring and the points evaporate; score without tricking and the goal is worth little. The tension between the two is the game.

The name is both a skate trick (a 50-50 grind) and the odds on a gamble you're holding open.

## Glossary

Use these terms exactly. If a concept you need isn't here, either you're inventing language the project doesn't use, or there's a real gap worth adding.

**Board** — the thing a player rides. A PhysX rigidbody with raycast suspension. Whether it is a skateboard or a hoverboard is an open decision; "board" is the term either way, so code and docs never have to be rewritten when it lands.

**Rider** — the player character on the board. Distinct from the board because they rotate independently: a shuvit spins the board under a stationary rider, a 180 turns both. Trick classification tracks board rotation and rider yaw separately for exactly this reason.

**Trick** — a named rotation of the board performed while airborne, detected by classifying the board's accumulated rotation on landing rather than by the player inputting a gesture. A rotation that matches no name in the table is credited as a generic **air**, never as a guessed name.

**Airborne** — the state in which rotation accumulates toward a trick. Gated by an explicit predicate, not inferred; leaving it is what triggers classification.

**Landing** — the resolution of a trick when the board returns to a surface. A clean binary: you **land** it or you **bail**. A named trick lands if it ran its full duration before touchdown and bails if it did not ([#6](https://github.com/kds1111/50-50/issues/6)); a spin can never bail. *Sketchy* was a third grade in the original design and was struck on [#8](https://github.com/kds1111/50-50/issues/8) — the commitment model produces a binary, and a middle grade would have needed its own threshold, payout fraction and feedback for nothing.

**Grind** — riding along a rail or a ledge edge, locked onto it: the board is snapped onto a grind line rather than balanced on geometry by the physics. Named by the board's angle to the rail at lock-on — **50-50** along it, **Boardslide** across it. Locking on counts as a landing. Pays the bank per second past a minimum time, capped per grind. Popping out or riding off the end banks it; stalling or being hit by the ball is a fall, which is a bail. Under the minimum it is a **graze**: nothing earned, nothing lost ([#12](https://github.com/kds1111/50-50/issues/12)). Lip tricks — stalls on coping — are out of the slice.

**Bail** — a failed landing: a named trick unfinished at touchdown, or a fall from a grind. Wipes the pending bank entirely ([#8](https://github.com/kds1111/50-50/issues/8)), then a knockdown in place: the board keeps its momentum and slides, the rider is down and cannot steer, and the whole greybox **flashes** — flashing means down and untouchable ([#6](https://github.com/kds1111/50-50/issues/6), [#25](https://github.com/kds1111/50-50/issues/25)). The respawn puts you back **where you lost it**: on clear ground at or beside the point the bail fired, never where the board slid to. A [[safe point]] is the fallback.

**Safe point** — a spot placed in the arena as somewhere a player can be returned to standing: clear flat ground, off the obstacles, with its own facing. The arena must define them ([#9](https://github.com/kds1111/50-50/issues/9)). The **fallback** for a bail ([#25](https://github.com/kds1111/50-50/issues/25)): used when nothing clear is found within the search, and used immediately when the fall was out of bounds or inside a goal, where "beside where you fell" is a wrong answer however clear the ground is.

**Bank** (or **pending bank**) — a **multiplier** accumulated from landed tricks but not yet scored. Starts at 1.00; each landed trick adds to it, and it clamps at a cap rather than growing forever ([#8](https://github.com/kds1111/50-50/issues/8)). Visible to the player *and to the opponent*, at risk until cemented, and lost entirely on a bail or on conceding. The bank is the hook of the whole game.

**Cement** — to convert the pending bank into permanent score. Scoring a goal is the only thing that cements, and a goal pays exactly the bank. Cementing resets the bank to 1.00 ([#8](https://github.com/kds1111/50-50/issues/8)).

**Score** — cemented points. Permanent, cannot be lost. Distinct from the bank in every context; never use "score" for the pending value.

**Forfeit** — to lose the pending bank without cementing it. Two events forfeit, both total: **bailing**, and **conceding** a goal ([#8](https://github.com/kds1111/50-50/issues/8)). A turnover does not. A goal therefore resets both players — the scorer by cementing, the conceder by forfeiting.

**Arena** — the playfield. A concrete skatepark-stadium: a goal at each end, ramps and rideable surfaces for generating air, walls and a ceiling as boundaries.

**Goal** — both the structure and the act of putting the ball in it. Every goal belongs to a **side**. The ball going in cements the attacking side's bank and forfeits the defending side's — credited by where the ball went, not by who touched it last, so an own goal counts for the opponent ([#20](https://github.com/kds1111/50-50/issues/20)).

**Side** — which end a player plays for: **A** or **B**. Side A starts at the −Z end and attacks +Z. A goal is *defended by* one side; a bot plays for a side exactly as a human does.

**Ball** — the contested object. A large, floaty rigidbody, readable at speed.

**Kickoff** — the reset at the start of a match and after every goal: ball to its spot, each player to their side's kickoff spot, then a short countdown with the boards held still. Anything in progress — a trick in the air, a grind, a knockdown — is cancelled with no consequence. Banks are untouched; the goal already settled them ([#24](https://github.com/kds1111/50-50/issues/24)). Distinct from the **respawn** after a bail, which puts one player at the nearest safe point.

**Match** — a contest with a kickoff after each goal, optionally ending on a clock. Highest cemented score wins. Played in one of two **modes**: **Match Play**, two players split-screen on one machine, or **Free Skate**, one player alone ([#24](https://github.com/kds1111/50-50/issues/24)). In the slice the clock is an Inspector switch, not a designed feature.

**Tick** — the fixed simulation step. All gameplay advances on ticks, never on frames. Note that under FishNet a tick is *not* `FixedUpdate` — FishNet drives `Physics.Simulate` itself.

**Replicate data** — the serializable input struct for one tick. The single channel through which any controller — human or bot — drives a board. If an input isn't in this struct, it doesn't exist to the simulation.

**Reconcile data** — the serializable state struct restoring a predicted object to an authoritative past tick. Any value that survives across ticks and affects simulation output belongs here.

**Bot** — a computer-controlled opponent. Out of the slice: replaced by split-screen as the way to test the game ([#23](https://github.com/kds1111/50-50/issues/23)). Any future bot produces replicate data exactly as a human does.

## Related documents

- The current effort is charted on the [vertical slice map](https://github.com/kds1111/50-50/issues/1) — destination, settled decisions, and what is deliberately not yet specified.
- `docs/research/` holds research findings, each tied to the ticket that asked for it.
- `docs/adr/` holds architectural decision records — none written yet; decisions have lived on the map so far.
