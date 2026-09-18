using FiftyFifty.Ball;
using UnityEngine;

namespace FiftyFifty.EditorTools
{
    /// <summary>
    /// Greybox pieces for the arena (#9). Primitives only — the map keeps produced art out of
    /// this slice, and greybox commits to nothing.
    ///
    /// Everything here is a box. A real quarterpipe is a curved transition, and these are flat
    /// banks standing in for one: a curve made of segments in greybox gives the raycast
    /// suspension seams to catch on, which would be a physics bug invented by the scenery rather
    /// than by the board. Flat banks launch predictably, which is what #6 needs from a ramp —
    /// trick durations are fixed seconds, so a ramp whose airtime cannot be learned makes them
    /// unlearnable too.
    /// </summary>
    public static class ArenaParts
    {
        private static readonly Color Concrete = new(0.62f, 0.61f, 0.6f);
        private static readonly Color DarkConcrete = new(0.4f, 0.39f, 0.4f);
        private static readonly Color Metal = new(0.78f, 0.79f, 0.82f);
        private static readonly Color Boundary = new(0.16f, 0.16f, 0.19f);

        /// <summary>
        /// A launch surface. Pitch is the parameter that matters: #6 wants a small number of
        /// clearly different, memorable airtimes, not a continuum nobody can learn.
        /// </summary>
        public static GameObject CreateBank(
            Vector3 position, float yaw, float pitch, float width, float length, string name)
        {
            GameObject bank = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bank.name = name;
            bank.transform.localScale = new Vector3(width, 0.5f, length);
            bank.transform.SetPositionAndRotation(
                position + new Vector3(0f, 0.25f, 0f),
                Quaternion.Euler(-pitch, yaw, 0f));
            GreyboxParts.Paint(bank, Concrete);
            return bank;
        }

        /// <summary>
        /// A grindable edge. Nothing detects a grind yet — that is #12, and it is blocked on this
        /// ticket and #6 — but the geometry has to exist before the question can be argued, and
        /// a ledge is also just a thing to pop off.
        /// </summary>
        public static GameObject CreateLedge(
            Vector3 position, float yaw, float length, float height, string name = "Ledge")
        {
            GameObject ledge = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ledge.name = name;
            ledge.transform.localScale = new Vector3(1.2f, height, length);
            ledge.transform.SetPositionAndRotation(
                position + new Vector3(0f, height * 0.5f, 0f),
                Quaternion.Euler(0f, yaw, 0f));
            GreyboxParts.Paint(ledge, DarkConcrete);
            return ledge;
        }

        /// <summary>A flat bar on two posts. Same note as the ledge: geometry before detection.</summary>
        public static GameObject CreateRail(
            Vector3 position, float yaw, float length, float height, string name = "Rail")
        {
            var root = new GameObject(name);
            root.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));

            GameObject bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bar.name = "Bar";
            bar.transform.SetParent(root.transform, false);
            bar.transform.localPosition = new Vector3(0f, height, 0f);
            bar.transform.localScale = new Vector3(0.12f, 0.12f, length);
            GreyboxParts.Paint(bar, Metal);

            for (int i = 0; i < 2; i++)
            {
                GameObject post = GameObject.CreatePrimitive(PrimitiveType.Cube);
                post.name = $"Post{i}";
                post.transform.SetParent(root.transform, false);
                post.transform.localPosition =
                    new Vector3(0f, height * 0.5f, (i == 0 ? -1f : 1f) * (length * 0.4f));
                post.transform.localScale = new Vector3(0.1f, height, 0.1f);
                GreyboxParts.Paint(post, Metal);
            }

            return root;
        }

        /// <summary>A low flat box. Something to roll across without leaving the ground.</summary>
        public static GameObject CreateManualPad(Vector3 position, float yaw, float width, float length)
        {
            GameObject pad = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pad.name = "Manual Pad";
            pad.transform.localScale = new Vector3(width, 0.35f, length);
            pad.transform.SetPositionAndRotation(
                position + new Vector3(0f, 0.175f, 0f),
                Quaternion.Euler(0f, yaw, 0f));
            GreyboxParts.Paint(pad, DarkConcrete);
            return pad;
        }

        /// <summary>
        /// A stair set, as a stack of boxes. Present because the reference has one and because a
        /// gap to clear is a different kind of obstacle from a thing to launch off — but note it
        /// is the piece most likely to be cut: stairs reward precision the ball does not care
        /// about.
        /// </summary>
        public static GameObject CreateStairs(Vector3 position, float yaw, int steps, float width)
        {
            var root = new GameObject("Stairs");
            root.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));

            const float rise = 0.3f;
            const float run = 0.45f;

            for (int i = 0; i < steps; i++)
            {
                GameObject step = GameObject.CreatePrimitive(PrimitiveType.Cube);
                step.name = $"Step{i}";
                step.transform.SetParent(root.transform, false);
                step.transform.localScale = new Vector3(width, rise * (i + 1), run);
                step.transform.localPosition =
                    new Vector3(0f, rise * (i + 1) * 0.5f, -(i * run));
                GreyboxParts.Paint(step, Concrete);
            }

            return root;
        }

        /// <summary>
        /// A boundary wall. The glossary calls walls and a ceiling the arena's boundaries; the
        /// ceiling is deliberately not built, because whether the arena is closed overhead is a
        /// live question and a lid would answer it by accident.
        /// </summary>
        public static GameObject CreateWall(Vector3 position, Vector3 size, string name)
        {
            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = name;
            wall.transform.localScale = size;
            wall.transform.position = position;
            GreyboxParts.Paint(wall, Boundary);
            return wall;
        }

        /// <summary>
        /// A goal. Still the dumb target from #7 — it counts entries and nothing else, because a
        /// target that scored would prejudge #8 and #20.
        /// </summary>
        public static GoalTargetVolume CreateGoal(Vector3 position, float yaw, float width, float height)
        {
            var root = new GameObject("Goal");
            root.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));

            CreatePost(root, new Vector3(-width * 0.5f, height * 0.5f, 0f), new Vector3(0.4f, height, 0.4f));
            CreatePost(root, new Vector3(width * 0.5f, height * 0.5f, 0f), new Vector3(0.4f, height, 0.4f));
            CreatePost(root, new Vector3(0f, height, 0f), new Vector3(width + 0.4f, 0.4f, 0.4f));

            var volume = new GameObject("Volume");
            volume.transform.SetParent(root.transform, false);
            volume.transform.localPosition = new Vector3(0f, height * 0.5f, 0.3f);

            var box = volume.AddComponent<BoxCollider>();
            box.size = new Vector3(width, height, 0.6f);
            box.isTrigger = true;

            return volume.AddComponent<GoalTargetVolume>();
        }

        private static void CreatePost(GameObject parent, Vector3 localPosition, Vector3 scale)
        {
            GameObject post = GameObject.CreatePrimitive(PrimitiveType.Cube);
            post.name = "Post";
            post.transform.SetParent(parent.transform, false);
            post.transform.localPosition = localPosition;
            post.transform.localScale = scale;
            GreyboxParts.Paint(post, new Color(0.9f, 0.55f, 0.15f));
        }
    }
}
