using UnityEngine;
using TMPro;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.UI;

public class CustomerOrderUI : MonoBehaviour
{
    public TMP_Text label;
    public GameObject bubble;

    private Coroutine yappingCoroutine;
    private Camera mainCam;

    [Header("Billboard")]
    public Transform billboardTarget;

    [Header("Bubble Layout")]
    [SerializeField] private Vector2 bubblePadding = new(24f, 16f);
    [SerializeField, Min(0f)] private float maximumBubbleWidth = 420f;
    [SerializeField, Min(0f)] private float maximumBubbleHeight;

    void LateUpdate()
    {
        if (billboardTarget == null) return;

        if (mainCam == null || !mainCam.isActiveAndEnabled)
            mainCam = Camera.main;

        if (mainCam == null) return;
        billboardTarget.LookAt(billboardTarget.position + mainCam.transform.forward);
    }

    public void ShowScore(float score, float maxScore)
    {
        bubble.SetActive(true);
        label.text = $"{score:F1} / {maxScore:F0}";
        RefreshLayout();
    }

    public void StartYapping(List<FoodIngredientDefinition> ingredients)
    {
        var names = new List<string>();
        foreach (var ing in ingredients)
            names.Add(ing.IngredientName);

        StartOrderPresentation(names);
    }

    public void StartYappingNames(List<string> ingredients)
    {
        StartOrderPresentation(ingredients);
    }

    public void StopYapping()
    {
        if (yappingCoroutine != null)
        {
            StopCoroutine(yappingCoroutine);
            yappingCoroutine = null;
        }

        bubble.SetActive(false);
    }

    private void StartOrderPresentation(List<string> ingredients)
    {
        bubble.SetActive(true);
        if (yappingCoroutine != null)
        {
            StopCoroutine(yappingCoroutine);
        }

        yappingCoroutine = StartCoroutine(PresentOrderOnce(ingredients));
    }

    public void ShowOrderNumber(ulong customerId)
    {
        bubble.SetActive(true);
        label.text = $"#{customerId}";
        RefreshLayout();
    }

    public void Hide()
    {
        bubble.SetActive(false);
    }

    private IEnumerator PresentOrderOnce(List<string> ingredients)
    {
        for (int i = 0; i < ingredients.Count; i++)
        {
            label.text = ingredients[i];
            RefreshLayout();
            yield return new WaitForSeconds(1.2f);
            label.text = "";
            RefreshLayout();
            yield return new WaitForSeconds(0.3f);
        }

        label.text = string.Join("\n", ingredients);
        RefreshLayout();
        yappingCoroutine = null;
    }

    private void RefreshLayout()
    {
        if (bubble == null || label == null) return;

        RectTransform labelRect = label.rectTransform;
        RectTransform bubbleRect = bubble.GetComponent<RectTransform>();
        if (bubbleRect == null) return;

        label.textWrappingMode = maximumBubbleWidth > 0f
            ? TextWrappingModes.Normal
            : TextWrappingModes.NoWrap;
        label.ForceMeshUpdate();

        float textWidth = label.preferredWidth;
        if (maximumBubbleWidth > 0f)
        {
            textWidth = Mathf.Min(textWidth, maximumBubbleWidth - bubblePadding.x);
        }

        textWidth = Mathf.Max(1f, textWidth);
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(bubblePadding.x * 0.5f, bubblePadding.y * 0.5f);
        labelRect.offsetMax = new Vector2(-bubblePadding.x * 0.5f, -bubblePadding.y * 0.5f);

        float textHeight = label.GetPreferredValues(textWidth, 0f).y;
        float bubbleWidth = textWidth + bubblePadding.x;
        float bubbleHeight = textHeight + bubblePadding.y;

        if (maximumBubbleHeight > 0f)
        {
            bubbleHeight = Mathf.Min(bubbleHeight, maximumBubbleHeight);
            label.overflowMode = TextOverflowModes.Truncate;
        }

        bubbleRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, bubbleWidth);
        bubbleRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, bubbleHeight);
        LayoutRebuilder.ForceRebuildLayoutImmediate(bubbleRect);
    }

}