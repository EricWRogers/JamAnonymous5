using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;

public class PlayerCamera : NetworkBehaviour
{
    [Header("Target")]
    [Tooltip("Assign the player GameObject here")]
    public Transform player;

    [Header("Sensitivity")]
    public float sensitivityX = 0.15f;
    public float sensitivityY = 0.15f;

    [Header("Vertical Clamp")]
    public float minYAngle = -80f;
    public float maxYAngle = 80f;

    private InputSystem_Actions inputs;
    private float rotationX = 0f; // up/down (camera only)
    private float rotationY = 0f; // left/right (whole player)

    void Awake()
    {
        inputs = new InputSystem_Actions();

        // Lock and hide the cursor
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void OnEnable() => inputs.Player.Enable();
    void OnDisable() => inputs.Player.Disable();

    public override void OnNetworkSpawn()
    {
        if (!IsOwner)
        {
            gameObject.GetComponent<Camera>().enabled = false; //Believe it or not, its too early to do this in awake.
            return;
        }

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }


    void LateUpdate()
    {
        if (!IsOwner || NetworkSessionMenu.IsGameMenuOpen) return;

        Vector2 look = inputs.Player.Look.ReadValue<Vector2>();

        rotationY += look.x * sensitivityX;
        rotationX -= look.y * sensitivityY; // subtract so mouse up = look up
        rotationX = Mathf.Clamp(rotationX, minYAngle, maxYAngle);

        // Rotate the whole player left/right
        player.rotation = Quaternion.Euler(0f, rotationY, 0f);

        // Rotate only the camera up/down
        transform.localRotation = Quaternion.Euler(rotationX, 0f, 0f);
    }
}
