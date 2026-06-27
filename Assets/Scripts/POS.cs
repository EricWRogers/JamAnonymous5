using System;
using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;

public class POS : NetworkBehaviour
{
    public GameObject startShift;
    public GameObject mealSelect;
    public GameObject ingredientSelect;

    public TextMeshProUGUI text;

    public FoodIngredientDefinition dong;
    public FoodIngredientDefinition patty;
    
    private bool isThisPatty = false;

    public List<FoodIngredientDefinition> ingredientsForOrder = new List<FoodIngredientDefinition>();
    
    public string orderText;
    
    public void Start()
    {
        startShift.SetActive(true);
        mealSelect.SetActive(false);
        ingredientSelect.SetActive(false);
    }

    public void StartShift()
    {
        startShift.SetActive(false);
        mealSelect.SetActive(true);
    }

    public void SelectBurger()
    {
        mealSelect.SetActive(false);
        ingredientSelect.SetActive(true);
        
        ingredientsForOrder.Add(patty);
        isThisPatty = true;
        
        orderText = "";
        text.text = orderText;
    }
    
    public void SelectHotdog()
    {
        mealSelect.SetActive(false);
        ingredientSelect.SetActive(true);
        
        ingredientsForOrder.Add(dong);
        isThisPatty = false;

        orderText = "";
        text.text = orderText;
    }

    public void SubmitOrder()
    {
        ingredientSelect.SetActive(false);
        mealSelect.SetActive(true);
    }

    public void AddIngredient(FoodIngredientButtonDefinition foodIngredient)
    {
        ingredientsForOrder.Add(foodIngredient.ingredient);
        
        orderText += foodIngredient.ingredient.IngredientName + "\n";
        text.text = orderText;
    }

    public void AddMoreMeat()
    {
        if (isThisPatty)
        {
            ingredientsForOrder.Add(patty);
            orderText += patty.IngredientName + "\n";
        }
        else
        {
            ingredientsForOrder.Add(dong);
            orderText += dong.IngredientName + "\n";
        }
        
        text.text = orderText;
    }
}
