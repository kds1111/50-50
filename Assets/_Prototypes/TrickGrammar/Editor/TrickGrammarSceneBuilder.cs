using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Prototypes.TrickGrammar.EditorTools
{
    /// <summary>
    /// PROTOTYPE. Builds the trick-grammar test scene from code so it can be regenerated
    /// or tweaked without hand-assembling a scene, and so the terrain the board is judged
    /// on is explicit and reviewable rather than dragged into place.
    ///
    /// Menu: Prototypes > Rebuild Trick Grammar Scene
    /// </summary>
    public static class TrickGrammarSceneBuilder
    {
        private const string ScenePath = "Assets/_Prototypes/TrickGrammar/TrickGrammar.unity";

        [MenuItem("Prototypes/Rebuild Trick Grammar Scene")]
        public static void Build()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            CreateLighting();
            CreateGround();
            CreateKicker(new Vector3(0f, 0f, 24f), 0f);
            CreateKicker(new Vector3(-14f, 0f, 6f), 90f);
            CreateQuarterPipe(new Vector3(16f, 0f, 10f), -90f);
            CreateFlatBank(new Vector3(10f, 0f, -14f), 200f);

            GameObject board = CreateBoard();
            CreateCamera(board.transform);
            CreateHud(board.GetComponent<BoardController>());

            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log($"[Prototype] Trick grammar scene written to {ScenePath}");
        }

        private static void CreateLighting()
        {
            var lightObject = new GameObject("Directional Light");
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            light.shadows = LightShadows.Soft;
            lightObject.transform.rotation = Quaternion.Euler(48f, -30f, 0f);

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.45f, 0.46f, 0.5f);
        }

        private static void CreateGround()
        {
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(80f, 1f, 80f);
            ground.transform.position = new Vector3(0f, -0.5f, 0f);
            Paint(ground, new Color(0.32f, 0.32f, 0.34f));
        }

        /// <summary>A kicker: the cheapest source of air a skateboard has.</summary>
        private static void CreateKicker(Vector3 position, float yaw)
        {
            GameObject ramp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ramp.name = "Kicker";
            ramp.transform.localScale = new Vector3(6f, 0.4f, 7f);
            ramp.transform.SetPositionAndRotation(
                position + new Vector3(0f, 0.6f, 0f),
                Quaternion.Euler(-16f, yaw, 0f));
            Paint(ramp, new Color(0.55f, 0.42f, 0.3f));
        }

        /// <summary>
        /// A quarterpipe, approximated by a fan of angled slabs. Crude on purpose —
        /// the question is whether a transition gives usable air, not whether the
        /// curve is correct.
        /// </summary>
        private static void CreateQuarterPipe(Vector3 position, float yaw)
        {
            var root = new GameObject("QuarterPipe");
            root.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));

            const int segments = 7;
            const float radius = 5.5f;

            for (int i = 0; i < segments; i++)
            {
                float t0 = (i / (float)segments) * 90f;
                float t1 = ((i + 1) / (float)segments) * 90f;
                float mid = (t0 + t1) * 0.5f;

                GameObject slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
                slab.name = $"Segment{i}";
                slab.transform.SetParent(root.transform, false);

                float y = radius - (Mathf.Cos(mid * Mathf.Deg2Rad) * radius);
                float z = Mathf.Sin(mid * Mathf.Deg2Rad) * radius;

                slab.transform.localPosition = new Vector3(0f, y, z);
                slab.transform.localRotation = Quaternion.Euler(-mid, 0f, 0f);
                slab.transform.localScale = new Vector3(9f, 0.4f, (radius * Mathf.PI / 2f / segments) + 0.35f);
                Paint(slab, new Color(0.5f, 0.5f, 0.54f));
            }
        }

        /// <summary>A long shallow bank — speed carrier, low-risk pop.</summary>
        private static void CreateFlatBank(Vector3 position, float yaw)
        {
            GameObject bank = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bank.name = "Bank";
            bank.transform.localScale = new Vector3(10f, 0.4f, 12f);
            bank.transform.SetPositionAndRotation(
                position + new Vector3(0f, 0.9f, 0f),
                Quaternion.Euler(-9f, yaw, 0f));
            Paint(bank, new Color(0.42f, 0.44f, 0.4f));
        }

        private static GameObject CreateBoard()
        {
            var root = new GameObject("Board");
            root.transform.position = new Vector3(0f, 0.6f, -6f);

            Rigidbody rb = root.AddComponent<Rigidbody>();
            rb.mass = 12f;
            rb.linearDamping = 0f;
            rb.angularDamping = 0.15f;
            // A low centre of mass is what stops the board from tipping on every landing.
            rb.centerOfMass = new Vector3(0f, -0.15f, 0f);

            BoxCollider collider = root.AddComponent<BoxCollider>();
            collider.size = new Vector3(0.36f, 0.12f, 0.9f);

            // Deck — visual only, but oriented so flips and shuvits are readable.
            GameObject deck = GameObject.CreatePrimitive(PrimitiveType.Cube);
            deck.name = "Deck";
            deck.transform.SetParent(root.transform, false);
            deck.transform.localScale = new Vector3(0.34f, 0.06f, 0.88f);
            Object.DestroyImmediate(deck.GetComponent<BoxCollider>());
            Paint(deck, new Color(0.16f, 0.16f, 0.2f));

            // Nose marker: without it you cannot see which way the board is pointing
            // mid-flip, which makes every shuvit unreadable.
            GameObject nose = GameObject.CreatePrimitive(PrimitiveType.Cube);
            nose.name = "Nose";
            nose.transform.SetParent(root.transform, false);
            nose.transform.localPosition = new Vector3(0f, 0.05f, 0.42f);
            nose.transform.localScale = new Vector3(0.3f, 0.04f, 0.12f);
            Object.DestroyImmediate(nose.GetComponent<BoxCollider>());
            Paint(nose, new Color(0.95f, 0.35f, 0.1f));

            // Grip-side marker, so a kickflip is visibly distinct from a heelflip.
            GameObject grip = GameObject.CreatePrimitive(PrimitiveType.Cube);
            grip.name = "GripStripe";
            grip.transform.SetParent(root.transform, false);
            grip.transform.localPosition = new Vector3(0.12f, 0.05f, 0f);
            grip.transform.localScale = new Vector3(0.06f, 0.04f, 0.84f);
            Object.DestroyImmediate(grip.GetComponent<BoxCollider>());
            Paint(grip, new Color(0.2f, 0.8f, 0.95f));

            // Rider stand-in. Purely visual: the controller tracks rider yaw as a number,
            // not as physics, which is what keeps a shuvit distinguishable from a 180.
            GameObject rider = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            rider.name = "Rider";
            rider.transform.SetParent(root.transform, false);
            rider.transform.localPosition = new Vector3(0f, 0.55f, 0f);
            rider.transform.localScale = new Vector3(0.28f, 0.5f, 0.28f);
            Object.DestroyImmediate(rider.GetComponent<CapsuleCollider>());
            Paint(rider, new Color(0.85f, 0.82f, 0.75f));

            root.AddComponent<BoardController>();
            return root;
        }

        private static void CreateCamera(Transform target)
        {
            var cameraObject = new GameObject("Chase Camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.fieldOfView = 68f;
            cameraObject.AddComponent<AudioListener>();

            ProtoChaseCamera chase = cameraObject.AddComponent<ProtoChaseCamera>();
            chase.Target = target;
        }

        private static void CreateHud(BoardController board)
        {
            var hudObject = new GameObject("Trick HUD");
            TrickHud hud = hudObject.AddComponent<TrickHud>();
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
            var material = new Material(shader) { color = color };
            renderer.sharedMaterial = material;
        }
    }
}
