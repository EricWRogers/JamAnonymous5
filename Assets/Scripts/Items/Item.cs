using Unity.Netcode;
using UnityEngine;


public class Item : NetworkBehaviour
{
    [Header("Item Info")]
    public string itemName = "Item";
    public ItemType itemType = ItemType.Food;

    public enum ItemType { Food, Utensil, Ingredient }

    private Rigidbody rb;
    private Collider col;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        col = GetComponent<Collider>();
    }


    public void PickUp()
    {
        if (!IsServer)
        {
            Debug.LogWarning("[Item] PickUp() called on a non-server instance. Ignoring.");
            return;
        }

        rb.isKinematic = true;
        col.enabled = false;
    }

    public void FollowHoldPoint(Transform holdPoint)
    {
        if (!IsServer) return;

        transform.position = holdPoint.position;
        transform.rotation = holdPoint.rotation;
    }


    public void Drop(Vector3 dropPosition)
    {
        if (!IsServer)
        {
            Debug.LogWarning("[Item] Drop() called on a non-server instance. Ignoring.");
            return;
        }

        transform.SetParent(null);
        transform.position = dropPosition;

        rb.isKinematic = false;
        col.enabled = true;
    }
}