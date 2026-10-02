// Editor-only validation harness. Copy into Assets/Editor in a disposable copy
// of the project, then run Unity -batchmode -nographics -executeMethod
// Phase1HandlingValidation.RunBatch (without -quit).
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class Phase1HandlingValidation
{
    private const string Pending = "PDT.Phase1Validation.Pending";
    private static readonly List<string> Results = new List<string>();
    private static readonly BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;
    private static PhysicsScene physicsScene;
    private static Scene scene;
    private static PlayerCarController car;
    private static Rigidbody body;
    private static GameObject vehicle;
    private static GameObject ground;

    static Phase1HandlingValidation()
    {
        // Use update rather than delayCall: unrelated startup callbacks can
        // throw during a fresh headless import and discard the delayCall queue.
        EditorApplication.update += () =>
        {
            if (EditorApplication.isPlaying && !EditorApplication.isCompiling && SessionState.GetBool(Pending, false))
            {
                SessionState.SetBool(Pending, false);
                Validate();
            }
        };
    }

    public static void RunBatch()
    {
        if (!Application.isBatchMode)
            throw new InvalidOperationException("Run only in a disposable batch project copy.");
        // Empty scene prevents any wallet/network components from running.
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        SessionState.SetBool(Pending, true);
        Debug.Log("Phase 1 validation: entering Play mode.");
        EditorApplication.EnterPlaymode();
    }

    private static void Validate()
    {
        int exitCode = 0;
        try
        {
            scene = SceneManager.CreateScene("Handling checks", new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            physicsScene = scene.GetPhysicsScene();
            ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            SceneManager.MoveGameObjectToScene(ground, scene);
            ground.transform.position = new Vector3(0f, -0.5f, 0f);
            ground.transform.localScale = new Vector3(500f, 1f, 500f);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/PDT/Prefabs/DreamMobileCar.prefab");
            Require(prefab != null, "Actual vehicle prefab imports");
            vehicle = Object.Instantiate(prefab, Vector3.up, Quaternion.identity);
            SceneManager.MoveGameObjectToScene(vehicle, scene);
            car = vehicle.GetComponent<PlayerCarController>();
            body = vehicle.GetComponent<Rigidbody>();
            Require(car != null && body != null, "Existing controller and Rigidbody are reused");
            var material = vehicle.GetComponentInChildren<BoxCollider>().sharedMaterial;
            Require(material != null && material.dynamicFriction == 0f && material.staticFriction == 0f
                && material.frictionCombine == PhysicsMaterialCombine.Minimum, "Car material uses controller-managed grip");
            Require(body.interpolation == RigidbodyInterpolation.Interpolate
                && body.collisionDetectionMode == CollisionDetectionMode.ContinuousDynamic,
                "Prefab enables interpolation and continuous dynamic collision detection");
            Physics.SyncTransforms();

            Settle();
            Input(1f, 0f);
            Step(10);
            Require(body.linearVelocity.z > 0.5f && body.linearVelocity.z < 2f, "Acceleration is gradual after 0.2 seconds");
            Step(190);
            Require(Mathf.Abs(body.linearVelocity.z - 16f) < 0.2f, "Forward speed settles at 16 m/s");
            Input(-1f, 0f);
            Step(25);
            Require(body.linearVelocity.z > 5f && body.linearVelocity.z < 9f, "Opposite input brakes without instant reverse");
            Step(100);
            Require(Mathf.Abs(body.linearVelocity.z + 5f) < 0.2f, "Holding brake engages capped reverse after stopping");
            Input(1f, 0f);
            Step(10);
            Require(body.linearVelocity.z < 0f && body.linearVelocity.z > -2f, "Forward input brakes reverse travel first");
            Step(100);
            Require(body.linearVelocity.z > 10f, "Forward drive resumes after reverse braking");

            Settle();
            body.linearVelocity = Vector3.forward * 10f;
            Input(0f, 0f);
            Step(25);
            Require(body.linearVelocity.z > 8f && body.linearVelocity.z < 9.5f, "Releasing throttle coasts rather than stopping instantly");
            Input(0f, 1f);
            Step(20);
            Require(body.rotation.eulerAngles.y > 5f && body.rotation.eulerAngles.y < 60f, "Steering still works while coasting");

            Settle();
            Input(0f, 1f);
            Step(40);
            Require(Quaternion.Angle(body.rotation, Quaternion.identity) < 0.5f, "No steering in place");
            body.linearVelocity = Vector3.forward * 3f;
            Step(1);
            float lowSpeedYaw = Mathf.Abs(body.angularVelocity.y);
            body.linearVelocity = body.rotation * Vector3.forward * 16f;
            Step(1);
            Require(Mathf.Abs(body.angularVelocity.y) < lowSpeedYaw * 0.7f, "High-speed steering sensitivity is reduced");
            body.linearVelocity = body.rotation * Vector3.back * 3f;
            Step(1);
            Require(body.angularVelocity.y < 0f, "Reverse travel reverses steering direction");

            Settle();
            body.linearVelocity = new Vector3(5f, 0f, 5f);
            Step(40);
            Require(Mathf.Abs(body.linearVelocity.x) < 0.1f && body.linearVelocity.z > 2f,
                "Grip removes lateral sliding while preserving forward travel");
            body.Sleep();
            Step(10);
            Input(1f, 0f);
            Step(25);
            Require(body.linearVelocity.z > 2f, "Input wakes a sleeping car and restores traction");

            car.ResetVehicle();
            body.position = new Vector3(0f, 10f, 0f);
            Input(1f, 1f);
            Step(15);
            Require(Mathf.Abs(body.linearVelocity.x) < 0.01f && Mathf.Abs(body.linearVelocity.z) < 0.01f
                && body.linearVelocity.y < -2f && Mathf.Abs(body.angularVelocity.y) < 0.01f,
                "Airborne input leaves gravity active without drive or steering forces");

            int instanceId = vehicle.GetInstanceID();
            Transform cameraTarget = vehicle.transform.Find("CameraTarget");
            body.position = new Vector3(7f, 2f, -3f);
            body.rotation = Quaternion.Euler(0f, 60f, 180f);
            body.linearVelocity = new Vector3(5f, -4f, 3f);
            body.angularVelocity = Vector3.one;
            car.ResetVehicle();
            Require(Vector3.Distance(body.position, Vector3.up) < 0.001f
                && Quaternion.Angle(body.rotation, Quaternion.identity) < 0.001f
                && body.linearVelocity.sqrMagnitude < 0.001f && body.angularVelocity.sqrMagnitude < 0.001f,
                "Manual reset restores upright spawn pose and clears all momentum");
            Require(vehicle.GetInstanceID() == instanceId && cameraTarget != null
                && vehicle.transform.Find("CameraTarget") == cameraTarget, "Reset preserves vehicle identity and camera target");
            body.position = new Vector3(50f, -20f, 50f);
            Tick();
            Require(Vector3.Distance(body.position, Vector3.up) < 0.001f, "Falling below the recovery threshold automatically resets");

            Settle();
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            SceneManager.MoveGameObjectToScene(wall, scene);
            wall.transform.position = new Vector3(0f, 1f, 6f);
            wall.transform.localScale = new Vector3(20f, 3f, 0.2f);
            Physics.SyncTransforms();
            body.linearVelocity = Vector3.forward * 16f;
            Input(1f, 0f);
            Step(60);
            Require(body.position.z < 5f && body.position.y > 0f, "Wall impact does not tunnel or drop through the ground");
            Require(Vector3.Dot(body.rotation * Vector3.up, Vector3.up) > 0.999f,
                "Existing pitch and roll locks keep the car upright after impact");
            Object.DestroyImmediate(wall);
            Vector3 otherSpawn = new Vector3(4f, 1f, -7f);
            Quaternion otherHeading = Quaternion.Euler(0f, 65f, 0f);
            var otherVehicle = Object.Instantiate(prefab, otherSpawn, otherHeading);
            SceneManager.MoveGameObjectToScene(otherVehicle, scene);
            var otherBody = otherVehicle.GetComponent<Rigidbody>();
            otherBody.position = Vector3.one * 30f;
            otherVehicle.GetComponent<PlayerCarController>().ResetVehicle();
            Require(Vector3.Distance(otherBody.position, otherSpawn) < 0.001f
                && Quaternion.Angle(otherBody.rotation, otherHeading) < 0.01f,
                "Each dynamically instantiated car remembers its own spawn position and heading");
            Object.DestroyImmediate(otherVehicle);
            Results.Add("PASS: All Phase 1 runtime physics checks completed.");
        }
        catch (Exception exception)
        {
            Results.Add("FAIL: " + exception);
            exitCode = 1;
        }
        finally
        {
            Directory.CreateDirectory("Logs");
            File.WriteAllLines("Logs/Phase1-results.txt", Results);
            Debug.Log(string.Join("\n", Results));
            EditorApplication.Exit(exitCode);
        }
    }

    private static void Require(bool condition, string description)
    {
        if (!condition) throw new Exception(description + " | position=" + (body == null ? "n/a" : body.position.ToString())
            + " velocity=" + (body == null ? "n/a" : body.linearVelocity.ToString()));
        Results.Add("PASS: " + description);
    }

    private static void Input(float move, float turn)
    {
        typeof(PlayerCarController).GetField("moveInput", PrivateInstance).SetValue(car, move);
        typeof(PlayerCarController).GetField("turnInput", PrivateInstance).SetValue(car, turn);
    }

    private static void Tick() => typeof(PlayerCarController).GetMethod("FixedUpdate", PrivateInstance).Invoke(car, null);

    private static void Step(int count)
    {
        for (int i = 0; i < count; i++)
        {
            Tick();
            physicsScene.Simulate(Time.fixedDeltaTime);
        }
    }

    private static void Settle()
    {
        car.ResetVehicle();
        Step(80);
        Require(Mathf.Abs(body.position.y - 0.2f) < 0.06f, "Actual prefab settles on its box collider");
    }
}
