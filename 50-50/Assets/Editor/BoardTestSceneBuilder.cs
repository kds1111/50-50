using FiftyFifty.Board;
using FiftyFifty.CameraRig;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FiftyFifty.EditorTools
{
    /// <summary>
    /// Builds the movement test scene for issue #16: a flat plane and one kicker, nothing else.
    ///
    /// Generated from code so the scene can be rebuilt after the board changes shape, and so
    /// what is being tested is readable here rather than buried in a .unity file. Anything you
    /// move by hand in the scene stays moved — rebuilding replaces the scene, so only rebuild
    /// when you want the original layout back.
    ///
    /// Menu: 50-50 > Rebuild Board Test Scene
    /// </summary>
    public static class BoardTestSceneBuilder
    {
        private const string ScenePath = "Assets/Scenes/BoardTest.unity";

        [MenuItem("50-50/Rebuild Board Test Scene")]
        public static void Build()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            CreateLighting();
            CreateGround();
            CreateKicker(new Vector3(0f, 0f, 22f));

            GameObject board = CreateBoard(new Vector3(0f, 0.4f, 0f));
            CreateCamera(board);
            CreateHud(board.GetComponent<BoardController>());

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log($"[50-50] Board test scene written to {ScenePath}");
        }

        private static void CreateLighting()
        {
            var go = new GameObject("Directional Light");
            Light light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.15f;
            light.shadows = LightShadows.Soft;
            go.transform.rotation = Quaternion.Euler(50f, -35f, 0f);

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.44f, 0.45f, 0.5f);
        }

        private static void CreateGround()
        {
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(70f, 1f, 70f);
            ground.transform.position = new Vector3(0f, -0.5f, 0f);
            Paint(ground, new Color(0.33f, 0.33f, 0.35f));

            // Stripes across the ground: without a reference pattern, speed is impossible to
            // judge on a flat grey plane.
            for (int i = 1; i < 14; i++)
            {
                GameObject stripe = GameObject.CreatePrimitive(PrimitiveType.Cube);
                stripe.name = $"Stripe{i}";
                stripe.transform.localScale = new Vector3(60f, 0.02f, 0.25f);
                stripe.transform.position = new Vector3(0f, 0.005f, (i * 5f) - 35f);
                Object.DestroyImmediate(stripe.GetComponent<BoxCollider>());
                Paint(stripe, new Color(0.27f, 0.27f, 0.29f));
            }
        }

        private static void CreateKicker(Vector3 position)
        {
            GameObject ramp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ramp.name = "Kicker";
            ramp.transform.localScale = new Vector3(7f, 0.5f, 8f);
            ramp.transform.SetPositionAndRotation(
                position + new Vector3(0f, 0.65f, 0f),
                Quaternion.Euler(-15f, 0f, 0f));
            Paint(ramp, new Color(0.54f, 0.42f, 0.3f));
        }

        private static GameObject CreateBoard(Vector3 position)
        {
            var root = new GameObject("Board");
            root.transform.position = position;

            Rigidbody rb = root.AddComponent<Rigidbody>();
            rb.mass = 10f;
            rb.linearDamping = 0f;
            rb.angularDamping = 0.2f;

            BoxCollider collider = root.AddComponent<BoxCollider>();
            collider.size = new Vector3(0.3f, 0.1f, 0.8f);

            // Deck.
            GameObject deck = GameObject.CreatePrimitive(PrimitiveType.Cube);
            deck.name = "Deck";
            deck.transform.SetParent(root.transform, false);
            deck.transform.localScale = new Vector3(0.28f, 0.05f, 0.8f);
            Object.DestroyImmediate(deck.GetComponent<BoxCollider>());
            Paint(deck, new Color(0.15f, 0.15f, 0.19f));

            // Nose marker — you cannot read board orientation in the air without one.
            GameObject nose = GameObject.CreatePrimitive(PrimitiveType.Cube);
            nose.name = "Nose";
            nose.transform.SetParent(root.transform, false);
            nose.transform.localPosition = new Vector3(0f, 0.04f, 0.38f);
            nose.transform.localScale = new Vector3(0.26f, 0.035f, 0.1f);
            Object.DestroyImmediate(nose.GetComponent<BoxCollider>());
            Paint(nose, new Color(0.95f, 0.36f, 0.1f));

            // Grip stripe down one side, so roll direction is readable mid-air.
            GameObject stripe = GameObject.CreatePrimitive(PrimitiveType.Cube);
            stripe.name = "GripStripe";
            stripe.transform.SetParent(root.transform, false);
            stripe.transform.localPosition = new Vector3(0.1f, 0.04f, 0f);
            stripe.transform.localScale = new Vector3(0.05f, 0.035f, 0.76f);
            Object.DestroyImmediate(stripe.GetComponent<BoxCollider>());
            Paint(stripe, new Color(0.2f, 0.8f, 0.95f));

            // Rider stand-in, so the board reads as ridden rather than remote-controlled.
            GameObject rider = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            rider.name = "Rider";
            rider.transform.SetParent(root.transform, false);
            rider.transform.localPosition = new Vector3(0f, 0.52f, 0f);
            rider.transform.localScale = new Vector3(0.26f, 0.48f, 0.26f);
            Object.DestroyImmediate(rider.GetComponent<CapsuleCollider>());
            Paint(rider, new Color(0.85f, 0.82f, 0.74f));

            var input = root.AddComponent<PlayerBoardInput>();
            var controller = root.AddComponent<BoardController>();
            controller.InputSource = input;

            return root;
        }

        private static void CreateCamera(GameObject board)
        {
            var go = new GameObject("Chase Camera");
            Camera cam = go.AddComponent<Camera>();
            cam.fieldOfView = 65f;
            go.AddComponent<AudioListener>();

            var chase = go.AddComponent<BoardChaseCamera>();
            chase.Target = board.transform;
            chase.InputSource = board.GetComponent<PlayerBoardInput>();
            chase.Board = board.GetComponent<BoardController>();
        }

        private static void CreateHud(BoardController board)
        {
            var go = new GameObject("Debug HUD");
            var hud = go.AddComponent<BoardDebugHud>();
            hud.Board = board;
        }

        private static void Paint(GameObject target, Color color)
        {
            var renderer = target.GetComponent<Renderer>();
            if (renderer == null)
            {
                return;
            }

            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            renderer.sharedMaterial = new Material(shader) { color = color };
        }
    }
}
