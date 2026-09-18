using System.Text;
using FiftyFifty.Board;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FiftyFifty.EditorTools
{
    /// <summary>Feeds a board scripted intent so traction can be driven without a player.</summary>
    public class ScriptedBoardInput : BoardInputSource
    {
        public BoardInputState State;

        public override BoardInputState Read() => State;
    }

    /// <summary>
    /// Headless measurement of traction (#18): how long a crooked landing slides, and what the
    /// powerslide actually buys you in degrees and costs you in speed.
    ///
    /// Both are numbers you cannot eyeball — "did that slide longer?" is exactly the question
    /// the suspension probe existed to stop us guessing at.
    ///
    /// Menu: 50-50 > Probe Board Traction
    /// </summary>
    public static class BoardTractionProbe
    {
        private const float Dt = 1f / 50f;

        [MenuItem("50-50/Probe Board Traction")]
        public static void Run()
        {
            using var world = new ProbeWorld();

            LandingSlides(world);
            Powerslide(world);
        }

        /// <summary>Drop the board travelling at an angle to its nose and watch it hook up.</summary>
        private static void LandingSlides(ProbeWorld world)
        {
            var log = new StringBuilder();
            log.AppendLine("=== LANDING SLIDE ===");
            log.AppendLine("angle   slid for   grip floor   speed in   after 1.5s   kept");

            foreach (float angle in new[] { 5f, 15f, 30f, 60f, 90f, 135f })
            {
                GameObject ground = CreateGround(world);
                BoardController board = CreateBoard(world, new Vector3(0f, 0.9f, 0f), out ScriptedBoardInput input);

                Rigidbody rb = board.GetComponent<Rigidbody>();
                Vector3 travel = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                rb.linearVelocity = (travel * 10f) + (Vector3.up * -1f);

                float speedIn = rb.linearVelocity.magnitude;
                float slideSeconds = 0f;
                float gripFloor = 1f;
                bool landed = false;

                int stepsAfterLanding = 0;

                for (int i = 0; i < 300; i++)
                {
                    board.SimulateTick(Dt);
                    world.Step(Dt);

                    if (!landed && board.Grounded)
                    {
                        landed = true;
                    }

                    if (!landed)
                    {
                        continue;
                    }

                    if (board.Sliding)
                    {
                        slideSeconds += Dt;
                        gripFloor = Mathf.Min(gripFloor, board.Grip);
                    }

                    if (++stepsAfterLanding >= 75)
                    {
                        break;
                    }
                }

                float speedOut = rb.linearVelocity.magnitude;

                log.AppendLine($"{angle,5:0}°   {slideSeconds,8:0.00}s   {gripFloor,10:0.00}   " +
                               $"{speedIn,8:0.0}   {speedOut,9:0.0}   {speedOut / speedIn * 100f,4:0}%");

                Object.DestroyImmediate(board.gameObject);
                Object.DestroyImmediate(ground);
            }

            log.AppendLine("(under the 15 degree deadzone nothing should slide at all)");
            Debug.Log(log.ToString());
        }

        /// <summary>Turn at speed with and without the powerslide, and compare.</summary>
        private static void Powerslide(ProbeWorld world)
        {
            var log = new StringBuilder();
            log.AppendLine("=== POWERSLIDE ===");
            log.AppendLine("mode          turned   speed in   speed out   kept");

            foreach (bool drifting in new[] { false, true })
            {
                GameObject ground = CreateGround(world);
                BoardController board = CreateBoard(world, new Vector3(0f, 0.18f, 0f), out ScriptedBoardInput input);
                Rigidbody rb = board.GetComponent<Rigidbody>();

                // Get up to speed first.
                input.State = new BoardInputState { Throttle = 1f };
                for (int i = 0; i < 150; i++)
                {
                    board.SimulateTick(Dt);
                    world.Step(Dt);
                }

                float headingIn = board.Heading;
                float speedIn = rb.linearVelocity.magnitude;

                // One second of full lock, with or without the drift.
                input.State = new BoardInputState
                {
                    Throttle = 1f,
                    Steer = 1f,
                    PowerslideHeld = drifting,
                };

                for (int i = 0; i < 50; i++)
                {
                    board.SimulateTick(Dt);
                    world.Step(Dt);
                }

                float turned = Mathf.Abs(board.Heading - headingIn);
                float speedOut = rb.linearVelocity.magnitude;

                log.AppendLine($"{(drifting ? "powerslide" : "normal turn"),-12}   {turned,5:0}°   " +
                               $"{speedIn,8:0.0}   {speedOut,9:0.0}   {speedOut / speedIn * 100f,4:0}%");

                Object.DestroyImmediate(board.gameObject);
                Object.DestroyImmediate(ground);
            }

            Debug.Log(log.ToString());
        }

        private static GameObject CreateGround(ProbeWorld world)
        {
            GameObject ground = world.CreatePrimitive(PrimitiveType.Cube);
            ground.transform.localScale = new Vector3(400f, 1f, 400f);
            ground.transform.position = new Vector3(0f, -0.5f, 0f);
            return ground;
        }

        private static BoardController CreateBoard(ProbeWorld world, Vector3 position, out ScriptedBoardInput input)
        {
            GameObject go = world.CreateObject("ProbeBoard");
            go.transform.position = position;

            Rigidbody rb = go.AddComponent<Rigidbody>();
            rb.mass = 10f;
            rb.linearDamping = 0f;
            rb.angularDamping = 0.2f;

            go.AddComponent<BoxCollider>().size = new Vector3(0.3f, 0.1f, 0.8f);

            input = go.AddComponent<ScriptedBoardInput>();

            BoardController board = go.AddComponent<BoardController>();
            board.InputSource = input;
            board.SendMessage("Awake", SendMessageOptions.DontRequireReceiver);

            return board;
        }
    }
}
