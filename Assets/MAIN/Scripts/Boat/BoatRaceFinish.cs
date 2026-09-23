using UnityEngine;

/// <summary>
/// Finish volume for boat mode. Player must still be riding the hull.
/// </summary>
[RequireComponent(typeof(Collider))]
public class BoatRaceFinish : MonoBehaviour
{
    void Reset()
    {
        var col = GetComponent<Collider>();
        if (col != null)
            col.isTrigger = true;
    }

    void OnTriggerEnter(Collider other)
    {
        TryFinish(other);
    }

    void OnTriggerStay(Collider other)
    {
        TryFinish(other);
    }

    static void TryFinish(Collider other)
    {
        if (BoatRaceMode.Current == null || other == null)
            return;
        if (other.GetComponentInParent<HorrorFirstPersonController>() == null)
            return;
        BoatRaceMode.Current.NotifyFinish();
    }
}
