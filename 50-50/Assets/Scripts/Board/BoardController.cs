using UnityEngine;

namespace FiftyFifty.Board
{
    /// <summary>
    /// Skateboard movement. First pass for issue #16 — expect to retune every number here
    /// by playing, not by reading.
    ///
    /// Decisions this implements (settled by grilling on #16):
    ///   - Discrete push, not a throttle. You kick, you coast, you kick again.
    ///   - Rail-like grip. No sliding; turns are carving arcs.
    ///   - One direction. No fakie/switch stance.
    ///   - Instant ollie at a single height. No charge.
    ///   - Attitude control in the air: the stick rotates the board.
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

        [Tooltip("Stiffness. Higher = the board sits harder on the ground and bumps more.")]
        public float SpringStrength = 900f;

        [Tooltip("Bounce absorption. Too low and the board pogos; too high and it feels stuck.")]
        public float SpringDamper = 110f;

        [Header("Push")]
        [Tooltip("Speed added by one kick, in m/s.")]
        public float PushImpulse = 3.2f;

        [Tooltip("Minimum seconds between kicks. Stops the board being a machine gun.")]
        public float PushCooldown = 0.45f;

        [Tooltip("Speed the board will not push past. Gravity and ramps can still exceed it.")]
        public float TopSpeed = 12f;

        [Tooltip("How quickly a coasting board loses speed. This is what makes pushing matter.")]
        public float RollingResistance = 0.35f;

        [Tooltip("Extra deceleration while braking.")]
        public float BrakeStrength = 6f;

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

        [Tooltip("Backward tip on pop, so the board noses up like a real ollie. Cosmetic-ish.")]
        public float PopTipTorque = 1.1f;

        [Tooltip("Seconds after landing before another ollie is allowed.")]
        public float PopCooldown = 0.12f;

        [Header("Air Control")]
        [Tooltip("How fast the stick rotates the board in the air, degrees per second.")]
        public float AttitudeRate = 260f;

        [Tooltip("How sharply attitude control responds. Higher = twitchier.")]
        public float AttitudeSharpness = 12f;

        [Tooltip("Air resistance. Mostly stops the board drifting oddly on long airs.")]
        public float AirDrag = 0.02f;

        [Header("Landing")]
        [Tooltip("On landing, snap the board's rotation upright over this many seconds. 0 = never.")]
        public float LandingAlignTime = 0.12f;

        [Header("Physics")]
        [Tooltip("Extra gravity. 1 = normal. Higher makes airs snappier and less floaty.")]
        public float GravityScale = 1.6f;

        [Tooltip("Lower = harder to tip over. Below the deck is right for a board.")]
        public Vector3 CenterOfMass = new Vector3(0f, -0.12f, 0f);

        [Header("Debug (read-only)")]
        [SerializeField] private bool _grounded;
        [SerializeField] private float _speed;
        [SerializeField] private int _wheelsOnGround;
        [SerializeField] private float _timeSincePush;
        [SerializeField] private float _timeInAir;

        public bool Grounded => _grounded;
        public float Speed => _speed;
        public float TimeInAir => _timeInAir;

        private Rigidbody _rb;
        private BoardInputState _input;
        private float _pushTimer;
        private float _popTimer;
        private float _landingAlignTimer;
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

            bool wasGrounded = _grounded;
            ApplySuspension();

            if (_grounded)
            {
                if (!wasGrounded)
                {
                    _landingAlignTimer = LandingAlignTime;
                    _timeInAir = 0f;
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

            _pushTimer = Mathf.Max(0f, _pushTimer - dt);
            _popTimer = Mathf.Max(0f, _popTimer - dt);
            _timeSincePush += dt;
            _speed = _rb.linearVelocity.magnitude;
        }

        /// <summary>
        /// A spring per wheel, cast down from the deck. This is what makes the board sit on
        /// terrain, lean on transitions and ride over bumps, rather than sliding as a box.
        /// </summary>
        private void ApplySuspension()
        {
            _wheelsOnGround = 0;
            Vector3 normalSum = Vector3.zero;
            float maxDistance = RideHeight + SuspensionTravel;

            foreach (Vector3 local in WheelPoints)
            {
                Vector3 origin = transform.TransformPoint(local);

                if (!Physics.Raycast(origin, -transform.up, out RaycastHit hit, maxDistance))
                {
                    continue;
                }

                _wheelsOnGround++;
                normalSum += hit.normal;

                float compression = (maxDistance - hit.distance) / maxDistance;
                Vector3 wheelVelocity = _rb.GetPointVelocity(origin);
                float verticalSpeed = Vector3.Dot(wheelVelocity, transform.up);

                float force = (compression * SpringStrength) - (verticalSpeed * SpringDamper);
                _rb.AddForceAtPosition(transform.up * force, origin);
            }

            _grounded = _wheelsOnGround > 0;
            _groundNormal = _grounded ? normalSum.normalized : Vector3.up;
        }

        /// <summary>
        /// Discrete kicks, not a throttle. Speed decays whenever you are not pushing, so
        /// keeping speed is an activity rather than a state.
        /// </summary>
        private void ApplyDrive(float dt)
        {
            Vector3 forward = Vector3.ProjectOnPlane(transform.forward, _groundNormal).normalized;
            float forwardSpeed = Vector3.Dot(_rb.linearVelocity, forward);

            if (_input.PushPressed && _pushTimer <= 0f && forwardSpeed < TopSpeed)
            {
                _rb.AddForce(forward * PushImpulse, ForceMode.VelocityChange);
                _pushTimer = PushCooldown;
                _timeSincePush = 0f;
            }

            _rb.AddForce(-_rb.linearVelocity * RollingResistance, ForceMode.Acceleration);

            if (_input.BrakeHeld)
            {
                _rb.AddForce(-_rb.linearVelocity * BrakeStrength, ForceMode.Acceleration);
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
            _rb.MoveRotation(turn * _rb.rotation);

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

            _rb.AddForce(_groundNormal * PopVelocity, ForceMode.VelocityChange);
            _rb.AddTorque(-transform.right * PopTipTorque, ForceMode.VelocityChange);
            _popTimer = PopCooldown;
            _grounded = false;
        }

        /// <summary>
        /// Airborne, the stick rotates the board directly. Torque-free and deliberate: the
        /// player is choosing an orientation, which is also what makes a trick nameable later.
        /// </summary>
        private void ApplyAirControl(float dt)
        {
            Vector2 attitude = _input.Attitude;
            if (attitude.sqrMagnitude < 0.0001f)
            {
                return;
            }

            Vector3 axis = (transform.forward * -attitude.x) + (transform.right * attitude.y);
            if (axis.sqrMagnitude < 0.0001f)
            {
                return;
            }

            float degrees = AttitudeRate * dt;
            Quaternion target = Quaternion.AngleAxis(degrees, axis.normalized) * _rb.rotation;
            _rb.MoveRotation(Quaternion.Slerp(_rb.rotation, target, AttitudeSharpness * dt));
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
            _rb.MoveRotation(Quaternion.Slerp(_rb.rotation, upright, t));
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
