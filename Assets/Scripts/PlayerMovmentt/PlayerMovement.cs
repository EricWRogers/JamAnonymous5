using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;    
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(AudioSource))]
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

    public Transform groundCheck;

    [Header("Footsteps")]
    [SerializeField] private AudioSource footstepAudioSource;
    [SerializeField] private AudioClip[] footstepClips;
    [SerializeField] private float footstepWalkInterval = 0.42f;
    [SerializeField] private float footstepSprintInterval = 0.28f;
    [SerializeField] private float footstepMinSpeed = 0.2f;
    [SerializeField, Range(0f, 1f)] private float footstepVolume = 1f;
    [SerializeField, Range(0f, 0.25f)] private float footstepPitchVariance = 0.08f;

    private Rigidbody rb;
    private InputSystem_Actions inputs;
    private bool isGrounded;
    private float coyoteTimer;
    private Vector2 moveInput;
    private Animator animator;
    private Vector3 previousPosition;
    private float remotePlanarSpeed;
    private float nextFootstepTime;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.freezeRotation = true;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
        inputs = new InputSystem_Actions();

        TryGetComponent<Animator>(out animator);

        if (footstepAudioSource == null)
        {
            footstepAudioSource = GetComponent<AudioSource>();
        }

        previousPosition = transform.position;
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
        if (NetworkSessionMenu.IsGameMenuOpen) return;

        if (coyoteTimer <= 0f) return;

        rb.AddForce(Vector3.up * jumpForce, ForceMode.VelocityChange);
        coyoteTimer = 0f;
    }

    void Update()
    {
        isGrounded = CheckGrounded();

        if (IsOwner)
        {
            if (NetworkSessionMenu.IsGameMenuOpen)
            {
                moveInput = Vector2.zero;
                StopHorizontalMovement();
            }
            else
            {
                moveInput = inputs.Player.Move.ReadValue<Vector2>();

                if (isGrounded)
                    coyoteTimer = coyoteTime;
                else
                    coyoteTimer -= Time.deltaTime;
            }
        }
        else
        {
            Vector3 delta = transform.position - previousPosition;
            remotePlanarSpeed = new Vector3(delta.x, 0f, delta.z).magnitude / Mathf.Max(Time.deltaTime, 0.0001f);
        }

        if (animator != null)
        {
            Vector3 horiz = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
            float speedForAnim = horiz.magnitude;
            bool jumping = !isGrounded;

            animator.SetFloat("Speed", speedForAnim);
            animator.SetBool("IsJumping", jumping);
        }

        UpdateFootsteps();
        previousPosition = transform.position;
    }

    void FixedUpdate()
    {
        if (!IsOwner) return;
        if (NetworkSessionMenu.IsGameMenuOpen)
        {
            StopHorizontalMovement();
            ApplyExtraGravity();
            return;
        }

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
        Vector3 origin = groundCheck != null ? groundCheck.position : transform.position;
        return Physics.Raycast(origin, Vector3.down, groundCheckDistance, groundLayer);
    }

    private void UpdateFootsteps()
    {
        if (footstepAudioSource == null) return;
        if (footstepClips == null || footstepClips.Length == 0) return;
        if (!isGrounded) return;

        float planarSpeed = GetPlanarSpeed();

        if (planarSpeed < footstepMinSpeed)
        {
            StopFootstepAudio();
            return;
        }

        if (Time.time < nextFootstepTime) return;
        if (footstepAudioSource.isPlaying) return;

        AudioClip clip = footstepClips[Random.Range(0, footstepClips.Length)];

        if (clip == null) return;

        footstepAudioSource.pitch = Random.Range(1f - footstepPitchVariance, 1f + footstepPitchVariance);
        footstepAudioSource.clip = clip;
        footstepAudioSource.volume = footstepVolume;
        footstepAudioSource.Play();
        nextFootstepTime = Time.time + GetFootstepInterval(planarSpeed);
    }

    private void StopFootstepAudio()
    {
        if (footstepAudioSource == null) return;
        if (!footstepAudioSource.isPlaying) return;

        footstepAudioSource.Stop();
    }

    private void StopHorizontalMovement()
    {
        rb.linearVelocity = new Vector3(0f, rb.linearVelocity.y, 0f);
        StopFootstepAudio();
    }

    private float GetPlanarSpeed()
    {
        if (!IsOwner)
        {
            return remotePlanarSpeed;
        }

        Vector3 horizontalVelocity = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
        return horizontalVelocity.magnitude;
    }

    private float GetFootstepInterval(float planarSpeed)
    {
        float sprintThreshold = (walkSpeed + sprintSpeed) * 0.5f;
        return planarSpeed >= sprintThreshold ? footstepSprintInterval : footstepWalkInterval;
    }
    }
