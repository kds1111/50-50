using System.Text;
using FiftyFifty.Board;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FiftyFifty.EditorTools
{
    /// <summary>
    /// Headless measurement of the board's suspension. Builds a board over a flat plane,
    /// steps physics manually, and prints the height and vertical speed every step.
    ///
    /// Exists because the landing bounce was being debugged by reasoning about the code rather
    /// than by watching numbers, which produced three wrong diagnoses in a row.
    ///
    /// Menu: 50-50 > Probe Board Suspension
    /// </summary>
    public static class BoardSuspensionProbe
    {
        [MenuItem("50-50/Probe Board Suspension")]
        public static void Run()
        {
            if (!ProbeScene.Begin(out string sceneToRestore))
            {
                return;
            }

            SimulationMode previous = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script;

            try
            {
                Probe("REST  (spawned exactly at ride height)", 0.18f, 240);
                Probe("DROP  (from 2m up)", 2.0f, 240);
            }
            finally
            {
                Physics.simulationMode = previous;
                ProbeScene.End(sceneToRestore);
            }
        }

        private static void Probe(string label, float startHeight, int steps)
        {
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.transform.localScale = new Vector3(60f, 1f, 60f);
            ground.transform.position = new Vector3(0f, -0.5f, 0f);

            var boardObject = new GameObject("ProbeBoard");
            boardObject.transform.position = new Vector3(0f, startHeight, 0f);

            Rigidbody rb = boardObject.AddComponent<Rigidbody>();
            rb.mass = 10f;
            rb.linearDamping = 0f;
            rb.angularDamping = 0.2f;

            BoxCollider box = boardObject.AddComponent<BoxCollider>();
            box.size = new Vector3(0.3f, 0.1f, 0.8f);

            BoardController board = boardObject.AddComponent<BoardController>();
            board.SendMessage("Awake", SendMessageOptions.DontRequireReceiver);

            const float dt = 1f / 50f;
            var log = new StringBuilder();
            log.AppendLine($"=== {label} ===");
            log.AppendLine("step   height   vSpeed   grounded");

            float minAfterSettle = float.MaxValue;
            float maxAfterSettle = float.MinValue;

            for (int i = 0; i < steps; i++)
            {
                board.SimulateTick(dt);
                Physics.Simulate(dt);

                float height = rb.position.y;
                float vSpeed = rb.linearVelocity.y;

                // Log densely early, then every 10th step.
                if (i < 30 || i % 10 == 0)
                {
                    log.AppendLine($"{i,4}   {height,6:0.000}   {vSpeed,6:0.000}   {board.Grounded}");
                }

                // Measure the wobble only after it has had time to settle.
                if (i > 120)
                {
                    minAfterSettle = Mathf.Min(minAfterSettle, height);
                    maxAfterSettle = Mathf.Max(maxAfterSettle, height);
                }
            }

            float wobble = maxAfterSettle - minAfterSettle;
            log.AppendLine($"settled band: {minAfterSettle:0.0000} .. {maxAfterSettle:0.0000}  " +
                           $"(wobble {wobble * 1000f:0.0} mm)");
            log.AppendLine(wobble < 0.002f ? "VERDICT: stable" : "VERDICT: OSCILLATING");

            Debug.Log(log.ToString());

            Object.DestroyImmediate(boardObject);
            Object.DestroyImmediate(ground);
        }
    }
}
