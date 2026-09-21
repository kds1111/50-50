# 1. The rules of the game live in an assembly Unity cannot reach

Date: 2026-09-20
Status: Accepted
Ticket: [#10](https://github.com/kds1111/50-50/issues/10)

## Context

Two constraints were settled when the project was charted, and both are load-bearing:

**Net-shaped.** Input is a serializable command, simulation runs on a fixed tick, and no game
state mutates in `Update()`, so FishNet prediction can be added later without a rewrite. Under
prediction a tick is replayed, so simulation must never decide anything from the wall clock or
from a rendered `transform` — a replayed tick reads a clock that has moved on and a pose that
was never there.

**90/10 verification.** Rules, scoring and trick detection are tested without a human pressing
Play; feel is judged only by playing. That means rules live in plain C# rather than inside
MonoBehaviours.

Neither constraint was enforced by anything except attention, and attention lost. [#26] had to
hunt four wall-clock and rendered-transform reads out of the simulation by hand, and a fifth
survived that sweep and was found later in `PlayerBoardInput`. The headless test runner was
handed a list of twelve file paths maintained by hand, so a rule added without touching that
list would silently never be tested. Nothing stopped a rule being written into a MonoBehaviour
where it could not be tested at all, and `BoardController` reached 1,131 lines, `MatchDirector`
646 and `BoardTrickController` 630 partly for that reason.

The codebase was in better shape than those numbers suggest — twelve rule files were already
free of Unity types, the fixed-tick and frame work were already separated, and `BoardInputState`
was already shaped as a network command struct. What was missing was anything making that
separation true rather than merely intended.

## Decision

**One assembly, `FiftyFifty.Sim`, compiled without a reference to `UnityEngine`.**

`Assets/Scripts/Sim/FiftyFifty.Sim.asmdef` sets `noEngineReferences: true`. Code in that folder
cannot reference `UnityEngine` — not by convention, but because the assembly is not compiled
against it. `Time.time`, `transform`, `Vector3`, `Debug.Log` and `MonoBehaviour` are absent, and
reaching for one is a compile error rather than a review finding.

Three tiers, of which only the first boundary is enforced:

1. **Pure simulation** — `Assets/Scripts/Sim/`. Decides outcomes from values. Headless-tested.
   The bank and its cap, what a goal pays, whether a trick landed or bailed, which trick a
   rotation is, grind naming and payout, match phase, pad dealing, respawn ordering and facing.
2. **Engine simulation** — everything else that runs on the fixed tick. Asks the world questions
   and moves bodies: suspension, traction, the respawn clearance search, ball carrying. Judged by
   playing. Separated from tier 3 by convention and by Unity's own lifecycle, not by an assembly.
3. **Presentation** — `Update` and `LateUpdate`. Reads state, never mutates it: HUDs, the camera,
   the bail flash.

**Tuning values.** `[SerializeField]`, `[Header]`, `[Tooltip]` and `[Range]` are Unity attributes
and unavailable in tier 1. `[Serializable]` is `System.Serializable` and is available, so a
settings class defined in `Sim` can be exposed as a single `[SerializeField]` field on a
MonoBehaviour and Unity renders it in the Inspector. That is preferred to a MonoBehaviour holding
a parallel set of fields and copying them across one at a time, which is what
`RespawnSpotFinder.Settings` does today and what a review flagged as duplication. A value that
genuinely needs a `[Range]` slider is a feel value and belongs on the MonoBehaviour.

**Geometry crosses as tuples.** `Vector3` and `Vector2` are Unity types. Tier 1 expresses
geometry as `(float X, float Z)`, as `RespawnPlacement` already does.

**Migration is gradual.** The twelve already-Unity-free rule files moved into `Sim` as the proof,
which required no code change — only relocation. Nothing else was refactored. Rules still living
inside MonoBehaviours move one ticket at a time, each sized to one module, so no large diff ever
lands at once.

**FishNet stays unwired.** This decision shapes the code so prediction can be added; it does not
add it. Tuning feel with reconciliation switched on is the failure mode the project set out to
avoid, and the board feels good now.

## Consequences

**The test set is no longer maintained by hand.** The headless runner compiles everything under
`Sim`, so the assembly is the list. A rule added to `Sim` is tested by existing.

**A class of bug becomes impossible.** Wall-clock and rendered-transform reads inside pure
simulation cannot be written. This was verified by planting `UnityEngine.Time.time` in
`MatchFlow` and confirming the build fails with `CS0103: The name 'UnityEngine' does not exist
in the current context`.

**Some bugs are untouched by this.** A comment, tooltip or glossary line asserting behaviour the
code does not implement is not an architectural problem and no boundary prevents it. Four such
claims were found in one week. [#31] is the sweep that catches those.

**A cost at the boundary.** Anything in `Sim` needing geometry pays for it in tuple conversion at
the call site, and settings classes lose `[Header]` grouping. Both were judged cheaper than the
alternative, which is the constraint holding only while someone is watching.

**Tier 2 and tier 3 remain unenforced.** Only the pure/engine line has an assembly. The engine and
presentation tiers are separated by convention, because Unity's lifecycle already expresses that
split and a second assembly would buy little for the ceremony it costs. If `Update` methods start
mutating game state again, revisit.

[#26]: https://github.com/kds1111/50-50/issues/26
[#31]: https://github.com/kds1111/50-50/issues/31
