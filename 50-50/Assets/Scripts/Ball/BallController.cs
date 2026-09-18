using UnityEngine;

namespace FiftyFifty.Ball
{
    /// <summary>
    /// The ball. Big, light and floaty on purpose (#7): it has to be readable at the board's
    /// 12 m/s, and it hangs while the board falls — the board runs at gravity x1.6 and the ball
    /// at x1.0 — which is what makes an aerial contest possible on 0.5 to 1.2 seconds of
    /// skateboard airtime.
    ///
    /// Possession is a held grab, not a magnet (#7). While carried the ball leaves the physics
    /// world entirely: kinematic, world collider off, parked at the carrier's carry point. It
    /// can still be taken, but only by a rule — the hold timer expiring, or a punch — never by
    /// an accidental collision. A punch finds it with an overlap query rather than a trigger,
    /// so there is no second collider to keep in sync with the shrink.
    ///
    /// Written like the board: a public dt-explicit SimulateTick, no gameplay state touched in
    /// Update. That is what lets the probe drive it headlessly, and it is the shape FishNet's
    /// non-controlled predicted object wants later (see docs/research, rule C5 — the ball must
    /// be a predicted NetworkObject, never an OfflineRigidbody).
    /// </summary>
    [DefaultExecutionOrder(200)]
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(SphereCollider))]
    public class BallController : MonoBehaviour
    {
        [Header("Body")]
        [Tooltip("Ball diameter in metres. Roughly waist-high on the rider, and about 1.25x the " +
                 "length of the board — big enough to read at speed. The carry point clears " +
                 "itself as this grows, so it is safe to crank.")]
        public float Diameter = 0.9f;

        [Tooltip("Mass in kg. Light: the board is 10kg, and the ball should move when hit.")]
        public float Mass = 0.6f;

        [Tooltip("Gravity multiplier. The board uses 1.6 to keep airs snappy; the ball stays at " +
                 "1.0 so it hangs in the air long enough to be played.")]
        public float GravityScale = 1f;

        [Tooltip("How much energy a bounce keeps.")]
        [Range(0f, 1f)] public float Bounciness = 0.6f;

        [Tooltip("Surface friction. Low, so the ball rolls and skids rather than gripping.")]
        [Range(0f, 1f)] public float Friction = 0.25f;

        [Tooltip("Air resistance on the ball's travel. Low: a pass should carry.")]
        public float LinearDamping = 0.05f;

        [Tooltip("How quickly spin bleeds off. Low, so spin stays visible.")]
        public float AngularDamping = 0.05f;

        [Tooltip("Speed the ball will never exceed, m/s. Stops a punch in a corner from " +
                 "launching it out of the county.")]
        public float MaxSpeed = 30f;

        [Header("Carrying")]
        [Tooltip("Visual mesh. Scaled down while carried — the ball shrinks in your hands and " +
                 "grows back when you let go. Visual only: the collider is off while carried.")]
        public Transform Visual;

        [Tooltip("How small the ball goes while carried, as a fraction of its size.")]
        [Range(0.2f, 1f)] public float CarriedScale = 0.6f;

        [Tooltip("Seconds the shrink and re-grow take.")]
        public float ScaleSpeed = 0.12f;

        [Header("Audio (hooks — drop clips in, none ship)")]
        public AudioSource Audio;
        public AudioClip GrabClip;
        public AudioClip ReleaseClip;
        public AudioClip PunchClip;
        public AudioClip FumbleClip;

        [Header("Debug (read-only)")]
        [SerializeField] private bool _carried;
        [SerializeField] private float _speed;

        public bool Carried => _carried;
        public Vector3 Position => _rb != null ? _rb.position : transform.position;
        public Vector3 Velocity => _rb != null ? _rb.linearVelocity : Vector3.zero;
        public float Radius => Diameter * 0.5f;

        /// <summary>Radius while carried, i.e. after the shrink. What a carry point must clear.</summary>
        public float CarriedRadius => Diameter * CarriedScale * 0.5f;

        /// <summary>Who is holding it, or null. Compared by reference, never by tag.</summary>
        public Component Holder { get; private set; }

        private Rigidbody _rb;
        private SphereCollider _collider;
        private Vector3 _carryPoint;
        private Vector3 _carrierVelocity;
        private float _visualScale = 1f;
        private Vector3 _spawnPosition;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _collider = GetComponent<SphereCollider>();

            ApplyBodySettings();

            if (Visual == null)
            {
                Visual = transform.childCount > 0 ? transform.GetChild(0) : null;
            }

            if (Audio == null)
            {
                Audio = GetComponent<AudioSource>();
            }

            _spawnPosition = transform.position;
            _visualScale = 1f;
        }

        /// <summary>
        /// Pushes the Inspector numbers into PhysX. Also runs from OnValidate so changing the
        /// diameter in play mode does what you expect instead of nothing.
        /// </summary>
        public void ApplyBodySettings()
        {
            if (_rb == null)
            {
                _rb = GetComponent<Rigidbody>();
            }

            if (_collider == null)
            {
                _collider = GetComponent<SphereCollider>();
            }

            _rb.mass = Mass;
            _rb.useGravity = false;
            _rb.linearDamping = LinearDamping;
            _rb.angularDamping = AngularDamping;
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
            _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            _collider.radius = Radius;

            if (_collider.sharedMaterial == null)
            {
                _collider.sharedMaterial = new PhysicsMaterial("Ball");
            }

            _collider.sharedMaterial.bounciness = Bounciness;
            _collider.sharedMaterial.dynamicFriction = Friction;
            _collider.sharedMaterial.staticFriction = Friction;
            _collider.sharedMaterial.bounceCombine = PhysicsMaterialCombine.Maximum;
        }

        /// <summary>One simulation step. Public and dt-explicit so the probe can drive it.</summary>
        public void SimulateTick(float dt)
        {
            if (_carried)
            {
                // Parked, not simulated. MovePosition rather than transform so interpolation
                // still smooths it and the physics pose stays the truth.
                _rb.MovePosition(_carryPoint);
                _speed = _carrierVelocity.magnitude;
                return;
            }

            _rb.AddForce(Physics.gravity * GravityScale, ForceMode.Acceleration);

            if (_rb.linearVelocity.magnitude > MaxSpeed)
            {
                _rb.linearVelocity = _rb.linearVelocity.normalized * MaxSpeed;
            }

            _speed = _rb.linearVelocity.magnitude;
        }

        private void FixedUpdate()
        {
            SimulateTick(Time.fixedDeltaTime);
        }

        /// <summary>Where the carrier wants the ball this tick. Called by the holder.</summary>
        public void SetCarryPoint(Vector3 worldPoint, Vector3 carrierVelocity)
        {
            _carryPoint = worldPoint;
            _carrierVelocity = carrierVelocity;
        }

        public void Attach(Component holder, Vector3 worldPoint, Vector3 carrierVelocity)
        {
            Holder = holder;
            _carried = true;
            _carryPoint = worldPoint;
            _carrierVelocity = carrierVelocity;

            // Order matters: a kinematic body refuses velocity writes, so stop it first.
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
            _rb.isKinematic = true;
            _collider.enabled = false;

            Play(GrabClip);
        }

        /// <summary>
        /// Let go. Velocity is whatever the releaser says it is — a drop inherits their motion,
        /// a punch adds its impulse on top, a fumble gets a nudge forward.
        /// </summary>
        public void Release(Vector3 velocity, AudioClip clip = null)
        {
            Holder = null;
            _carried = false;

            _rb.isKinematic = false;
            _collider.enabled = true;
            _rb.linearVelocity = Vector3.ClampMagnitude(velocity, MaxSpeed);

            Play(clip != null ? clip : ReleaseClip);
        }

        public void ResetTo(Vector3 position)
        {
            if (_carried)
            {
                Release(Vector3.zero);
            }

            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
            _rb.position = position;
            transform.position = position;
        }

        public void ResetToSpawn()
        {
            ResetTo(_spawnPosition);
        }

        public void Play(AudioClip clip)
        {
            if (Audio != null && clip != null)
            {
                Audio.PlayOneShot(clip);
            }
        }

        /// <summary>Shrink and re-grow. Visual only, so it lives outside the physics step.</summary>
        private void Update()
        {
            if (Visual == null)
            {
                return;
            }

            float wanted = _carried ? CarriedScale : 1f;
            float rate = ScaleSpeed <= 0f ? 1f : Time.deltaTime / ScaleSpeed;
            _visualScale = Mathf.MoveTowards(_visualScale, wanted, rate);
            Visual.localScale = Vector3.one * (Diameter * _visualScale);
        }

        private void OnValidate()
        {
            if (Application.isPlaying)
            {
                ApplyBodySettings();
            }
        }
    }
}
