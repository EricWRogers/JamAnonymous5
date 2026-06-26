using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;    
[RequireComponent(typeof(Rigidbody))]
public class PlayerMovement : NetworkBehaviour
{
    [Header("Movement")]
    public float walkSpeed = 10f;
    public float sprintSpeed = 15f;

    public float acceleration = 20f;

    [Header("Jump")]
    public float jumpForce = 7f;
    public float coyoteTime = 0.12f;

    [Header("Ground Detection")]
    public LayerMask groundLayer;
    public float groundCheckDistance = 1.05f;

    private Rigidbody rb;
    private InputSystem_Actions inputs;
    private bool isGrounded;
    private float coyoteTimer;
    private Vector2 moveInput;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.freezeRotation = true;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
        inputs = new InputSystem_Actions();
    }

    void OnEnable()
    {
        inputs.Player.Enable();
        inputs.Player.Jump.performed += OnJump;
    }

    void OnDisable()
    {
        inputs.Player.Jump.performed -= OnJump;
        inputs.Player.Disable();
    }

    private void OnJump(InputAction.CallbackContext ctx)
    {
        if (!IsOwner) return;

        if (coyoteTimer <= 0f) return;

        rb.AddForce(Vector3.up * jumpForce, ForceMode.VelocityChange);
        coyoteTimer = 0f;
    }

    void Update()
    {
        if (!IsOwner) return;

        moveInput = inputs.Player.Move.ReadValue<Vector2>();

        isGrounded = CheckGrounded();

        if (isGrounded)
            coyoteTimer = coyoteTime;
        else
            coyoteTimer -= Time.deltaTime;
    }

    void FixedUpdate()
    {
        if (!IsOwner) return;

        ApplyMovement();
    }

    void ApplyMovement()
    {
        // Move relative to where the player is facing
        Vector3 inputDir = (transform.forward * moveInput.y + transform.right * moveInput.x).normalized;
        float speed = inputs.Player.Sprint.IsPressed() ? sprintSpeed : walkSpeed;
        Vector3 targetVelocity = inputDir * speed;
        Vector3 velocityChange = new Vector3(
            targetVelocity.x - rb.linearVelocity.x,
            0f,
            targetVelocity.z - rb.linearVelocity.z
        );
        velocityChange = Vector3.ClampMagnitude(velocityChange, acceleration * Time.fixedDeltaTime);
        rb.AddForce(velocityChange, ForceMode.VelocityChange);
    }

    bool CheckGrounded()
    {
        return Physics.Raycast(transform.position, Vector3.down, groundCheckDistance, groundLayer);
    }
}