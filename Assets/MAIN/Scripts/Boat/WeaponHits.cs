using UnityEngine;

/// <summary>
/// Прицел по IDamageable для молотка/пилы/пуль. Позже тот же луч уходит в RequestDamage.
/// </summary>
public static class WeaponHits
{
    static readonly RaycastHit[] Hits = new RaycastHit[24];

    public static bool AimDamageable(GameObject owner, float dist, out IDamageable target, out RaycastHit hit)
    {
        target = null;
        hit = default;
        Camera cam = BoatBuildUtil.Cam(owner);
        if (cam == null)
            return false;
        int n = Physics.SphereCastNonAlloc(
            cam.transform.position, 0.12f, cam.transform.forward, Hits, dist, ~0, QueryTriggerInteraction.Ignore);
        int best = -1;
        float bestD = float.PositiveInfinity;
        for (int i = 0; i < n; i++)
        {
            if (Hits[i].collider == null)
                continue;
            if (owner != null && Hits[i].collider.transform.IsChildOf(owner.transform))
                continue;
            var dmg = Hits[i].collider.GetComponentInParent<IDamageable>();
            if (dmg == null || dmg.IsDead)
                continue;
            if (Hits[i].distance < bestD)
            {
                bestD = Hits[i].distance;
                best = i;
            }
        }
        if (best < 0)
            return false;
        hit = Hits[best];
        target = hit.collider.GetComponentInParent<IDamageable>();
        return target != null;
    }
}
