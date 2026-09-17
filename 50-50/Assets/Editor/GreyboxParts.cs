using FiftyFifty.Ball;
using FiftyFifty.Board;
using FiftyFifty.CameraRig;
using UnityEngine;

namespace FiftyFifty.EditorTools
{
    /// <summary>
    /// The greybox pieces the test scenes are assembled from. Shared so that the board in the
    /// ball scene is the same board as in the movement scene — if they drift, the ball scene
    /// stops being able to tell you anything about the board.
    /// </summary>
    public static class GreyboxParts
    {
        public static void CreateLighting()
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

        /// <summary>Flat plane with stripes across it — speed is unreadable on plain grey.</summary>
        public static void CreateGround(float size = 70f)
        {
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(size, 1f, size);
            ground.transform.position = new Vector3(0f, -0.5f, 0f);
            Paint(ground, new Color(0.33f, 0.33f, 0.35f));

            int stripes = Mathf.RoundToInt(size / 5f) - 1;
            for (int i = 1; i <= stripes; i++)
            {
                GameObject stripe = GameObject.CreatePrimitive(PrimitiveType.Cube);
                stripe.name = $"Stripe{i}";
                stripe.transform.localScale = new Vector3(size - 10f, 0.02f, 0.25f);
                stripe.transform.position = new Vector3(0f, 0.005f, (i * 5f) - (size * 0.5f));
                Object.DestroyImmediate(stripe.GetComponent<BoxCollider>());
                Paint(stripe, new Color(0.27f, 0.27f, 0.29f));
            }
        }

        public static void CreateKicker(Vector3 position, float yaw = 0f)
        {
            GameObject ramp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ramp.name = "Kicker";
            ramp.transform.localScale = new Vector3(7f, 0.5f, 8f);
            ramp.transform.SetPositionAndRotation(
                position + new Vector3(0f, 0.65f, 0f),
                Quaternion.Euler(-15f, yaw, 0f));
            Paint(ramp, new Color(0.54f, 0.42f, 0.3f));
        }

        /// <summary>
        /// The board. The rider capsule keeps its collider (#7) — without one the ball can only
        /// ever be struck at ankle height, and aerial play is dead before it is tested.
        /// </summary>
        public static GameObject CreateBoard(Vector3 position)
        {
            var root = new GameObject("Board");
            root.transform.position = position;

            Rigidbody rb = root.AddComponent<Rigidbody>();
            rb.mass = 10f;
            rb.linearDamping = 0f;
            rb.angularDamping = 0.2f;

            BoxCollider collider = root.AddComponent<BoxCollider>();
            collider.size = new Vector3(0.3f, 0.1f, 0.8f);

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

            GameObject rider = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            rider.name = "Rider";
            rider.transform.SetParent(root.transform, false);
            rider.transform.localPosition = new Vector3(0f, 0.52f, 0f);
            rider.transform.localScale = new Vector3(0.26f, 0.48f, 0.26f);
            Paint(rider, new Color(0.85f, 0.82f, 0.74f));

            var input = root.AddComponent<PlayerBoardInput>();
            var controller = root.AddComponent<BoardController>();
            controller.InputSource = input;

            return root;
        }

        public static GameObject CreateCamera(GameObject board)
        {
            var go = new GameObject("Chase Camera");
            Camera cam = go.AddComponent<Camera>();
            cam.fieldOfView = 65f;
            go.AddComponent<AudioListener>();

            var chase = go.AddComponent<BoardChaseCamera>();
            chase.Target = board.transform;
            chase.InputSource = board.GetComponent<PlayerBoardInput>();
            chase.Board = board.GetComponent<BoardController>();

            return go;
        }

        /// <summary>
        /// The ball: an empty root carrying the body and the world collider, with the mesh as a
        /// child so it can shrink while carried without the collider changing size under it.
        /// </summary>
        public static BallController CreateBall(Vector3 position)
        {
            var root = new GameObject("Ball");
            root.transform.position = position;

            var ball = root.AddComponent<BallController>();

            Rigidbody rb = root.GetComponent<Rigidbody>();
            rb.mass = ball.Mass;
            rb.useGravity = false;
            rb.linearDamping = ball.LinearDamping;
            rb.angularDamping = ball.AngularDamping;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            root.GetComponent<SphereCollider>().radius = ball.Radius;

            GameObject mesh = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            mesh.name = "Mesh";
            mesh.transform.SetParent(root.transform, false);
            mesh.transform.localScale = Vector3.one * ball.Diameter;
            Object.DestroyImmediate(mesh.GetComponent<SphereCollider>());
            Paint(mesh, new Color(0.95f, 0.78f, 0.2f));

            // A band round the ball so its spin is visible — a plain sphere at speed reads as
            // sliding rather than rolling.
            GameObject band = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            band.name = "Band";
            band.transform.SetParent(mesh.transform, false);
            band.transform.localScale = new Vector3(1.01f, 0.06f, 1.01f);
            Object.DestroyImmediate(band.GetComponent<CapsuleCollider>());
            Paint(band, new Color(0.15f, 0.15f, 0.18f));

            var audio = root.AddComponent<AudioSource>();
            audio.playOnAwake = false;
            audio.spatialBlend = 1f;

            ball.Visual = mesh.transform;
            ball.Audio = audio;

            return ball;
        }

        public static void Paint(GameObject target, Color color)
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
