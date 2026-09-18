using FiftyFifty.Board;
using UnityEngine;

namespace FiftyFifty.CameraRig
{
    /// <summary>
    /// Chase camera that follows the board's heading on the ground plane, with right-stick
    /// orbit on top.
    ///
    /// It deliberately ignores the board's roll and pitch: a camera that tumbles with a
    /// flipping board makes air unreadable, and unreadable air means you cannot tell whether
    /// a landing was your fault.
    ///
    /// In the air it also stops chasing the heading. Spinning a 180 should not spin the view,
    /// because that is exactly when the player is trying to read where they will land. The
    /// camera holds the heading it had at takeoff and follows the spin only a little way, so
    /// the rotation is still legible without the world turning underneath you.
    /// </summary>
    public class BoardChaseCamera : MonoBehaviour
    {
        [Header("Target")]
        public Transform Target;

        [Tooltip("Optional. Used to read the camera stick. Found on the target if empty.")]
        public BoardInputSource InputSource;

        [Tooltip("Optional. Used to know whether the board is airborne. Found on the target if empty.")]
        public BoardController Board;

        [Header("Framing")]
        [Tooltip("Where the camera sits relative to the board's heading.")]
        public Vector3 Offset = new Vector3(0f, 2.1f, -5f);

        [Tooltip("How high above the board the camera aims.")]
        public float LookAtHeight = 0.7f;

        [Header("Follow")]
        [Tooltip("How quickly the camera catches up in position. Higher = stiffer.")]
        public float PositionDamping = 8f;

        [Tooltip("How quickly the camera swings to the board's heading on the ground.")]
        public float HeadingDamping = 4f;

        [Header("Airborne")]
        [Tooltip("How far the camera will follow a spin while airborne, in degrees either way. " +
                 "0 keeps the view perfectly still; 180 would follow the spin completely.")]
        [Range(0f, 180f)] public float AirHeadingFollow = 35f;

        [Tooltip("How quickly the camera follows within that limit. Lower than the ground " +
                 "value on purpose — a lazy camera in the air is a readable one.")]
        public float AirHeadingDamping = 2.5f;

        [Header("Orbit (right stick)")]
        public float OrbitSpeed = 140f;
        public float PitchSpeed = 90f;
        public float MinPitch = -15f;
        public float MaxPitch = 55f;

        [Tooltip("Seconds of no input before the camera drifts back behind the board. 0 = never.")]
        public float RecenterDelay = 1.2f;

        public float RecenterSpeed = 2.5f;


        private float _orbitYaw;
        private float _orbitPitch;
        private float _idleTime;
        private Quaternion _heading = Quaternion.identity;
        private float _anchorHeading;
        private bool _wasGrounded = true;

        private void Start()
        {
            if (Target != null && InputSource == null)
            {
                InputSource = Target.GetComponent<BoardInputSource>();
            }

            if (Target != null && Board == null)
            {
                Board = Target.GetComponent<BoardController>();
            }

            _anchorHeading = Target != null ? Target.eulerAngles.y : 0f;

            if (Target != null)
            {
                _heading = FlatHeading();
            }
        }

        private void LateUpdate()
        {
            if (Target == null)
            {
                return;
            }

            float dt = Time.deltaTime;

            // Read the stick without consuming the board's input latches: the camera must
            // never eat a push or an ollie. BoardInputSource.Read clears one-shots, so the
            // camera reads the device separately via whatever the source exposes publicly.
            Vector2 look = CameraLook();

            if (look.sqrMagnitude > 0.0001f)
            {
                _orbitYaw += look.x * OrbitSpeed * dt;
                _orbitPitch = Mathf.Clamp(_orbitPitch - (look.y * PitchSpeed * dt), MinPitch, MaxPitch);
                _idleTime = 0f;
            }
            else
            {
                _idleTime += dt;
                if (RecenterDelay > 0f && _idleTime > RecenterDelay)
                {
                    _orbitYaw = Mathf.Lerp(_orbitYaw, 0f, RecenterSpeed * dt);
                    _orbitPitch = Mathf.Lerp(_orbitPitch, 0f, RecenterSpeed * dt);
                }
            }

            UpdateHeading(dt);

            Quaternion orbit = Quaternion.Euler(_orbitPitch, _orbitYaw, 0f);
            Vector3 wanted = Target.position + (_heading * orbit * Offset);

            transform.position = Vector3.Lerp(transform.position, wanted, PositionDamping * dt);
            transform.LookAt(Target.position + (Vector3.up * LookAtHeight));
        }

        /// <summary>
        /// On the ground the camera sits behind the board. In the air it holds the heading it
        /// had at takeoff, following the spin only as far as AirHeadingFollow allows, so a 180
        /// reads as the board turning rather than the world turning.
        ///
        /// Landing no longer swings the view, because since #19 an air spin does not move the
        /// heading at all — the board and rider turn, the direction being driven does not. The
        /// camera follows the drive direction, so after a landed 180 it sits exactly where it
        /// was, looking at the front of a rider who is now riding backwards.
        ///
        /// The only thing that turns the view is the flip button, and that turns the drive
        /// direction with it.
        /// </summary>
        private void UpdateHeading(float dt)
        {
            bool grounded = Board == null || Board.Grounded;
            float boardHeading = Target.eulerAngles.y;

            if (grounded)
            {
                if (!_wasGrounded)
                {
                    // Landed: the board's heading is authoritative again.
                    _anchorHeading = boardHeading;
                }

                _wasGrounded = true;
                _heading = Quaternion.Slerp(_heading, FlatHeading(), Mathf.Clamp01(HeadingDamping * dt));
                return;
            }

            if (_wasGrounded)
            {
                // Took off: remember where the view was pointing and keep it.
                _anchorHeading = _heading.eulerAngles.y;
                _wasGrounded = false;
            }

            float spun = Mathf.DeltaAngle(_anchorHeading, boardHeading);
            float allowed = Mathf.Clamp(spun, -AirHeadingFollow, AirHeadingFollow);
            Quaternion wanted = Quaternion.Euler(0f, _anchorHeading + allowed, 0f);

            _heading = Quaternion.Slerp(_heading, wanted, Mathf.Clamp01(AirHeadingDamping * dt));
        }

        /// <summary>The board's facing, flattened — roll and pitch are deliberately discarded.</summary>
        private Quaternion FlatHeading()
        {
            Vector3 flat = Vector3.ProjectOnPlane(Target.forward, Vector3.up);
            if (flat.sqrMagnitude < 0.001f)
            {
                // Board is nose-up (mid-trick). Fall back to the last good heading.
                return _heading;
            }

            return Quaternion.LookRotation(flat.normalized, Vector3.up);
        }

        private Vector2 CameraLook()
        {
            var player = InputSource as PlayerBoardInput;
            return player != null ? player.PeekCameraLook() : Vector2.zero;
        }
    }
}
