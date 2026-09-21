# FiftyFifty.Sim

The rules of the game, in plain C#, where Unity cannot reach them.

`FiftyFifty.Sim.asmdef` sets `noEngineReferences: true`. That is the whole point of this
folder: code in here **cannot** reference `UnityEngine`, because the assembly is not compiled
against it. `Time.time`, `transform`, `Vector3`, `Debug.Log` and `MonoBehaviour` do not exist
here — not by convention, but because the symbols are absent and the compiler says so.

## Why

Two rules the project committed to, which only this boundary actually enforces:

- **Net-shaped.** Simulation must not decide anything from the wall clock or from a rendered
  transform, because a replayed tick reads a clock that has moved on and a transform that was
  never there. Four such reads had to be hunted out by hand on #26, and a fifth survived that
  sweep. In here they cannot be written.
- **90/10 verification.** Rules are tested headlessly; feel is judged by playing. The headless
  runner used to be handed a list of twelve file paths maintained by hand, which went stale
  every time a rule was added. This assembly *is* that list.

## What belongs here

Anything that decides an outcome from values: the bank and its cap, what a goal pays, whether
a trick landed or bailed, which trick a rotation is, grind naming and payout, match phase, pad
dealing, respawn ordering and facing.

## What does not

Anything that must ask the world a question or move a body: suspension, traction, the respawn
clearance search, ball carrying. Those run on the fixed tick and touch PhysX, and they are
judged by playing rather than by tests. They live in `Assets/Scripts/` outside this folder.

Note that `Vector3` and `Vector2` are Unity types and therefore unavailable in here. Geometry
crosses the boundary as tuples — `(float X, float Z)` — as `RespawnPlacement` does.

## Tuning values

`[SerializeField]`, `[Header]`, `[Tooltip]` and `[Range]` are Unity attributes and cannot be
used in here. `[Serializable]` is `System.Serializable` and can: a settings class defined in
this assembly can be exposed as a single `[SerializeField]` field on a MonoBehaviour and Unity
will render it in the Inspector. Prefer that to a MonoBehaviour copying fields across one at a
time.

A value that genuinely needs a `[Range]` slider is a feel value, and feel values belong on the
MonoBehaviour rather than in here.

See `docs/adr/0001-simulation-assembly-boundary.md`.
