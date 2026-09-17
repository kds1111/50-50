using System.Linq;
using System.Text;
using FiftyFifty.Ball;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FiftyFifty.EditorTools
{
    /// <summary>
    /// Headless measurement of the ball. Drops it, rolls it, and prints numbers.
    ///
    /// Same reasoning as the suspension probe: "is the ball too bouncy" and "does a pass carry"
    /// are questions about numbers, and a bouncing sphere is the worst possible instrument for
    /// reading them. The suspension probe found the landing bounce in minutes after three
    /// attempts at reasoning about the code got it wrong.
    ///
    /// Menu: 50-50 > Probe Ball
    /// </summary>
    public static class BallProbe
    {
        [MenuItem("50-50/Probe Ball")]
        public static void Run()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            SimulationMode previous = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script;

            try
            {
                Drop("DROP (from 3m)", 3f, 400);
                Roll("ROLL (punched at 14 m/s)", 14f, 500);
                PunchArc();
            }
            finally
            {
                Physics.simulationMode = previous;
            }
        }

        private static void Drop(string label, float startHeight, int steps)
        {
            GameObject ground = CreateGround();
            BallController ball = CreateBall(new Vector3(0f, startHeight, 0f));

            const float dt = 1f / 50f;
            var log = new StringBuilder();
            log.AppendLine($"=== {label} ===");
            log.AppendLine("step   height   vSpeed");

            float previousSpeed = 0f;
            int bounce = 0;
            float apex = 0f;
            int settledAt = -1;

            for (int i = 0; i < steps; i++)
            {
                ball.SimulateTick(dt);
                Physics.Simulate(dt);

                float height = ball.Position.y;
                float speed = ball.Velocity.y;

                if (i < 20 || i % 20 == 0)
                {
                    log.AppendLine($"{i,4}   {height,6:0.000}   {speed,6:0.000}");
                }

                // A bounce is the step where downward motion becomes upward.
                if (previousSpeed < -0.2f && speed > 0.2f)
                {
                    bounce++;
                    apex = 0f;
                    log.AppendLine($"       bounce {bounce}: leaves ground at {speed:0.00} m/s");
                }

                if (speed > 0f)
                {
                    apex = Mathf.Max(apex, height);
                }

                if (settledAt < 0 && i > 20 && Mathf.Abs(speed) < 0.05f && height < ball.Radius + 0.02f)
                {
                    settledAt = i;
                }

                previousSpeed = speed;
            }

            log.AppendLine($"bounces: {bounce}   last apex: {apex:0.00} m");
            log.AppendLine(settledAt >= 0
                ? $"settled after {settledAt} steps ({settledAt * dt:0.00}s)"
                : "NEVER SETTLED in the probe window");

            Debug.Log(log.ToString());

            Object.DestroyImmediate(ball.gameObject);
            Object.DestroyImmediate(ground);
        }

        private static void Roll(string label, float launchSpeed, int steps)
        {
            GameObject ground = CreateGround();
            BallController ball = CreateBall(new Vector3(0f, 0.36f, 0f));

            ball.GetComponent<Rigidbody>().linearVelocity = new Vector3(0f, 0f, launchSpeed);

            const float dt = 1f / 50f;
            var log = new StringBuilder();
            log.AppendLine($"=== {label} ===");
            log.AppendLine("step   distance   speed");

            int stoppedAt = -1;

            for (int i = 0; i < steps; i++)
            {
                ball.SimulateTick(dt);
                Physics.Simulate(dt);

                float speed = new Vector2(ball.Velocity.x, ball.Velocity.z).magnitude;

                if (i % 25 == 0)
                {
                    log.AppendLine($"{i,4}   {ball.Position.z,8:0.00}   {speed,6:0.00}");
                }

                if (stoppedAt < 0 && speed < 1f)
                {
                    stoppedAt = i;
                    log.AppendLine($"       below 1 m/s at step {i} ({i * dt:0.00}s), " +
                                   $"{ball.Position.z:0.0} m out");
                }
            }

            log.AppendLine($"travelled {ball.Position.z:0.0} m in {steps * dt:0.0}s");
            log.AppendLine(stoppedAt < 0
                ? "still rolling at the end of the window — a pass carries a long way"
                : "came to rest inside the window");

            Debug.Log(log.ToString());

            Object.DestroyImmediate(ball.gameObject);
            Object.DestroyImmediate(ground);
        }

        /// <summary>
        /// Where a punch actually connects, printed as a map seen from above. Exists because
        /// the first version measured the cone in 3D and every punch at a ball on the floor
        /// whiffed — the sort of thing that is invisible in code and obvious in a grid.
        /// </summary>
        private static void PunchArc()
        {
            BallController ball = CreateBall(new Vector3(0f, 0.35f, 0f));
            Rigidbody ballBody = ball.GetComponent<Rigidbody>();

            var holder = new GameObject("ProbeHandler");
            holder.transform.position = Vector3.zero;              // facing +Z
            var handler = holder.AddComponent<BallHandler>();
            handler.Ball = ball;
            handler.Board = null;

            var log = new StringBuilder();
            log.AppendLine("=== PUNCH ARC (ball on the ground, handler at 0,0 facing +Z) ===");
            log.AppendLine($"reach {handler.PunchReach} m flat, cone {handler.PunchConeDegrees} deg, " +
                           $"origin {handler.CarryOffset.y} m up");
            log.AppendLine("rows = metres forward, cols = metres left..right, # = connects");

            for (float forward = 3f; forward >= -1f; forward -= 0.5f)
            {
                var row = new StringBuilder($"{forward,5:0.0}  ");

                for (float side = -2.5f; side <= 2.5f; side += 0.5f)
                {
                    var at = new Vector3(side, 0.35f, forward);
                    ball.transform.position = at;
                    ballBody.position = at;
                    row.Append(handler.BallInPunchArc ? " # " : " . ");
                }

                log.AppendLine(row.ToString());
            }

            log.AppendLine("       " + string.Concat(System.Linq.Enumerable.Range(0, 11)
                .Select(i => $"{(i * 0.5f) - 2.5f,3:0.#}")));

            Debug.Log(log.ToString());

            Object.DestroyImmediate(holder);
            Object.DestroyImmediate(ball.gameObject);
        }

        private static GameObject CreateGround()
        {
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.transform.localScale = new Vector3(200f, 1f, 200f);
            ground.transform.position = new Vector3(0f, -0.5f, 0f);
            return ground;
        }

        private static BallController CreateBall(Vector3 position)
        {
            var go = new GameObject("ProbeBall");
            go.transform.position = position;

            BallController ball = go.AddComponent<BallController>();
            ball.SendMessage("Awake", SendMessageOptions.DontRequireReceiver);

            return ball;
        }
    }
}
