using UnityEngine;

public static class BoatBuildUtil
{
    public static Camera Cam(GameObject owner)
    {
        if (owner == null)
            return Camera.main;
        var c = owner.GetComponentInChildren<Camera>();
        return c != null ? c : Camera.main;
    }

    public static bool Aim(GameObject owner, float dist, out RaycastHit hit, bool preferPieces = false)
    {
        hit = default;
        Camera cam = Cam(owner);
        if (cam == null)
            return false;
        Ray ray = new Ray(cam.transform.position, cam.transform.forward);
        var hits = Physics.RaycastAll(ray, dist, ~0, QueryTriggerInteraction.Ignore);
        float bestAny = float.PositiveInfinity;
        float bestPiece = float.PositiveInfinity;
        RaycastHit anyHit = default;
        RaycastHit pieceHit = default;
        bool hasAny = false;
        bool hasPiece = false;
        for (int i = 0; i < hits.Length; i++)
        {
            Transform t = hits[i].transform;
            if (t == null)
                continue;
            if (owner != null && (t == owner.transform || t.IsChildOf(owner.transform)))
                continue;
            if (t.gameObject.layer == 2)
                continue;
            if (t.name == "Ghost" || t.name == "SawPreview" ||
                (t.name == "Vis" && t.parent != null && t.parent.name == "Ghost"))
                continue;
            if (hits[i].collider != null && hits[i].collider.isTrigger &&
                hits[i].collider.GetComponentInParent<BoatNail>() == null)
                continue;
            if (hits[i].distance < bestAny)
            {
                bestAny = hits[i].distance;
                anyHit = hits[i];
                hasAny = true;
            }
            if (hits[i].collider != null && IsBoatTarget(hits[i].collider) &&
                hits[i].distance < bestPiece)
            {
                bestPiece = hits[i].distance;
                pieceHit = hits[i];
                hasPiece = true;
            }
        }
        if (hasPiece && (preferPieces || bestPiece <= bestAny + 0.85f))
        {
            hit = pieceHit;
            return true;
        }
        if (preferPieces && !hasPiece)
        {
            var spheres = Physics.SphereCastAll(ray, 0.28f, dist, ~0, QueryTriggerInteraction.Ignore);
            float best = float.PositiveInfinity;
            bool found = false;
            for (int i = 0; i < spheres.Length; i++)
            {
                if (!IsBoatTarget(spheres[i].collider))
                    continue;
                Transform t = spheres[i].transform;
                if (owner != null && (t == owner.transform || t.IsChildOf(owner.transform)))
                    continue;
                if (spheres[i].distance < best)
                {
                    best = spheres[i].distance;
                    hit = spheres[i];
                    found = true;
                }
            }
            if (found)
                return true;
            Vector3 probe = hasAny ? anyHit.point : ray.GetPoint(Mathf.Min(2.2f, dist));
            int n = Physics.OverlapSphereNonAlloc(probe, 0.45f, SupportScratch, ~0, QueryTriggerInteraction.Ignore);
            float bestSqr = float.PositiveInfinity;
            Collider bestCol = null;
            for (int i = 0; i < n; i++)
            {
                var col = SupportScratch[i];
                if (!IsBoatTarget(col))
                    continue;
                Transform t = col.transform;
                if (owner != null && (t == owner.transform || t.IsChildOf(owner.transform)))
                    continue;
                Vector3 closest = col.ClosestPoint(probe);
                float sqr = (closest - probe).sqrMagnitude;
                if (sqr >= bestSqr)
                    continue;
                Vector3 to = closest - ray.origin;
                if (to.sqrMagnitude < 0.0001f)
                    to = ray.direction;
                if (!col.Raycast(new Ray(ray.origin, to.normalized), out RaycastHit pieceHit2, dist + 1f))
                {
                    Vector3 away = ray.origin - closest;
                    if (away.sqrMagnitude < 0.0001f)
                        away = Vector3.up;
                    if (!col.Raycast(new Ray(closest + away.normalized * 0.45f, -away.normalized), out pieceHit2, 0.9f))
                        continue;
                }
                bestSqr = sqr;
                bestCol = col;
                hit = pieceHit2;
            }
            if (bestCol != null)
                return true;
            if (!hasAny)
                return false;
        }
        hit = anyHit;
        return hasAny;
    }

    public static bool IsBoatTarget(Collider col)
    {
        if (col == null)
            return false;
        if (col.GetComponentInParent<BoatPiece>() != null)
            return true;
        var item = col.GetComponentInParent<BoatMaterialItem>();
        return item != null && !item.IsCarried;
    }

    static readonly Collider[] SupportScratch = new Collider[32];

    /// <summary>
    /// Верх ближайших деталей под точкой прицела — чтобы доска легла на два бревна,
    /// даже если луч попал в пол в щели между ними.
    /// </summary>
    public static bool TrySupportTop(Vector3 point, float radius, GameObject ignore, out float topY)
    {
        topY = point.y;
        int n = Physics.OverlapSphereNonAlloc(point, radius, SupportScratch, ~0, QueryTriggerInteraction.Ignore);
        bool any = false;
        for (int i = 0; i < n; i++)
        {
            var col = SupportScratch[i];
            if (col == null || col.isTrigger)
                continue;
            if (ignore != null && (col.transform == ignore.transform || col.transform.IsChildOf(ignore.transform)))
                continue;
            var piece = col.GetComponentInParent<BoatPiece>();
            if (piece == null)
                continue;
            Bounds b = col.bounds;
            Vector3 closest = b.ClosestPoint(point);
            Vector2 delta = new Vector2(closest.x - point.x, closest.z - point.z);
            if (delta.sqrMagnitude > radius * radius)
                continue;
            if (b.max.y < point.y - 0.35f)
                continue;
            if (!any || b.max.y > topY)
            {
                topY = b.max.y;
                any = true;
            }
        }
        return any;
    }

    public static BoatNail SpawnNail(BoatPiece a, BoatPiece b, Vector3 pos, Vector3 dir)
    {
        var go = new GameObject("Nail");
        go.transform.SetParent(a.transform, true);
        go.transform.position = pos;
        go.transform.rotation = Quaternion.FromToRotation(Vector3.up, dir.sqrMagnitude > 0.01f ? dir.normalized : Vector3.up);
        BoatVisuals.Attach(go.transform, PrimitiveType.Cylinder, new Vector3(0.02f, 0.06f, 0.02f), BoatVisuals.Metal);
        var box = go.AddComponent<BoxCollider>();
        box.size = new Vector3(0.03f, 0.12f, 0.03f);
        box.isTrigger = true;
        var nail = go.AddComponent<BoatNail>();
        nail.A = a;
        nail.B = b;
        a.RegisterNail(nail);
        b.RegisterNail(nail);
        return nail;
    }

    public static void Settle(BoatPiece piece, Collider support)
    {
        if (piece == null)
            return;

        var rb = piece.Body;
        if (rb != null)
        {
            rb.maxDepenetrationVelocity = 1.2f;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        var box = piece.GetComponent<BoxCollider>();
        if (box != null && support != null)
        {
            if (Physics.ComputePenetration(
                    box, piece.transform.position, piece.transform.rotation,
                    support, support.transform.position, support.transform.rotation,
                    out Vector3 dir, out float dist)
                && dist > 0.0001f)
            {
                if (dir.y < 0.15f)
                    dir = Vector3.up;
                piece.transform.position += dir.normalized * (dist + 0.03f);
            }
        }

        Physics.SyncTransforms();
        if (box != null)
        {
            Vector3 origin = box.bounds.center + Vector3.up * 0.8f;
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit ground, 4f, ~0, QueryTriggerInteraction.Ignore)
                && ground.collider != null
                && !ground.collider.transform.IsChildOf(piece.transform)
                && ground.collider.GetComponentInParent<BoatPiece>() != piece)
            {
                float bottom = box.bounds.min.y;
                if (bottom < ground.point.y - 0.002f)
                    piece.transform.position += Vector3.up * (ground.point.y - bottom + 0.02f);
            }
        }

        if (support != null)
        {
            var otherRb = support.GetComponentInParent<Rigidbody>();
            if (otherRb != null)
            {
                otherRb.linearVelocity = Vector3.zero;
                otherRb.angularVelocity = Vector3.zero;
            }
        }
    }
}
