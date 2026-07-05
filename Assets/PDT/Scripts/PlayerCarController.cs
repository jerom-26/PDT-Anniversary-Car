using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerCarController : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float turnSpeed = 90f;

    private float turnInput;
    private Rigidbody rb;
    private float moveInput;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
    }

    private void Update()
    {
        moveInput = 0f;
        turnInput = 0f;

        if (Keyboard.current.wKey.isPressed)
        {
            moveInput += 1f;
        }

        if (Keyboard.current.sKey.isPressed)
        {
            moveInput -= 1f;
        }

        if (Keyboard.current.aKey.isPressed)
        {
            turnInput -= 1f;
        }

        if (Keyboard.current.dKey.isPressed)
        {
            turnInput += 1f;
        }
    }

    private void FixedUpdate()
    {
        MoveCar();
        TurnCar();
    }

    private void MoveCar()
    {
        Vector3 movementVelocity = transform.forward * moveInput * moveSpeed;

       rb.linearVelocity = new Vector3(movementVelocity.x, rb.linearVelocity.y, movementVelocity.z);
    }

    private void TurnCar()
    {
        float turnAmount = turnInput * turnSpeed * moveInput * Time.fixedDeltaTime;

        Quaternion turnRotation = Quaternion.Euler(0f, turnAmount, 0f);

        rb.MoveRotation(rb.rotation * turnRotation);
    }

}
