using System;
using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Item))]
[RequireComponent(typeof(NetworkObject))]
public class ChoppableItem : NetworkBehaviour, IInteractable
{
    private enum EmbeddedVisualState
    {
        Whole,
        Chopped
    }

    [Header("Tool")]
    [SerializeField] private string requiredToolName = "Knife";
    [SerializeField] private float serverInteractRange = 4f;

    [Header("Visuals")]
    [Tooltip("Which state the renderers already on this object represent.")]
    [SerializeField] private EmbeddedVisualState embeddedVisualState = EmbeddedVisualState.Whole;
    [Tooltip("Optional scene root for the unchopped look.")]
    [SerializeField] private GameObject wholeVisualRoot;
    [Tooltip("Optional prefab used only as a local unchopped visual.")]
    [SerializeField] private GameObject wholeVisualPrefab;
    [Tooltip("Optional scene root for the chopped look.")]
    [SerializeField] private GameObject choppedVisualRoot;
    [Tooltip("Optional prefab used only as a local chopped visual.")]
    [SerializeField] private GameObject choppedVisualPrefab;
    [SerializeField] private Transform visualParent;

    [Header("Ingredient State")]
    [SerializeField] private FoodPrepState choppedPrepState = FoodPrepState.Chopped;

    private FoodIngredient foodIngredient;
    private GameObject wholeVisualInstance;
    private GameObject choppedVisualInstance;

    private NetworkVariable<bool> isChopped = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public bool IsChopped => isChopped.Value;

    private void Awake()
    {
        foodIngredient = GetComponent<FoodIngredient>();
    }

    public override void OnNetworkSpawn()
    {
        isChopped.OnValueChanged += OnChoppedChanged;
        ApplyChoppedState(isChopped.Value);
    }

    public override void OnNetworkDespawn()
    {
        isChopped.OnValueChanged -= OnChoppedChanged;
        DestroyVisualInstance(ref wholeVisualInstance);
        DestroyVisualInstance(ref choppedVisualInstance);
    }

    public void Interact(PlayerInteraction interactor)
    {
        if (!IsSpawned || isChopped.Value)
        {
            return;
        }

        RequestChopServerRpc();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestChopServerRpc(RpcParams rpcParams = default)
    {
        if (isChopped.Value) return;
        if (!TryGetSenderPickup(rpcParams.Receive.SenderClientId, out PlayerPickup playerPickup)) return;
        if (!IsPlayerCloseEnough(playerPickup)) return;
        if (!playerPickup.ServerTryGetHeldItem(out Item heldItem)) return;
        if (!IsValidTool(heldItem)) return;

        isChopped.Value = true;
        ApplyChoppedState(true);
    }

    private void OnChoppedChanged(bool previousValue, bool newValue)
    {
        ApplyChoppedState(newValue);
    }

    private void ApplyChoppedState(bool chopped)
    {
        SetEmbeddedVisualActive(embeddedVisualState == (chopped ? EmbeddedVisualState.Chopped : EmbeddedVisualState.Whole));
        SetWholeVisualActive(!chopped);
        SetChoppedVisualActive(chopped);

        if (chopped && foodIngredient != null)
        {
            foodIngredient.SetPrepState(choppedPrepState);
        }
    }

    private void SetWholeVisualActive(bool active)
    {
        SetConfiguredVisualActive(wholeVisualRoot, wholeVisualPrefab, ref wholeVisualInstance, active);
    }

    private void SetChoppedVisualActive(bool active)
    {
        SetConfiguredVisualActive(choppedVisualRoot, choppedVisualPrefab, ref choppedVisualInstance, active);
    }

    private void SetEmbeddedVisualActive(bool active)
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>(includeInactive: true);

        for (int i = 0; i < renderers.Length; i++)
        {
            if (IsConfiguredVisualRenderer(renderers[i]))
            {
                continue;
            }

            renderers[i].enabled = active;
        }
    }

    private void SetConfiguredVisualActive(
        GameObject visualRoot,
        GameObject visualPrefab,
        ref GameObject visualInstance,
        bool active)
    {
        if (visualRoot != null)
        {
            visualRoot.SetActive(active);
            return;
        }

        if (!active)
        {
            if (visualInstance != null)
            {
                visualInstance.SetActive(false);
            }

            return;
        }

        if (visualInstance == null && visualPrefab != null)
        {
            Transform parent = visualParent != null ? visualParent : transform;
            visualInstance = Instantiate(visualPrefab, parent);
            visualInstance.transform.localPosition = Vector3.zero;
            visualInstance.transform.localRotation = Quaternion.identity;
            visualInstance.transform.localScale = Vector3.one;
        }

        if (visualInstance != null)
        {
            visualInstance.SetActive(true);
        }
    }

    private bool IsConfiguredVisualRenderer(Renderer renderer)
    {
        if (renderer == null) return false;

        return IsRendererChildOf(renderer, wholeVisualRoot) ||
               IsRendererChildOf(renderer, choppedVisualRoot) ||
               IsRendererChildOf(renderer, wholeVisualInstance) ||
               IsRendererChildOf(renderer, choppedVisualInstance);
    }

    private bool IsRendererChildOf(Renderer renderer, GameObject root)
    {
        return renderer != null && root != null && renderer.transform.IsChildOf(root.transform);
    }

    private void DestroyVisualInstance(ref GameObject visualInstance)
    {
        if (visualInstance == null) return;

        Destroy(visualInstance);
        visualInstance = null;
    }

    private bool IsPlayerCloseEnough(PlayerPickup playerPickup)
    {
        if (playerPickup == null) return false;

        float allowedRange = Mathf.Max(0f, serverInteractRange);
        return Vector3.Distance(playerPickup.transform.position, transform.position) <= allowedRange;
    }

    private bool IsValidTool(Item heldItem)
    {
        if (heldItem == null) return false;
        if (heldItem.GetComponent<KnifeTool>() != null) return true;

        if (!string.IsNullOrWhiteSpace(requiredToolName))
        {
            return NamesMatch(heldItem.itemName, requiredToolName) ||
                   NamesMatch(heldItem.gameObject.name, requiredToolName);
        }

        return heldItem.itemType == Item.ItemType.Utensil;
    }

    private bool NamesMatch(string candidate, string requiredName)
    {
        if (string.IsNullOrWhiteSpace(candidate) || string.IsNullOrWhiteSpace(requiredName))
        {
            return false;
        }

        string normalizedCandidate = candidate.Replace("(Clone)", string.Empty).Trim();
        return string.Equals(normalizedCandidate, requiredName, StringComparison.OrdinalIgnoreCase);
    }

    private bool TryGetSenderPickup(ulong senderClientId, out PlayerPickup playerPickup)
    {
        playerPickup = null;

        if (NetworkManager.Singleton == null) return false;

        if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(senderClientId, out NetworkClient client))
        {
            return false;
        }

        if (client.PlayerObject == null)
        {
            return false;
        }

        playerPickup = client.PlayerObject.GetComponent<PlayerPickup>();

        if (playerPickup != null)
        {
            return true;
        }

        playerPickup = client.PlayerObject.GetComponentInChildren<PlayerPickup>();

        if (playerPickup != null)
        {
            return true;
        }

        playerPickup = client.PlayerObject.GetComponentInParent<PlayerPickup>();
        return playerPickup != null;
    }
}
