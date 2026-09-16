using UnityEngine;

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
        public float SpringStrength = 90f;

        [Tooltip("Bounce absorption. Critical damping is roughly 2 x sqrt(SpringStrength).")]
        public float SpringDamper = 19f;

        [Tooltip("Extra reach below full extension where a wheel still counts as touching, " +
                 "so ground contact does not flicker at the edge of the ray.")]
        public float GroundedHysteresis = 0.08f;

        [Tooltip("Cap on suspension acceleration, m/s squared.")]
        public float MaxSpringAcceleration = 120f;

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

        [Header("Steering")]
        [Tooltip("Turn rate in degrees per second at full lock.")]
        public float TurnRate = 130f;

        [Tooltip("Speed at which steering reaches full strength. Below this it scales down.")]
        public float FullSteerSpeed = 3f;

        [Tooltip("How much sideways velocity is scrubbed. 1 = fully on rails, 0 = frictionless ice.")]
        [Range(0f, 1f)] public float SidewaysGrip = 0.92f;

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
        [Tooltip("Upward speed kept on touchdown. 0 = fully absorbed, no bounce.")]
        [Range(0f, 1f)] public float LandingBounceRetained = 0f;

        [Tooltip("Seconds after touchdown where the suspension is extra damped.")]
        public float LandingSettleTime = 0.2f;

        [Tooltip("How much stiffer the damper is during that settle window.")]
        public float LandingSettleDamping = 1.8f;

        [Header("Physics")]
        [Tooltip("Extra gravity. 1 = normal. Higher makes airs snappier and less floaty.")]
        public float GravityScale = 1.6f;

        [Header("Visuals")]
        [Tooltip("Deck mesh, leaned into turns. VISUAL ONLY — never affects physics or heading.")]
        public Transform DeckVisual;

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

        public bool Grounded => _grounded;
        public float Speed => _speed;
        public float TimeInAir => _timeInAir;
        public float Heading => _heading;

        private Rigidbody _rb;
        private BoardInputState _input;
        private Vector3 _groundNormal = Vector3.up;
        private Vector3 _surfaceUp = Vector3.up;
        private float _popTimer;
        private float _popIgnoreTimer;
        private float _settleTimer;
        private float _visualLean;
        private Vector3[] _wheelOrigins;
        private float[] _wheelDistances;
        private int _hitCount;
        private Vector3 _spawnPosition;
        private float _spawnHeading;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
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
                DeckVisual = transform.Find("Deck");
            }

            _spawnPosition = transform.position;
            _spawnHeading = transform.eulerAngles.y;
            _heading = _spawnHeading;
            _surfaceUp = Vector3.up;
        }

        private void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;

            _input = InputSource != null ? InputSource.Read() : default;
            _rb.AddForce(Physics.gravity * GravityScale, ForceMode.Acceleration);

            bool wasGrounded = _grounded;
            ApplySuspension();

            if (_grounded)
            {
                if (!wasGrounded)
                {
                    OnTouchdown();
                }

                Steer(dt);
                ApplyDrive(dt);
                ApplyGrip(dt);
            }
            else
            {
                _timeInAir += dt;
                SpinInAir(dt);
                _rb.AddForce(-_rb.linearVelocity * AirDrag, ForceMode.Acceleration);
            }

            ApplyOllie();
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
        private void ApplySuspension()
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
                Vector3 origin = transform.TransformPoint(WheelPoints[i]);

                if (!Physics.Raycast(origin, -transform.up, out RaycastHit hit, probeDistance))
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
                float verticalSpeed = Vector3.Dot(wheelVelocity, transform.up);

                float spring = ((offset * SpringStrength) - (verticalSpeed * damper)) * share;
                float accel = spring + gravityShare;

                // Never pull the board down: a suspension that sucks makes ramps magnetic.
                accel = Mathf.Clamp(accel, 0f, MaxSpringAcceleration * share);

                _rb.AddForceAtPosition(transform.up * accel, origin, ForceMode.Acceleration);
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
            float forwardSpeed = Vector3.Dot(_rb.linearVelocity, transform.forward);
            float speedFactor = Mathf.Clamp01(Mathf.Abs(forwardSpeed) / Mathf.Max(0.01f, FullSteerSpeed));

            _heading += _input.Steer * TurnRate * speedFactor * dt;
        }

        /// <summary>In the air the heading keeps turning, which is all a 180 is.</summary>
        private void SpinInAir(float dt)
        {
            _heading += _input.Attitude.x * AirYawRate * dt;
        }

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
                _rb.AddForce(-_rb.linearVelocity * (_input.Brake * BrakeStrength), ForceMode.Acceleration);
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
            Vector3 right = transform.right;
            float lateral = Vector3.Dot(velocity, right);

            // Frame-rate independent: SidewaysGrip is "fraction removed per 1/60s".
            float scrub = 1f - Mathf.Pow(1f - Mathf.Clamp01(SidewaysGrip), dt * 60f);
            _rb.linearVelocity = velocity - (right * (lateral * scrub));
        }

        private void ApplyOllie()
        {
            if (!_input.PopPressed || !_grounded || _popTimer > 0f)
            {
                return;
            }

            Vector3 velocity = _rb.linearVelocity;
            velocity -= Vector3.Project(velocity, _groundNormal);
            _rb.linearVelocity = velocity + (_groundNormal * PopVelocity);

            _popTimer = PopCooldown;
            _popIgnoreTimer = PopGroundIgnoreTime;
            _grounded = false;
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

            Vector3 headingForward = Quaternion.Euler(0f, _heading, 0f) * Vector3.forward;
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

            Vector3 velocity = _rb.linearVelocity;
            float intoGround = Vector3.Dot(velocity, _groundNormal);
            if (intoGround < 0f)
            {
                velocity -= _groundNormal * (intoGround * (1f - LandingBounceRetained));
                _rb.linearVelocity = velocity;
            }
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
            DeckVisual.localRotation = Quaternion.Euler(0f, 0f, _visualLean);
        }

        public void Respawn()
        {
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
            _heading = _spawnHeading;
            _surfaceUp = Vector3.up;

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
