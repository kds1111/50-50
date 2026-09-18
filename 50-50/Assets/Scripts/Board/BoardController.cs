using UnityEngine;
using UnityEngine.SceneManagement;

namespace FiftyFifty.Board
{
    /// <summary>
    /// Skateboard movement.
    ///
    /// Physics owns POSITION. The controller owns ROTATION. The rigidbody's rotation is
    /// frozen, and orientation is set directly each step from a heading angle plus the slope
    /// underneath. Nothing can torque the board, so it cannot end up on its back, cannot roll
    /// while carving, and cannot tumble out of an ollie.
    ///
    /// That is a deliberate trade (settled on #16): the board no longer reacts physically to
    /// being knocked. When contact should spin a player, that becomes an explicit rule rather
    /// than an argument with the solver.
    ///
    /// Model:
    ///   - Constant acceleration on the right trigger. The push is an animation over it.
    ///   - Rail-like grip: sideways velocity is scrubbed, not simulated per truck.
    ///   - One direction. No fakie/switch stance.
    ///   - Instant ollie, single height, on A.
    ///   - In the air: yaw only. The board stays flat and lands flat.
    ///   - On the ground: the board follows the slope it is riding.
    ///   - Roll is visual only, on the deck mesh, and never touches physics.
    ///
    /// All simulation runs in FixedUpdate. Nothing mutates board state outside it.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class BoardController : MonoBehaviour
    {
        [Header("Input")]
        [Tooltip("Where intent comes from. Leave empty to find one on this object.")]
        public BoardInputSource InputSource;

        [Header("Suspension")]
        [Tooltip("Points the board is suspended from, in local space. Four is a skateboard.")]
        public Vector3[] WheelPoints =
        {
            new Vector3(-0.14f, 0f, 0.28f),
            new Vector3(0.14f, 0f, 0.28f),
            new Vector3(-0.14f, 0f, -0.28f),
            new Vector3(0.14f, 0f, -0.28f),
        };

        [Tooltip("How far the board floats above the ground at rest.")]
        public float RideHeight = 0.18f;

        [Tooltip("How far past the ride height the suspension still reaches.")]
        public float SuspensionTravel = 0.22f;

        [Tooltip("Stiffness, as acceleration per metre of error.")]
        public float SpringStrength = 140f;

        [Tooltip("Bounce absorption. Critical damping is roughly 2 x sqrt(SpringStrength) " +
                 "(~19 at strength 90). Deliberately above that: overdamped never bounces, " +
                 "it just settles. Lower it toward 19 if the board feels sluggish on bumps.")]
        public float SpringDamper = 24f;

        [Tooltip("Extra reach below full extension where a wheel still counts as touching, " +
                 "so ground contact does not flicker at the edge of the ray.")]
        public float GroundedHysteresis = 0.08f;

        [Tooltip("Cap on suspension acceleration, m/s squared.")]
        public float MaxSpringAcceleration = 220f;

        [Tooltip("How close to the ride height counts as actually landed. Small: this is what " +
                 "makes a landing register on the ground rather than in mid-air.")]
        public float GroundedTolerance = 0.06f;

        [Header("Drive")]
        [Tooltip("Acceleration at full trigger, in m/s squared.")]
        public float Acceleration = 14f;

        [Tooltip("Speed the board will not accelerate past. Gravity and ramps can still exceed it.")]
        public float TopSpeed = 12f;

        [Tooltip("How quickly a coasting board loses speed.")]
        public float RollingResistance = 0.25f;

        [Tooltip("Deceleration at full brake.")]
        public float BrakeStrength = 9f;

        [Tooltip("Once stopped, holding the brake backs the board up. Off makes the brake a " +
                 "brake and nothing else.")]
        public bool ReverseEnabled = true;

        [Tooltip("Forward speed below which the brake stops braking and starts reversing. Small " +
                 "and positive: it is the handover point, not a deadzone.")]
        public float ReverseThreshold = 0.4f;

        [Tooltip("Acceleration while reversing, m/s squared. Deliberately weaker than forward " +
                 "drive — reversing is for getting off a wall, not for playing.")]
        public float ReverseAcceleration = 7f;

        [Tooltip("Fastest the board will travel backwards, m/s.")]
        public float ReverseTopSpeed = 5f;

        [Tooltip("Seconds the brake must stay held after stopping before it starts reversing. " +
                 "Without it, every brake down to walking pace rolls you backwards by accident.")]
        public float ReverseEngageDelay = 0.25f;

        [Header("Steering")]
        [Tooltip("Turn rate in degrees per second at full lock.")]
        public float TurnRate = 130f;

        [Tooltip("Speed at which steering reaches full strength. Below this it scales down.")]
        public float FullSteerSpeed = 3f;

        [Tooltip("How much sideways velocity is scrubbed, as a fraction per 1/60s. 1 = fully on " +
                 "rails, 0 = frictionless ice. Tuned down from 0.92 to 0.2 by playing #18: the " +
                 "rails version drove like the board was slotted into the floor.")]
        [Range(0f, 1f)] public float SidewaysGrip = 0.2f;

        [Tooltip("Steer like a car backing up, where the stick swings the tail. Off by default: " +
                 "the board's rotation is commanded rather than simulated, so 'left turns left' " +
                 "stays true whichever way you are rolling.")]
        public bool InvertSteerInReverse = false;

        [Header("Heading (#19)")]
        [Tooltip("An air spin turns the board and rider. It does NOT turn the direction you " +
                 "drive.\n\n" +
                 "These are two separate things and neither drags the other. Land a 180 and you " +
                 "are facing backwards, still travelling the same way, with every control meaning " +
                 "exactly what it meant before — and nothing snaps back on landing, because " +
                 "nothing about your driving ever moved. The rotation is still accumulated, " +
                 "classified and scored; it is simply not a change of direction.\n\n" +
                 "Off restores the old behaviour, where spinning steers you.")]
        public bool AirSpinIsCosmetic = true;

        [Header("Traction — landing slide (#18)")]
        [Tooltip("Land crooked and the board keeps its heading while momentum carries on the old " +
                 "line, grip returning over the next moment. Off restores the on-rails board.")]
        public bool LandingSlideEnabled = true;

        [Tooltip("Degrees between the nose and the direction of travel that count as a straight " +
                 "landing. Every real landing is a degree or two out; without this the board " +
                 "skates on every touchdown.")]
        public float SlideDeadzoneDegrees = 15f;

        [Tooltip("Angle at which the slide is at full strength. Beyond it nothing gets worse — " +
                 "landing sideways and landing backwards slide the same.")]
        public float SlideFullAngleDegrees = 90f;

        [Tooltip("Sideways grip at a full-strength slide, against SidewaysGrip when hooked up. " +
                 "Careful with this number: grip is a fraction removed per 1/60s, so it bites far " +
                 "harder than it reads — a floor of 0.2 still scrubs a quarter of the slide every " +
                 "step. 0 is a free slide that keeps every bit of sideways momentum.")]
        [Range(0f, 1f)] public float SlideGripFloor = 0f;

        [Tooltip("Seconds of slide at the edge of the deadzone — a barely crooked landing.")]
        public float SlideSecondsAtDeadzone = 0.25f;

        [Tooltip("Seconds of slide at a full-strength landing.")]
        public float SlideSecondsAtFullAngle = 0.8f;

        [Tooltip("Fraction of sideways speed taken at the moment of a full-strength landing, so " +
                 "landing crooked costs something. 0 makes a slide free.")]
        [Range(0f, 1f)] public float SlideLandingScrub = 0.18f;

        [Tooltip("Slowest landing that can slide, m/s. Below it the board just sets down.")]
        public float SlideMinSpeed = 2f;

        [Header("Traction — powerslide (#18)")]
        [Tooltip("L3 (or Left Shift) plus a steering direction breaks traction for a quick turn.")]
        public bool PowerslideEnabled = true;

        [Tooltip("Sideways grip while the powerslide is held. 0 keeps all of your momentum " +
                 "through the drift, which is what it was tuned to by playing #18.")]
        [Range(0f, 1f)] public float PowerslideGrip = 0f;

        [Tooltip("Turn rate multiplier while held. Breaking traction alone gives a SLOWER turn, " +
                 "not a faster one — the board pivots at the same rate while momentum ignores " +
                 "it — so the sharpness has to be explicit.")]
        public float PowerslideTurnMultiplier = 1.7f;

        [Tooltip("Slowest speed at which the powerslide does anything, m/s. Without a floor it " +
                 "is a free pivot button and the steering model stops mattering.")]
        public float PowerslideMinSpeed = 3f;

        [Tooltip("Ignore the throttle while powersliding, so the drift spends the speed you had. " +
                 "This is what stops drifting every corner being strictly better than turning.")]
        public bool PowerslideBlocksThrottle = true;

        [Tooltip("Fraction of speed per second bled while powersliding. 0 by default — the " +
                 "throttle block is the intended cost; this is the second dial if it is not enough.")]
        [Range(0f, 1f)] public float PowerslideDragPerSecond = 0f;

        [Tooltip("Seconds for grip to ramp back after the powerslide is released.")]
        public float PowerslideRecoverySeconds = 0.35f;

        [Header("Slope")]
        [Tooltip("How quickly the board tilts to match the ground it is riding. Higher = snappier.")]
        public float SlopeFollowSpeed = 12f;

        [Tooltip("Steepest slope the board will tilt to match, in degrees.")]
        public float MaxSlopeAngle = 60f;

        [Header("Ollie")]
        [Tooltip("Upward speed added by an ollie, in m/s.")]
        public float PopVelocity = 5.2f;

        [Tooltip("Seconds after landing before another ollie is allowed.")]
        public float PopCooldown = 0.12f;

        [Tooltip("Seconds after a pop where the suspension ignores the ground, so the spring " +
                 "does not immediately fight the jump.")]
        public float PopGroundIgnoreTime = 0.12f;

        [Header("Air")]
        [Tooltip("Spin rate in the air, degrees per second. Stick left/right. Yaw only — the " +
                 "board stays flat, so it always lands flat.")]
        public float AirYawRate = 320f;

        [Tooltip("How quickly the board flattens out after leaving a slope.")]
        public float AirFlattenSpeed = 8f;

        [Tooltip("Air resistance.")]
        public float AirDrag = 0.02f;

        [Header("Landing")]
        [Tooltip("Seconds after touchdown where the suspension is extra damped.")]
        public float LandingSettleTime = 0.2f;

        [Tooltip("How much stiffer the damper is during that settle window.")]
        public float LandingSettleDamping = 1f;

        [Header("Disturbance (ball contact)")]
        [Tooltip("Biggest heading wobble a hit can cause, in degrees either way. Capped hard on " +
                 "purpose (#7): a ball should cost you your line, never your orientation.")]
        public float MaxDisturbanceDegrees = 22f;

        [Tooltip("How fast the wobble oscillates, in Hz.")]
        public float DisturbanceFrequency = 3.5f;

        [Tooltip("How quickly the wobble dies away. Higher = shorter.")]
        public float DisturbanceDecay = 6f;

        [Tooltip("How long Disturbed stays true after a hit. The trick grading on #6 reads this " +
                 "to know a landing was ruined by contact rather than by the player.")]
        public float DisturbedFlagSeconds = 0.5f;

        [Tooltip("STAND-IN for #6's trick tag: airborne yaw past this many turns counts as being " +
                 "in a trick, which is what makes a plain ollie immune to ball contact. Replace " +
                 "the InTrick property with the real tag when the classifier lands.")]
        public float TrickYawTurnsThreshold = 0.25f;

        [Header("Physics")]
        [Tooltip("Extra gravity. 1 = normal. Higher makes airs snappier and less floaty.")]
        public float GravityScale = 1.6f;

        [Header("Visuals")]
        [Tooltip("Deck mesh, leaned into turns. VISUAL ONLY — never affects physics or heading.")]
        public Transform DeckVisual;

        [Tooltip("Rider mesh. Turns with a spin but NOT with a shuvit — a shuvit spins the board " +
                 "under a stationary rider and a 180 turns both, which is the only thing that " +
                 "tells them apart. VISUAL ONLY.")]
        public Transform RiderVisual;

        [Tooltip("How far the deck tips into a full-lock turn, in degrees.")]
        public float LeanAngle = 14f;

        [Tooltip("How quickly the visual lean follows the stick.")]
        public float LeanSpeed = 8f;

        [Header("Debug (read-only)")]
        [SerializeField] private bool _grounded;
        [SerializeField] private float _speed;
        [SerializeField] private int _wheelsOnGround;
        [SerializeField] private float _heading;
        [SerializeField] private float _timeInAir;
        [SerializeField] private float _airYawTurns;

        /// <summary>
        /// How far the board and rider are turned relative to the direction being driven.
        ///
        /// A spin adds to this and leaves the heading alone. The flip button does the exact
        /// opposite — it turns the heading and subtracts the same amount here — so the drive
        /// direction reverses while nothing on screen visibly moves.
        /// </summary>
        [SerializeField] private float _visualYaw;
        [SerializeField] private bool _powersliding;
        [SerializeField] private float _grip;
        [SerializeField] private bool _disturbed;

        public bool Grounded => _grounded;
        public float Speed => _speed;
        public float TimeInAir => _timeInAir;
        public float Heading => _heading;

        /// <summary>The physics body. Read its pose rather than the transform — with
        /// interpolation on, the transform is the rendered pose, not the simulated one.</summary>
        public Rigidbody Body => _rb;

        /// <summary>Signed turns of yaw accumulated since leaving the ground. Reset by a pop.</summary>
        public float AirYawTurns => _airYawTurns;

        /// <summary>
        /// STAND-IN for the trick tag #6 will provide. A plain ollie accumulates no yaw, so it
        /// never counts as a trick — which is what keeps ollieing to block a goal free of risk.
        /// </summary>
        public bool InTrick => NamedTrickTag != null
            ? NamedTrickTag()
            : !_grounded && Mathf.Abs(_airYawTurns) >= TrickYawTurnsThreshold;

        /// <summary>
        /// Set by BoardTrickController while it is enabled, and null otherwise.
        ///
        /// The expression above is the stand-in #7 shipped: airborne with enough accumulated
        /// yaw. #6 rule 12 replaced it rather than refined it — only a NAMED trick makes a
        /// player punishable, and a pure spin never does — so the two are not approximations of
        /// each other. Keeping the stand-in as the fallback is what makes the trick component
        /// removable without changing how the ball behaves in a scene that never had it.
        /// </summary>
        [System.NonSerialized] public System.Func<bool> NamedTrickTag;

        /// <summary>
        /// Cosmetic rotation of the deck mesh, set by BoardTrickController. Identity by default,
        /// which is exactly what the board looked like before tricks existed.
        ///
        /// VISUAL ONLY, and that is load-bearing: #16 froze the rigidbody's rotation and made
        /// the controller the sole owner of orientation, because every rotation bug this project
        /// had came from something else negotiating it. A kickflip turns this mesh 360 degrees
        /// while the simulation board stays flat and level underneath, so suspension, landing
        /// detection and grip never see an upside-down board.
        /// </summary>
        [System.NonSerialized] public Quaternion TrickVisualRotation = Quaternion.identity;


        /// <summary>Grip is below normal: either a crooked landing or a held powerslide.</summary>
        public bool Sliding => _grip < SidewaysGrip - 0.001f;

        /// <summary>The powerslide is held and biting.</summary>
        public bool Powersliding => _powersliding;

        /// <summary>Sideways grip in force this tick, for readouts.</summary>
        public float Grip => _grip;

        /// <summary>Degrees between the nose and the direction of travel. 0 when barely moving.</summary>
        public float SlipAngle
        {
            get
            {
                Vector3 flat = Vector3.ProjectOnPlane(_rb != null ? _rb.linearVelocity : Vector3.zero, Vector3.up);
                return flat.magnitude < 0.2f ? 0f : Vector3.Angle(Vector3.ProjectOnPlane(_forward, Vector3.up), flat);
            }
        }

        /// <summary>True for a moment after ball contact ruined the board's line.</summary>
        public bool Disturbed => _disturbed;

        /// <summary>The intent this board acted on last tick. Read it rather than calling
        /// Read() again — the input source clears its one-shot latches when read, so a second
        /// caller would silently eat an ollie or a punch.</summary>
        public BoardInputState LastInput => _input;

        /// <summary>Scales top speed from outside. Carrying the ball costs speed (#7).</summary>
        [System.NonSerialized] public float ExternalTopSpeedScale = 1f;

        /// <summary>Scales steering from outside, same idea.</summary>
        [System.NonSerialized] public float ExternalTurnScale = 1f;

        /// <summary>Set from outside to forbid the ollie — one of the carry debuffs on #7.</summary>
        [System.NonSerialized] public bool OllieBlocked;

        /// <summary>
        /// Set from outside when tricks performed right now must not be credited. Nothing in
        /// this class reads it; it is here so the classifier on #6 has one place to look.
        /// </summary>
        [System.NonSerialized] public bool TrickCreditBlocked;

        private Rigidbody _rb;
        private PhysicsScene _physicsScene;
        private BoardInputState _input;
        private Vector3 _groundNormal = Vector3.up;
        private Vector3 _surfaceUp = Vector3.up;
        private float _popTimer;
        private float _popIgnoreTimer;
        private float _settleTimer;
        private float _reverseHoldTimer;
        private float _slideTimer;
        private float _slideDuration;
        private float _slideGrip;
        private float _powerslideRecoveryTimer;
        private float _wobbleAmplitude;
        private float _wobblePhase;
        private float _wobbleOffset;
        private float _disturbedTimer;
        private float _visualLean;
        private Vector3[] _wheelOrigins;
        private float[] _wheelDistances;
        private int _hitCount;
        private Vector3 _up = Vector3.up;
        private Vector3 _forward = Vector3.forward;
        private Vector3 _right = Vector3.right;
        private Vector3 _spawnPosition;
        private float _spawnHeading;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();

            // Cast into THIS object's physics scene, not the global one. Identical in an ordinary
            // scene, and the difference is what lets the probes measure the board in a private
            // world without touching the scene you have open. It is also the shape stacked
            // scenes want later, where a server and a client each own their own physics.
            _physicsScene = gameObject.scene.GetPhysicsScene();
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
            _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            _rb.useGravity = false;

            // The whole point: physics never rotates the board. Orientation is ours.
            _rb.freezeRotation = true;

            if (InputSource == null)
            {
                InputSource = GetComponent<BoardInputSource>();
            }

            if (DeckVisual == null)
            {
                // BoardMesh is the cosmetic group everything visible hangs off. Deck is the
                // older, looser shape — scenes built before #6 still have it, and still work.
                DeckVisual = transform.Find("BoardMesh") ?? transform.Find("Deck");
            }

            if (RiderVisual == null)
            {
                RiderVisual = transform.Find("Rider");
            }

            _spawnPosition = transform.position;
            _grip = SidewaysGrip;
            _spawnHeading = transform.eulerAngles.y;
            _heading = _spawnHeading;
            _surfaceUp = Vector3.up;
        }

        private void FixedUpdate()
        {
            SimulateTick(Time.fixedDeltaTime);
        }

        /// <summary>
        /// One simulation step. Public and dt-explicit so a test harness can drive the board
        /// without the player loop — which is how this gets measured rather than guessed at.
        /// </summary>
        public void SimulateTick(float dt)
        {

            _input = InputSource != null ? InputSource.Read() : default;

            // Read the PHYSICS pose, never the transform. With interpolation on, transform is
            // the rendered pose during FixedUpdate — a slightly different height than physics
            // actually has. Feeding that into the suspension makes the spring chase its own
            // interpolation, which self-oscillates: the board bounces with no input at all.
            _up = _rb.rotation * Vector3.up;
            _forward = _rb.rotation * Vector3.forward;
            _right = _rb.rotation * Vector3.right;

            _rb.AddForce(Physics.gravity * GravityScale, ForceMode.Acceleration);

            bool wasGrounded = _grounded;
            ApplySuspension(dt);

            if (_grounded)
            {
                if (!wasGrounded)
                {
                    OnTouchdown();
                }

                UpdateTraction(dt);
                Steer(dt);
                ApplyDrive(dt);
                ApplyGrip(dt);
            }
            else
            {
                EndTraction();
                _timeInAir += dt;
                SpinInAir(dt);
                _rb.AddForce(-_rb.linearVelocity * AirDrag, ForceMode.Acceleration);
            }

            ApplyOllie();
            ApplyHeadingFlip();
            UpdateDisturbance(dt);
            ApplyOrientation(dt);

            _popTimer = Mathf.Max(0f, _popTimer - dt);
            _popIgnoreTimer = Mathf.Max(0f, _popIgnoreTimer - dt);
            _settleTimer = Mathf.Max(0f, _settleTimer - dt);
            _speed = _rb.linearVelocity.magnitude;
        }

        /// <summary>
        /// A spring per wheel, in two passes.
        ///
        /// Pass one finds which wheels are in contact; pass two applies the spring, sharing
        /// the load across exactly those wheels. Two passes because the share depends on the
        /// count — doing it in one pass is what made gravity compensation four times too weak,
        /// so the board sank below its ride height and the spring carried the weight instead
        /// of just correcting error. A spring under constant load rings; that was the bounce.
        /// </summary>
        private void ApplySuspension(float dt)
        {
            if (_popIgnoreTimer > 0f)
            {
                _wheelsOnGround = 0;
                _grounded = false;
                return;
            }

            float maxDistance = RideHeight + SuspensionTravel;
            float probeDistance = maxDistance + GroundedHysteresis;

            _wheelsOnGround = 0;
            _hitCount = 0;
            Vector3 normalSum = Vector3.zero;
            bool nearRideHeight = false;

            EnsureWheelBuffers();

            // Pass one: who is touching?
            for (int i = 0; i < WheelPoints.Length; i++)
            {
                Vector3 origin = _rb.position + (_rb.rotation * WheelPoints[i]);

                if (!_physicsScene.Raycast(origin, -_up, out RaycastHit hit, probeDistance))
                {
                    continue;
                }

                normalSum += hit.normal;

                if (hit.distance > maxDistance)
                {
                    continue;
                }

                _wheelOrigins[_hitCount] = origin;
                _wheelDistances[_hitCount] = hit.distance;
                _hitCount++;

                // Grounded means resting on the surface, NOT merely within suspension reach.
                // Using the full reach meant touchdown fired while the board was still a
                // whole suspension-travel above the ground, killing its fall in mid-air and
                // then dropping it — which read as a bounce.
                if (hit.distance <= RideHeight + GroundedTolerance)
                {
                    nearRideHeight = true;
                }
            }

            _wheelsOnGround = _hitCount;
            _grounded = nearRideHeight;

            if (normalSum.sqrMagnitude > 0.001f)
            {
                _groundNormal = normalSum.normalized;
            }

            if (_hitCount == 0)
            {
                return;
            }

            // Pass two: share the load across the wheels that are actually touching.
            float share = 1f / _hitCount;
            float gravityShare = Physics.gravity.magnitude * GravityScale * share;
            float damper = _settleTimer > 0f ? SpringDamper * LandingSettleDamping : SpringDamper;

            for (int i = 0; i < _hitCount; i++)
            {
                Vector3 origin = _wheelOrigins[i];
                float offset = RideHeight - _wheelDistances[i];

                Vector3 wheelVelocity = _rb.GetPointVelocity(origin);
                float verticalSpeed = Vector3.Dot(wheelVelocity, _up);

                float spring = ((offset * SpringStrength) - (verticalSpeed * damper)) * share;
                float accel = spring + gravityShare;

                // Never pull the board down: a suspension that sucks makes ramps magnetic.
                accel = Mathf.Clamp(accel, 0f, MaxSpringAcceleration * share);

                // Never REVERSE the board's fall, only arrest it. Without this the damper
                // computes a huge force from the impact speed and applies it for a whole
                // step, which converts a 7 m/s landing into a 2 m/s rebound — the bounce.
                if (verticalSpeed < 0f)
                {
                    float arrest = (-verticalSpeed / dt) * share;
                    accel = Mathf.Min(accel, arrest + gravityShare);
                }

                _rb.AddForceAtPosition(_up * accel, origin, ForceMode.Acceleration);
            }
        }

        private void EnsureWheelBuffers()
        {
            if (_wheelOrigins == null || _wheelOrigins.Length < WheelPoints.Length)
            {
                _wheelOrigins = new Vector3[WheelPoints.Length];
                _wheelDistances = new float[WheelPoints.Length];
            }
        }

        /// <summary>Steering turns the heading. Nothing else rotates the board on the ground.</summary>
        private void Steer(float dt)
        {
            float forwardSpeed = Vector3.Dot(_rb.linearVelocity, _forward);

            // Steering authority normally comes from how fast you are going FORWARD, which is
            // right until the board is sideways: mid-drift the nose points away from the travel,
            // forward speed collapses, and the powerslide's extra turn rate is cancelled out by
            // its own effect. While drifting, the board is still moving quickly — measure that.
            float steerSpeed = _powersliding
                ? Vector3.ProjectOnPlane(_rb.linearVelocity, _groundNormal).magnitude
                : Mathf.Abs(forwardSpeed);

            float speedFactor = Mathf.Clamp01(steerSpeed / Mathf.Max(0.01f, FullSteerSpeed));

            float direction = InvertSteerInReverse && forwardSpeed < -0.2f ? -1f : 1f;

            float turnRate = TurnRate * (_powersliding ? PowerslideTurnMultiplier : 1f);

            _heading += _input.Steer * turnRate * ExternalTurnScale * speedFactor * direction * dt;
        }

        /// <summary>In the air the heading keeps turning, which is all a 180 is.</summary>
        /// <summary>
        /// Spin the board in the air. Board and rider turn together; the heading does not move.
        ///
        /// Landing therefore has nothing to put back: the orientation earned in the air is kept,
        /// and the player carries on driving exactly as before. That is #19's verdict, and the
        /// reason there is no snap.
        /// </summary>
        private void SpinInAir(float dt)
        {
            float delta = _input.Attitude.x * AirYawRate * dt;

            if (AirSpinIsCosmetic)
            {
                _visualYaw += delta;
            }
            else
            {
                _heading += delta;
            }

            _airYawTurns += delta / 360f;
        }

        /// <summary>
        /// Turn the direction you drive around, without turning the board.
        ///
        /// The heading gains a half turn and the visual offset loses one, so they cancel exactly:
        /// nothing on screen rotates, but the throttle, the steering and the camera all now mean
        /// the other way. The board carries on pointing wherever the last trick left it, which is
        /// correct — with no switch stance either end leads equally well.
        ///
        /// Ground only. In the air it would silently change where a player lands up driving with
        /// nothing visible happening, which reads as broken rather than powerful.
        ///
        /// Instant, and grip does not fight it: grip scrubs velocity perpendicular to the board,
        /// and a half turn has no perpendicular component, so momentum is kept and the throttle
        /// simply starts working against it. The drive code already copes with a negative forward
        /// speed without a special case.
        /// </summary>
        private void ApplyHeadingFlip()
        {
            if (!_input.HeadingFlipPressed || !_grounded)
            {
                return;
            }

            _heading += 180f;
            _visualYaw -= 180f;
        }

        /// <summary>
        /// A ball hit while mid-trick puts a damped wobble on the heading. It is an offset, not
        /// a change to the heading itself, so it decays back to the line the player chose rather
        /// than stealing it. Amplitude is clamped: contact costs you your line, not your bearings.
        /// </summary>
        public void Disturb(float strength01)
        {
            float added = Mathf.Clamp01(strength01) * MaxDisturbanceDegrees;
            _wobbleAmplitude = Mathf.Min(MaxDisturbanceDegrees, _wobbleAmplitude + added);
            _wobblePhase = 0f;
            _disturbedTimer = DisturbedFlagSeconds;
        }

        private void UpdateDisturbance(float dt)
        {
            _disturbedTimer = Mathf.Max(0f, _disturbedTimer - dt);
            _disturbed = _disturbedTimer > 0f;

            if (_wobbleAmplitude <= 0.01f)
            {
                _wobbleAmplitude = 0f;
                _wobbleOffset = 0f;
                return;
            }

            _wobblePhase += DisturbanceFrequency * 360f * dt;
            _wobbleAmplitude *= Mathf.Exp(-DisturbanceDecay * dt);
            _wobbleOffset = _wobbleAmplitude * Mathf.Sin(_wobblePhase * Mathf.Deg2Rad);
        }

        private void ApplyDrive(float dt)
        {
            Vector3 forward = Vector3.ProjectOnPlane(_forward, _groundNormal).normalized;
            float forwardSpeed = Vector3.Dot(_rb.linearVelocity, forward);

            bool throttleAllowed = !(_powersliding && PowerslideBlocksThrottle);

            if (throttleAllowed && _input.Throttle > 0.01f && forwardSpeed < TopSpeed * ExternalTopSpeedScale)
            {
                _rb.AddForce(forward * (_input.Throttle * Acceleration), ForceMode.Acceleration);
            }

            _rb.AddForce(-_rb.linearVelocity * RollingResistance, ForceMode.Acceleration);

            if (_input.Brake <= 0.01f)
            {
                _reverseHoldTimer = 0f;
                return;
            }

            // One button, two jobs: it scrubs speed while the board is still rolling forward,
            // and once that is spent it backs up. Without the handover you can bury yourself in
            // a wall and have no way out (#7 put walls in the test scene and found this).
            if (forwardSpeed > ReverseThreshold || !ReverseEnabled)
            {
                _reverseHoldTimer = 0f;
                _rb.AddForce(-_rb.linearVelocity * (_input.Brake * BrakeStrength), ForceMode.Acceleration);
                return;
            }

            _reverseHoldTimer += dt;

            if (_reverseHoldTimer < ReverseEngageDelay)
            {
                _rb.AddForce(-_rb.linearVelocity * (_input.Brake * BrakeStrength), ForceMode.Acceleration);
                return;
            }

            if (forwardSpeed > -ReverseTopSpeed * ExternalTopSpeedScale)
            {
                _rb.AddForce(-forward * (_input.Brake * ReverseAcceleration), ForceMode.Acceleration);
            }
        }

        /// <summary>
        /// Scrub sideways velocity so the board goes where it points. Done on the velocity
        /// directly rather than as forces at each truck — per-wheel lateral forces are exactly
        /// what were rolling the board over while carving.
        /// </summary>
        private void ApplyGrip(float dt)
        {
            Vector3 velocity = _rb.linearVelocity;
            Vector3 right = _right;
            float lateral = Vector3.Dot(velocity, right);

            // Frame-rate independent: grip is "fraction removed per 1/60s".
            float scrub = 1f - Mathf.Pow(1f - Mathf.Clamp01(_grip), dt * 60f);
            _rb.linearVelocity = velocity - (right * (lateral * scrub));
        }

        /// <summary>
        /// One traction model, two ways in (#18). Grip is temporarily low and then it comes back:
        /// a crooked landing lowers it for a moment scaled by how crooked, and the powerslide
        /// lowers it for as long as it is held. Sharing the model is what makes landing straight
        /// into a drift work without a line of code for that case.
        ///
        /// Nothing here touches the heading. The board still points exactly where the player
        /// aimed it (#16) — all that changes is whether it travels that way.
        /// </summary>
        private void UpdateTraction(float dt)
        {
            float speed = Vector3.ProjectOnPlane(_rb.linearVelocity, _groundNormal).magnitude;

            bool wantsPowerslide = PowerslideEnabled
                                   && _input.PowerslideHeld
                                   && speed >= PowerslideMinSpeed;

            if (_powersliding && !wantsPowerslide)
            {
                _powerslideRecoveryTimer = PowerslideRecoverySeconds;
            }

            _powersliding = wantsPowerslide;

            if (_powersliding)
            {
                _slideTimer = 0f;
                _grip = PowerslideGrip;

                if (PowerslideDragPerSecond > 0f)
                {
                    _rb.AddForce(-_rb.linearVelocity * PowerslideDragPerSecond, ForceMode.Acceleration);
                }

                return;
            }

            if (_powerslideRecoveryTimer > 0f)
            {
                _powerslideRecoveryTimer = Mathf.Max(0f, _powerslideRecoveryTimer - dt);

                float recovered = PowerslideRecoverySeconds <= 0f
                    ? 1f
                    : 1f - (_powerslideRecoveryTimer / PowerslideRecoverySeconds);

                _grip = Mathf.Lerp(PowerslideGrip, SidewaysGrip, recovered);
                return;
            }

            if (_slideTimer > 0f)
            {
                _slideTimer = Mathf.Max(0f, _slideTimer - dt);

                float through = _slideDuration <= 0f ? 1f : 1f - (_slideTimer / _slideDuration);
                _grip = Mathf.Lerp(_slideGrip, SidewaysGrip, through);
                return;
            }

            _grip = SidewaysGrip;
        }

        /// <summary>Airborne: no surface, no traction state to carry into the landing.</summary>
        private void EndTraction()
        {
            _powersliding = false;
            _grip = SidewaysGrip;
        }

        /// <summary>
        /// How crooked was that landing? The angle between the nose and the direction of travel
        /// decides how long the board slides, how little grip it has, and how much sideways speed
        /// it loses on touchdown. Landing backwards is simply the far end of the same scale — for
        /// now. Whether it should instead leave the rider switch is #19.
        /// </summary>
        private void BeginLandingSlide()
        {
            if (!LandingSlideEnabled)
            {
                return;
            }

            Vector3 velocity = _rb.linearVelocity;
            Vector3 flat = Vector3.ProjectOnPlane(velocity, _groundNormal);

            if (flat.magnitude < SlideMinSpeed)
            {
                return;
            }

            Vector3 nose = Vector3.ProjectOnPlane(_forward, _groundNormal);

            if (nose.sqrMagnitude < 0.0001f)
            {
                return;
            }

            float angle = Vector3.Angle(nose, flat);
            float span = Mathf.Max(0.01f, SlideFullAngleDegrees - SlideDeadzoneDegrees);
            float crooked = Mathf.Clamp01((angle - SlideDeadzoneDegrees) / span);

            if (crooked <= 0f)
            {
                return;
            }

            _slideGrip = Mathf.Lerp(SidewaysGrip, SlideGripFloor, crooked);
            _slideDuration = Mathf.Lerp(SlideSecondsAtDeadzone, SlideSecondsAtFullAngle, crooked);
            _slideTimer = _slideDuration;
            _grip = _slideGrip;

            // The cost of landing crooked, taken once rather than bled, so it is predictable.
            float lateral = Vector3.Dot(velocity, _right);
            _rb.linearVelocity = velocity - (_right * (lateral * SlideLandingScrub * crooked));
        }

        private void ApplyOllie()
        {
            if (!_input.PopPressed || !_grounded || _popTimer > 0f || OllieBlocked)
            {
                return;
            }

            Vector3 velocity = _rb.linearVelocity;
            velocity -= Vector3.Project(velocity, _groundNormal);
            _rb.linearVelocity = velocity + (_groundNormal * PopVelocity);

            _popTimer = PopCooldown;
            _popIgnoreTimer = PopGroundIgnoreTime;
            _grounded = false;
            _airYawTurns = 0f;
        }

        /// <summary>
        /// The whole of the board's rotation, in one place: face the heading, tilt to the
        /// slope while grounded, flatten out in the air. Roll is never part of it.
        /// </summary>
        private void ApplyOrientation(float dt)
        {
            Vector3 targetUp = Vector3.up;

            if (_grounded && Vector3.Angle(Vector3.up, _groundNormal) <= MaxSlopeAngle)
            {
                targetUp = _groundNormal;
            }

            float followSpeed = _grounded ? SlopeFollowSpeed : AirFlattenSpeed;
            _surfaceUp = Vector3.Slerp(_surfaceUp, targetUp, Mathf.Clamp01(followSpeed * dt)).normalized;

            Vector3 headingForward = Quaternion.Euler(0f, _heading + _wobbleOffset, 0f) * Vector3.forward;
            Vector3 forwardOnSurface = Vector3.ProjectOnPlane(headingForward, _surfaceUp);

            if (forwardOnSurface.sqrMagnitude < 0.0001f)
            {
                return;
            }

            _rb.MoveRotation(Quaternion.LookRotation(forwardOnSurface.normalized, _surfaceUp));
        }

        private void OnTouchdown()
        {
            _settleTimer = LandingSettleTime;
            _timeInAir = 0f;
            _airYawTurns = 0f;

            BeginLandingSlide();

            // Deliberately does NOT cancel the board's fall. It used to, and the spring then
            // fired in the same step at a force computed from the impact speed that had just
            // been cancelled — which is what launched the board back up. The suspension alone
            // absorbs the landing now, and cannot overshoot.
        }

        /// <summary>Visual lean only. Outside the physics step because it changes nothing.</summary>
        private void Update()
        {
            if (DeckVisual == null)
            {
                return;
            }

            float wanted = _grounded ? -_input.Steer * LeanAngle : 0f;
            _visualLean = Mathf.Lerp(_visualLean, wanted, Mathf.Clamp01(LeanSpeed * Time.deltaTime));

            // A spin turns the whole assembly. A trick turns only the board, under a rider who
            // stays put — which is the entire difference between a 180 and a shuvit.
            Quaternion spin = Quaternion.Euler(0f, _visualYaw, 0f);

            DeckVisual.localRotation =
                spin * TrickVisualRotation * Quaternion.Euler(0f, 0f, _visualLean);

            if (RiderVisual != null)
            {
                RiderVisual.localRotation = spin;
            }
        }

        /// <summary>
        /// Put the board down somewhere specific, facing a given heading. Used by the bail
        /// recovery on #6, which respawns at the NEAREST safe point rather than at spawn — a
        /// fixed spawn would cost a bailed player the bank and the play, and nobody would
        /// attempt a trick near a goal.
        /// </summary>
        public void RespawnAt(Vector3 position, float heading)
        {
            Vector3 previousSpawn = _spawnPosition;
            float previousHeading = _spawnHeading;

            _spawnPosition = position;
            _spawnHeading = heading;
            Respawn();

            _spawnPosition = previousSpawn;
            _spawnHeading = previousHeading;
        }

        public void Respawn()
        {
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
            _heading = _spawnHeading;
            _surfaceUp = Vector3.up;
            _reverseHoldTimer = 0f;
            _slideTimer = 0f;
            _powerslideRecoveryTimer = 0f;
            _powersliding = false;
            _grip = SidewaysGrip;
            _airYawTurns = 0f;
            _visualYaw = 0f;
            _wobbleAmplitude = 0f;
            _wobbleOffset = 0f;
            _disturbedTimer = 0f;

            Quaternion upright = Quaternion.Euler(0f, _spawnHeading, 0f);
            _rb.position = _spawnPosition;
            _rb.rotation = upright;
            transform.SetPositionAndRotation(_spawnPosition, upright);
            _timeInAir = 0f;
        }

        private void OnDrawGizmosSelected()
        {
            if (WheelPoints == null)
            {
                return;
            }

            Gizmos.color = Color.cyan;
            float maxDistance = RideHeight + SuspensionTravel;
            foreach (Vector3 local in WheelPoints)
            {
                Vector3 origin = transform.TransformPoint(local);
                Gizmos.DrawSphere(origin, 0.02f);
                Gizmos.DrawLine(origin, origin - (transform.up * maxDistance));
            }
        }
    }
}
