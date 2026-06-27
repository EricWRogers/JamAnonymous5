using UnityEngine;
using System.Collections.Generic;

public class Grill : MonoBehaviour
{
    public float cookSpeed = 0.05f;

    public Color CookedColor = new Color(0.4f, 0.2f, 0.05f);
    public Color BurntColor = new Color(0.1f, 0.05f, 0.0f);

    private class GrillItem
    {
        public Renderer renderer;
        public Color originalColor;
        public float cookLevel;
    }

    private readonly Dictionary<GameObject, GrillItem> itemsOnGrill = new();

    void OnTriggerStay(Collider other)
    {
        var root = other.transform.root.gameObject;
        var rend = root.GetComponentInChildren<Renderer>();
        if (rend == null) return;

        if (!itemsOnGrill.ContainsKey(root))
        {
            itemsOnGrill[root] = new GrillItem
            {
                renderer = rend,
                originalColor = rend.material.color,
                cookLevel = 0f
            };
        }

        var item = itemsOnGrill[root];
        item.cookLevel = Mathf.Min(item.cookLevel + cookSpeed * Time.deltaTime, 1f);

        Color target;
        if (item.cookLevel < 0.5f)
            target = Color.Lerp(item.originalColor, CookedColor, item.cookLevel * 2f);
        else
            target = Color.Lerp(CookedColor, BurntColor, (item.cookLevel - 0.5f) * 2f);

        rend.material.color = target;
    }
}