using FiftyFifty.Board;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FiftyFifty.Ball
{
    /// <summary>
    /// On-screen readout for the ball test scene (#7). IMGUI so it needs no canvas and no
    /// wiring — delete it when the real HUD arrives.
    ///
    /// It prints the things that decide whether a feeling is the ball's fault or the rules':
    /// possession state and the hold countdown, the last punch's speed, whether the board counts
    /// as being in a trick right now, and whether the last hit disturbed it.
    /// </summary>
    public class BallDebugHud : MonoBehaviour
    {
        public BoardController Board;
        public BallHandler Handler;
        public BallController Ball;
        public BallImpact Impact;
        public GoalTargetVolume Target;

        [Header("Keys")]
        [Tooltip("Puts the board and the ball back where they started.")]
        public Key ResetAllKey = Key.R;

        [Tooltip("Puts only the ball back, so you can re-serve without losing your position.")]
        public Key ResetBallKey = Key.T;

        private GUIStyle _big;
        private GUIStyle _small;

        private void Update()
        {
            Keyboard keys = Keyboard.current;

            if (keys == null)
            {
                return;
            }

            if (keys[ResetAllKey].wasPressedThisFrame)
            {
                if (Board != null) Board.Respawn();
                if (Handler != null) Handler.ResetPossession();
                if (Ball != null) Ball.ResetToSpawn();
            }
            else if (keys[ResetBallKey].wasPressedThisFrame)
            {
                if (Handler != null) Handler.ResetPossession();
                if (Ball != null) Ball.ResetToSpawn();
            }
        }

        private void OnGUI()
        {
            _big ??= new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold };
            _small ??= new GUIStyle(GUI.skin.label) { fontSize = 14 };

            GUILayout.BeginArea(new Rect(18, 18, 560, 420));

            if (Board != null)
            {
                GUILayout.Label($"{Board.Speed:0.0} m/s", _big);
                GUILayout.Label(
                    Board.Grounded
                        ? "grounded"
                        : $"AIR {Board.TimeInAir:0.00}s   yaw {Board.AirYawTurns:0.00} turns" +
                          (Board.InTrick ? "   IN TRICK (vulnerable)" : "   ollie (safe)"),
                    _small);

                if (Board.Disturbed)
                {
                    GUILayout.Label("DISTURBED — landing would be downgraded", _small);
                }
            }

            GUILayout.Space(8);

            if (Handler != null)
            {
                string possession = Handler.Carrying
                    ? $"CARRYING   {Handler.HoldRemaining:0.0}s left"
                    : Handler.GrabCooldownRemaining > 0f
                        ? $"no ball — grab cooling down {Handler.GrabCooldownRemaining:0.0}s"
                        : Handler.CanGrabNow ? "no ball" : "no ball — hold meter too low to grab";

                GUILayout.Label(
                    $"hold meter {Handler.HoldRemaining:0.0} / {Handler.HoldSeconds:0.0}s" +
                    (Handler.Carrying || Handler.HoldMeterFull ? "" : "   (refills once the ball is away)"),
                    _small);

                GUILayout.Label(possession, _small);
                GUILayout.Label(
                    Handler.Carrying
                        ? "punch: releases and strikes"
                        : Handler.BallInPunchArc ? "punch: ball IN RANGE" : "punch: ball out of range",
                    _small);
                GUILayout.Label($"last: {Handler.LastEvent}", _small);
            }

            if (Ball != null)
            {
                GUILayout.Label($"ball {Ball.Velocity.magnitude:0.0} m/s" +
                                (Ball.Carried ? "   (held)" : ""), _small);
            }

            if (Impact != null)
            {
                GUILayout.Label($"last ball hit: {Impact.LastVerdict}", _small);
            }

            if (Target != null)
            {
                GUILayout.Label($"target hits: {Target.Entries}" +
                                (Target.Entries > 0 ? $"   last at {Target.LastEntrySpeed:0.0} m/s" : ""),
                                _small);
            }

            GUILayout.Space(10);
            GUILayout.Label(
                "pad:  LS steer / air spin   RS camera   RT accelerate   LT brake   A ollie   RB grab   LB punch\n" +
                "keys: A,D steer / air spin   W accelerate   S brake   arrows camera   SPACE ollie   E grab   F punch\n" +
                "R reset board + ball    T reset ball only",
                _small);

            GUILayout.EndArea();
        }
    }
}
