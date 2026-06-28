using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;    
[RequireComponent(typeof(Rigidbody))]
public class PlayerMovement : NetworkBehaviour
{
    [Header("Movement")]
    public float walkSpeed = 10f;
    public float sprintSpeed = 15f;

    public float acceleration = 60f;
    public float deceleration = 90f;
    public float airAcceleration = 20f;

    [Header("Jump")]
    public float jumpForce = 6f;
    public float coyoteTime = 0.12f;
    public float riseGravityMultiplier = 1.4f;
    public float fallGravityMultiplier = 2.2f;

    [Header("Ground Detection")]
    public LayerMask groundLayer;
    public float groundCheckDistance = 1.05f;

    private Rigidbody rb;
    private InputSystem_Actions inputs;
    private bool isGrounded;
    private float coyoteTimer;
    private Vector2 moveInput;
    private Animator animator;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.freezeRotation = true;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
        inputs = new InputSystem_Actions();

        TryGetComponent<Animator>(out animator);
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

        Debug.DrawRay(transform.position, Vector3.down * groundCheckDistance,
        isGrounded ? Color.green : Color.red);
        Debug.Log($"Grounded: {CheckGrounded()}");
        
        if (!IsOwner) return;
        
        moveInput = inputs.Player.Move.ReadValue<Vector2>();

        isGrounded = CheckGrounded();

        if (isGrounded)
            coyoteTimer = coyoteTime;
        else
            coyoteTimer -= Time.deltaTime;

        if (animator != null)
        {
            Vector3 horiz = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
            float speedForAnim = horiz.magnitude;
            bool jumping = !isGrounded;

            animator.SetFloat("Speed", speedForAnim);
            animator.SetBool("IsJumping", jumping);
        }
    }

    void FixedUpdate()
    {
        if (!IsOwner) return;

        ApplyMovement();
        ApplyExtraGravity();
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
        float accelRate = GetAccelerationRate(inputDir);
        velocityChange = Vector3.ClampMagnitude(velocityChange, accelRate * Time.fixedDeltaTime);
        rb.AddForce(velocityChange, ForceMode.VelocityChange);
    }

    float GetAccelerationRate(Vector3 inputDir)
    {
        if (!isGrounded)
            return airAcceleration;

        return inputDir.sqrMagnitude > 0f ? acceleration : deceleration;
    }

    void ApplyExtraGravity()
    {
        if (isGrounded)
            return;

        float gravityMultiplier = rb.linearVelocity.y < 0f ? fallGravityMultiplier : riseGravityMultiplier;
        rb.AddForce(Physics.gravity * (gravityMultiplier - 1f), ForceMode.Acceleration);
    }

    bool CheckGrounded()
    {
        return Physics.Raycast(transform.position, Vector3.down, groundCheckDistance, groundLayer);
    }
}
