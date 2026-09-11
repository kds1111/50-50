# Trick detection and combo-banking precedents

Research for [#4](https://github.com/kds1111/50-50/issues/4), part of the [50-50 vertical slice map](https://github.com/kds1111/50-50/issues/1).
Written so #8 (trick-grammar prototype) and #6 (scoring rules) can argue from facts.

## How to read the citations

Games do not publish their source. Evidence here is tiered, and every claim is tagged:

- **[dev]** — a developer or publisher saying it: patents, official manuals, first-party support pages, patch notes, dev interviews. Closest thing to primary.
- **[artifact]** — an observable property of a shipped build (assembly names, settings, UI), verifiable by anyone with the game.
- **[community]** — player reverse-engineering, wikis, forum threads. Directionally useful, individually unreliable. Never load-bearing alone.
- **[adjacent]** — peer-reviewed or industry work from outside games that bears on the same problem.

---

# Thread 1 — Trick detection

## 1.1 There are three families, not two

The ticket frames this as input grammar vs rotation tracking. Shipped games actually occupy three positions:

| Family | The trick is… | Canonical examples |
|---|---|---|
| **Input grammar** | …a *name the player asked for*, recognised from a gesture, then animated. | Tony Hawk's Pro Skater (button+direction), EA Skate "Flick-It" (stick gesture) |
| **Simulated articulation** | …whatever the *feet* did to the board; the board is a rigidbody driven by simulated foot forces. | Skater XL, Session, Neversoft's "Nail the Trick" |
| **Rotation classification** | …a *label applied afterwards* by integrating board rotation and matching it to a taxonomy. | The naming layer that sits on top of Skater XL / Session |

The second and third are usually conflated as "the Skater XL approach", but they are separable and that separation is the single most useful finding in this thread. Skater XL ships them as **different assemblies**: a shipped Skater XL install contains `SkaterXL.Core.dll`, `SkaterXL.Data.dll` and a distinct **`SkaterXL.TrickDetection.dll`** (visible in the assembly references of community mods built against the game). **[artifact]** The physics does not know what a kickflip is. A separate module watches the physics and decides.

That matters for 50-50: you can adopt rotation classification *without* adopting foot-level articulation. The board can be driven however you like — raycast suspension, boost, torque impulses — and the classifier just reads the resulting rigidbody state.

## 1.2 Input grammar, as actually built

### Tony Hawk's Pro Skater

Discrete gesture → named trick → authored animation → fixed base score from a table. Rotation is an *independent, parallel* input channel: the player spins with the left stick / shoulder buttons while the trick animation plays, and spin contributes a separate multiplier rather than defining the trick. The official THPS2 manual's multiplier table runs from 180° = 1.5x up to 900° = 6.0x. **[dev]**

The important architectural consequence: in THPS, **the board's visible rotation is an output of the trick, not an input to naming it**. There is nothing to classify. The naming problem does not exist.

### EA Skate — "Flick-It"

EA's own control guide: *"With Flick-It, you steer with the left stick and perform flip tricks and board adjustments with the right stick."* and *"Flick the right stick just right and you'll ollie, kickflip, pop-shuvit, laserflip, and more."* **[dev]**

The development detail is the useful one. Per Scott Blackwood (EA Black Box) in a 2008 Gamasutra interview, the first Flick-It prototype had no graphics at all: *"the initial prototype simply read unique analog stick motions and displayed a basic text message saying what trick had been performed, along with speed and accuracy ratings."* Capturing the flicks required reading *"input data from each control pad … at a rate of 120 Hz."* **[dev]**

Two things fall out of that:

1. The recogniser is a **standalone pure function**: stick sample stream → `(trickName, speed, accuracy)`. It was built and tuned with a text readout before any skater existed. That is exactly the prototype shape ticket #8 should take, whichever family wins.
2. Gesture recognition is **sample-rate sensitive**. 120 Hz polling for a gesture that lasts ~150 ms. On a Unity fixed tick this is a real constraint — a 50 Hz fixed tick gives you ~7 samples of a flick.

### Neversoft / Activision's "Nail the Trick" — US9095776B2

The most detailed developer-authored description of a skate control system that exists in public, because it is a patent. **[dev]**

- Three sub-modes inside a **"trick mode"**: sticks-as-feet (default), sticks-as-hands (left trigger, with four grab points — nose, tail, toe-side, heel-side), sticks-as-weight (right trigger, for manuals / nose-manuals / caspers).
- Trick mode is **entered when airborne and exited when the board touches the ground horizontally with trucks down**.
- Landing is a **timed window**, not an instantaneous test: re-grabbing during board rotation is allowed *"within a small window of time during board rotation and when the upper surface of the board is near parallel to the skate surface while the trucks are down"*; missing that window **triggers a bail**.
- The camera is placed **behind the skater** during trick mode explicitly to establish *"correspondence between location of the control … and relative displayed position of the hand"*.

That last point is the camera-disambiguation problem being solved by **forcing the camera into the board's frame**. 50-50 cannot do that — the camera belongs to the ball. See §1.6.

## 1.3 Simulated articulation + rotation classification, as actually built

### Skater XL

Easy Day Studios' Dain Hedgpeth, on the design position: *"[Skater XL is] physics-based, but it's not absolutely grueling. Physics doesn't mean it's a sim, and simulation is not a designed experience."* and *"a designed experience where we pick which parts of the physics you actually engage in and how that mapping actually goes from the user's intent into the gameplay."* **[dev]**

Mechanically: each stick is bound to a foot; the board responds to what the feet do; *"You can control board rotations by flicking your foot with the analog sticks slower or faster"*; board catches on L3/R3. **[dev]**

The decisive finding about Skater XL is negative: **it shipped with no score and no combo system at all.** Trick naming is display-only, and even that was incomplete enough that the community built third-party trick trackers with their own *"Grind and Trick Rotation Calculations"*. **[community]**

That is the clearest available statement of rotation tracking's cost. The studio that committed hardest to it did not build a scoring system on top of it — arguably because a continuous, unbounded trick space does not hand you stable names or stable point values, which is precisely what a scoring system needs.

### Session

Same premise — each analog stick is a foot, weight transfer is explicit — per crea-ture Studios, whose founder's originating idea was *"using a second stick for the representation of both feet."* **[dev]**

Session's **Trick Display** shipped marked **experimental**, and its most-reported defect is a naming one: frontside and backside air rotations displayed inverted, most reliably for goofy-footed skaters. A player's definition in the thread — *"If the rider is spinning it will indicate which side of the rider is first to face in the direction of travel"* — is exactly the rule the classifier has to get right, and exactly the rule whose sign flips with stance. **[community]**

This is the canonical rotation-classification failure and it is not an edge case: it is the second most common trick property after "how far did it spin".

## 1.4 What "a kickflip" actually is, in axes

A skateboard trick name is a tuple over three board-local rotation axes plus rider state. Using the board's own frame:

- **Roll** (longitudinal, nose-to-tail axis) — the *flip* axis.
- **Yaw** (vertical) — the *shuv / spin* axis.
- **Pitch** (transverse, across the board) — end-over-end.

| Trick | Roll | Yaw | Notes |
|---|---|---|---|
| Ollie | 0 | 0 | pop only |
| Kickflip | 360° | 0 | flip toward the heel side / "backside corner" flick |
| Heelflip | 360° opposite | 0 | for regular stance, *"the board spins clockwise from the perspective of a view from behind the skater"* |
| Pop shuvit | 0 | 180° | board only; rider does not turn |
| 360 shuvit | 0 | 360° | |
| Varial kickflip | 360° | 180° backside | |
| Hardflip | 360° | 180° frontside | same magnitudes as varial — **only the yaw sign differs** |
| Laserflip | 360° heelflip | 360° frontside | |
| Impossible | 360° wrap around the back foot | 0 | not cleanly any of the three axes |

(Axis assignments and combinations per the flip-trick literature. **[community]**, but uncontroversial and matches how the games name things.)

Three consequences for an implementation:

1. **Classification = quantised integration.** Integrate board angular velocity **expressed in board-local axes** into three accumulators while airborne, reset on ground contact, then quantise: roll to whole turns, yaw to half-turns, pitch to whole turns. The name is a lookup on `(rollTurns, yawTurns, pitchTurns, riderYawTurns, stance)`.
2. **Body yaw must be tracked separately from board yaw.** A 180° yaw with the rider turning is a "frontside 180"; a 180° yaw with the rider square is a "pop shuvit". Same board rotation, different trick. If your board and rider are one rigidbody you *cannot* name these apart.
3. **Sign is stance-relative and the sign is where the bugs live.** Varial vs hardflip, frontside vs backside, are pure sign distinctions. Regular/goofy and forward/switch each flip a sign. Evidence this is genuinely hard at studio scale: EA's 2025 `skate.` shipped *"all Footplant tricks were named inaccurately when skating Regular"* and had to fix it in patch 0.30.1. **[dev]**

## 1.5 Landing vs bail

Nobody judges this as a single boolean. The shipped pattern is **a gate plus a grade**.

**The gate** (the patent's formulation, and the shape every implementation converges on): board upper surface **near parallel to the riding surface**, **trucks down** (i.e. the wheel side, not the deck or an end), inside a **time window** during which rotation can still be arrested. Miss it → bail. **[dev]**

In practice that decomposes into three independent tests, all cheap in PhysX:

- **Orientation** — `dot(board.up, contactNormal) > cos(tolerance)`.
- **Velocity alignment** — transform velocity into board-local space and check the horizontal component lies within a cone of board-forward. The common Unity-forum formulation is to convert velocity to the skater's local space, drop one axis, and test the resulting angle against a tolerance; a related approach compares the angle between last-airborne velocity and landing velocity. **[community]**
- **Contact part** — trucks/wheels vs deck vs nose/tail.

**The grade.** Landing quality scales the payout rather than gating it:

- THPS2, official manual: *"Landing a perfect trick gives you 150% of the trick score, sloppy gives you 75%."* **[dev]**
- `skate.` (2025) ships explicit **"Landing Modifiers for Sketchy or Clean"** and separately scores "Total Clean Trick Score" — i.e. sketchy landings are landings, just worth less. **[dev]**

**Tolerance is a shipped, player-visible tuning knob, not a constant.**

- Session's "casual grind" toggle *"changes only the tolerance for how exact your position and angle need to be."* **[community]**
- THPS 1+2 ships **"No Bail"** and **"Perfect Balance"** options — the studio shipped a switch that removes the loss condition entirely. **[community]**

Build the tolerance as a serialized field from day one. You will need to turn it off to playtest board feel independently of scoring (relevant to the map's 90/10 verification split).

## 1.6 Disambiguating board rotation from camera-relative stick input

This is the problem 50-50 has worst, because the ball probably owns the camera. Shipped games solve it three ways:

1. **Separate by device.** Skate and THPS split the sticks: left stick = steer/spin, in camera-or-travel space; right stick = the board, in board space. Costs an entire stick. **[dev]**
2. **Separate by state.** The patent's "trick mode": sticks mean *feet* only while airborne, and mean steering/weight on the ground. Costs nothing in buttons but requires an unambiguous airborne predicate. **[dev]**
3. **Separate by held modifier.** **Rocket League's air roll is the closest precedent to 50-50's situation**, because there the camera is locked to the ball and the stick still has to be able to mean "rotate the car in its own frame". Psyonix's answer: a **held modifier** turns the stick from steering semantics into body-frame roll, and dedicated **"Air Roll Left" / "Air Roll Right"** bindings were added for console in patch v1.31 precisely so the modifier could be direction-specific. **[dev]/[community]**

The fourth option, which none of the skate games take but 50-50 can: **don't map rotation to the stick at all.** If board rotation is a *consequence* of physics (boost vectoring, jump impulses, contact with the ball) rather than a commanded rotation, the camera-relativity problem evaporates — there is no stick gesture to disambiguate, only rigidbody state to read.

## 1.7 Failure modes, with receipts

The interesting result is that **both** families fail, in mirrored ways, and both keep failing after ship at AAA scale.

| Failure | Family | Evidence |
|---|---|---|
| False positive — game gives a trick you didn't ask for | grammar | `skate.` 0.32.0: *"Updated Flick-It detection for Impossibles to reduce accidental Impossibles."* **[dev]** |
| Misclassification under unusual dynamics | grammar/continuous | `skate.` 0.32.0: *"Fixed an issue where high-speed grinds could cause the wrong grind type to be detected."* **[dev]** |
| Naming wrong because of stance sign | classification | `skate.` 0.30.1: *"Fixed all Footplant tricks were named inaccurately when skating Regular."* **[dev]** |
| Frontside/backside inverted | classification | Session Trick Display, goofy stance **[community]** |
| Trick performed but never surfaced to the player | classification/UI | `skate.` 0.30.1: Coffin Air and Double Ground Grab *"would not properly display on the score ticker."* **[dev]** |
| Score value attached to the wrong name | classification | `skate.` 0.32.0: *"Fixed an issue where score values for Judo and BS Footplants were swapped."* **[dev]** |

And a calibration point from outside games: a wearable IMU system classifying **real** freestyle snowboard tricks from three-axis rotation data *"correctly identified the tricks … more than 90 percent of the time"* — i.e. roughly one in ten wrong — and could not detect a fall occurring within about a second after landing. **[adjacent]**

A game has perfect, noiseless state, so it should beat 90%. But the study isolates the right lesson: **the hard part of rotation classification is the taxonomy, not the sensing.** You will have exact rotation numbers and still argue about what to call them.

## 1.8 Trade-offs, condensed

| | Input grammar | Rotation classification |
|---|---|---|
| Player intent | Explicit. Player names the trick before it happens. | Implicit. Player discovers what they did. |
| Readability | High — the name was requested, so the feedback is trivially "correct". | Depends entirely on the naming layer's quality. |
| Expressive range | Finite vocabulary. Nothing between two tricks. | Continuous. Every input produces *something*. |
| Failure feel | "The game ignored me" / "I didn't ask for that." | "That wasn't a hardflip." |
| Input budget | Expensive — consumes a stick or a mode. | Free — reads state you already simulate. |
| Scoring stability | Excellent. Table of names → base values. | Poor by default. Needs aggressive quantisation to become table-like. |
| Animation | Authored clips, always clean. | Procedural/physical; visuals and physics can never disagree. |
| Proof it works | THPS, Skate — decades of it. | Skater XL, Session — and Skater XL shipped **without a score system**. |

**Hybrids ship.** Riders Republic offers two presets, **Racer** and **Trickster**, where Trickster *"gives you more granular control over spins in order to rack up big points"* at the cost of ease. **[community]** The families are ends of a dial, not a binary.

---

# Thread 2 — Combo banking

## 2.1 Tony Hawk's Pro Skater, precisely

### What counts as a completed trick

A trick's base value enters a **pending combo total**, not the score, the moment it is performed. Nothing reaches the score until the combo ends cleanly.

Base values are table-driven per trick, with **held tricks accruing over time**: *"Grabs have a base score associated with them, however they can be held for a longer time for additional points. Manuals, grinds, and lip tricks can also be held for extra points."* **[dev]**

### What continues the combo

Activision's own statement: *"You need to string multiple tricks together to form a **Combo**. The more tricks you add **before you put your board down**, the higher your score multiplier gets."* **[dev]**

"Putting your board down" is the terminating event. The entire history of the series is the history of **postponing** it:

- THPS2 adds the **manual** — roll on two wheels, combo survives ground contact.
- THPS3 adds the **revert** — land a vert trick and spin out of it into a manual, so transition no longer breaks the chain. Activision on THPS 1+2: *"Be sure to change your Stance (Riding Switch) in order to improve your Scores, too. You can do that by using Reverts."* **[dev]**

Every one of these is a **combo-link primitive**: a low-value action whose only job is to keep the bank pending. That is a design pattern, not an accident.

### How the multiplier accumulates

Three independent sources, all additive into one multiplier:

- **Per trick**: the first trick starts at 1x and each subsequent trick adds one. **[community]**
- **Per spin**: *"Adding spins to your moves introduces a multiplier. With each 180 degree spin, your score multiplier goes up"* — official table 180° = 1.5x … 900° = 6.0x. **[dev]**
- **Per gap**: *"Gaps are environmental obstacles that you can discover in each Park. If you overcome one of those in the middle of Combo, that'll add to your multiplier just like a trick."* **[dev]**

Final payout ≈ `Σ(base × landing grade) × multiplier`, applied on landing.

### How the bank is lost

Bail. A bail scores **zero for the entire pending combo** — not a reduced amount, not the last trick. Every trick already performed is forfeited. As a community formulation puts it, bailing and landing are the same event structurally — *"failing to keep your combo, with the only thing that would change being the color of your score at the bottom of the screen."* **[community]**

The asymmetry is the whole game: the multiplier grows linearly with tricks while the probability of a bail compounds, so the expected-value curve has a peak and the player is always deciding whether they are past it.

### Anti-spam

**This is not optional and THPS gives you the numbers.** Official THPS2 manual: *"Doing a trick the first time will give you 100% of its full point value. Each subsequent time you pull off the same trick in a level, your score decreases as the table indicates"* — **100% / 75% / 50% / 25% / 10%** and 10% thereafter. **[dev]** THPS3 example: a kickflip degrades **100, 75, 50, 25, 10**. **[community]**

**Watch the scope word.** The THPS2 manual says **"in a level"** — degradation persists across the whole run. The THPS 1+2 support page says *"Using the same trick repeatedly in **your Combo** causes that Trick's score to drop."* **[dev]** Per-run and per-combo are different games: per-run forces variety across the session, per-combo resets the puzzle each time you land. Pick deliberately.

### Feedback that the bank is at risk

- **The pending combo is always on screen** — the trick string and running pending total display continuously and only merge into the score total on landing, visually distinct from the committed score. **[community]**
- **The balance meter** is the explicit risk gauge for the combo-link primitives (grinds, manuals, lip tricks): a bar that drifts toward an edge and bails you if it hits it, giving continuous, legible "you are about to lose it" pressure. **[community]**
- **Escape hatch shipped**: THPS 1+2 includes **"No Bail"** and **"Perfect Balance"** options. **[community]**

## 2.2 OlliOlli — the same mechanic with the clearest UI

OlliOlli World is worth copying for vocabulary and HUD alone: the combo multiplier sits top-left next to a **"blue bubble indicating the points you have banked"**; on landing, *"your points in the combo are multiplied and added to your banked points"*; *"if you bail during a combo, you lose it"*; landings are graded (none / **Good** / **Perfect**). **[community]**

**Two permanently visible numbers — banked and pending — with the pending one animating into banked at the moment of commitment.** That is the cheapest possible solution to 50-50's HUD requirement and it is proven.

## 2.3 Score that stays provisional until a later event confirms it

The ticket asks whether anything does 50-50's exact thing. Findings:

### Roller Champions (Ubisoft) — the closest arena-sport precedent

Steam's own description: *"take the ball, make a lap while maintaining team possession, dodge opponents, and score"*, and you increase your score by *"completing additional laps before attempting a goal."* **[dev]** Per Ubisoft's game guide: possession through all four gates completes a lap; one lap unlocks a **1-point** shot, two laps **3 points**, three laps **5 points** (instant win); **if the opposing team takes possession, all the gates reset.** **[dev]** — *(that guide URL now 404s; recovered via search index — treat the 1/3/5 numbers as [dev] but re-verify before relying on them.)*

This is the structure 50-50 has, with one important difference: Roller Champions holds the **value of the eventual goal** provisional, not a separate pool of points. There is only ever one scoring event; laps set its size. And crucially, **the provisional value is destroyed by an opponent action** (losing possession), not only by player error.

### NBA Street (EA) — trick value as a resource, not a pending score

Trick moves and baskets fill a **Trick/Gamebreaker meter**; riskier moves pay more trick points. A full meter is spent on a **Gamebreaker** basket, which adds to your score *and subtracts the same amount from the opponent's*. **[community]**

The instructive difference: the meter is **not at risk**. Trick value converts to a stored resource that you spend when you choose. This is the *soft* version of 50-50's mechanic — all of the "tricks matter to the match outcome", none of the "you can lose it all". Worth having on the table as the fallback if total loss tests badly.

### Extraction shooters — the genre-scale proof

Tarkov / Hunt: *"if you successfully extract, you keep the items you found, but if you die, you lose your gear."* **[community]** A whole genre built on exactly "provisional until a confirming event", including the standard mitigations the genre invented once total loss proved too bitter — insurance, secure containers, partial loss. Those mitigations are the prior art for softening 50-50's bail rule.

### Push-your-luck — the design-literature name for the mechanic

BoardGameGeek's mechanism definition: *"Players must decide between settling for existing gains, or risking them all for further rewards."* **[community]** The literature's standard softener is **fractional bust** — busting costs you one category of unbanked gains rather than all of them, which *"makes it a less bitter experience"* and keeps pushing rational. Directly applicable to what a bail should cost.

### The absence, which is itself a finding

**I found no shipped team/arena sports game where accumulated *trick* score is held provisional and released by a *goal*.** Roller Champions holds a goal's value provisional. NBA Street converts tricks into a spendable resource. THPS / OlliOlli hold trick score provisional but the release event is **landing**, which the player fully controls.

50-50's combination — trick bank, released by a *contested* event — appears unoccupied. Which means **there is no tuned precedent to copy the numbers from**, and the structural difference below is unexplored territory that playtesting will have to settle.

## 2.4 The structural difference nobody else has

In THPS and OlliOlli, **the player controls both the risk and the release.** You decide when to stop and land. The whole risk/reward curve rests on that.

In 50-50, **the release event is contested.** The bot can prevent the goal. That breaks three assumptions THPS relies on:

1. **Hold time becomes unbounded.** In THPS a combo lasts seconds. A 50-50 bank could last a whole half. Without a decay, a cap, or a proximity requirement, the dominant strategy is "trick in the corner forever, never contest the ball" — the exact opposite of the intended game.
2. **The bank can be lost to events the player didn't cause.** Conceding, a turnover, the timer. THPS's only loss condition is player error, so it never had to answer "is it fair to take my bank for something the bot did?"
3. **The opponent's optimal play is to destroy value rather than create it.** Defending isn't just denying a goal; it is denying the bank. That may be great (real tension) or awful (the bot's best move is to stall). Needs playtesting, not reasoning.

Roller Champions' rule — opponent possession resets your progress — is the one piece of prior art for point 2, and it is *harsh*: total reset, caused by the opponent.

---

# Implications for 50-50

## Detection: rotation classification, not an input grammar

**Recommendation: read the board's rigidbody state and classify it. Do not spend inputs on a trick grammar.** Three reasons, in order of weight:

1. **The input budget is already spent.** Every shipped grammar consumes a whole stick (Skate: the right stick is board-only; THPS: face buttons plus shoulders) or a whole mode (the patent's "trick mode", which is legal only because a skate game has nothing else to do while airborne). 50-50's player is simultaneously driving, boosting, aiming at a ball, and positioning against a bot. There is no spare stick and no spare mode. Rotation classification costs **zero** dedicated inputs.
2. **It fits the architecture the map already committed to.** The board is a PhysX rigidbody; angular velocity is already there. A classifier is a pure function of simulation state, so it lives in plain C# outside MonoBehaviours (the map's explicit requirement), runs on the fixed tick, mutates nothing in `Update()`, adds nothing to the FishNet input command struct, and is unit-testable — which is exactly what the 90/10 split asks of rules and trick detection.
3. **Grammar false positives are worse here than in a skate game.** EA still ships fixes for *"accidental Impossibles"*. In THPS a false positive costs you a combo. In 50-50 it injects points into a contested match. And an arena game has a false-positive source skate games do not have: **another body hits your board.**

**But** rotation classification's documented weakness is precisely what 50-50's scoring needs — stable names, stable values. Skater XL shipped without a score system and that is not a coincidence. So constrain it hard:

- **Integrate in board-local axes**, three accumulators (roll / yaw / pitch turns), plus a separate **rider-yaw** accumulator so a shuvit and a 180 are distinguishable. Gate on an explicit airborne predicate; reset on ground contact.
- **Quantise aggressively.** Name is a lookup on the rounded tuple, not a function of raw degrees. The slice needs three tricks: **spin** (yaw ±0.5 / ±1.0), **flip** (roll ±1.0), **varial** (both). Everything unmatched scores as a generic "air" rather than inventing a name — silence beats a wrong name (see the `skate.` naming bugs).
- **Thresholds and hysteresis, not thresholds alone.** Require ≥ ~0.75 of a turn before crediting a turn, plus a **minimum airtime**, or you will credit tricks off the angular jitter of a ball collision. Budget for this; it is the dominant novel failure mode and no skate game has had to solve it.
- **Points are a function of the name, not the degrees.** Table-driven, so values are repeatable, the HUD can show them, and the bot is scored by the same code path.

**Prototype shape for #8: copy EA's 2007 prototype exactly.** Greybox board, no scoring, no VFX — just a text readout printing the detected trick name plus the raw accumulator values each time you land. Tune thresholds against that readout before anything else exists. It is the cheapest possible way to find out whether the taxonomy is legible.

## Landing and bail: gate plus grade, tolerance exposed

Use the patent's gate shape — board-up vs contact normal within tolerance, underside contact, velocity within a cone of board-forward — but **grade rather than gate**: `Clean / Sketchy / Bail`, with Sketchy paying a fraction (THPS2's 150%/75% and `skate.`'s Clean/Sketchy modifier are both precedent).

This matters more in 50-50 than in a skate game, because **landings here are routinely disturbed by things the player didn't do** — ball contact, bot contact, awkward arena geometry. A binary bail on every imperfect landing will read as the game being unfair, because often it will be. Grading keeps the bank alive with a haircut.

Expose the tolerance as a serialized field and ship a **"no bail" playtest toggle** from day one (THPS 1+2 shipped one). The human needs to be able to test board feel with scoring rules switched off — that is a direct requirement of the 90/10 verification split, not a nicety.

## Camera: solve it Rocket League's way, or dissolve it

If the ball owns the camera — and the map flags that as unresolved — then **camera-relative stick input and board-relative rotation cannot share a channel**. Two acceptable answers:

- **Held modifier** (Rocket League's air-roll-left/right): stick stays camera/steer-relative always; a held button reinterprets it in board frame. Proven under exactly these conditions.
- **Dissolve it**: make board rotation purely a *consequence* of physics — boost vectoring, jump impulses, contact — with no commanded rotation at all. Then there is no gesture to disambiguate and the classifier reads only rigidbody state.

The second is the stronger fit and costs nothing, and it is the natural partner to rotation classification. It also means the camera question (currently in the map's "not yet specified") stops blocking trick detection: **rotation classification is camera-agnostic by construction**, which is a good reason to prefer it before the camera is decided.

## Where the skateboard/hoverboard fork changes the answer

The recommendation (rotation classification) holds either way, but the *costs move*:

**If skateboard:**

- **Naming is free.** The real taxonomy already exists in exactly the three board axes, with agreed names, agreed magnitudes, and player expectations you didn't have to teach. Huge readability win.
- **Signal is expensive.** Airtime comes from ollies and ramps — scarce, bursty, roughly 0.5–1.2 s. The classifier gets a short integration window, so rotations must be small and thresholds tight. Expect more tuning pain and more false negatives.
- **Bail must be forgiving** because airtime is hard-won; losing a rare airborne moment to a 5° landing error will feel terrible.
- Grammar is *more* viable here as a fallback, because short discrete airtime is what grammars are good at — but it still costs the stick you don't have.

**If hoverboard:**

- **Signal is cheap.** Airtime is abundant and player-controlled, so integration windows are long, rotations can be large and unambiguous, and threshold tuning is easy. Classification gets genuinely comfortable.
- **Naming is expensive.** There is no real vocabulary. You must invent trick names and point values and then teach them — losing the free readability skateboarding hands you. Budget design time for a made-up taxonomy that reads as intentional rather than arbitrary.
- **The airborne gate breaks.** If hovering means continuous attitude control, the board is rotating all the time and "airborne" stops being a clean predicate for "start integrating". You would have to **reintroduce an explicit trick window** — a held button, or a window opened by a boost/jump — which is the patent's "trick mode" coming back and costs an input after all.

**Net:** the skateboard buys you a free vocabulary and costs you signal quality; the hoverboard buys you signal quality and costs you a vocabulary plus one input. Neither flips the recommendation. If the fork lands on hoverboard, add "explicit trick-window state" and "author a trick vocabulary" to the build tickets.

## Banking: THPS's structure, not THPS's release condition

Copy the shape: `pendingBank = Σ(baseValue(trick) × landingGrade) × multiplier`, multiplier accumulating from tricks, half-turns, and an arena analogue of gaps (wall rides, transfers, airtime over the ball).

Then adapt for the contested release:

1. **Ship anti-spam in v1, with THPS's numbers.** 100 / 75 / 50 / 25 / 10. Without it the optimum is one spin on repeat and the bank stops being a puzzle. Scope it **per bank** (resetting at each goal) — that is the analogue of THPS 1+2's per-combo decay and it makes each bank a fresh problem, where per-match decay would push players into an ever-narrowing trick set over a short slice.
2. **Bound the hold.** Unbounded pending value is the failure mode unique to 50-50. Pick one and test it: a soft cap, a decay timer, or a requirement that banking only accrues in the attacking half / near the ball. Untreated, "trick forever in the corner" is optimal play.
3. **Decide what forfeits the bank besides bailing.** Conceding a goal? A turnover? Nothing? Roller Champions' precedent is harsh — opponent possession resets everything. That may be correct; it is also the most likely thing to make the game feel unfair, and it is the single highest-value playtest question in the scoring ticket.
4. **Consider fractional bust instead of total loss.** Push-your-luck design literature and the extraction-shooter genre both converged on partial loss (insurance, secure containers, "you only lose one colour"). Losing a whole half's bank to one sketchy landing *and* again when the bot scores may double-punish. Halving the bank on a bail is a cheap alternative worth testing against total loss.
5. **HUD: copy OlliOlli.** Two numbers, permanently visible, visually distinct — **banked score** and **pending bank** — with the pending one animating into banked on the goal. Add a risk cue: in THPS the balance meter tells you the bank is fragile; in 50-50 the fragility is "bank is large and you are airborne / out of position", so the cue should scale with bank size and time held.
6. **Keep it a pure type.** A `PendingBank` with `AddTrick(TrickName, LandingGrade)`, `Cement()`, `Forfeit(Reason)` — table-driven, no MonoBehaviour, unit-tested, and used identically by the bot. That satisfies the map's "rules and scoring live in plain C#" constraint and makes the whole scoring model arguable from tests rather than vibes.

## Questions the scoring ticket (#6) must answer, that research cannot

- **Does the bot bank too?** A heuristic fixture will either never trick (bank irrelevant to the match) or trick trivially (bank dominant). Neither is a game. Decide deliberately.
- **Is a goal worth points on its own, or purely a cement event?** If purely a cement event, a 0-bank goal is worthless and the incentive to score collapses.
- **Does conceding forfeit the bank, or merely fail to cement it?**
- **Per-bank or per-match trick degradation?** (Both have THPS precedent; they are different games.)

---

## Sources

### [dev] — developer / publisher statements

- Activision Support, *Tony Hawk's Pro Skater 1 + 2 — Scoring and Combos* — https://support.activision.com/tony-hawks-pro-skater-1-2/articles/tony-hawks-pro-skater-1-2-scoring-and-combos
- *Tony Hawk's Pro Skater 2* (Game Boy Advance) instruction manual, Scoring, pp. 16–17 — spin multiplier table, repeated-trick degradation table, perfect/sloppy landing percentages — https://world-of-nintendo.com/manuals/game_boy_advance/tony_hawks_pro_skater_2.shtml
- US Patent **US9095776B2**, *Video game extremity control and object interaction*, assignee Activision Publishing — foot/hand/weight sub-modes, trick-mode entry/exit, landing window, bail condition — https://patents.google.com/patent/US9095776B2/en
- Electronic Arts, *skate. — Get Control!* (official Flick-It control guide) — https://www.ea.com/games/skate/skate/news/get-control
- Electronic Arts, *skate. 0.32.0 Patch Notes* — accidental Impossibles, wrong grind type at high speed, swapped score values — https://www.ea.com/games/skate/skate/news/update-032-0
- Electronic Arts, *skate. 0.30.1 Patch Notes* — Footplant naming by stance, score ticker omissions, Sketchy/Clean landing modifiers — https://www.ea.com/games/skate/skate/news/update-030-1
- Christian Nutt, *New Tricks: Scott Blackwood Talks Skate And Skate 2*, Gamasutra, 17 Oct 2008 — text-only Flick-It prototype printing trick name + speed/accuracy, 120 Hz pad polling. (Original and Internet Archive copy are both currently unreachable from this environment; quotations taken from Wikipedia's *Skate (2007 video game)* article, which cites this interview directly.) — https://en.wikipedia.org/wiki/Skate_(2007_video_game)
- Game Informer, *Exploring Skater XL's Bag Of Tricks* — interview with Dain Hedgpeth, Easy Day Studios — https://gameinformer.com/2018/12/24/exploring-skater-xls-bag-of-tricks
- Red Bull, *Session skateboard game interview — crea-ture Studios* — origin of the two-stick / two-feet model — https://www.redbull.com/us-en/session-interview-crea-ture-studios
- Roller Champions Steam store page (Ubisoft) — lap-before-goal scoring — https://store.steampowered.com/app/2211280/Roller_Champions/
- Ubisoft, *Roller Champions Game Guide* — four gates per lap, 1/3/5 points, gate reset on losing possession. **URL now returns 404**; content recovered via search index, re-verify before relying on the exact numbers — https://www.ubisoft.com/en-au/game/roller-champions/news-updates/3PfX2zoJnrnKuvTGsZzday/roller-champions-game-guide

### [artifact] — observable properties of shipped builds

- Skater XL managed assembly list, via a community mod's project references: `SkaterXL.Core.dll`, `SkaterXL.Data.dll`, **`SkaterXL.TrickDetection.dll`**, `BFSUtilities.dll`, `Rewired_Core.dll` — https://github.com/roquef/skaterxl-grabs-customizer/blob/main/grabs-customizer.csproj

### [community] — player reverse-engineering, wikis, forums. Verify before relying on.

- Session: Skate Sim Steam discussion, *Backside vs Frontside — Rotations* — Trick Display inverting FS/BS, worst for goofy; noted as experimental — https://steamcommunity.com/app/861650/discussions/0/1743392068395347609/
- Skater XL Steam discussions on the absence of a combo/point system — https://steamcommunity.com/app/962730/discussions/0/1744480966983394608/
- DWG Trick Tracker (community Skater XL mod) — third-party "Grind and Trick Rotation Calculations" — https://github.com/DeadWalking/SkaterXL_Mods
- Tony Hawk's Games Wiki, *Combo* — per-trick multiplier accrual, THPS3 kickflip degradation 100/75/50/25/10 — https://tonyhawkgames.fandom.com/wiki/Combo
- PowerUp!, *THPS 1+2 includes 'No Bail' and 'Perfect Balance' options* — https://powerup-gaming.com/2020/08/13/tony-hawks-pro-skater-1-2-includes-no-bail-and-perfect-balance-options/
- Wikipedia, *Flip trick* — rotational axis definitions for kickflip / heelflip / shuvit / varial / hardflip / laserflip / impossible — https://en.wikipedia.org/wiki/Flip_trick
- Attack of the Fanboy, *OlliOlli World: How to Improve Your Score* — banked-points bubble, combo multiplied into banked on landing, bail loses combo, graded landings — https://attackofthefanboy.com/guides/olliolli-world-tips-for-improving-your-score/
- GameFAQs, *NBA Street Vol. 2 FAQ* — trick meter fill, Gamebreaker conversion and opponent score reduction — https://gamefaqs.gamespot.com/gamecube/583515-nba-street-vol-2/faqs/46431
- BoardGameGeek mechanism 2661, *Push Your Luck* — https://boardgamegeek.com/boardgamemechanic/2661/push-your-luck
- Rocket League Help, *Directional Air Roll* — dedicated Air Roll Left/Right bindings, added for console in v1.31 — https://www.rocketleague-help.com/directional-air-roll
- GGRecon, *Riders Republic Controls: Racer or Trickster?* — https://www.ggrecon.com/guides/riders-republic-controls-racer-trickster/
- Unity Discussions, *Aligning a skateboard to a predicted landing point* — landing-angle tests in board-local velocity space — https://discussions.unity.com/t/aligning-a-skateboard-to-a-predicted-landing-point/915371
- Session Steam discussions on grind/landing tolerance and the "casual grind" toggle — https://steamcommunity.com/app/861650/discussions/0/3824161508152275773/

### [adjacent] — outside games

- IEEE Spectrum, *Wearable Device Tracks Tricks in Freestyle Snowboarding* — IMU three-axis rotation classification, >90% correct, cannot detect a fall within ~1 s of landing — https://spectrum.ieee.org/wearable-device-tracks-tricks-in-freestyle-snowboarding
- Groh et al., *Wearable Trick Classification in Freestyle Snowboarding* (underlying paper) — https://www.researchgate.net/publication/304791675_Wearable_Trick_Classification_in_Freestyle_Snowboarding
