using TMPro;
using UnityEngine;

/// <summary>Only this marked switch, not arbitrary truck colliders, is interactable.</summary>
public sealed class FoodTruckShopControl : MonoBehaviour, IInteractable
{
    [SerializeField] private FoodTruckShop shop;
    [SerializeField] private TMP_Text label;
    private FoodTruckShop.ShopState? shownState;
    public void Interact(PlayerInteraction interactor)
    {
        if (interactor != null && interactor.IsOwner && shop != null)
            shop.RequestOpen(shop.State == FoodTruckShop.ShopState.Closed);
    }
    public void ShowState(FoodTruckShop.ShopState state)
    {
        if (label == null || shownState == state) return;
        shownState = state;
        string text = state switch
        {
            FoodTruckShop.ShopState.Closed => "[F] OPEN TRUCK\nPark & exit driver's seat",
            FoodTruckShop.ShopState.Opening => "OPENING...\n[F] Close shop",
            FoodTruckShop.ShopState.Open => "SHOP OPEN\n[F] Close shop",
            _ => "CLOSING...\nWait before driving"
        };
        foreach (var textLabel in GetComponentsInChildren<TMP_Text>()) textLabel.text = text;
    }
}
