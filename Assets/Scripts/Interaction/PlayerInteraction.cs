using Unity.Netcode;
using UnityEngine;

public class PlayerInteraction : NetworkBehaviour
{
    [Header("Interaction")]
    [SerializeField] private float interactRange = 3f;
    [SerializeField] private float sphereCastRadius = 0.25f;
    [SerializeField] private LayerMask interactLayers = ~0;
    [SerializeField, Min(4)] private int maxInteractionHits = 24;
    [SerializeField] private bool logDebugMessages = true;
    [SerializeField] private bool logHitDebugMessages = true;

    [Header("References")]
    [SerializeField] private Camera playerCamera;

    private InputSystem_Actions inputs;
    private IHoldInteractable currentHoldInteractable;
    private float holdInteractTimer;
    private RaycastHit[] raycastHits;
    private RaycastHit[] sphereCastHits;

    private void Awake()
    {
        inputs = new InputSystem_Actions();
        int hitBufferSize = Mathf.Max(4, maxInteractionHits);
        raycastHits = new RaycastHit[hitBufferSize];
        sphereCastHits = new RaycastHit[hitBufferSize];

        if (playerCamera == null && TryGetComponent(out PlayerPickup pickup))
        {
            playerCamera = pickup.playerCamera;
        }
    }

    private void Update()
    {
        if (!IsOwner || !IsSpawned) return;
        if (NetworkSessionMenu.IsGameMenuOpen) { CancelHoldInteract(); return; }

        if (inputs.Player.Interact.WasPressedThisFrame())
        {
            if (TryGetComponent(out PlayerPickup pickup) && pickup.TryDeliverFromView())
            {
                CancelHoldInteract();
                return;
            }
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
        float closestBlockingDistance = float.MaxValue;
        bool hitAnything = false;

        EnsureHitBuffers();

        int rayHitCount = Physics.RaycastNonAlloc(
            ray,
            raycastHits,
            interactRange,
            interactLayers,
            QueryTriggerInteraction.Collide
        );
        int sphereHitCount = Physics.SphereCastNonAlloc(
            ray,
            sphereCastRadius,
            sphereCastHits,
            interactRange,
            interactLayers,
            QueryTriggerInteraction.Collide
        );

        CheckHits(
            raycastHits,
            rayHitCount,
            ref closestInteractable,
            ref closestDistance,
            ref closestBlockingDistance,
            ref hitAnything
        );

        // A shelf touched by the wider assistance cast must not hide an item
        // directly under the crosshair. Resolve the direct ray independently.
        if (closestInteractable != null && closestDistance <= closestBlockingDistance + 0.001f)
            return true;
        closestInteractable = null;
        closestDistance = float.MaxValue;
        closestBlockingDistance = float.MaxValue;

        CheckHits(
            sphereCastHits,
            sphereHitCount,
            ref closestInteractable,
            ref closestDistance,
            ref closestBlockingDistance,
            ref hitAnything
        );

        if (closestInteractable != null && closestDistance > closestBlockingDistance + 0.001f)
        {
            closestInteractable = null;
        }

        if (closestInteractable == null && !hitAnything)
        {
            Log("No colliders were hit. Make the spawner collider larger or increase Interact Range/Sphere Cast Radius.");
        }

        return closestInteractable != null;
    }

    private void CheckHits(
        RaycastHit[] hits,
        int hitCount,
        ref IInteractable closestInteractable,
        ref float closestDistance,
        ref float closestBlockingDistance,
        ref bool hitAnything)
    {
        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = hits[i];

            if (hit.collider == null || IsOwnPlayerCollider(hit.collider))
            {
                continue;
            }

            hitAnything = true;
            IInteractable interactable = GetInteractableFromHit(hit);

            if (interactable == null)
            {
                if (!hit.collider.isTrigger)
                {
                    closestBlockingDistance = Mathf.Min(closestBlockingDistance, hit.distance);
                }

                LogHit(hit, "hit collider, but no IInteractable was found on it or its parents.");
                continue;
            }

            if (hit.distance >= closestDistance) continue;

            closestInteractable = interactable;
            closestDistance = hit.distance;
        }
    }

    private bool IsOwnPlayerCollider(Collider candidate)
    {
        if (candidate == null) return false;

        PlayerInteraction owner = candidate.GetComponentInParent<PlayerInteraction>();
        return owner == this;
    }

    private void EnsureHitBuffers()
    {
        int requiredSize = Mathf.Max(4, maxInteractionHits);

        if (raycastHits == null || raycastHits.Length != requiredSize)
        {
            raycastHits = new RaycastHit[requiredSize];
        }

        if (sphereCastHits == null || sphereCastHits.Length != requiredSize)
        {
            sphereCastHits = new RaycastHit[requiredSize];
        }
    }

    private IInteractable GetInteractableFromHit(RaycastHit hit)
    {
        IngredientBox box = hit.collider.GetComponentInParent<IngredientBox>();
        if (box != null) return box;
        Item item = hit.collider.GetComponentInParent<Item>();
        if (item != null && !item.IsHeld && TryGetComponent(out PlayerPickup pickup) && !pickup.IsHoldingItem())
            return item;
        InteractableTarget target = hit.collider.GetComponentInParent<InteractableTarget>();

        if (target != null && target.TryGetInteractable(out IInteractable targetInteractable) &&
            CanTargetInteraction(targetInteractable, hit.collider))
        {
            return targetInteractable;
        }

        MonoBehaviour[] behaviours = hit.collider.GetComponentsInParent<MonoBehaviour>();

        for (int i = 0; i < behaviours.Length; i++)
        {
            if (behaviours[i] is IInteractable interactable && behaviours[i] is not Item &&
                CanTargetInteraction(interactable, hit.collider))
            {
                return interactable;
            }
        }

        return hit.collider.GetComponentInParent<Item>();
    }

    private bool CanTargetInteraction(IInteractable interactable, Collider hitCollider)
    {
        if (interactable is not VehicleDriverSeat seat) return true;
        if (!seat.IsEntryInteractionCollider(hitCollider)) return false;
        return !TryGetComponent(out PlayerPickup pickup) || !pickup.IsHoldingItem();
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
        CancelHoldInteract();
        if (inputs != null)
        {
            inputs.Player.Disable();
        }
    }
}
