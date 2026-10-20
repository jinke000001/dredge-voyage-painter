using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Jinke.DredgeMerchantTravel
{
    internal sealed class LandingQueryDiagnostics
    {
        public string sceneName, error;
        public int sceneHandle, copiedColliders;
        public bool independentPhysicsScene, activeScenePreserved, sourceFlagsPreserved, selfTestPassed, cleanupQueued;
        public Dictionary<string, bool> checks = new Dictionary<string, bool>();
    }

    internal sealed class LandingQueryWorld : IDisposable
    {
        // Do not unload this scene: DredgeInputManager resets all native actions on ANY sceneUnloaded event.
        // One empty local physics scene lasts until process exit; each inspection destroys only its own shapes.
        private static Scene queryScene;
        private readonly Dictionary<Transform, Transform> transforms = new Dictionary<Transform, Transform>();
        private readonly Dictionary<Collider, Collider> colliders = new Dictionary<Collider, Collider>();
        private GameObject root;
        private BoxCollider probe;
        private Mesh testMesh;
        internal readonly LandingQueryDiagnostics Diagnostics = new LandingQueryDiagnostics();
        internal bool Ready { get { return Diagnostics.selfTestPassed; } }

        internal LandingQueryWorld(float radius)
        {
            try
            {
                int activeScene = SceneManager.GetActiveScene().handle;
                if (!queryScene.IsValid() || !queryScene.isLoaded)
                    queryScene = SceneManager.CreateScene("Jinke.MerchantTravel.Physics." + Guid.NewGuid().ToString("N"),
                        new CreateSceneParameters(LocalPhysicsMode.Physics3D));
                Diagnostics.sceneName = queryScene.name;
                Diagnostics.sceneHandle = queryScene.handle;
                PhysicsScene physics = queryScene.GetPhysicsScene();
                Diagnostics.independentPhysicsScene = physics.IsValid() && !physics.Equals(Physics.defaultPhysicsScene);
                Diagnostics.activeScenePreserved = SceneManager.GetActiveScene().handle == activeScene;
                if (!Diagnostics.independentPhysicsScene || !Diagnostics.activeScenePreserved)
                    throw new InvalidOperationException("Independent physics scene unavailable.");
                root = NewObject("Merchant Travel Query Shapes", null);
                root.SetActive(true);
                GameObject probeObject = NewObject("Clearance Box", root.transform);
                probe = probeObject.AddComponent<BoxCollider>();
                probe.size = new Vector3(radius * 2f, 5f, radius * 2f);
                probeObject.SetActive(true);
                SelfTest(radius);
                if (!Ready) Diagnostics.error = "Geometry positive/negative self-test failed.";
            }
            catch (Exception exception) { Diagnostics.error = exception.GetType().Name + ": " + exception.Message; }
        }

        internal static bool Owns(Scene scene) { return queryScene.IsValid() && scene.handle == queryScene.handle; }

        private static GameObject NewObject(string name, Transform parent)
        {
            var value = new GameObject(name);
            value.SetActive(false);
            // There are no colliders or scripts until the empty object belongs to the isolated physics scene.
            SceneManager.MoveGameObjectToScene(value, queryScene);
            value.hideFlags = HideFlags.HideInHierarchy;
            if (parent != null) value.transform.SetParent(parent, false);
            return value;
        }

        private Transform CopyTransform(Transform source)
        {
            Transform copy;
            if (transforms.TryGetValue(source, out copy)) return copy;
            Transform parent = source.parent != null ? CopyTransform(source.parent) : root.transform;
            GameObject value = NewObject("Geometry Transform", parent);
            copy = value.transform;
            copy.localPosition = source.localPosition;
            copy.localRotation = source.localRotation;
            copy.localScale = source.localScale;
            transforms.Add(source, copy);
            value.SetActive(true);
            return copy;
        }

        private Collider CopyCollider(Collider source)
        {
            Collider copy;
            if (colliders.TryGetValue(source, out copy)) return copy;
            GameObject value = CopyTransform(source.transform).gameObject;
            var box = source as BoxCollider;
            var sphere = source as SphereCollider;
            var capsule = source as CapsuleCollider;
            var mesh = source as MeshCollider;
            if (box != null)
            {
                var target = value.AddComponent<BoxCollider>();
                target.center = box.center; target.size = box.size; copy = target;
            }
            else if (sphere != null)
            {
                var target = value.AddComponent<SphereCollider>();
                target.center = sphere.center; target.radius = sphere.radius; copy = target;
            }
            else if (capsule != null)
            {
                var target = value.AddComponent<CapsuleCollider>();
                target.center = capsule.center; target.radius = capsule.radius;
                target.height = capsule.height; target.direction = capsule.direction; copy = target;
            }
            else if (mesh != null && mesh.sharedMesh != null)
            {
                var target = value.AddComponent<MeshCollider>();
                target.cookingOptions = mesh.cookingOptions;
                target.convex = mesh.convex;
                target.sharedMesh = mesh.sharedMesh; // Shared source mesh is read-only; no vertices/materials are changed.
                copy = target;
            }
            else throw new NotSupportedException("Unsupported collision shape: " + source.GetType().Name);
            copy.isTrigger = false;
            copy.enabled = true;
            if (copy.attachedRigidbody != null || !copy.gameObject.activeInHierarchy || copy.bounds.size.sqrMagnitude <= 0)
                throw new InvalidOperationException("Copied collider has no usable native shape.");
            colliders.Add(source, copy);
            Diagnostics.copiedColliders++;
            return copy;
        }

        private bool Overlap(Collider target, Vector3 point)
        {
            Vector3 direction;
            float distance;
            return Physics.ComputePenetration(probe, point, Quaternion.identity, target,
                target.transform.position, target.transform.rotation, out direction, out distance);
        }

        internal bool Overlaps(Collider source, Vector3 point)
        {
            if (!Ready) throw new InvalidOperationException("Geometry self-test has not passed.");
            return Overlap(CopyCollider(source), point);
        }

        private void SelfTest(float radius)
        {
            // Exercise the exact copy path used for distant inactive game colliders, including a disabled source.
            var sources = new List<Collider>();
            GameObject boxObject = NewObject("Self Check Source Box", root.transform);
            BoxCollider box = boxObject.AddComponent<BoxCollider>(); box.size = Vector3.one * 2f; box.enabled = false;
            sources.Add(box);
            GameObject sphereObject = NewObject("Self Check Source Sphere", root.transform);
            SphereCollider sphere = sphereObject.AddComponent<SphereCollider>(); sphere.radius = 1f;
            sources.Add(sphere);
            GameObject capsuleObject = NewObject("Self Check Source Capsule", root.transform);
            CapsuleCollider capsule = capsuleObject.AddComponent<CapsuleCollider>(); capsule.radius = 1f; capsule.height = 4f;
            sources.Add(capsule);
            testMesh = CubeMesh();
            foreach (bool convex in new[] { false, true })
            {
                GameObject meshObject = NewObject("Self Check Source Mesh", root.transform);
                MeshCollider mesh = meshObject.AddComponent<MeshCollider>(); mesh.convex = convex; mesh.sharedMesh = testMesh;
                sources.Add(mesh);
            }
            GameObject scaledSphereObject = NewObject("Self Check Nonuniform Sphere", root.transform);
            scaledSphereObject.transform.localScale = new Vector3(1, 2, 1);
            SphereCollider scaledSphere = scaledSphereObject.AddComponent<SphereCollider>(); scaledSphere.radius = 1f;
            sources.Add(scaledSphere);
            GameObject scaledCapsuleObject = NewObject("Self Check Nonuniform Capsule", root.transform);
            scaledCapsuleObject.transform.localScale = new Vector3(1, 1, 2);
            CapsuleCollider scaledCapsule = scaledCapsuleObject.AddComponent<CapsuleCollider>(); scaledCapsule.radius = 1f; scaledCapsule.height = 4f;
            sources.Add(scaledCapsule);
            string[] names = { "box", "sphere", "capsule", "mesh", "convexMesh", "scaledSphere", "scaledCapsule" };
            var positive = new bool[names.Length];
            var negative = new bool[names.Length];
            bool broadphasePassed = true;
            for (int i = 0; i < sources.Count; i++)
            {
                Collider copied = CopyCollider(sources[i]);
                Vector3 overlapPoint = new Vector3(radius + (i < 5 ? .5f : 1.5f), 0, 0);
                positive[i] = Overlap(copied, overlapPoint);
                negative[i] = Overlap(copied, new Vector3(radius + 3f, 0, 0));
                Bounds sourceBounds;
                bool broadphaseOverlap = LandingPlanner.TryBounds(sources[i], out sourceBounds) &&
                    sourceBounds.Intersects(new Bounds(overlapPoint, probe.size));
                broadphasePassed &= broadphaseOverlap;
                Diagnostics.checks.Add(names[i] + "Overlap", positive[i]);
                Diagnostics.checks.Add(names[i] + "Apart", negative[i]);
                Diagnostics.checks.Add(names[i] + "BroadphaseOverlap", broadphaseOverlap);
            }
            Diagnostics.sourceFlagsPreserved = true;
            for (int i = 0; i < sources.Count; i++)
                Diagnostics.sourceFlagsPreserved &= !sources[i].gameObject.activeSelf && sources[i].enabled == (i != 0);
            Diagnostics.selfTestPassed = LandingRules.GeometrySelfTestPassed(Diagnostics.independentPhysicsScene &&
                Diagnostics.activeScenePreserved && Diagnostics.sourceFlagsPreserved && broadphasePassed, positive, negative);
        }

        private static Mesh CubeMesh()
        {
            var mesh = new Mesh { name = "Merchant Travel Geometry Self Check" };
            mesh.vertices = new[] { new Vector3(-1,-1,-1), new Vector3(1,-1,-1), new Vector3(1,1,-1), new Vector3(-1,1,-1),
                new Vector3(-1,-1,1), new Vector3(1,-1,1), new Vector3(1,1,1), new Vector3(-1,1,1) };
            mesh.triangles = new[] { 4,5,6, 4,6,7, 0,3,2, 0,2,1, 1,2,6, 1,6,5, 0,4,7, 0,7,3, 3,7,6, 3,6,2, 0,1,5, 0,5,4 };
            mesh.RecalculateBounds();
            return mesh;
        }

        public void Dispose()
        {
            if (root != null) { root.SetActive(false); Object.Destroy(root); root = null; }
            if (testMesh != null) { Object.Destroy(testMesh); testMesh = null; }
            colliders.Clear(); transforms.Clear();
            Diagnostics.cleanupQueued = true;
        }
    }
}
