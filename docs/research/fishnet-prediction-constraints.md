# FishNet prediction constraints on gameplay code

Research for [#3](https://github.com/kds1111/50-50/issues/3) (part of the [vertical slice map, #1](https://github.com/kds1111/50-50/issues/1)).

**Question:** what constraints does FishNet's Prediction V2 place on gameplay code, such that code written now — local-only, with no networking active — ports to online 1v1 later without a rewrite?

**Answer in one line:** the constraints are real but cheap, and they are almost entirely about *where state lives and what advances it* — every value that affects the simulation must be either (a) an input field on a serializable struct, (b) a field reset from a reconcile struct, or (c) derivable from the physics engine's own rigidbody state. Everything else — `Update`, `Time.deltaTime`, coroutines, `System.Random`, wall-clock timers — is off the table inside the simulation. The last section is the checkable rules list.

**Read the caveat in [§7](#7-can-a-single-player-build-run-the-same-simulation-path) before treating "one code path" as free.** It is achievable, but not by doing nothing.

---

## 0. Sources and confidence

Everything below is cited. Source tiers used:

- **[SRC]** — FishNet source at release tag `4.7.3`, on GitHub. Highest trust: this is what actually runs.
- **[DOC]** — FishNet official documentation, `fish-networking.gitbook.io`. High trust, but it has drifted from source in at least two places (noted inline).
- **[UNITY]** — Unity 6.3 (`6000.3`) official Scripting API / Manual.
- **[DERIVED]** — not stated anywhere by FishNet; deduced from the source's control flow. Flagged explicitly every time.

No blog posts, videos, or forum answers were used as load-bearing evidence. Two community-adjacent facts (Yak being paid, docs drift) come from first-party pages and release notes respectively.

Project context this was written against: Unity 6.3 (`6000.3.16f1`), URP, new Input System, Unity PhysX rigidbodies — a board with raycast hover/wheel suspension plus a ball. One developer plus an agent. No networking ships in the current milestone.

---

## 1. Version: is "Prediction V2" current?

Yes, and the name is now vestigial — there is only one prediction system, and it is V2. The phrase "Prediction V2" survives only in old URLs and old tutorials.

| Fact | Evidence |
|---|---|
| Latest FishNet release is **4.7.3**, published 2026-09-02 | [GitHub releases API](https://github.com/FirstGearGames/FishNet/releases) |
| Prediction 2 became the **default** in 4.2.0 (2024-04-10) | 4.2.0 release notes: *"Changed made Prediction 2 the default prediction system."* / *"Changed removed Prediction_V2 define. Prediction 2 is now default."* |
| Prediction 1 was **deleted** in 4.3.8 (2024-07-18) | 4.3.8 release notes: *"Removed Prediction 1."* |
| Docs no longer version the prediction section | Current path is `/docs/guides/features/prediction`; the old `/manual/guides/prediction/version-2/...` paths 404 |
| Unity 6 LTS is fully supported | [DOC] Unity Compatibility table: *"Unity 6 LTS — All features work, fully supported."* 4.7.3 release notes add Unity 6.5 scene-handle support, so the 6.x line is actively tracked |

**So: target FishNet 4.7.x, use `[Replicate]` / `[Reconcile]`, and treat any tutorial that mentions `[ReplicateV2]`, `ReplicateV2`, `PredictedObject`, or an `asServer` parameter as obsolete.** Those APIs are gone.

Two known doc-vs-source drifts to be aware of when reading the official guides:

- The "Predicting States in Code" page uses `ReplicateState.ReplayedCreated` as a single enum value. In 4.7.3 `ReplicateState` is `[Flags]` with only `Invalid/Ticked/Replayed/Created`; the equivalent is the extension `state.IsReplayedCreated()`. **[SRC]** `Assets/FishNet/Runtime/Object/Prediction/ReplicateState.cs`
- The `PredictionRigidbody` page shows `NetworkTrigger_OnEnter(Collider other)`. 4.7.0 release notes: *"Changed (break) NetworkCollider/Trigger now provides Tick as well. Callbacks must be updated to include 'uint tick'."*

Version constant oddity, noted for honesty: the `4.7.3` tag's `NetworkManager.FISHNET_VERSION` and `package.json` both still read `4.7.2`. The tag and release notes are the reliable identifier.

---

## 2. What FishNet needs a *tick* to be

This is the load-bearing architectural fact, and it is stronger than "use FixedUpdate".

### 2.1 FishNet takes over the physics clock

When `TimeManager.PhysicsMode` is set to `TimeManager` — which **[DOC]** says is mandatory for prediction (*"When using Client-Side Prediction you must use the TimeManager setting"*) — FishNet does three things at startup **[SRC]** (`Assets/FishNet/Runtime/Managing/Timing/TimeManager.cs`):

1. Sets `Time.fixedDeltaTime = (float)TickDelta`.
2. Sets `Physics.simulationMode = SimulationMode.Script` and `Physics2D.simulationMode = SimulationMode2D.Script`.
3. From then on calls `Physics.Simulate(tickDelta)` itself, once per tick.

`SimulationMode.Script` means, per **[UNITY]** ([`SimulationMode`](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/SimulationMode.html)), *"execute the physics simulation manually when you call Physics.Simulate"*. And per **[UNITY]** ([`Physics.Simulate`](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Physics.Simulate.html)), in this mode `Physics.Simulate` *"won't trigger FixedUpdate — that continues running independently at the rate defined by Time.fixedDeltaTime"*.

**Consequence: `FixedUpdate` still fires, but it is no longer the physics step.** It is a timer that happens to share a period with the tick and runs at an unrelated point in the frame. Gameplay code in `FixedUpdate` is therefore *worse* than gameplay code in `Update` — it looks correct and isn't.

### 2.2 The tick loop is driven from `Update`, not `FixedUpdate`

**[SRC]** `Assets/FishNet/Runtime/Transporting/NetworkReaderLoop.cs` is a `[DefaultExecutionOrder(short.MinValue)]` MonoBehaviour on the TimeManager's GameObject whose `Update()` calls `TimeManager.TickUpdate()`. `TickUpdate` accumulates `Time.unscaledDeltaTime` and runs a `do…while` that fires, per elapsed tick, in this exact order **[SRC]** (`TimeManager.IncreaseTick`):

```
OnPreTick
  → PredictionManager.ReconcileToStates()     // corrections + replay happen HERE, before OnTick
  → OnTick                                    //   ← your replicate runs here
  → OnPrePhysicsSimulation
  → Physics.Simulate(tickDelta) + Physics2D.Simulate(tickDelta)
  → OnPostPhysicsSimulation
  → OnPostTick                                //   ← your CreateReconcile runs here (rigidbodies)
  → PredictionManager.SendStateUpdate()
```

Two things follow directly:

- **Inputs go in on `OnTick`; rigidbody state comes out on `OnPostTick`.** **[DOC]** is explicit: *"When using physics bodies, such as a rigidbody, you would send the reconcile during OnPostTick because you want to send the state after the physics have simulated your replicate inputs."*
- **A frame can run zero, one, or several ticks.** `Allow Tick Dropping` + `Maximum Frame Ticks` cap the catch-up burst **[DOC]**. This is Unity's own accumulator problem (**[UNITY]** [fixed updates](https://docs.unity3d.com/6000.3/Documentation/Manual/fixed-updates.html)) reimplemented on top of `Update`, and it is why anything per-frame is structurally unsuited to driving simulation state.

### 2.3 Tick rate is a knob, and it is the fixed timestep

`TimeManager.TickRate` sets `TickDelta`, which becomes `Time.fixedDeltaTime`. Whatever tick rate we pick is the physics step for the whole game, offline and online. Picking it once, early, and writing all tuning against `TickDelta` is free; retro-fitting it is not, because every force constant is implicitly calibrated to the step size.

`TimeManager` also warns that only one instance may own manual physics **[SRC]**, so there is exactly one clock in the project.

---

## 3. The shape FishNet wants: replicate and reconcile

These are not style preferences. FishNet's IL post-processor (`PredictionProcessor`) **hard-fails the build** on each of the following. Quoted strings are the literal error messages from **[SRC]** `Assets/FishNet/CodeGenerating/Processing/Prediction/PredictionProcessor.cs`.

### 3.1 Method-shape rules (compile-time enforced)

| Rule | Error if violated |
|---|---|
| Replicate method takes **exactly 3** parameters, in order: data, `ReplicateState state = ReplicateState.Invalid`, `Channel channel = Channel.Unreliable` | *"Replicate method … requires exactly 3 parameters. In order: replicate data, state = ReplicateState.Invalid, channel = Channel.Unreliable"* |
| Reconcile method takes **exactly 2**: data, `Channel channel = Channel.Unreliable` | *"Reconcile method … requires exactly 2 parameters. In order: reconcile data, channel = Channel.Unreliable."* |
| Both prediction methods must be **`private`** | *"Method … is a prediction method and must be private."* (comment in source: this guards against `base.Replicate` being called from another replicate, which would run it twice) |
| A type has **at most one** replicate/reconcile pair | *"… contains multiple prediction sets; currently only one set is allowed."* |
| You cannot have one without the other | *"… must contain both a [Replicate] and [Reconcile] method when using prediction."* |
| You must `override CreateReconcile()` **and call your reconcile method from inside it** — codegen scans the IL for the call | *"… does not implement method CreateReconcile …"* / *"… implements CreateReconcile but does not call reconcile method …"* |
| Max 255 replicated methods per inheritance hierarchy | `MAX_PREDICTION_ALLOWANCE = byte.MaxValue` **[SRC]** `NetworkBehaviourHelper.cs` |

Canonical shape **[DOC]** ([Controlling an Object](https://fish-networking.gitbook.io/docs/guides/features/prediction/creating-code/controlling-an-object.md)):

```csharp
[Replicate]
private void RunInputs(ReplicateData data,
                       ReplicateState state = ReplicateState.Invalid,
                       Channel channel = Channel.Unreliable) { … }

[Reconcile]
private void ReconcileState(ReconcileData data,
                            Channel channel = Channel.Unreliable) { … }

public override void CreateReconcile()
{
    ReconcileData rd = new ReconcileData(PredictionRigidbody, _stamina);
    ReconcileState(rd);   // codegen verifies this call exists
}
```

`CreateReconcile()` must be called **every tick on every peer** — client, server, owner or not. **[DOC]**: *"be sure to call CreateReconcile no matter if client, server, owner or not."* The client uses its own locally-built reconcile as a fallback when a server state packet is lost **[SRC]** (`NetworkBehaviour.Reconcile_Client_AddToLocalHistory`, and the demo's comment *"The client will use their copy as a fallback if they do not get data from the server"*).

### 3.2 `IReplicateData` — the input struct

The interface is tiny **[SRC]** `Assets/FishNet/Runtime/Object/Prediction/Interfaces.cs`:

```csharp
public interface IReplicateData
{
    uint GetTick();
    void SetTick(uint value);
    void Dispose();
}
```

`IReconcileData` is **byte-for-byte the same three members**. There is no structural difference between the two interfaces; the difference is entirely in how FishNet uses them.

Constraints that are not obvious from the interface:

1. **Struct, not class.** *"Prediction methods must use a class or structure as the first parameter type. Structures are recommended to avoid allocations."* **[SRC]**. Structs, always — these are created every tick.
2. **The tick backing field must be `private` or `protected`.** Codegen literally walks the IL of `GetTick()` looking for an `ldfld` and then checks the field's accessibility **[SRC]** (`TickFieldIsNonSerializable`): *"Make a new private or protected field of uint type and return it's value within GetTick."* The method name says why — **private/protected means not serialized**. Which means:
3. **Every field you want sent must be `public`.** The tick field being excluded *by virtue of being private* is the proof that public = on the wire.
4. **It must be FishNet-serializable.** *"Replicate data type … does not support serialization. Use a supported type or create a custom serializer."* Generics and arrays additionally need a hand-written comparer **[DOC]** ([Custom Comparers](https://fish-networking.gitbook.io/docs/guides/features/prediction/custom-comparers.md)) — `[CustomComparer]` on a `static bool Compare(T a, T b)`. Avoid arrays in input data entirely and this never comes up.
5. **`default` must mean "no input".** This is the sharpest non-obvious constraint. **[SRC]** `NetworkBehaviour.Replicate_Authoritative` calls `PublicPropertyComparer<T>.IsDefault(data)` and, if the data is all-default, stops resending it; the receiving side then synthesises `default` data with the `Created` flag *absent*. **[DOC]**: *"On non-owned objects a number of replicates will arrive as ReplicateState Created, but will contain default values. This is our PredictionManager.RedundancyCount feature working."* If an input field's neutral value isn't zero/false, FishNet's bandwidth optimisation will read "idle" as "that value", and idle will behave like input.
6. **`Dispose()` exists for allocating data.** If the struct holds nothing that allocates, `public void Dispose() { }` is correct and complete **[DOC]**.

Reference shape **[DOC]**:

```csharp
public struct ReplicateData : IReplicateData
{
    public bool Jump;          // public  → serialized
    public float Horizontal;   // public  → serialized
    public float Vertical;

    private uint _tick;        // private → NOT serialized (required)
    public void Dispose() { }
    public uint GetTick() => _tick;
    public void SetTick(uint value) => _tick = value;
}
```

Note `SetTick` is FishNet's job, not ours — the demo comments *"Tick is set at runtime. There is no need to manually assign this value."* **[SRC]**.

### 3.3 `IReconcileData` — the state struct

Same three members. The content rule is stated as a danger callout in **[DOC]** ([Advanced Controls](https://fish-networking.gitbook.io/docs/guides/features/prediction/creating-code/advanced-controls.md)), and it is the single most important sentence in the whole prediction documentation:

> **If a value can affect your prediction do not store it outside the replicate method, unless you are also reconciling the value. An exception applies if you are setting the value inside your replicate method.**

The worked example is stamina: if you don't reconcile it, a replay re-runs inputs against post-replay stamina, the sprint that previously succeeded now fails, and you get a permanent desync that reads as jitter.

So the reconcile struct is: **every field of the behaviour that both (a) survives across ticks and (b) influences the replicate's output.** For our board that is at minimum the `PredictionRigidbody` references, plus every gameplay counter — boost charge, air time, trick-in-progress state, grounded-grace ticks, cooldown ticks.

### 3.4 `ReplicateState` — the fourth thing the replicate must respect

`ReplicateState` is a `[Flags] byte` **[SRC]**: `Invalid=0, Ticked=1, Replayed=2, Created=4`, with extensions `ContainsTicked/ContainsReplayed/ContainsCreated`, `IsTickedCreated`, `IsReplayedCreated`, and `IsFuture()` (which is exactly `state == Replayed`, i.e. replayed but never ticked and not created).

Three usage rules from **[DOC]** ([Using States in Code](https://fish-networking.gitbook.io/docs/guides/features/prediction/creating-code/understanding-replicatestate/using-states-in-code.md)):

- **One-shot side effects** — audio, VFX, a trick-registered ping — must be gated on `state.ContainsTicked() && !state.ContainsReplayed()`, or they re-fire on every replay.
- **Animator / visual reads of input** should be gated on `state.ContainsCreated()`, or they flicker between real input and the default-filled gaps.
- **Spectated objects in the future** (`state.IsFuture()`) should have their velocities zeroed or the body paused, because *"physics simulates with every tick, replayed or not, regardless of if replicate runs"* — a rigidbody carries inertia into the future even if your replicate returns early.

---

## 4. Rigidbody prediction and reconciliation against Unity PhysX

### 4.1 What actually gets snapshotted

A `PredictionRigidbody` serializes exactly one thing: a `RigidbodyState` **[SRC]** `Assets/FishNet/Runtime/Object/Prediction/PredictionRigidbody.cs` →

```csharp
public struct RigidbodyState        // [SRC] Runtime/Generated/Component/Prediction/RigidbodyState.cs
{
    public Vector3    Position;        // rb.transform.localPosition
    public Quaternion Rotation;        // rb.transform.localRotation
    public bool       IsKinematic;
    public Vector3    Velocity;        // rb.linearVelocity on Unity 6000.1+
    public Vector3    AngularVelocity;
}
```

Velocity and angular velocity are skipped on the wire entirely when `IsKinematic` is true **[SRC]** (`WriteRigidbodyState`). Rotation is quaternion-packed.

**That is the whole physical snapshot.** Five values. Position, rotation, linear velocity, angular velocity, kinematic flag.

Note also **[SRC]**, in the `PredictionRigidbody` writer:

```csharp
/* This used to write pr.GetPendingForces() but is no longer needed, assuming the user properly
 * reconciles everything that modifies the predictionRigidbody. */
```

Pending forces are no longer sent. FishNet's correctness now *depends* on us reconciling every field that feeds force application. The comment is the contract.

### 4.2 What is therefore NOT snapshotted, and must be reconstructable

Anything PhysX holds that isn't in those five values is not restored on a correction. In practice:

- **Contact state / collision manifolds.** Not serialized. Rebuilt by the re-simulation.
- **Sleep state, accumulated drag effects, solver warm-starting.** Not serialized.
- **Every gameplay field on your own script.** Not serialized unless *you* put it in `IReconcileData`.
- **Anything living on a non-networked rigidbody.** See §4.4.

The practical rule: **after `Reconcile` runs, re-running the same inputs from the restored state must reproduce the same outcome.** If it doesn't, something is being read from state that reconcile didn't restore.

### 4.3 How the correction actually replays

**[SRC]** `Assets/FishNet/Runtime/Managing/Prediction/PredictionManager.cs`, inside `ReconcileToStates`:

```
IsReconciling = true
  apply server state → your [Reconcile] method runs
  Physics.SyncTransforms() + Physics2D.SyncTransforms()      // push restored transforms into PhysX
  for tick in (stateTick+1 .. localTick-1):
      your [Replicate] runs with ReplicateState.Replayed …
      Physics.Simulate(tickDelta)                            // full scene re-simulated, per tick
IsReconciling = false
```

Four consequences worth internalising:

1. **The entire physics scene is re-simulated, once per replayed tick.** Not just your object. Whatever else is in the scene gets stepped again — which is why offline bodies need special handling (§4.4).
2. **`Physics.SyncTransforms()` runs once, after reconcile, before the first replay step.** So writing a transform inside `[Reconcile]` is safe and correct; writing one inside `[Replicate]` without your own sync is not, because PhysX won't see it until the next `Simulate`.
3. **A replicate can run many times for the same tick** — once live, then again on every subsequent correction that reaches back past it. Any side effect that isn't idempotent will fire repeatedly. This is what `state.ContainsReplayed()` is for.
4. **Replay excludes the current local tick** (source comment: *"This prevents client from running 1 local tick into the future since the OnTick has not run yet"*).

### 4.4 What breaks reconciliation

Ordered roughly by how likely we are to hit it on this project:

| Breakage | Mechanism | Source |
|---|---|---|
| Touching `Rigidbody` directly instead of `PredictionRigidbody` | Force bypasses FishNet's application/replay bookkeeping | **[DOC]**: *"Be sure to always apply and set velocities using PredictionRigidbody and never on the rigidbody itself; this includes if also accessing from another script."* |
| A cross-tick gameplay field not in the reconcile struct | Replay resumes from the post-replay value → permanent desync, seen as jitter | **[DOC]** Advanced Controls, stamina example |
| A non-networked rigidbody the player interacts with | Re-simulated N extra times during a correction | **[DOC]** Offline Rigidbodies: *"add an OfflineRigidbody component to prevent non-networked objects from simulating multiple times during a correction."* And note what that component actually does **[SRC]**: on `OnPreReconcile` it **pauses** (makes kinematic) the body and unpauses on `OnPostReconcile`. It does **not** rewind it. During replay the body sits frozen at its *present* pose, not its pose at the replayed tick — so any collision against it during replay is wrong. Gameplay-relevant dynamic objects must be networked predicted objects, not offline rigidbodies. |
| `OnCollisionEnter` / `OnTriggerEnter` used for gameplay | Unity's enter/exit callbacks don't reliably fire under manual re-simulation | **[DOC]** Network Collider: *"These components are needed because of a limitation in Unity's physics system that affects their OnCollisionEnter and OnCollisionExit methods, causing them to not always be executed."* Use `NetworkCollision` / `NetworkTrigger`. Constraint to note: *"we currently only support these components on primitive shapes: box, cube, sphere, circle, etc."* |
| Multiple rigidbodies, only one reconciled | The unreconciled bodies drift | **[DOC]**: *"If you are using multiple rigidbodies you at the very least need to reconcile their states as well."* |
| A `CharacterController` moved without disabling it first | Its internal state doesn't refresh; transform and physics disagree | **[DOC]** Controlling an Object. (Not applicable to us — we're on rigidbodies — but it rules out CharacterController as a shortcut.) |
| Graphics colliders under the smoothed graphical object | Smoothing rolls the graphical transform back each tick; anything gameplay-relevant beneath it is in the wrong place | **[DOC]** Configuring NetworkObject: *"typically your colliders and triggers which affect gameplay or transforming the NetworkObject should be on the same GameObject as your NetworkObject. Or at the very least, not within the graphical object."* |

### 4.5 The multi-body vehicle case — directly relevant to our board

FishNet ships a demo that is almost exactly our board: a root rigidbody plus two wheel rigidbodies plus a boost pad — **[SRC]** `Assets/FishNet/Demos/Prediction/Rigidbody/Scripts/RigidbodyPrediction.cs`. Its reconcile data is the template to copy:

```csharp
public struct ReconcileData : IReconcileData
{
    public PredictionRigidbody Root;
    public PredictionRigidbody FrontWheel;
    public PredictionRigidbody RearWheel;
    public uint BoostStartTick;        // ← a duration, stored as a TICK, and reconciled
    public bool SpringNextReplicate;   // ← a deferred impulse flag, reconciled
    private uint _tick; …
}
```

Two patterns to lift wholesale:

**Durations are tick numbers, not seconds.** Boost is `_boostStartTick` plus `TimeManager.TimeToTicks(_boostDuration, TickRounding.RoundUp)`, compared against the *data's* tick (`rd.GetTick()`), not against the current tick:

```csharp
if (_boostStartTick != TimeManager.UNSET_TICK && rdTick >= _boostStartTick)
{
    forwardForce += new Vector3(0f, 0f, _boostForce);
    uint endTick = _boostStartTick + TimeManager.TimeToTicks(_boostDuration, TickRounding.RoundUp);
    if (rdTick >= endTick) _boostStartTick = TimeManager.UNSET_TICK;
}
```

The comment explains why the `rdTick >= _boostStartTick` guard exists: *"This is done in the scenario a boost happened outside replay, we don't want to boost during a replay before the boost started."* Timers keyed to ticks replay correctly; timers keyed to seconds do not.

**Forces are not multiplied by delta.** From the same file:

```
/* Notice that forces are NOT multiplied by delta. Just like Unity physics,
 * predictionRigidbodies do not include delta in calculated forces. */
```

Where you *do* need a per-step scalar — a stamina drain, a charge meter — use `(float)base.TimeManager.TickDelta` **[DOC]**, never `Time.deltaTime`. **[UNITY]** backs this from the engine side: *"If you pass framerate-dependent step values (such as Time.deltaTime) to the physics engine, your simulation will be non-deterministic because of the unpredictable fluctuations in framerate."*

### 4.6 Determinism: how much do we actually need?

Less than people assume, and the map's existing position ("full bit-determinism is not required; FishNet reconciles state snapshots") is correct.

FishNet's model is **state reconciliation**, not lockstep. The server periodically ships an authoritative `RigidbodyState`; the client snaps to it and replays. Small PhysX divergence between machines is corrected on the next reconcile rather than compounding. What matters is not cross-machine bit-equality but **within-machine replay reproducibility**: the same inputs from the same restored state must give the same result *on this machine, this run*. That is what randomness, wall-clock time, and per-frame deltas destroy — and PhysX's own float noise does not.

`RigidbodyState` is also allowed to be sent less than every tick — the demo shows throttling to every 10th tick **[SRC]** — so reconcile frequency is a tuning knob, not an architectural commitment.

---

## 5. Patterns that silently poison prediction later

Each of these compiles, runs, and feels fine in a local-only build. Each becomes a desync the day networking turns on. This is the list the ticket specifically asked for.

### 5.1 State mutated in `Update`

`Update` runs once per frame; ticks run zero-to-many times per frame **[SRC]** `TimeManager.IncreaseTick`. Anything advanced per frame is advanced at a rate that varies with the player's hardware, and — fatally — is **not** re-advanced during a replay. `PredictionManager.ReconcileToStates` replays `[Replicate]`; it does not replay `Update`.

What `Update` is legitimately for, per **[DOC]**: **latching edge-triggered input only.**

```csharp
private void Update()
{
    if (base.IsOwner && Input.GetKeyDown(KeyCode.Space))
        _jump = true;              // a latch, read and cleared in CreateReplicateData
}
```

The latch is consumed once when building the replicate data and immediately reset (`_jump = false`). It is a mailbox, not state.

Corollary: **`FixedUpdate` is not a safe alternative** (§2.1). Under `SimulationMode.Script` it is decoupled from the physics step.

### 5.2 Non-deterministic randomness

**[DERIVED]** — FishNet documents nothing about RNG; this follows from `ReconcileToStates` re-invoking the replicate delegate for the same tick on every correction.

A shared `System.Random` or `UnityEngine.Random` advances its internal state on each call. Call it inside a replicate and the first (live) run and the fifth (replayed) run for the same tick get different numbers, so the same input produces different physics, so the reconcile never converges. And the sequence is per-machine, so server and client disagree from the first call.

Two fixes, both cheap:

- **Derive from the tick.** A pure hash of `(data.GetTick(), objectId, salt)` gives the same value for the same tick on every replay and every machine.
- **Or carry the generator state in `IReconcileData`** so it is restored on correction. Works, costs bandwidth, only worth it for a long stateful sequence.

Randomness outside the simulation — cosmetic particle jitter, audio pitch variation, announcer lines — is unaffected and needs no ceremony. For 50-50 the relevant cases are trick-scoring tie-breaks and any bail variation; keep both tick-derived.

### 5.3 Coroutines, `Invoke`, and wall-clock timers

**[DERIVED]** from the same mechanism, plus direct support from the demo's tick-based boost timer (§4.5).

A coroutine is driven by Unity's frame/`WaitForSeconds` scheduler. It does not rewind, does not replay, and does not know what tick it is on. A correction that reaches back past the coroutine's start cannot undo it, and a replayed replicate that would have started it cannot start it again correctly. `Invoke`, `InvokeRepeating`, `Time.time` deadlines and `await Task.Delay` all share the flaw.

Replacement: **a `uint` tick deadline in a reconciled field**, compared against the *replicate data's* tick.

```csharp
private uint _bailEndTick = TimeManager.UNSET_TICK;   // and it lives in ReconcileData

// inside [Replicate]:
if (_bailEndTick != TimeManager.UNSET_TICK && rd.GetTick() >= _bailEndTick)
    _bailEndTick = TimeManager.UNSET_TICK;            // bail recovery complete
```

`TimeManager.UNSET_TICK` is `0` **[SRC]**, and `TimeManager.TimeToTicks(seconds, TickRounding.RoundUp)` converts designer-facing seconds to ticks. Tune in seconds; store and compare in ticks.

For 50-50 this hits: bail recovery, boost duration, trick input windows, kickoff countdown, the match timer. All of them.

### 5.4 Physics reads outside the tick

A raycast is a read of PhysX's current state. Its answer depends entirely on *when* it is issued, and there are three distinct "whens":

- **Inside `[Replicate]`** — correct. During a replay the scene has been rewound (reconcile wrote transforms, `Physics.SyncTransforms()` pushed them into PhysX) and re-stepped tick by tick, so the raycast sees the world as it was. **[DOC]** does exactly this for a ground check: *"`IsGrounded()` … we're going to pretend it uses a raycast or overlap to check"*, inside the replicate.
- **In `Update` / `LateUpdate`** — wrong. The scene is mid-frame and, under `SimulationMode.Script`, at whatever state the last `Simulate` left it. Never re-run on replay.
- **In a physics callback** — wrong, and unreliable. `OnCollisionEnter` / `OnTriggerEnter` *"do not always get executed"* under re-simulation **[DOC]**; use `NetworkCollision` / `NetworkTrigger`, which give enter/stay/exit that survive replay (and since 4.7.0 hand you the `uint tick`).

This is directly load-bearing for us: **the board's hover/wheel suspension raycasts must be issued inside the replicate method**, and the suspension force applied through `PredictionRigidbody` in the same call. Suspension in `FixedUpdate` is the default way to write this and is exactly wrong here.

One caveat from §4.4: raycasts during replay see *networked, reconciled* objects in their rewound pose, and `OfflineRigidbody` objects **frozen at their present pose**. So the ball must be a predicted NetworkObject — if it were an offline rigidbody, every suspension or contact query against it during a replay would be answered against the wrong position.

### 5.5 `Time.deltaTime` in gameplay maths

Three independent reasons, all cited above: **[UNITY]** says frame-dependent steps make physics non-deterministic; **[SRC]** says prediction rigidbody forces don't take delta at all; **[DOC]** uses `(float)base.TimeManager.TickDelta` wherever a per-step scalar is genuinely needed.

Also note `TimeManager` accumulates on `Time.unscaledDeltaTime` **[SRC]**, so `Time.timeScale` does not slow the tick loop — another reason `Time.deltaTime` and tick time are not interchangeable.

Rule of thumb: inside the simulation, `Time.deltaTime`, `Time.fixedDeltaTime`, `Time.time` and `Time.realtimeSinceStartup` are all banned. `TimeManager.TickDelta` and `TimeManager.LocalTick` replace them. `Time.deltaTime` remains correct for camera smoothing, UI tweens, and anything else purely visual.

### 5.6 Summary table

| Poison | Replaces with |
|---|---|
| State advanced in `Update` | State advanced in `[Replicate]`; `Update` latches edge input only |
| Gameplay in `FixedUpdate` | `[Replicate]` via `TimeManager.OnTick` / `TickNetworkBehaviour` |
| `Random.value` in sim | Tick-derived hash, or RNG state in `IReconcileData` |
| Coroutine / `Invoke` / `Time.time` deadline | `uint` tick deadline in a reconciled field |
| Raycast in `Update`/`FixedUpdate` | Raycast inside `[Replicate]` |
| `OnCollisionEnter` for gameplay | `NetworkCollision` / `NetworkTrigger` |
| `rb.AddForce(...)` | `predictionRigidbody.AddForce(...)` then `.Simulate()` |
| `x += v * Time.deltaTime` | `x += v * (float)TimeManager.TickDelta`, or no delta at all for forces |
| Private gameplay field not in reconcile | Field mirrored in `IReconcileData` and restored in `[Reconcile]` |

---

## 6. Practical notes for our specific setup

**The board (raycast hover / wheel suspension).** Model on `RigidbodyPrediction.cs` **[SRC]**: root `PredictionRigidbody` plus one per suspension body if we go multi-body, all reconciled. Suspension raycast inside `[Replicate]`; spring/damper force via `PredictionRigidbody.AddForceAtPosition` (supported — it's in the serialized `ForceApplicationType` enum **[SRC]**); one `Simulate()` per `PredictionRigidbody` at the end of the replicate. Grounded/airborne is *computed* in the replicate from the raycast, so it doesn't need reconciling; any *grace window* on it (coyote time) is a tick deadline and does.

**The ball.** A predicted NetworkObject in its own right, using the [Non-Controlled Object](https://fish-networking.gitbook.io/docs/guides/features/prediction/creating-code/non-controlled-object.md) pattern **[DOC]** — an empty `ReplicateData`, a `PredictionRigidbody` reconcile, `RunInputs(default)` and `CreateReconcile()` both called from `OnPostTick`. Not an `OfflineRigidbody` (§4.4). Arena geometry is static and needs neither.

**Trick detection and the pending bank.** These are the map's "plain C#, out of MonoBehaviours, lightweight tests" pieces, and that lines up perfectly with prediction: a pure function `(inputs, state) → state` is trivially replay-safe. Two constraints: the trick state machine's fields must appear in `IReconcileData`, and every input window must be a tick count. The bank itself only changes on a goal, so it is coarse enough to live in reconcile data cheaply.

**The heuristic bot.** Server-controlled objects run replicates too — **[SRC]** `Replicate_Authoritative` allows it when `!Owner.IsValid && IsServerStarted` ("ownerless and server"), and **[DOC]** recommends `base.HasAuthority` for this. So the bot should produce the *same* `ReplicateData` struct a human produces, from a decision function called in `OnTick`. That keeps one input path and makes the bot trivially swappable for a second human later.

**Tick rate.** Pick one now (60 Hz matches Unity's 0.02 default closely enough at 50, or go 60 outright) and never tune a force constant without remembering it is calibrated to that step.

**Smoothing.** Set the NetworkObject's Graphical Object, keep gameplay colliders off it, and let `NetworkTickSmoother` / `OfflineTickSmoother` interpolate visuals between ticks **[DOC]**. Consider "detach graphical object" for the camera target — **[DOC]**: *"Cameras as well can display what appears to be stuttering or jitter of your graphical object, even though it's completely fine."*

---

## 7. Can a single-player build run the same simulation path?

**Yes — but only if FishNet is actually started.** This is the one place where "just write it net-shaped and it'll work" is not quite true, and it's worth being precise because the whole map rests on it.

### 7.1 What runs with no networking started

The tick loop does. `NetworkReaderLoop.Update() → TimeManager.TickUpdate() → IncreaseTick()` has **no `IsServerStarted` / `IsClientStarted` gate** **[SRC]**. Drop a NetworkManager with a TimeManager in the scene and set Physics Mode to TimeManager, and you immediately get: `Time.fixedDeltaTime = TickDelta`, `Physics.simulationMode = Script`, `OnPreTick/OnTick/Simulate/OnPostTick` firing at the tick rate, and a monotonically increasing `LocalTick`. That is the entire fixed-tick simulation spine, with zero connections. The existence of `OfflineTickSmoother` — which smooths non-networked objects between ticks via `InstanceFinder` **[DOC]** — corroborates that ticks are expected to run offline.

### 7.2 What does *not* run with no networking started

`[Replicate]` bodies. **[SRC]** `NetworkBehaviour.Replicate_Authoritative` opens with:

```csharp
bool ownerlessAndServer = !Owner.IsValid && IsServerStarted;
if (!IsOwner && !ownerlessAndServer)
    return;
```

With no server and no owner, neither branch holds, so the replicate never invokes. On top of that, `[Replicate]`/`[Reconcile]` live on `NetworkBehaviour`s whose `OnStartNetwork` only fires when the NetworkObject is spawned. **A truly "no FishNet running" single-player build silently does nothing.**

### 7.3 The fix: single-player *is* host mode

Run server + client in one process. Single-player is then "a host with no remote clients", and there is exactly one code path — the same replicate, the same reconcile data, the same tick.

Two ways to do it:

- **Free (what we should do now):** start server and client on the default Tugboat transport bound to loopback. Real sockets, no external traffic, negligible cost at our scale. One `StartConnection()` pair behind a "Play" button.
- **Paid:** **Yak**, FishNet Pro's offline transport — *"an offline transport that runs your multiplayer code without opening any sockets, seamlessly supporting offline/singleplayer play … you do not need to change any of your code to take advantage of Yak"*, and **[DOC]** recommends pairing it with Multipass to serve offline and online from one build. This is the clean long-term answer but it is behind FishNet Pro; the loopback-host approach is functionally equivalent for our purposes.

Either way: **one simulation path, chosen at connection time, not at compile time.** No `#if SINGLEPLAYER`, no parallel controller.

### 7.4 The caveat that matters most

**Host mode never reconciles.** **[DOC]**: *"The server is always considered right and never has to correct data, so it never reconciles or replays inputs."* **[SRC]** agrees in a comment inside `Replicate_Authoritative`: *"the server does not reconcile."* In host mode the owner's replicate is invoked exactly once per tick with `ReplicateState.Ticked | ReplicateState.Created` and that's the end of it.

So local-first host mode exercises: the tick cadence, the input struct, the replicate body, the reconcile *data* being built every tick. It does **not** exercise: replay, correction, `state.ContainsReplayed()`, `state.IsFuture()`, or any of the desync bugs those surface.

That is fine — that's the deal the map already made — but it means:

1. **Every rule in §8 must be followed on faith during the local milestone.** The bugs they prevent are invisible until a real client connects. This is exactly why a checkable rules list is the deliverable rather than a test suite.
2. **The cheapest possible reconcile smoke-test is worth having before the slice is called done**: build once, run a second instance connecting to the first, and turn on `TransportManager`'s Latency Simulator (**[DOC]**: latency, packet loss, out-of-order, and *"Simulate Host: When enabled, this will also simulate latency for host client"*). Half a day, and it converts every §8 violation from a future mystery into a visible jitter.

### 7.5 Cost of following this now vs. retrofitting

Adopting the shape now costs: a NetworkManager in the scene, `TickNetworkBehaviour` instead of `MonoBehaviour`, an input struct we wanted anyway for the new Input System, a reconcile struct that's mostly a field list, and the discipline in §8. Call it a day.

Retrofitting later costs: rewriting every controller, re-tuning every force constant against a new fixed timestep, converting every timer, and re-deriving feel — which the map explicitly identifies as the failure mode to avoid. The asymmetry is large and the research supports the map's existing commitment without qualification.

---

## 8. The rules — checkable list

A future session can hold new gameplay code against this and answer yes or no per line. Grouped by what they protect. `[COMPILE]` = FishNet's IL post-processor fails the build. `[SILENT]` = compiles and runs locally; breaks only once networking is live.

### A. Where simulation code lives

| # | Rule | Enforced |
|---|---|---|
| A1 | Every gameplay behaviour that changes simulation state derives from `NetworkBehaviour` (or `TickNetworkBehaviour`) and runs its logic inside a `[Replicate]` method. | `[SILENT]` |
| A2 | No game state is mutated in `Update`, `LateUpdate`, or `FixedUpdate`. `Update` may only latch edge-triggered input into a field that `CreateReplicateData` reads and immediately clears. | `[SILENT]` |
| A3 | `FixedUpdate` contains no gameplay at all. Under `SimulationMode.Script` it is not the physics step. | `[SILENT]` |
| A4 | Replicate is invoked from `OnTick`. `CreateReconcile()` is invoked from `OnPostTick` for anything touching a rigidbody. | `[SILENT]` |
| A5 | `CreateReconcile()` is called every tick on every peer — not gated on `IsOwner` or `IsServerStarted`. | `[SILENT]` |
| A6 | Exactly one NetworkManager/TimeManager exists, with Physics Mode = `TimeManager`. | Runtime error |

### B. Method and struct shape

| # | Rule | Enforced |
|---|---|---|
| B1 | Replicate signature is exactly `private void X(TData d, ReplicateState state = ReplicateState.Invalid, Channel channel = Channel.Unreliable)`. | `[COMPILE]` |
| B2 | Reconcile signature is exactly `private void Y(TData d, Channel channel = Channel.Unreliable)`. | `[COMPILE]` |
| B3 | Both prediction methods are `private`. | `[COMPILE]` |
| B4 | The class `override`s `CreateReconcile()` and calls its reconcile method from inside it. | `[COMPILE]` |
| B5 | At most one replicate/reconcile pair per class. | `[COMPILE]` |
| B6 | Input and state types are `struct`s implementing `IReplicateData` / `IReconcileData`, with `GetTick`/`SetTick`/`Dispose`. | `[COMPILE]` |
| B7 | The `_tick` backing field is `private` or `protected` and is never set by our code. | `[COMPILE]` |
| B8 | Every field meant to travel is `public`; everything private in a prediction struct is deliberately excluded from the wire. | `[SILENT]` |
| B9 | Prediction structs contain no arrays, collections, or generics — or ship a `[CustomComparer]` if they must. | `[COMPILE]` |
| B10 | `default(TReplicateData)` means "no input". No field's neutral value is non-zero/non-false. | `[SILENT]` |

### C. State and reconciliation

| # | Rule | Enforced |
|---|---|---|
| C1 | Every field that survives across ticks *and* affects the replicate's output appears in `IReconcileData` and is restored in `[Reconcile]`. If you add a field to a predicted behaviour, you add it to the reconcile struct in the same commit. | `[SILENT]` |
| C2 | Fields that are recomputed from scratch inside the replicate every tick (e.g. grounded-from-raycast) are exempt from C1 — but any *grace window* on them is a tick deadline and is not exempt. | `[SILENT]` |
| C3 | Every rigidbody the player's simulation touches has its own `PredictionRigidbody`, and every one is reconciled. | `[SILENT]` |
| C4 | Forces and velocities are applied through `PredictionRigidbody`, never on `Rigidbody` directly — including from other scripts. `Simulate()` is called once per `PredictionRigidbody` at the end of the replicate, and *only* from inside the replicate. | `[SILENT]` |
| C5 | Any dynamic rigidbody that affects gameplay (the ball) is a predicted NetworkObject, not an `OfflineRigidbody`. Non-gameplay dynamic props get `OfflineRigidbody`. | `[SILENT]` |
| C6 | Trigger and collision gameplay uses `NetworkCollision` / `NetworkTrigger` (primitive colliders only), never raw `OnCollisionEnter`/`OnTriggerEnter`. | `[SILENT]` |
| C7 | Gameplay colliders sit on the NetworkObject's own GameObject, never under the smoothed Graphical Object. | `[SILENT]` |

### D. Determinism under replay

| # | Rule | Enforced |
|---|---|---|
| D1 | No `Random` / `System.Random` inside a replicate. Randomness in simulation is a pure function of `(data.GetTick(), objectId, salt)`, or its generator state is reconciled. Purely cosmetic randomness is exempt. | `[SILENT]` |
| D2 | No coroutines, `Invoke`, `InvokeRepeating`, `Task.Delay`, or `Time.time`/`Time.realtimeSinceStartup` deadlines driving gameplay. Durations are `uint` tick deadlines stored in reconciled fields. | `[SILENT]` |
| D3 | Tick deadlines are compared against the **replicate data's** tick (`data.GetTick()`), not `TimeManager.LocalTick`, and guard against firing before their start tick during a replay. | `[SILENT]` |
| D4 | Designer-facing durations stay in seconds in the inspector and are converted with `TimeManager.TimeToTicks(seconds, TickRounding.RoundUp)`. Unset deadlines use `TimeManager.UNSET_TICK`. | `[SILENT]` |
| D5 | No `Time.deltaTime` / `Time.fixedDeltaTime` / `Time.time` in simulation maths. Per-step scalars use `(float)base.TimeManager.TickDelta`. Forces take no delta at all. Visual-only code may use `Time.deltaTime` freely. | `[SILENT]` |
| D6 | All physics queries (raycasts, overlaps, spherecasts) that affect simulation are issued **inside** the replicate method. | `[SILENT]` |
| D7 | No static or singleton mutable state read or written inside a replicate unless it is reconciled. | `[SILENT]` |
| D8 | No reads of `Input`, the Input System, or any device state inside a replicate — input arrives only via the data struct. | `[SILENT]` |

### E. Replay-awareness of side effects

| # | Rule | Enforced |
|---|---|---|
| E1 | One-shot effects (audio, VFX, haptics, score events, HUD pings) are gated on `state.ContainsTicked() && !state.ContainsReplayed()`. | `[SILENT]` |
| E2 | Animator parameters driven by input are gated on `state.ContainsCreated()`. | `[SILENT]` |
| E3 | Predicted objects we don't own handle `state.IsFuture()` — zero linear and angular velocity and return early, or pause via `NetworkObject.RigidbodyPauser`. | `[SILENT]` |
| E4 | A replicate body is safe to execute N times for the same tick. If a line isn't, it is gated by E1. | `[SILENT]` |

### F. One code path

| # | Rule | Enforced |
|---|---|---|
| F1 | Single-player starts FishNet as a host (server + client in-process). No `#if`, no build-flag branch, no parallel non-networked controller. | `[SILENT]` |
| F2 | The heuristic bot produces the same `ReplicateData` struct a human does, from a decision function called on `OnTick`. Ownerless + server-started makes the server its controller. | `[SILENT]` |
| F3 | Rules and scoring (trick detection, the pending bank, match state) stay pure C# outside MonoBehaviours, called from the replicate, with their cross-tick fields in the reconcile struct. | `[SILENT]` |
| F4 | Tick rate is fixed once, early, and every force constant is understood as calibrated to that step. | `[SILENT]` |
| F5 | Before the slice is called done, run two instances with the Latency Simulator on (latency + packet loss) at least once, to surface any `[SILENT]` violation while the code is still small. | Process |

---

## 9. Open questions

Things this research could not settle from primary sources, flagged so a later ticket can resolve them in-engine rather than by reading:

- **Tick rate choice.** 50 Hz (Unity's 0.02 default) vs 60 Hz is a feel question for a board with suspension springs. Spring stiffness limits are step-size dependent; this needs playing, not reading.
- **One rigidbody or several for the board.** The FishNet demo uses three (root + two wheels). Whether our suspension wants that or a single body with four raycast springs is a feel and tuning question, not a netcode one — both reconcile fine.
- **`PredictionRigidbody.AddForceAtPosition` under replay with many contact points.** Supported and serialized, but suspension applies several force-at-position calls per tick per body; worth watching the reconcile cost once the board exists.
- **Whether FishNet Pro (for Yak + Multipass) is worth buying** before the online map. Not needed for the local milestone; loopback host mode covers it.
- **Adaptive interpolation settings** for a fast arena game. Pure tuning, meaningless before there is motion to judge.

---

## Source index

FishNet documentation (fish-networking.gitbook.io) — Markdown versions available by appending `.md`:
- [Prediction: What Is Client-Side Prediction](https://fish-networking.gitbook.io/docs/guides/features/prediction/what-is-client-side-prediction)
- [Configuring TimeManager](https://fish-networking.gitbook.io/docs/guides/features/prediction/configuring-timemanager)
- [Configuring PredictionManager](https://fish-networking.gitbook.io/docs/guides/features/prediction/configuring-predictionmanager)
- [Configuring NetworkObject](https://fish-networking.gitbook.io/docs/guides/features/prediction/configuring-networkobject)
- [Creating Code: Controlling an Object](https://fish-networking.gitbook.io/docs/guides/features/prediction/creating-code/controlling-an-object)
- [Creating Code: Non-Controlled Object](https://fish-networking.gitbook.io/docs/guides/features/prediction/creating-code/non-controlled-object)
- [Creating Code: Advanced Controls](https://fish-networking.gitbook.io/docs/guides/features/prediction/creating-code/advanced-controls)
- [Understanding ReplicateState](https://fish-networking.gitbook.io/docs/guides/features/prediction/creating-code/understanding-replicatestate) and [Using States in Code](https://fish-networking.gitbook.io/docs/guides/features/prediction/creating-code/understanding-replicatestate/using-states-in-code) / [Predicting States in Code](https://fish-networking.gitbook.io/docs/guides/features/prediction/creating-code/understanding-replicatestate/predicting-states-in-code)
- [PredictionRigidbody](https://fish-networking.gitbook.io/docs/guides/features/prediction/predictionrigidbody), [Custom Comparers](https://fish-networking.gitbook.io/docs/guides/features/prediction/custom-comparers), [Offline Rigidbodies](https://fish-networking.gitbook.io/docs/guides/features/prediction/offline-rigidbodies), [Interpolations](https://fish-networking.gitbook.io/docs/guides/features/prediction/interpolations)
- Components: [TimeManager](https://fish-networking.gitbook.io/docs/fishnet-building-blocks/components/managers/time-manager), [TransportManager](https://fish-networking.gitbook.io/docs/fishnet-building-blocks/components/managers/transportmanager), [Network Collider](https://fish-networking.gitbook.io/docs/fishnet-building-blocks/components/prediction/network-collider), [OfflineRigidbody](https://fish-networking.gitbook.io/docs/fishnet-building-blocks/components/prediction/offlinerigidbody), [TickNetworkBehaviour](https://fish-networking.gitbook.io/docs/fishnet-building-blocks/components/network-behaviour-components/ticknetworkbehaviour), [OfflineTickSmoother](https://fish-networking.gitbook.io/docs/fishnet-building-blocks/components/tick-smoothers/offlineticksmoother)
- Transports: [Yak (Pro)](https://fish-networking.gitbook.io/docs/fishnet-building-blocks/transports/yak-pro-feature), [Multipass](https://fish-networking.gitbook.io/docs/fishnet-building-blocks/transports/multipass)

FishNet source, tag `4.7.3` ([github.com/FirstGearGames/FishNet](https://github.com/FirstGearGames/FishNet)):
- `Assets/FishNet/Runtime/Object/Prediction/Interfaces.cs` — `IReplicateData` / `IReconcileData`
- `Assets/FishNet/Runtime/Object/Prediction/Attributes.cs`, `ReplicateState.cs`, `PredictionRigidbody.cs`
- `Assets/FishNet/Runtime/Generated/Component/Prediction/RigidbodyState.cs`, `OfflineRigidbody.cs`
- `Assets/FishNet/Runtime/Managing/Timing/TimeManager.cs` — tick loop, physics mode
- `Assets/FishNet/Runtime/Managing/Prediction/PredictionManager.cs` — `ReconcileToStates`, replay loop
- `Assets/FishNet/Runtime/Object/NetworkBehaviour/NetworkBehaviour.Prediction.cs` — authoritative/non-authoritative dispatch
- `Assets/FishNet/Runtime/Transporting/NetworkReaderLoop.cs` — where `TickUpdate` is driven from
- `Assets/FishNet/CodeGenerating/Processing/Prediction/PredictionProcessor.cs` — compile-time validation
- `Assets/FishNet/Demos/Prediction/Rigidbody/Scripts/RigidbodyPrediction.cs` — multi-rigidbody vehicle reference
- [Release notes](https://github.com/FirstGearGames/FishNet/releases) — 4.2.0, 4.3.8, 4.7.0, 4.7.3

Unity 6.3 (`6000.3`) documentation:
- [`Physics.Simulate`](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Physics.Simulate.html), [`SimulationMode`](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/SimulationMode.html), [`Physics.simulationMode`](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Physics-simulationMode.html), [`Time.fixedDeltaTime`](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Time-fixedDeltaTime.html), [Fixed updates (Manual)](https://docs.unity3d.com/6000.3/Documentation/Manual/fixed-updates.html)
