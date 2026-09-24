using UnityEngine;

/// <summary>
/// Точка камнепада. Forward — куда камни летят (обычно к реке).
/// </summary>
public class RockfallPoint : MonoBehaviour
{
    [SerializeField] Vector2 interval = new Vector2(1.6f, 3.2f);
    [SerializeField] Vector2Int burst = new Vector2Int(1, 2);
    [SerializeField] float throwSpeed = 16f;

    float _nextAt;

    public void Arm(float delay)
    {
        _nextAt = Time.time + Mathf.Max(0.4f, delay);
    }

    public bool Ready => Time.time >= _nextAt;

    public int RollBurst(float haste)
    {
        haste = Mathf.Clamp01(haste);
        int a = Mathf.Max(1, burst.x);
        int b = Mathf.Max(a, burst.y + Mathf.RoundToInt(haste * 1.2f));
        return Random.Range(a, b + 1);
    }

    public void ScheduleNext(float haste = 0f)
    {
        haste = Mathf.Clamp01(haste);
        float lo = Mathf.Lerp(1.4f, 0.08f, haste);
        float hi = Mathf.Lerp(2.8f, 0.32f, haste);
        _nextAt = Time.time + Random.Range(lo, hi);
    }

    public Vector3 ThrowVelocity(Vector3 toward)
    {
        Vector3 dir = transform.forward;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.01f)
            dir = Vector3.down;
        else
            dir.Normalize();
        toward.y = 0f;
        if (toward.sqrMagnitude > 0.2f)
            dir = Vector3.Slerp(dir, toward.normalized, 0.45f).normalized;
        Vector3 v = dir * throwSpeed + Vector3.down * Random.Range(22f, 34f);
        v += transform.right * Random.Range(-3.2f, 3.2f);
        return v;
    }

    void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.55f, 0.42f, 0.28f, 0.9f);
        Gizmos.DrawSphere(transform.position, 0.45f);
        Gizmos.DrawLine(transform.position, transform.position + transform.forward * 3.5f);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.85f, 0.62f, 0.32f, 1f);
        Gizmos.DrawWireSphere(transform.position, 1.1f);
        Gizmos.DrawRay(transform.position, transform.forward * 8f);
    }
}
