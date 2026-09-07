using UnityEngine;

public class CustomerEntryTrigger : MonoBehaviour
{
    public static CustomerEntryTrigger Instance;

    public POS pos;

    private void Awake()
    {
        Instance = this;
    }

    private void OnTriggerStay(Collider other)
    {
        if (pos != null)
            pos.CustomerEntered(other);
    }
}
