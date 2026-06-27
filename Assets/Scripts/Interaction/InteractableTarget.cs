using UnityEngine;

public class InteractableTarget : MonoBehaviour
{
    [SerializeField] private MonoBehaviour interactableBehaviour;

    public bool TryGetInteractable(out IInteractable interactable)
    {
        interactable = interactableBehaviour as IInteractable;

        if (interactable != null)
        {
            return true;
        }

        MonoBehaviour[] behaviours = GetComponentsInParent<MonoBehaviour>();

        for (int i = 0; i < behaviours.Length; i++)
        {
            if (behaviours[i] is IInteractable parentInteractable)
            {
                interactable = parentInteractable;
                return true;
            }
        }

        return false;
    }
}
