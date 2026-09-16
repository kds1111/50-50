using UnityEngine;

namespace FiftyFifty.Board
{
    /// <summary>
    /// Skateboard movement. First pass for issue #16 — expect to retune every number here
    /// by playing, not by reading.
    ///
    /// Decisions this implements (settled by grilling on #16):
    ///   - Constant acceleration on the right trigger. The push is an animation, not
    ///     a physics event (changed 2026-09-15; was a discrete kick).
    ///   - Rail-like grip. No sliding; turns are carving arcs.
    ///   - One direction. No fakie/switch stance.
    ///   - Instant ollie at a single height, on A. No charge.
    ///   - The jump arcs smoothly: the board levels on pop, stays level in the air unless
    ///     the stick says otherwise, and settles on landing instead of bouncing.
    ///   - Attitude control in the air: stick yaws and pitches the board, bumpers roll it.
    ///   - Terrain gives speed back through gravity only. No pumping.
    ///
    /// All simulation runs in FixedUpdate and nothing mutates board state outside it. That
    /// is the one rule from the FishNet research worth respecting this early: breaking it is
    /// what makes a later port a rewrite rather than a refactor.
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

        [Tooltip("Stiffness, as acceleration per metre of error. Around 90 is a firm board; " +
                 "much higher starts to vibrate at a 50Hz physics step.")]
        public float SpringStrength = 90f;

        [Tooltip("Bounce absorption. Critical damping is roughly 2 x sqrt(SpringStrength), " +
                 "so ~19 for a strength of 90. Below that it pogos; far above it feels stuck.")]
        public float SpringDamper = 19f;

        [Tooltip("Extra reach below full extension where a wheel still counts as touching. " +
                 "Stops ground contact flickering on and off at the edge of the ray.")]
        public float GroundedHysteresis = 0.08f;

        [Header("Drive")]
        [Tooltip("Acceleration at full trigger, in m/s squared.")]
        public float Acceleration = 14f;

        [Tooltip("Speed the board will not accelerate past. Gravity and ramps can still exceed it.")]
        public float TopSpeed = 12f;

        [Tooltip("How quickly a coasting board loses speed.")]
        public float RollingResistance = 0.25f;

        [Tooltip("Deceleration at full brake.")]
        public float BrakeStrength = 9f;

        [Header("Steering")]
        [Tooltip("Turn rate in degrees per second at full lock.")]
        public float TurnRate = 110f;

        [Tooltip("Speed at which steering reaches full strength. Below this it scales down.")]
        public float FullSteerSpeed = 4f;

        [Tooltip("How hard the wheels resist sliding sideways. High = rails, low = drifty.")]
        public float Grip = 18f;

        [Tooltip("Visual lean into a turn, in degrees. Purely cosmetic.")]
        public float LeanAngle = 12f;

        [Header("Ollie")]
        [Tooltip("Upward speed added by an ollie, in m/s. One height, no charge.")]
        public float PopVelocity = 5.2f;

        [Tooltip("Seconds after landing before another ollie is allowed.")]
        public float PopCooldown = 0.12f;

        [Tooltip("Cancel spin at the moment of pop, so the jump starts clean and level. " +
                 "Turning this off is what made the board flip onto its back.")]
        public bool LevelOnPop = true;

        [Tooltip("Seconds after a pop where the suspension ignores the ground. Without it the " +
                 "spring is still in contact and immediately fights the jump.")]
        public float PopGroundIgnoreTime = 0.12f;

        [Header("Air Control")]
        [Tooltip("Stick left/right in the air: spin rate about the board's up axis, deg/sec. " +
                 "This is what a 180 or a 360 is made of.")]
        public float AirYawRate = 320f;

        [Tooltip("Stick up/down in the air: pitch rate, deg/sec. Nose up and down.")]
        public float AirPitchRate = 220f;

        [Tooltip("Bumpers in the air: roll rate about the board's long axis, deg/sec. " +
                 "This is what a flip is made of.")]
        public float AirRollRate = 300f;

        [Tooltip("How sharply attitude control responds. Higher = twitchier.")]
        public float AttitudeSharpness = 14f;

        [Tooltip("Air resistance. Mostly stops the board drifting oddly on long airs.")]
        public float AirDrag = 0.02f;

        [Tooltip("How strongly the board returns to level when the stick is neutral. " +
                 "0 = fully committed to whatever rotation you left the ground with.")]
        public float AirAutoLevel = 6f;

        [Tooltip("How much residual spin is bled off each second in the air. Higher = calmer.")]
        public float AirAngularDamping = 4f;

        [Header("Landing")]
        [Tooltip("On landing, ease the board upright over this many seconds. 0 = never.")]
        public float LandingAlignTime = 0.18f;

        [Tooltip("Cancel spin on touchdown. This is most of what stops the board bouncing away.")]
        public bool KillSpinOnLanding = true;

        [Tooltip("Upward speed kept on touchdown. 0 = the landing is fully absorbed, no bounce.")]
        [Range(0f, 1f)] public float LandingBounceRetained = 0f;

        [Tooltip("Seconds after touchdown where the suspension is extra damped, so the spring " +
                 "settles instead of pogoing. This is the rest of the no-bounce fix.")]
        public float LandingSettleTime = 0.25f;

        [Tooltip("How much stiffer the damper is during that settle window.")]
        public float LandingSettleDamping = 1.8f;

        [Tooltip("Cap on suspension acceleration, in m/s squared. Stops a deep compression " +
                 "from launching the board back into the air.")]
        public float MaxSpringAcceleration = 120f;

        [Header("Physics")]
        [Tooltip("Extra gravity. 1 = normal. Higher makes airs snappier and less floaty.")]
        public float GravityScale = 1.6f;

        [Tooltip("Lower = harder to tip over. Below the deck is right for a board.")]
        public Vector3 CenterOfMass = new Vector3(0f, -0.12f, 0f);

        [Header("Debug (read-only)")]
        [SerializeField] private bool _grounded;
        [SerializeField] private float _speed;
        [SerializeField] private int _wheelsOnGround;
        [SerializeField] private float _timeInAir;

        public bool Grounded => _grounded;
        public float Speed => _speed;
        public float TimeInAir => _timeInAir;

        private Rigidbody _rb;
        private BoardInputState _input;
        private float _popTimer;
        private float _landingAlignTimer;
        private float _settleTimer;
        private Quaternion _pendingRotation;
        private float _popIgnoreTimer;
        private Vector3 _groundNormal = Vector3.up;
        private Vector3 _spawnPosition;
        private Quaternion _spawnRotation;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
            _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            _rb.centerOfMass = CenterOfMass;
            _rb.useGravity = false; // applied manually so GravityScale means something

            if (InputSource == null)
            {
                InputSource = GetComponent<BoardInputSource>();
            }

            _spawnPosition = transform.position;
            _spawnRotation = transform.rotation;
        }

        private void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;

            _input = InputSource != null ? InputSource.Read() : default;

            _rb.centerOfMass = CenterOfMass;
            _rb.AddForce(Physics.gravity * GravityScale, ForceMode.Acceleration);

            // Rotation is accumulated here and applied ONCE at the end of the step. Calling
            // MoveRotation more than once per step silently discards all but the last call,
            // which is how steering was being eaten by the landing alignment.
            _pendingRotation = _rb.rotation;

            bool wasGrounded = _grounded;
            ApplySuspension();

            if (_grounded)
            {
                if (!wasGrounded)
                {
                    OnTouchdown();
                }

                ApplyDrive(dt);
                ApplySteering(dt);
                ApplyGrip();
                ApplyLandingAlignment(dt);
            }
            else
            {
                _timeInAir += dt;
                ApplyAirControl(dt);
                _rb.AddForce(-_rb.linearVelocity * AirDrag, ForceMode.Acceleration);
            }

            ApplyOllie(dt);

            if (!Mathf.Approximately(Quaternion.Angle(_pendingRotation, _rb.rotation), 0f))
            {
                _rb.MoveRotation(_pendingRotation);
            }

            _popTimer = Mathf.Max(0f, _popTimer - dt);
            _settleTimer = Mathf.Max(0f, _settleTimer - dt);
            _popIgnoreTimer = Mathf.Max(0f, _popIgnoreTimer - dt);
            _speed = _rb.linearVelocity.magnitude;
        }

        /// <summary>
        /// A spring per wheel, cast down from the deck. This is what makes the board sit on
        /// terrain, lean on transitions and ride over bumps, rather than sliding as a box.
        /// </summary>
        private void ApplySuspension()
        {
            if (_popIgnoreTimer > 0f)
            {
                _wheelsOnGround = 0;
                _grounded = false;
                return;
            }

            int wheels = Mathf.Max(1, WheelPoints.Length);
            float maxDistance = RideHeight + SuspensionTravel;
            float probeDistance = maxDistance + GroundedHysteresis;

            _wheelsOnGround = 0;
            Vector3 normalSum = Vector3.zero;

            // Gravity is cancelled per grounded wheel, so the spring only has to correct the
            // difference between where the board is and where it should ride. Without this,
            // stiffness and ride height are tangled: stiff enough to feel solid means strong
            // enough to launch the board, which is exactly what was happening.
            float gravityPerWheel = Physics.gravity.magnitude * GravityScale / wheels;

            foreach (Vector3 local in WheelPoints)
            {
                Vector3 origin = transform.TransformPoint(local);

                if (!Physics.Raycast(origin, -transform.up, out RaycastHit hit, probeDistance))
                {
                    continue;
                }

                normalSum += hit.normal;

                // Hysteresis band: a wheel counts as touching a little beyond full extension,
                // so contact does not flicker on and off at the edge of the ray.
                bool touching = hit.distance <= maxDistance;
                if (touching)
                {
                    _wheelsOnGround++;
                }
                else
                {
                    continue;
                }

                // Measured from the RIDE HEIGHT, not from the ray length. Positive when the
                // board is lower than it should sit, negative when it is higher.
                float offset = RideHeight - hit.distance;

                Vector3 wheelVelocity = _rb.GetPointVelocity(origin);
                float verticalSpeed = Vector3.Dot(wheelVelocity, transform.up);

                float damper = _settleTimer > 0f ? SpringDamper * LandingSettleDamping : SpringDamper;

                // Acceleration, not force: independent of the rigidbody's mass, so changing
                // the board's weight does not silently retune the whole feel.
                float accel = (offset * SpringStrength) - (verticalSpeed * damper) + gravityPerWheel;

                // Only ever push away from the ground. A suspension that pulls down is what
                // makes a board feel magnetised to ramps.
                accel = Mathf.Clamp(accel, 0f, MaxSpringAcceleration);

                _rb.AddForceAtPosition(transform.up * (accel / wheels), origin, ForceMode.Acceleration);
            }

            _grounded = _wheelsOnGround > 0;
            _groundNormal = normalSum.sqrMagnitude > 0.001f ? normalSum.normalized : Vector3.up;
        }

        /// <summary>
        /// Constant acceleration from the trigger. The push is an animation played over this,
        /// not a physics event — so speed is smooth and predictable while chasing a ball.
        /// </summary>
        private void ApplyDrive(float dt)
        {
            Vector3 forward = Vector3.ProjectOnPlane(transform.forward, _groundNormal).normalized;
            float forwardSpeed = Vector3.Dot(_rb.linearVelocity, forward);

            if (_input.Throttle > 0.01f && forwardSpeed < TopSpeed)
            {
                _rb.AddForce(forward * (_input.Throttle * Acceleration), ForceMode.Acceleration);
            }

            _rb.AddForce(-_rb.linearVelocity * RollingResistance, ForceMode.Acceleration);

            if (_input.Brake > 0.01f)
            {
                _rb.AddForce(-_rb.linearVelocity * (_input.Brake * BrakeStrength),
                    ForceMode.Acceleration);
            }
        }

        /// <summary>
        /// Steering scales with speed: a stationary board should not pirouette on the spot.
        /// Rotation is applied about the ground normal so the board turns along a transition
        /// rather than trying to turn in the world's flat plane.
        /// </summary>
        private void ApplySteering(float dt)
        {
            Vector3 forward = Vector3.ProjectOnPlane(transform.forward, _groundNormal).normalized;
            float forwardSpeed = Vector3.Dot(_rb.linearVelocity, forward);
            float speedFactor = Mathf.Clamp01(Mathf.Abs(forwardSpeed) / Mathf.Max(0.01f, FullSteerSpeed));

            if (speedFactor <= 0.001f || Mathf.Abs(_input.Steer) < 0.01f)
            {
                return;
            }

            float degrees = _input.Steer * TurnRate * speedFactor * dt;
            Quaternion turn = Quaternion.AngleAxis(degrees, _groundNormal);
            _pendingRotation = turn * _pendingRotation;

            // Redirect existing velocity into the new heading, or the board would keep
            // travelling the old way and the turn would feel like a slide.
            _rb.linearVelocity = turn * _rb.linearVelocity;
        }

        /// <summary>Kills sideways slide at each wheel. High grip is what makes it rail-like.</summary>
        private void ApplyGrip()
        {
            foreach (Vector3 local in WheelPoints)
            {
                Vector3 origin = transform.TransformPoint(local);
                Vector3 wheelVelocity = _rb.GetPointVelocity(origin);
                Vector3 right = transform.right;
                float lateral = Vector3.Dot(wheelVelocity, right);
                _rb.AddForceAtPosition(-right * (lateral * Grip / WheelPoints.Length), origin);
            }
        }

        private void ApplyOllie(float dt)
        {
            if (!_input.PopPressed || !_grounded || _popTimer > 0f)
            {
                return;
            }

            // Pop straight up from the surface. No tip torque: a nose-up kick plus a low
            // centre of mass is what rotated the board onto its back.
            Vector3 velocity = _rb.linearVelocity;
            velocity -= Vector3.Project(velocity, _groundNormal);
            _rb.linearVelocity = velocity + (_groundNormal * PopVelocity);

            if (LevelOnPop)
            {
                _rb.angularVelocity = Vector3.zero;
            }

            _popTimer = PopCooldown;
            _popIgnoreTimer = PopGroundIgnoreTime;
            _grounded = false;
        }

        /// <summary>
        /// Airborne, the stick rotates the board directly. Torque-free and deliberate: the
        /// player is choosing an orientation, which is also what makes a trick nameable later.
        /// </summary>
        private void ApplyAirControl(float dt)
        {
            // Bleed residual spin picked up from the ground, so a scrappy takeoff does not
            // become a tumble. Without this the board keeps whatever the suspension gave it.
            if (AirAngularDamping > 0f)
            {
                _rb.angularVelocity = Vector3.Lerp(
                    _rb.angularVelocity, Vector3.zero, Mathf.Clamp01(AirAngularDamping * dt));
            }

            Vector2 attitude = _input.Attitude;
            float roll = _input.AirRoll;

            if (attitude.sqrMagnitude < 0.0001f && Mathf.Abs(roll) < 0.01f)
            {
                ApplyAirAutoLevel(dt);
                return;
            }

            // Each axis gets its own rate, because they are not equally useful: yaw is how
            // you turn to face the play (and what a 180 is made of), pitch is how you set up
            // a landing, roll is a flip.
            Vector3 rotation =
                (transform.up * (attitude.x * AirYawRate))
                + (transform.right * (attitude.y * AirPitchRate))
                + (transform.forward * (-roll * AirRollRate));

            if (rotation.sqrMagnitude < 0.0001f)
            {
                return;
            }

            float degrees = rotation.magnitude * dt;
            Quaternion target = Quaternion.AngleAxis(degrees, rotation.normalized) * _pendingRotation;
            _pendingRotation = Quaternion.Slerp(_pendingRotation, target, Mathf.Clamp01(AttitudeSharpness * dt));
        }

        /// <summary>
        /// With the stick neutral, drift back towards level. This is what makes a plain ollie
        /// a clean up-and-down hop: you only rotate when you ask to. Set AirAutoLevel to 0 for
        /// a fully committed board, which is more honest to skating and much harder.
        /// </summary>
        private void ApplyAirAutoLevel(float dt)
        {
            if (AirAutoLevel <= 0f)
            {
                return;
            }

            Vector3 flatForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            if (flatForward.sqrMagnitude < 0.001f)
            {
                return;
            }

            Quaternion upright = Quaternion.LookRotation(flatForward.normalized, Vector3.up);
            _pendingRotation = Quaternion.Slerp(_pendingRotation, upright, Mathf.Clamp01(AirAutoLevel * dt));
        }

        /// <summary>
        /// Briefly ease the board flat after landing, so a slightly-off landing does not
        /// leave it fighting the suspension. Set LandingAlignTime to 0 to feel it without.
        /// </summary>
        private void ApplyLandingAlignment(float dt)
        {
            if (_landingAlignTimer <= 0f)
            {
                return;
            }

            _landingAlignTimer -= dt;

            Vector3 flatForward = Vector3.ProjectOnPlane(transform.forward, _groundNormal).normalized;
            if (flatForward.sqrMagnitude < 0.001f)
            {
                return;
            }

            Quaternion upright = Quaternion.LookRotation(flatForward, _groundNormal);
            float t = Mathf.Clamp01(dt / Mathf.Max(0.001f, LandingAlignTime));
            _pendingRotation = Quaternion.Slerp(_pendingRotation, upright, t);
        }

        /// <summary>
        /// Called on the tick the board regains the ground. Absorbs the landing rather than
        /// letting the suspension spring fire it back into the air, which is what caused the
        /// board to bounce around after every ollie.
        /// </summary>
        private void OnTouchdown()
        {
            _landingAlignTimer = LandingAlignTime;
            _settleTimer = LandingSettleTime;
            _timeInAir = 0f;

            if (KillSpinOnLanding)
            {
                _rb.angularVelocity = Vector3.zero;
            }

            Vector3 velocity = _rb.linearVelocity;
            float intoGround = Vector3.Dot(velocity, _groundNormal);
            if (intoGround < 0f)
            {
                // Remove most of the downward speed so the spring has little to react against.
                velocity -= _groundNormal * (intoGround * (1f - LandingBounceRetained));
                _rb.linearVelocity = velocity;
            }
        }

        /// <summary>Put the board back at its starting position. Bound to R in the test scene.</summary>
        public void Respawn()
        {
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
            _rb.position = _spawnPosition;
            _rb.rotation = _spawnRotation;
            transform.SetPositionAndRotation(_spawnPosition, _spawnRotation);
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
