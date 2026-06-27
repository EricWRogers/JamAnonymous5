public interface IInteractable
{
    void Interact(PlayerInteraction interactor);
}

public interface IHoldInteractable : IInteractable
{
    float HoldInteractDuration { get; }

    bool CanHoldInteract(PlayerInteraction interactor);
    void HoldInteractComplete(PlayerInteraction interactor);
}
