# 50-50 — domain context

An arena soccer game on skateboards. Two players compete in a concrete skatepark-stadium; tricks build a **pending bank** of points, and a **goal** is what cements that bank into your score. Trick without scoring and the points evaporate; score without tricking and the goal is worth little. The tension between the two is the game.

The name is both a skate trick (a 50-50 grind) and the odds on a gamble you're holding open.

## Glossary

Use these terms exactly. If a concept you need isn't here, either you're inventing language the project doesn't use, or there's a real gap worth adding.

**Board** — the thing a player rides. A PhysX rigidbody with raycast suspension. Whether it is a skateboard or a hoverboard is an open decision; "board" is the term either way, so code and docs never have to be rewritten when it lands.

**Rider** — the player character on the board. Distinct from the board because they rotate independently: a shuvit spins the board under a stationary rider, a 180 turns both. Trick classification tracks board rotation and rider yaw separately for exactly this reason.

**Trick** — a named rotation of the board performed while airborne, detected by classifying the board's accumulated rotation on landing rather than by the player inputting a gesture. A rotation that matches no name in the table is credited as a generic **air**, never as a guessed name.

**Airborne** — the state in which rotation accumulates toward a trick. Gated by an explicit predicate, not inferred; leaving it is what triggers classification.

**Landing** — the resolution of a trick when the board returns to a surface. Graded, not gated: **Clean**, **Sketchy** (pays a fraction), or **Bail**. Graded because in an arena a landing is often disturbed by the ball or the opponent rather than by the player's own error.

**Bail** — a failed landing. Costs some or all of the pending bank; how much is an open decision.

**Bank** (or **pending bank**) — trick points accumulated but not yet scored. Visible to the player, at risk until cemented, and lost on events the scoring rules define. The bank is the hook of the whole game.

**Cement** — to convert the pending bank into permanent score. Scoring a goal is the only thing that cements.

**Score** — cemented points. Permanent, cannot be lost. Distinct from the bank in every context; never use "score" for the pending value.

**Forfeit** — to lose the pending bank without cementing it. Which events forfeit (bailing, conceding, turnover) is an open decision in the scoring rules.

**Arena** — the playfield. A concrete skatepark-stadium: a goal at each end, ramps and rideable surfaces for generating air, walls and a ceiling as boundaries.

**Goal** — both the structure and the act of putting the ball in it. Scoring a goal cements the scorer's bank.

**Ball** — the contested object. A large, floaty rigidbody, readable at speed.

**Kickoff** — the reset that follows a goal, returning ball and players to starting positions.

**Match** — a timed contest ending on the clock, with a kickoff after each goal. Highest cemented score wins.

**Tick** — the fixed simulation step. All gameplay advances on ticks, never on frames. Note that under FishNet a tick is *not* `FixedUpdate` — FishNet drives `Physics.Simulate` itself.

**Replicate data** — the serializable input struct for one tick. The single channel through which any controller — human or bot — drives a board. If an input isn't in this struct, it doesn't exist to the simulation.

**Reconcile data** — the serializable state struct restoring a predicted object to an authoritative past tick. Any value that survives across ticks and affects simulation output belongs here.

**Bot** — the heuristic opponent. A test fixture, not a designed opponent. It produces replicate data exactly as a human does.

## Related documents

- The current effort is charted on the [vertical slice map](https://github.com/kds1111/50-50/issues/1) — destination, settled decisions, and what is deliberately not yet specified.
- `docs/research/` holds research findings, each tied to the ticket that asked for it.
- `docs/adr/` holds architectural decision records.
