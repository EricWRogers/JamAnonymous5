using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using System.Collections.Generic;
using UnityEngine.InputSystem;
using Unity.Netcode;

public class WorldSpaceUIInteractor : MonoBehaviour
{
    public float interactDistance = 3f;

    private InputSystem_Actions inputs;
    private NetworkObject networkObject;
    private readonly List<RaycastResult> raycastResults = new List<RaycastResult>();

    void Awake()
    {
        inputs = new InputSystem_Actions();
        networkObject = GetComponentInParent<NetworkObject>();
    }

    void Update()
    {
        if (networkObject != null && networkObject.IsSpawned && !networkObject.IsOwner) return;
        if (!inputs.Player.Attack.WasPressedThisFrame()) return;
        if (EventSystem.current == null) return;

        PointerEventData pointerData = new PointerEventData(EventSystem.current)
        {
            position = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f)
        };

        raycastResults.Clear();
        EventSystem.current.RaycastAll(pointerData, raycastResults);

        foreach (var result in raycastResults)
        {
            if (Vector3.Distance(transform.position, result.worldPosition) > interactDistance) continue;

            var button = result.gameObject.GetComponentInParent<Button>();
            if (button != null && button.IsInteractable())
            {
                button.onClick.Invoke();
                break;
            }
        }
    }

    void OnEnable()
    {
        inputs?.Player.Enable();
    }

    void OnDisable()
    {
        inputs?.Player.Disable();
    }

    void OnDestroy()
    {
        inputs?.Dispose();
    }
}
