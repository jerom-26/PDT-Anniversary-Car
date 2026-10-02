using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;

[RequireComponent(typeof(Rigidbody))]
public class PlayerCarController : MonoBehaviour
{
    [Header("Speed (metres per second)")]
    [FormerlySerializedAs("moveSpeed")]
    [SerializeField, Min(0.1f)] private float maxForwardSpeed = 16f;
    [SerializeField, Min(0.1f)] private float maxReverseSpeed = 5f;

    [Header("Acceleration (metres per second squared)")]
    [SerializeField, Min(0f)] private float acceleration = 8f;
    [SerializeField, Min(0f)] private float reverseAcceleration = 5f;
    [Tooltip("Opposite input brakes to a stop before engaging the other direction.")]
    [SerializeField, Min(0f)] private float braking = 18f;
    [SerializeField, Min(0f)] private float coastingDeceleration = 2.5f;

    [Header("Steering")]
    [Tooltip("Maximum yaw rate in degrees per second.")]
    [SerializeField] private float turnSpeed = 90f;
    [SerializeField, Range(0.1f, 1f)] private float highSpeedSteeringMultiplier = 0.4f;
    [Tooltip("Speed at which full low-speed steering becomes available. No turning in place.")]
    [SerializeField, Min(0.1f)] private float fullSteeringSpeed = 3f;
    [Tooltip("How quickly steering input moves between -1 and 1.")]
    [SerializeField, Min(0.1f)] private float steeringResponse = 6f;

    [Header("Grip")]
    [Tooltip("Sideways velocity damping per second. Higher values reduce sliding.")]
    [SerializeField, Min(0f)] private float lateralGrip = 10f;
    [Tooltip("Limits sideways correction so impacts and small slides are still possible.")]
    [SerializeField, Min(0f)] private float maxGripAcceleration = 25f;

    [Header("Recovery")]
    [Tooltip("Automatically return to the original spawn pose after falling this far below it.")]
    [SerializeField, Min(1f)] private float fallResetDistance = 15f;

    private float turnInput;
    private Rigidbody rb;
    private float moveInput;
    private float smoothedTurnInput;
    private bool resetRequested;
    private bool hasGroundContact;
    private Vector3 groundNormal = Vector3.up;
    private Vector3 spawnPosition;
    private Quaternion spawnRotation;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        // Instantiate supplies the entitled vehicle's spawn pose before Awake.
        // Recovery moves this same instance; it never creates or unlocks a vehicle.
        spawnPosition = rb.position;
        spawnRotation = Quaternion.Euler(0f, rb.rotation.eulerAngles.y, 0f);
    }

    private void Update()
    {
        moveInput = 0f;
        turnInput = 0f;

        Keyboard keyboard = Keyboard.current;
        if (keyboard == null || !Application.isFocused)
        {
            return;
        }

        if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed)
        {
            moveInput += 1f;
        }

        if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed)
        {
            moveInput -= 1f;
        }

        if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed)
        {
            turnInput -= 1f;
        }

        if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed)
        {
            turnInput += 1f;
        }

        resetRequested |= keyboard.rKey.wasPressedThisFrame;
    }

    private void FixedUpdate()
    {
        if (resetRequested || rb.position.y < spawnPosition.y - fallResetDistance)
        {
            ResetVehicle();
            return;
        }

        // Sleeping bodies do not send OnCollisionStay. Wake on input so support
        // is detected again before applying drive forces.
        if (moveInput != 0f || turnInput != 0f)
        {
            rb.WakeUp();
        }

        bool grounded = hasGroundContact && Vector3.Dot(rb.rotation * Vector3.up, Vector3.up) > 0.5f;
        hasGroundContact = false;
        smoothedTurnInput = Mathf.MoveTowards(smoothedTurnInput, turnInput, steeringResponse * Time.fixedDeltaTime);
        if (!grounded)
        {
            return; // Keep gravity and momentum in the air; no midair drive or grip.
        }

        Vector3 forward = Vector3.ProjectOnPlane(rb.rotation * Vector3.forward, groundNormal).normalized;
        Vector3 right = Vector3.Cross(groundNormal, forward).normalized;
        float forwardSpeed = Vector3.Dot(rb.linearVelocity, forward);
        MoveCar(forward, forwardSpeed);
        ApplyGrip(right);
        TurnCar(forwardSpeed);
    }

    private void MoveCar(Vector3 forward, float forwardSpeed)
    {
        float targetSpeed = 0f;
        float changeRate = coastingDeceleration;
        if (moveInput != 0f)
        {
            bool changingDirection = forwardSpeed * moveInput < -0.1f;
            targetSpeed = changingDirection ? 0f : moveInput * (moveInput > 0f ? maxForwardSpeed : maxReverseSpeed);
            changeRate = changingDirection ? braking : (moveInput > 0f ? acceleration : reverseAcceleration);
        }

        float nextSpeed = Mathf.MoveTowards(forwardSpeed, targetSpeed, changeRate * Time.fixedDeltaTime);
        rb.AddForce(forward * (nextSpeed - forwardSpeed), ForceMode.VelocityChange);
    }

    private void ApplyGrip(Vector3 right)
    {
        float sidewaysSpeed = Vector3.Dot(rb.linearVelocity, right);
        float correction = -sidewaysSpeed * (1f - Mathf.Exp(-lateralGrip * Time.fixedDeltaTime));
        float limit = maxGripAcceleration * Time.fixedDeltaTime;
        rb.AddForce(right * Mathf.Clamp(correction, -limit, limit), ForceMode.VelocityChange);
    }

    private void TurnCar(float forwardSpeed)
    {
        float speed = Mathf.Abs(forwardSpeed);
        float lowSpeedFactor = Mathf.Clamp01(speed / Mathf.Max(0.1f, fullSteeringSpeed));
        float highSpeedFactor = Mathf.Lerp(1f, highSpeedSteeringMultiplier,
            Mathf.Clamp01(speed / Mathf.Max(0.1f, maxForwardSpeed)));
        float yawRate = smoothedTurnInput * turnSpeed * Mathf.Deg2Rad
            * lowSpeedFactor * highSpeedFactor * Mathf.Sign(forwardSpeed);

        // Steer from actual travel direction, including while coasting/braking.
        // Setting yaw velocity lets the physics solver resolve collisions.
        Vector3 angularVelocity = rb.angularVelocity;
        angularVelocity.y = yawRate;
        rb.angularVelocity = angularVelocity;
    }

    private void OnCollisionEnter(Collision collision) => RecordGroundContact(collision);

    private void OnCollisionStay(Collision collision) => RecordGroundContact(collision);

    private void RecordGroundContact(Collision collision)
    {
        for (int i = 0; i < collision.contactCount; i++)
        {
            Vector3 normal = collision.GetContact(i).normal;
            // Walls and the underside of a platform cannot provide traction.
            if (normal.y >= 0.65f)
            {
                hasGroundContact = true;
                groundNormal = normal;
                return;
            }
        }
    }

    public void ResetVehicle()
    {
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.position = spawnPosition;
        rb.rotation = spawnRotation;
        moveInput = 0f;
        turnInput = 0f;
        smoothedTurnInput = 0f;
        resetRequested = false;
        hasGroundContact = false;
        groundNormal = Vector3.up;
        rb.WakeUp();
    }

    private void OnDisable()
    {
        moveInput = 0f;
        turnInput = 0f;
        smoothedTurnInput = 0f;
        resetRequested = false;
        hasGroundContact = false;
    }
}
