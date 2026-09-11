# PROTOTYPE — trick grammar

Throwaway. Exists to answer [#6, "How deep the trick grammar goes"](https://github.com/kds1111/50-50/issues/6). Delete the whole `_Prototypes/` folder once that ticket closes — the validated decision goes into real code, this does not.

## Run it

Open `TrickGrammar.unity` in this folder and press Play. Nothing else to set up.

To regenerate the scene from code: **Prototypes > Rebuild Trick Grammar Scene**.

## Controls

| | Gamepad | Keyboard |
|---|---|---|
| Steer | Left stick X | A / D |
| Push / brake | RT / LT | W / S |
| Ollie | Hold **A**, release to pop | Hold **Space**, release |
| Trick stick | Right stick | Arrow keys |
| Body spin | Left stick X (airborne) | Q / E |
| Switch scheme | — | **1** / **2** |
| No-bail toggle | — | **B** |
| Respawn | — | **R** |

The ollie **charges while held and pops on release** — that is the only way to get air off flat ground. Everything else comes off the kickers, the quarterpipe or the bank.

## The two schemes being compared

**1 — Consequence.** No in-air control at all. Lean at the moment of pop is your only influence, and whatever the board does is what gets named. Costs zero inputs, which is why the research recommended it. The thing to feel for: *can you aim for a specific trick, or is it a slot machine?*

**2 — Flick.** Right stick at the moment of pop imparts rotation — X flips the board about its long axis (kickflip / heelflip), Y shuvits it under the rider. Left stick in the air spins the whole body (180 / 360). Costs the right stick, which the arena game will want for the ball. The thing to feel for: *is the extra control worth the input it costs?*

Under Consequence the rider is welded to the board, so a shuvit is **not expressible at all** — every degree of yaw reads as a body spin. That is a real limitation of the scheme, not a bug.

## What the readout means

`flip` / `shuv` / `spin` / `pitch` are signed turns (1.00 = a full 360). They are the raw accumulator values, printed so you can tell **whether the board lied or the classifier did** when a name looks wrong.

- Name wrong but numbers match what you felt → the classifier's thresholds are wrong. Cheap fix.
- Numbers don't match what you felt → the board is wrong. Expensive fix, and worth knowing early.

`grade` is `Clean` / `Sketchy` / `Bail` — landings are graded, not gated, because in the real arena a landing is routinely disturbed by the ball or the opponent rather than by your own error.

Press **B** to turn bails off entirely when you want to judge board feel with the rules out of the way.

## Known crudeness (deliberate)

- The quarterpipe is a fan of flat slabs, not a curve.
- No grinds, no rails — that is [#12](https://github.com/kds1111/50-50/issues/12), a different detection family.
- No ball, no goals, no score, no bank.
- No tests. The classifier is written as pure C# so it *can* be tested, but tests land when it becomes real code, not here.
- Spin is applied kinematically via `MoveRotation`, which fights the physics solver slightly. Acceptable for feel; not how it ships.
