using Unity.Netcode;
using UnityEngine;

public class PlayerInteraction : NetworkBehaviour
{
    [Header("Interaction")]
    [SerializeField] private float interactRange = 3f;
    [SerializeField] private float sphereCastRadius = 0.25f;
    [SerializeField] private LayerMask interactLayers = ~0;
    [SerializeField] private bool logDebugMessages = true;
    [SerializeField] private bool logHitDebugMessages = true;

    [Header("References")]
    [SerializeField] private Camera playerCamera;

    private InputSystem_Actions inputs;
    private IHoldInteractable currentHoldInteractable;
    private float holdInteractTimer;

    private void Awake()
    {
        inputs = new InputSystem_Actions();

        if (playerCamera == null && TryGetComponent(out PlayerPickup pickup))
        {
            playerCamera = pickup.playerCamera;
        }
    }

    private void Update()
    {
        if (!IsOwner) return;

        if (inputs.Player.Interact.WasPressedThisFrame())
        {
            if (TryGetInteractable(out IInteractable interactable))
            {
                interactable.Interact(this);
                TryStartHoldInteract(interactable);
            }
            else
            {
                Log("Interact pressed, but no interactable was found.");
            }
        }

        UpdateHoldInteract();
    }

    private void TryStartHoldInteract(IInteractable interactable)
    {
        currentHoldInteractable = null;
        holdInteractTimer = 0f;

        if (interactable is not IHoldInteractable holdInteractable) return;
        if (!holdInteractable.CanHoldInteract(this)) return;

        currentHoldInteractable = holdInteractable;
    }

    private void UpdateHoldInteract()
    {
        if (currentHoldInteractable == null) return;

        if (!inputs.Player.Interact.IsPressed())
        {
            CancelHoldInteract();
            return;
        }

        if (!TryGetInteractable(out IInteractable interactable) ||
            !ReferenceEquals(interactable, currentHoldInteractable) ||
            !currentHoldInteractable.CanHoldInteract(this))
        {
            CancelHoldInteract();
            return;
        }

        holdInteractTimer += Time.deltaTime;

        if (holdInteractTimer < currentHoldInteractable.HoldInteractDuration)
        {
            return;
        }

        IHoldInteractable completedInteractable = currentHoldInteractable;
        CancelHoldInteract();
        completedInteractable.HoldInteractComplete(this);
    }

    private void CancelHoldInteract()
    {
        currentHoldInteractable = null;
        holdInteractTimer = 0f;
    }

    private bool TryGetInteractable(out IInteractable interactable)
    {
        interactable = null;

        if (playerCamera == null)
        {
            Log("Player camera is missing.");
            return false;
        }

        Ray ray = playerCamera.ScreenPointToRay(
            new Vector3(Screen.width / 2f, Screen.height / 2f, 0f)
        );

        return TryGetClosestInteractable(ray, out interactable);
    }

    private bool TryGetClosestInteractable(Ray ray, out IInteractable closestInteractable)
    {
        closestInteractable = null;
        float closestDistance = float.MaxValue;
        bool hitAnything = false;

        CheckHits(
            Physics.RaycastAll(ray, interactRange, interactLayers, QueryTriggerInteraction.Collide),
            ref closestInteractable,
            ref closestDistance,
            ref hitAnything
        );

        CheckHits(
            Physics.SphereCastAll(ray, sphereCastRadius, interactRange, interactLayers, QueryTriggerInteraction.Collide),
            ref closestInteractable,
            ref closestDistance,
            ref hitAnything
        );

        if (closestInteractable == null && !hitAnything)
        {
            Log("No colliders were hit. Make the spawner collider larger or increase Interact Range/Sphere Cast Radius.");
        }

        return closestInteractable != null;
    }

    private void CheckHits(RaycastHit[] hits, ref IInteractable closestInteractable, ref float closestDistance, ref bool hitAnything)
    {
        for (int i = 0; i < hits.Length; i++)
        {
            hitAnything = true;

            if (hits[i].distance >= closestDistance) continue;

            IInteractable interactable = GetInteractableFromHit(hits[i]);

            if (interactable == null)
            {
                LogHit(hits[i], "hit collider, but no IInteractable was found on it or its parents.");
                continue;
            }

            closestInteractable = interactable;
            closestDistance = hits[i].distance;
        }
    }

    private IInteractable GetInteractableFromHit(RaycastHit hit)
    {
        InteractableTarget target = hit.collider.GetComponentInParent<InteractableTarget>();

        if (target != null && target.TryGetInteractable(out IInteractable targetInteractable))
        {
            return targetInteractable;
        }

        MonoBehaviour[] behaviours = hit.collider.GetComponentsInParent<MonoBehaviour>();

        for (int i = 0; i < behaviours.Length; i++)
        {
            if (behaviours[i] is IInteractable interactable)
            {
                return interactable;
            }
        }

        return null;
    }

    private void LogHit(RaycastHit hit, string message)
    {
        if (!logDebugMessages || !logHitDebugMessages) return;

        Debug.Log(
            $"[PlayerInteraction] {message} Hit: {hit.collider.name}, Layer: {LayerMask.LayerToName(hit.collider.gameObject.layer)}, Distance: {hit.distance:F2}",
            hit.collider
        );
    }

    private void Log(string message)
    {
        if (!logDebugMessages) return;

        Debug.Log($"[PlayerInteraction] {message}", this);
    }

    private void OnEnable()
    {
        if (inputs != null)
        {
            inputs.Player.Enable();
        }
    }

    private void OnDisable()
    {
        if (inputs != null)
        {
            inputs.Player.Disable();
        }
    }
}
