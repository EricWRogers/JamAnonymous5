using TMPro;
using Unity.Netcode;
using UnityEngine;

// Run after PlayerCamera so the label uses this frame's camera rotation.
[DefaultExecutionOrder(1000)]
[RequireComponent(typeof(PlayerMovement))]
public class PlayerNameLabel : MonoBehaviour
{
    [SerializeField] private Vector3 offset = new Vector3(0f, 2.25f, 0f);
    [SerializeField, Min(0.1f)] private float fontSize = 2.5f;
    [SerializeField] private Color textColor = Color.white;

    private PlayerMovement player;
    private Camera viewerCamera;
    private TextMeshPro label;
    private string displayedName;

    private void Awake()
    {
        player = GetComponent<PlayerMovement>();
    }

    private void LateUpdate()
    {
        if (!player.IsSpawned || !player.IsClient || player.IsOwner)
        {
            HideLabel();
            return;
        }

        if (viewerCamera == null || !viewerCamera.isActiveAndEnabled)
        {
            var localPlayer = NetworkManager.Singleton.LocalClient?.PlayerObject;
            var cameraController = localPlayer != null
                ? localPlayer.GetComponentInChildren<PlayerCamera>()
                : null;
            viewerCamera = cameraController != null ? cameraController.GetComponent<Camera>() : null;
        }

        if (viewerCamera == null || !viewerCamera.isActiveAndEnabled)
        {
            HideLabel();
            return;
        }

        if (label == null)
        {
            // This child has no NetworkTransform: each client controls its own copy.
            var labelObject = new GameObject("Player Name", typeof(TextMeshPro));
            labelObject.transform.SetParent(transform, false);
            label = labelObject.GetComponent<TextMeshPro>();
            label.fontSize = fontSize;
            label.color = textColor;
            label.alignment = TextAlignmentOptions.Center;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.richText = false;
            label.rectTransform.sizeDelta = new Vector2(4f, 0.5f);
            UpdateName();
        }

        UpdateName();

        label.gameObject.SetActive(true);
        label.transform.position = transform.position + offset;
        // TMP's readable face points along local -Z, toward the viewing camera.
        label.transform.rotation = viewerCamera.transform.rotation;
    }

    private void UpdateName()
    {
        string currentName = GameManager.Instance != null
            ? GameManager.Instance.GetPlayerName(player.OwnerClientId)
            : $"Player {player.OwnerClientId}";
        if (displayedName == currentName) return;
        displayedName = currentName;
        label.text = currentName;
    }

    private void HideLabel()
    {
        if (label != null)
            label.gameObject.SetActive(false);
    }

    private void OnDisable() => HideLabel();

    private void OnDestroy()
    {
        if (label != null)
            Destroy(label.gameObject);
    }
}
