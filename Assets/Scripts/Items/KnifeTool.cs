using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Item))]
public class KnifeTool : MonoBehaviour
{
    private void Reset()
    {
        Item item = GetComponent<Item>();

        if (item != null)
        {
            item.itemName = "Knife";
            item.itemType = Item.ItemType.Utensil;
        }
    }
}
