using UnityEngine;
using UnityEngine.InputSystem;

namespace Prototypes.TrickGrammar
{
    /// <summary>
    /// PROTOTYPE — throwaway. Exists to answer ticket #6, "how deep does the trick grammar go",
    /// by being driven, not by being read.
    ///
    /// Net-shaped where it is free to be: input is gathered into a plain struct in Update and
    /// consumed on a fixed 60Hz tick, so the feel tuned here transfers when FishNet takes over
    /// the tick. It is NOT FishNet code — no replicate, no reconcile. That lands with the
    /// seams ticket, after this prototype has been thrown away.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class BoardController : MonoBehaviour
    {
        [Header("Scheme (switch live with 1 / 2)")]
        public TrickScheme Scheme = TrickScheme.Flick;

        [Header("Suspension")]
        public float RestLength = 0.45f;
        public float SpringStrength = 900f;
        public float SpringDamper = 90f;
        public float WheelRadius = 0.08f;

        [Header("Drive")]
        public float DriveForce = 22f;
        public float TopSpeed = 18f;
        public float SteerTorque = 9f;
        public float GripStrength = 14f;
        public float RollingResistance = 0.4f;

        [Header("Ollie")]
        public float PopBase = 3.6f;
        public float PopCharged = 3.4f;
        public float PopChargeTime = 0.45f;
        public float PopPitchKick = 1.2f;

        [Header("Air")]
        public float FlickFlipTorque = 26f;
        public float FlickShuvTorque = 12f;
        public float SpinRateTurnsPerSecond = 1.15f;
        public float ConsequenceLeanTorque = 7f;
        public float AirDrag = 0.02f;

        [Header("Landing")]
        public bool NoBail = false;
        public float BailSpeedLoss = 0.55f;

        // ---- surfaced state, read by the HUD ----
        public bool Grounded { get; private set; }
        public float PopCharge { get; private set; }
        public AirRotation Live => _live;
        public AirRotation LastRotation { get; private set; }
        public string LastTrick { get; private set; } = "—";
        public LandingGrade LastGrade { get; private set; } = LandingGrade.Clean;
        public float Speed => _rb != null ? _rb.linearVelocity.magnitude : 0f;

        private Rigidbody _rb;
        private Vector3[] _wheels;
        private BoardInput _input;
        private bool _popReleasedLatch;
        private AirRotation _live;
        private float _riderYaw;
        private float _prevRiderYaw;
        private float _prevBoardYaw;
        private Vector3 _spawnPosition;
        private Quaternion _spawnRotation;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
            _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            // Match the tick the game will eventually run on, so feel transfers.
            Time.fixedDeltaTime = 1f / 60f;

            _wheels = new[]
            {
                new Vector3(-0.16f, 0f, 0.33f),
                new Vector3(0.16f, 0f, 0.33f),
                new Vector3(-0.16f, 0f, -0.33f),
                new Vector3(0.16f, 0f, -0.33f),
            };

            _spawnPosition = transform.position;
            _spawnRotation = transform.rotation;
            _prevBoardYaw = transform.eulerAngles.y;
            _riderYaw = _prevBoardYaw;
            _prevRiderYaw = _prevBoardYaw;
        }

        private void Update()
        {
            // Only latching here. No simulation state is mutated in Update — that rule is
            // what makes this port to a predicted tick without a rewrite.
            var gp = Gamepad.current;
            var kb = Keyboard.current;

            if (kb != null)
            {
                if (kb.digit1Key.wasPressedThisFrame) Scheme = TrickScheme.Consequence;
                if (kb.digit2Key.wasPressedThisFrame) Scheme = TrickScheme.Flick;
                if (kb.bKey.wasPressedThisFrame) NoBail = !NoBail;
                if (kb.rKey.wasPressedThisFrame) Respawn();
            }

            float steer = 0f;
            float throttle = 0f;
            Vector2 trick = Vector2.zero;
            Vector2 spin = Vector2.zero;
            bool popHeld = false;

            if (gp != null)
            {
                Vector2 ls = gp.leftStick.ReadValue();
                steer = ls.x;
                throttle = gp.rightTrigger.ReadValue() - gp.leftTrigger.ReadValue();
                trick = gp.rightStick.ReadValue();
                spin = ls;
                popHeld = gp.buttonSouth.isPressed;
                if (gp.buttonSouth.wasReleasedThisFrame) _popReleasedLatch = true;
            }

            if (kb != null)
            {
                if (kb.aKey.isPressed) steer -= 1f;
                if (kb.dKey.isPressed) steer += 1f;
                if (kb.wKey.isPressed) throttle += 1f;
                if (kb.sKey.isPressed) throttle -= 1f;

                // Keyboard trick stick: arrow keys.
                if (kb.leftArrowKey.isPressed) trick.x -= 1f;
                if (kb.rightArrowKey.isPressed) trick.x += 1f;
                if (kb.upArrowKey.isPressed) trick.y += 1f;
                if (kb.downArrowKey.isPressed) trick.y -= 1f;

                if (kb.qKey.isPressed) spin.x -= 1f;
                if (kb.eKey.isPressed) spin.x += 1f;

                if (kb.spaceKey.isPressed) popHeld = true;
                if (kb.spaceKey.wasReleasedThisFrame) _popReleasedLatch = true;
            }

            _input.Steer = Mathf.Clamp(steer, -1f, 1f);
            _input.Throttle = Mathf.Clamp(throttle, -1f, 1f);
            _input.TrickStick = Vector2.ClampMagnitude(trick, 1f);
            _input.SpinStick = Vector2.ClampMagnitude(spin, 1f);
            _input.PopHeld = popHeld;
        }

        private void FixedUpdate()
        {
            BoardInput input = _input;
            input.PopReleased = _popReleasedLatch;
            _popReleasedLatch = false;

            float dt = Time.fixedDeltaTime;

            int groundedWheels = SimulateSuspension(input, dt, out Vector3 surfaceNormal);
            bool groundedNow = groundedWheels > 0;

            if (groundedNow)
            {
                SimulateGroundedDrive(input, dt);
            }
            else
            {
                SimulateAir(input, dt);
            }

            AccumulateRotation(groundedNow, dt);
            HandleOllie(input, groundedNow, dt);

            if (Grounded && !groundedNow)
            {
                BeginAir();
            }
            else if (!Grounded && groundedNow)
            {
                Land(surfaceNormal);
            }

            Grounded = groundedNow;
        }

        private int SimulateSuspension(BoardInput input, float dt, out Vector3 surfaceNormal)
        {
            int grounded = 0;
            surfaceNormal = Vector3.up;

            foreach (Vector3 local in _wheels)
            {
                Vector3 origin = transform.TransformPoint(local);
                float maxDistance = RestLength + WheelRadius;

                if (!Physics.Raycast(origin, -transform.up, out RaycastHit hit, maxDistance))
                {
                    continue;
                }

                grounded++;
                surfaceNormal = hit.normal;

                float compression = (maxDistance - hit.distance) / RestLength;
                Vector3 wheelVelocity = _rb.GetPointVelocity(origin);
                float springVelocity = Vector3.Dot(wheelVelocity, transform.up);

                float force = (compression * SpringStrength) - (springVelocity * SpringDamper);
                _rb.AddForceAtPosition(transform.up * force, origin);

                // Sideways grip: kill lateral slide at the wheel so the board tracks.
                Vector3 right = transform.right;
                float lateral = Vector3.Dot(wheelVelocity, right);
                _rb.AddForceAtPosition(-right * (lateral * GripStrength), origin);
            }

            return grounded;
        }

        private void SimulateGroundedDrive(BoardInput input, float dt)
        {
            Vector3 forward = transform.forward;
            float speedAlongForward = Vector3.Dot(_rb.linearVelocity, forward);

            if (Mathf.Abs(speedAlongForward) < TopSpeed)
            {
                _rb.AddForce(forward * (input.Throttle * DriveForce), ForceMode.Acceleration);
            }

            _rb.AddForce(-_rb.linearVelocity * RollingResistance, ForceMode.Acceleration);

            // Steering scales with speed — a stationary board should not pirouette.
            float steerScale = Mathf.Clamp01(Mathf.Abs(speedAlongForward) / 4f);
            float direction = Mathf.Sign(speedAlongForward == 0f ? 1f : speedAlongForward);
            _rb.AddTorque(Vector3.up * (input.Steer * SteerTorque * steerScale * direction),
                ForceMode.Acceleration);

            _riderYaw = transform.eulerAngles.y;
        }

        private void SimulateAir(BoardInput input, float dt)
        {
            _rb.AddForce(-_rb.linearVelocity * AirDrag, ForceMode.Acceleration);

            if (Scheme != TrickScheme.Flick)
            {
                // Consequence scheme: no in-air control at all. Whatever the pop gave you
                // is what you land with. This is the scheme being tested, not a stub.
                _riderYaw = transform.eulerAngles.y;
                return;
            }

            // Whole-body spin is kinematic on the rider, so it stays distinguishable from
            // board-only yaw. This is what lets a shuvit and a 180 be told apart at all.
            float spinInput = input.SpinStick.x;
            if (Mathf.Abs(spinInput) > 0.2f)
            {
                float degrees = spinInput * SpinRateTurnsPerSecond * 360f * dt;
                _riderYaw += degrees;
                _rb.MoveRotation(Quaternion.AngleAxis(degrees, Vector3.up) * _rb.rotation);
            }
        }

        private void HandleOllie(BoardInput input, bool grounded, float dt)
        {
            if (grounded && input.PopHeld)
            {
                PopCharge = Mathf.Min(1f, PopCharge + (dt / PopChargeTime));
            }

            if (!input.PopReleased)
            {
                if (!grounded)
                {
                    PopCharge = 0f;
                }

                return;
            }

            if (!grounded)
            {
                PopCharge = 0f;
                return;
            }

            float pop = PopBase + (PopCharged * PopCharge);
            _rb.AddForce(transform.up * pop, ForceMode.VelocityChange);
            _rb.AddTorque(-transform.right * PopPitchKick, ForceMode.VelocityChange);

            if (Scheme == TrickScheme.Flick)
            {
                // Right stick at the moment of pop imparts the rotation.
                // X flips the board about its long axis, Y shuvits it under the rider.
                Vector2 flick = input.TrickStick;
                if (Mathf.Abs(flick.x) > 0.25f)
                {
                    _rb.AddTorque(transform.forward * (-flick.x * FlickFlipTorque),
                        ForceMode.VelocityChange);
                }

                if (Mathf.Abs(flick.y) > 0.25f)
                {
                    _rb.AddTorque(transform.up * (flick.y * FlickShuvTorque),
                        ForceMode.VelocityChange);
                }
            }
            else
            {
                // Consequence scheme: lean at takeoff is the only influence you have.
                Vector2 lean = input.SpinStick;
                _rb.AddTorque(transform.forward * (-lean.x * ConsequenceLeanTorque),
                    ForceMode.VelocityChange);
                _rb.AddTorque(transform.right * (lean.y * ConsequenceLeanTorque * 0.5f),
                    ForceMode.VelocityChange);
            }

            PopCharge = 0f;
        }

        private void BeginAir()
        {
            _live = default;
            _prevBoardYaw = transform.eulerAngles.y;
            _riderYaw = _prevBoardYaw;
            _prevRiderYaw = _prevBoardYaw;
        }

        private void AccumulateRotation(bool grounded, float dt)
        {
            if (grounded)
            {
                return;
            }

            _live.AirTime += dt;

            // Flip and pitch come straight off the board's own angular velocity, read in
            // board-local axes. This is the rotation-classification approach: what the
            // board did, not what the player pressed.
            Vector3 localAngular = transform.InverseTransformDirection(_rb.angularVelocity);
            _live.FlipTurns += -localAngular.z * dt / (2f * Mathf.PI);
            _live.PitchTurns += localAngular.x * dt / (2f * Mathf.PI);

            // Yaw is split two ways. The rider's yaw is the whole-body spin; the board's
            // yaw relative to the rider is the shuvit. Tracking them separately is the
            // only way a shuvit and a 180 are ever distinguishable.
            float boardYaw = transform.eulerAngles.y;
            float boardDelta = Mathf.DeltaAngle(_prevBoardYaw, boardYaw);
            _prevBoardYaw = boardYaw;

            float riderDelta = Mathf.DeltaAngle(_prevRiderYaw, _riderYaw);
            _prevRiderYaw = _riderYaw;

            // Under Consequence the rider is welded to the board, so every degree of yaw
            // is whole-body spin and a shuvit is not expressible at all. That limitation
            // is the point of the comparison, not an oversight.
            float spinDelta = Scheme == TrickScheme.Flick ? riderDelta : boardDelta;
            float shuvDelta = boardDelta - spinDelta;

            _live.SpinTurns += spinDelta / 360f;
            _live.ShuvTurns += shuvDelta / 360f;
        }

        private void Land(Vector3 surfaceNormal)
        {
            AirRotation landed = _live;
            LastRotation = landed;
            LastTrick = TrickClassifier.Name(landed);

            float upDot = Vector3.Dot(transform.up, surfaceNormal);
            Vector3 velocity = _rb.linearVelocity;
            float alignment = velocity.sqrMagnitude < 0.5f
                ? 1f
                : Mathf.Abs(Vector3.Dot(velocity.normalized, transform.forward));

            LastGrade = TrickClassifier.Grade(upDot, alignment);

            if (LastGrade == LandingGrade.Bail && !NoBail)
            {
                _rb.linearVelocity *= 1f - BailSpeedLoss;
                _rb.angularVelocity += Random.insideUnitSphere * 3f;
            }

            _live = default;
            _riderYaw = transform.eulerAngles.y;
            _prevRiderYaw = _riderYaw;
        }

        public void Respawn()
        {
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
            transform.SetPositionAndRotation(_spawnPosition, _spawnRotation);
            _live = default;
            LastTrick = "—";
            LastGrade = LandingGrade.Clean;
            _riderYaw = transform.eulerAngles.y;
            _prevRiderYaw = _riderYaw;
            _prevBoardYaw = _riderYaw;
        }

        private void OnDrawGizmosSelected()
        {
            if (_wheels == null)
            {
                return;
            }

            Gizmos.color = Color.cyan;
            foreach (Vector3 local in _wheels)
            {
                Vector3 origin = transform.TransformPoint(local);
                Gizmos.DrawLine(origin, origin - (transform.up * (RestLength + WheelRadius)));
            }
        }
    }
}
