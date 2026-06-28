using UnityEngine;
using TMPro;
using System.Collections;
using System.Collections.Generic;

public class CustomerOrderUI : MonoBehaviour
{
    public TMP_Text label;
    public GameObject bubble;

    private Coroutine yappingCoroutine;

    private Camera mainCam;

    [Header("Billboard")]
    public Transform billboardTarget;

    void Start()
    {
        mainCam = Camera.main;
    }

    void LateUpdate()
    {
        if (billboardTarget == null || mainCam == null) return;
        billboardTarget.LookAt(billboardTarget.position + mainCam.transform.forward);
    }

    public void ShowScore(float score, float maxScore)
    {
        bubble.SetActive(true);
        label.text = $"{score:F1} / {maxScore:F0}";
    }

    public void StartYapping(List<FoodIngredientDefinition> ingredients)
    {
        var names = new List<string>();
        foreach (var ing in ingredients)
            names.Add(ing.IngredientName);

        bubble.SetActive(true);
        yappingCoroutine = StartCoroutine(YapLoop(names));
    }

    public void StartYappingNames(List<string> ingredients)
    {
        bubble.SetActive(true);
        yappingCoroutine = StartCoroutine(YapLoop(ingredients));
    }

    public void StopYapping()
    {
        if (yappingCoroutine != null)
            StopCoroutine(yappingCoroutine);

        bubble.SetActive(false);
    }

    public void ShowOrderNumber(ulong customerId)
    {
        bubble.SetActive(true);
        label.text = $"#{customerId}";
    }

    public void Hide()
    {
        bubble.SetActive(false);
    }

    IEnumerator YapLoop(List<string> ingredients)
    {
        while (true)
        {
            for (int i = 0; i < ingredients.Count; i++)
            {
                label.text = ingredients[i];
                yield return new WaitForSeconds(1.2f);
                label.text = "";
                yield return new WaitForSeconds(0.3f);
            }
            label.text = "Done!";
            yield return new WaitForSeconds(1.5f);
            label.text = "";
            yield return new WaitForSeconds(0.3f);
        }
    }
}