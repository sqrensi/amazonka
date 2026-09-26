using UnityEngine;
using UnityEngine.InputSystem;

public static class BoatBuildUtil
{
    public static Camera Cam(GameObject owner)
    {
        if (owner == null)
            return Camera.main;
        var c = owner.GetComponentInChildren<Camera>();
        return c != null ? c : Camera.main;
    }

    public static bool SupportsClosestPoint(Collider col)
    {
        if (col == null || !col.enabled)
            return false;
        if (col is BoxCollider || col is SphereCollider || col is CapsuleCollider)
            return true;
        var mesh = col as MeshCollider;
        return mesh != null && mesh.convex;
    }

    public static Vector3 ClosestPoint(Collider col, Vector3 point)
    {
        if (col == null)
            return point;
        if (SupportsClosestPoint(col))
            return col.ClosestPoint(point);
        return col.bounds.ClosestPoint(point);
    }

    public static bool Aim(GameObject owner, float dist, out RaycastHit hit, bool preferPieces = false, bool allowPieceSteal = true)
    {
        hit = default;
        Camera cam = Cam(owner);
        if (cam == null)
            return false;
        Ray ray = new Ray(cam.transform.position, cam.transform.forward);
        var hits = Physics.RaycastAll(ray, dist, ~0, QueryTriggerInteraction.Collide);
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
                hits[i].collider.GetComponentInParent<BoatNail>() == null &&
                hits[i].collider.GetComponentInParent<BoatWater>() == null)
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
        if (hasPiece && (preferPieces || (allowPieceSteal && bestPiece <= bestAny + 0.85f)))
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
                Vector3 closest = ClosestPoint(col, probe);
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

    static readonly Collider[] SupportScratch = new Collider[96];
    static readonly Collider[] SitScratch = new Collider[48];

    /// <summary>
    /// Высота опоры только если деталь своим следом реально ложится на другую.
    /// Длинная доска на двух брёвнах со щелью между ними — следа хватает на оба.
    /// Сосед сбоку, в который доска не упирается, высоту не задаёт.
    /// </summary>
    public static bool TryBlockedSupportY(Vector3 xz, float floorY, Quaternion rot, Vector3 size, GameObject ignore, out float topY)
    {
        topY = float.NegativeInfinity;
        Vector3 half = size * 0.5f;
        half.x = Mathf.Max(0.04f, half.x * 0.92f);
        half.z = Mathf.Max(0.04f, half.z * 0.92f);
        Vector3 searchHalf = half;
        searchHalf.y = 0.85f;
        Vector3 searchPos = new Vector3(xz.x, floorY + 0.15f, xz.z);
        int n = Physics.OverlapBoxNonAlloc(searchPos, searchHalf, SupportScratch, rot, ~0, QueryTriggerInteraction.Ignore);
        bool any = false;
        float best = float.PositiveInfinity;
        for (int i = 0; i < n; i++)
        {
            var col = SupportScratch[i];
            if (col == null || col.isTrigger)
                continue;
            if (ignore != null && (col.transform == ignore.transform || col.transform.IsChildOf(ignore.transform)))
                continue;
            if (!IsBoatTarget(col))
                continue;

            float candY = col.bounds.max.y;
            if (candY < floorY - 0.35f || candY > floorY + 0.85f)
                continue;

            Vector3 probe = new Vector3(xz.x, candY, xz.z);
            Vector3 closest = ClosestPoint(col, probe);
            Vector3 planar = closest - probe;
            planar.y = 0f;
            float rad = Mathf.Max(half.x, half.z) + 0.12f;
            if (planar.sqrMagnitude > rad * rad)
                continue;

            float y = closest.y;
            Vector3 origin = new Vector3(xz.x, candY + 0.45f, xz.z);
            if (col.Raycast(new Ray(origin, Vector3.down), out RaycastHit onTop, 1.4f))
                y = onTop.point.y;
            float dist = Mathf.Abs(y - floorY);
            if (!any || dist < best - 0.01f || (Mathf.Abs(dist - best) < 0.01f && y > topY))
            {
                topY = y;
                best = dist;
                any = true;
            }
        }
        return any;
    }

    public static bool TryPlacePose(GameObject owner, float dist, Vector3 size, Quaternion rot, out Vector3 pos)
    {
        return TryPlacePose(owner, dist, size, Vector3.zero, rot, out pos);
    }

    public static bool TryPlacePose(GameObject owner, float dist, Vector3 size, Vector3 localCenter, Quaternion rot, out Vector3 pos, bool spanNeighbors = true)
    {
        pos = default;
        Camera cam = Cam(owner);
        if (cam == null)
            return false;
        Ray ray = new Ray(cam.transform.position, cam.transform.forward);
        bool aimed = Aim(owner, dist, out RaycastHit hit, preferPieces: false, allowPieceSteal: false);
        bool water = BoatWater.RaycastSurface(ray, dist, out Vector3 waterPt, out float waterY);

        bool aimPiece = aimed && IsBoatTarget(hit.collider);
        if (water && !aimPiece && (!aimed || (waterPt - ray.origin).magnitude + 0.04f < hit.distance))
        {
            pos = SitOnPoint(waterPt, Vector3.up, rot, localCenter, size, owner, waterY, spanNeighbors);
            return true;
        }

        if (!aimed)
            return false;

        BoatMaterialItem.PromoteAround(hit.point, 0.45f);
        Vector3 n = hit.normal.sqrMagnitude > 0.01f ? hit.normal.normalized : Vector3.up;
        if (hit.collider != null && hit.collider.GetComponentInParent<BoatWater>() != null)
        {
            float y = water ? waterY : hit.point.y;
            pos = SitOnPoint(hit.point, Vector3.up, rot, localCenter, size, owner, y, spanNeighbors);
            return true;
        }
        pos = SitOnPoint(hit.point, n, rot, localCenter, size, owner, hit.point.y, spanNeighbors);
        return true;
    }

    static Vector3 SitOnPoint(Vector3 hitPoint, Vector3 n, Quaternion rot, Vector3 localCenter, Vector3 size, GameObject owner, float seedY, bool spanNeighbors)
    {
        Vector3 at = hitPoint;
        Vector3 nH = new Vector3(n.x, 0f, n.z);
        if (n.y < 0.5f && nH.sqrMagnitude > 0.0001f)
            at += nH.normalized * 0.035f;

        if (!spanNeighbors)
        {
            float thick = Mathf.Min(0.08f, ProjectExtent(rot, size, n) + 0.02f);
            Vector3 worldCenter = at + n * (thick + 0.025f);
            return worldCenter - rot * localCenter;
        }

        Vector3 pos = LiftOrigin(new Vector3(at.x, seedY, at.z), rot, localCenter, size, seedY);
        pos = SuggestLayerSit(pos, rot, localCenter, size, owner, seedY);
        return ResolveOverlapSit(pos, rot, localCenter, size, owner);
    }

    static float _layerY;
    static float _layerUntil;
    static Vector3 _layerXZ;

    public static void RememberPlaceY(Vector3 pos, Quaternion rot, Vector3 localCenter, Vector3 size)
    {
        Vector3 worldC = pos + rot * localCenter;
        _layerY = worldC.y - ProjectExtent(rot, size, Vector3.up);
        _layerXZ = pos;
        _layerUntil = Time.time + 48f;
    }

    static Vector3 SuggestLayerSit(Vector3 pos, Quaternion rot, Vector3 localCenter, Vector3 size, GameObject owner, float seedY)
    {
        Camera cam = Cam(owner);
        Vector3 look = cam != null ? cam.transform.forward : Vector3.forward;
        float feet = owner != null ? owner.transform.position.y : seedY;
        float lookH = cam != null ? cam.transform.position.y + look.y * 2.5f : seedY;
        float rec = seedY;

        if (look.y > -0.5f && TryBandDeckY(pos, rot, size, owner, lookH, 1.05f, out float bandY))
            rec = Mathf.Max(rec, bandY);

        if (Time.time < _layerUntil)
        {
            Vector3 flat = pos - _layerXZ;
            flat.y = 0f;
            if (flat.sqrMagnitude < 20f && look.y > -0.44f && Mathf.Abs(lookH - _layerY) < 0.9f)
            {
                if (TryBandDeckY(pos, rot, size, owner, _layerY, 0.5f, out float ly))
                    rec = ly;
                else if (Mathf.Abs(seedY - _layerY) > 0.16f)
                    rec = Mathf.Lerp(seedY, _layerY, 0.7f);
            }
        }

        if (feet - seedY > 0.48f && look.y > -0.42f)
        {
            float stand = feet + 0.05f;
            if (TryBandDeckY(pos, rot, size, owner, stand, 0.75f, out float sy))
                rec = Mathf.Max(rec, sy);
            else
                rec = Mathf.Max(rec, stand);
        }

        if (rec <= seedY + 0.07f)
            return pos;
        return LiftOrigin(new Vector3(pos.x, rec, pos.z), rot, localCenter, size, rec);
    }

    static bool TryBandDeckY(Vector3 pos, Quaternion rot, Vector3 size, GameObject ignore, float band, float slack, out float y)
    {
        y = 0f;
        Vector3 worldC = new Vector3(pos.x, band, pos.z);
        Vector3 half = size * 0.5f;
        half.x = Mathf.Max(0.14f, half.x);
        half.z = Mathf.Max(0.14f, half.z);
        half.y = Mathf.Max(0.28f, slack);
        int n = Physics.OverlapBoxNonAlloc(worldC, half, SupportScratch, rot, ~0, QueryTriggerInteraction.Ignore);
        bool any = false;
        float best = float.PositiveInfinity;
        for (int i = 0; i < n; i++)
        {
            var col = SupportScratch[i];
            if (!UsableSupport(col, ignore))
                continue;
            float top = Mathf.Max(col.bounds.max.y, PieceTopY(col, worldC));
            float d = Mathf.Abs(top - band);
            if (d > slack)
                continue;
            if (!any || d < best)
            {
                y = top;
                best = d;
                any = true;
            }
        }
        return any;
    }

    static float _sitStickY;
    static bool _sitStick;
    static float _sitStickUntil;

    static Vector3 ResolveOverlapSit(Vector3 pos, Quaternion rot, Vector3 localCenter, Vector3 size, GameObject ignore)
    {
        bool hit = TryPenetratedRestY(pos, rot, localCenter, size, ignore, out float restY);
        if (hit)
        {
            if (_sitStick && restY < _sitStickY - 0.12f && Time.time < _sitStickUntil)
                restY = _sitStickY;
            else if (_sitStick)
                restY = Mathf.Lerp(_sitStickY, restY, 0.35f);
            _sitStick = true;
            _sitStickY = restY;
            _sitStickUntil = Time.time + 0.28f;
            return LiftOrigin(new Vector3(pos.x, restY, pos.z), rot, localCenter, size, restY);
        }
        if (_sitStick && Time.time < _sitStickUntil)
            return LiftOrigin(new Vector3(pos.x, _sitStickY, pos.z), rot, localCenter, size, _sitStickY);
        _sitStick = false;
        return pos;
    }

    static bool TryPenetratedRestY(Vector3 pos, Quaternion rot, Vector3 localCenter, Vector3 size, GameObject ignore, out float restY)
    {
        restY = float.NegativeInfinity;
        Vector3 worldC = pos + rot * localCenter;
        Vector3 half = size * 0.5f;
        half.x = Mathf.Max(0.04f, half.x * 0.9f);
        half.y = Mathf.Max(0.02f, half.y * 0.9f);
        half.z = Mathf.Max(0.04f, half.z * 0.9f);
        float bottom = worldC.y - ProjectExtent(rot, size, Vector3.up);
        int hits = 0;

        int n = Physics.OverlapBoxNonAlloc(worldC, half, SupportScratch, rot, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
            ConsiderSupport(SupportScratch[i], ignore, worldC, bottom, ref restY, ref hits);

        Vector3 along = rot * Vector3.forward;
        float rad = Mathf.Max(0.05f, Mathf.Max(half.x, half.y) * 0.9f);
        float len = half.z;
        for (int s = -2; s <= 2; s++)
        {
            Vector3 p = worldC + along * (len * s / 2f);
            int m = Physics.OverlapSphereNonAlloc(p, rad, SitScratch, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < m; i++)
                ConsiderSupport(SitScratch[i], ignore, worldC, bottom, ref restY, ref hits);
        }

        return hits > 0;
    }

    static void ConsiderSupport(Collider col, GameObject ignore, Vector3 worldC, float bottom, ref float restY, ref int hits)
    {
        if (!UsableSupport(col, ignore))
            return;
        float top = Mathf.Max(col.bounds.max.y, PieceTopY(col, worldC));
        if (top <= bottom + 0.04f)
            return;
        if (top > restY)
            restY = top;
        hits++;
    }

    static bool UsableSupport(Collider col, GameObject ignore)
    {
        if (col == null || col.isTrigger || !IsBoatTarget(col))
            return false;
        if (ignore != null && (col.transform == ignore.transform || col.transform.IsChildOf(ignore.transform)))
            return false;
        return true;
    }

    static float PieceTopY(Collider col, Vector3 near)
    {
        Vector3 closest = ClosestPoint(col, new Vector3(near.x, col.bounds.max.y, near.z));
        Vector3 origin = closest + Vector3.up * 0.55f;
        origin.y = Mathf.Max(origin.y, col.bounds.max.y + 0.35f);
        if (col.Raycast(new Ray(origin, Vector3.down), out RaycastHit hit, 2.2f))
            return hit.point.y;
        return col.bounds.max.y;
    }

    public static void FollowGhost(ref bool ready, ref Vector3 pos, ref Vector3 vel, ref Quaternion rot, Vector3 targetPos, Quaternion targetRot)
    {
        if (!ready)
        {
            pos = targetPos;
            rot = targetRot;
            vel = Vector3.zero;
            ready = true;
            _sitStick = false;
            return;
        }
        pos = Vector3.SmoothDamp(pos, targetPos, ref vel, 0.26f, 8.5f, Time.deltaTime);
        rot = Quaternion.Slerp(rot, targetRot, 1f - Mathf.Exp(-4.4f * Time.deltaTime));
    }

    public static Vector3 LiftOrigin(Vector3 pos, Quaternion rot, Vector3 localCenter, Vector3 size, float supportY)
    {
        Vector3 worldCenter = pos + rot * localCenter;
        float bottom = worldCenter.y - ProjectExtent(rot, size, Vector3.up);
        float need = supportY + 0.03f;
        if (bottom < need)
            pos.y += need - bottom;
        return pos;
    }

    public static BoatNail SpawnNail(BoatPiece a, BoatPiece b, Vector3 aim, Vector3 hitNormal)
    {
        NailPose(a, b, aim, hitNormal, out Vector3 pos, out Vector3 dir);
        var go = new GameObject("Nail");
        go.transform.SetParent(a.transform, true);
        go.transform.SetPositionAndRotation(pos, Quaternion.FromToRotation(Vector3.up, dir));
        BoatVisuals.Attach(go.transform, PrimitiveType.Cylinder, new Vector3(0.018f, 0.055f, 0.018f), BoatVisuals.Metal);
        var box = go.AddComponent<BoxCollider>();
        box.size = new Vector3(0.04f, 0.13f, 0.04f);
        box.isTrigger = true;
        var nail = go.AddComponent<BoatNail>();
        nail.A = a;
        nail.B = b;
        nail.Aim = pos;
        a.RegisterNail(nail);
        b.RegisterNail(nail);
        nail.BindPlanted();
        return nail;
    }

    public static Collider SolidCollider(BoatPiece piece)
    {
        return SnapCollider(piece);
    }

    public static Collider SnapCollider(BoatPiece piece)
    {
        if (piece == null)
            return null;
        if (piece.Kind == BoatPieceKind.Oar)
        {
            Transform plate = piece.transform.Find("OarlockPlate");
            if (plate != null)
            {
                var lockCol = plate.GetComponent<Collider>();
                if (lockCol != null && lockCol.enabled)
                    return lockCol;
            }
        }
        var cols = piece.GetComponents<Collider>();
        for (int i = 0; i < cols.Length; i++)
        {
            if (cols[i] != null && cols[i].enabled && !cols[i].isTrigger)
                return cols[i];
        }
        cols = piece.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < cols.Length; i++)
        {
            if (cols[i] != null && cols[i].enabled && !cols[i].isTrigger)
                return cols[i];
        }
        return null;
    }
    public static void NailPose(BoatPiece a, BoatPiece b, Vector3 aim, Vector3 hitNormal, out Vector3 pos, out Vector3 dir)
    {
        var ca = SolidCollider(a);
        var cb = SolidCollider(b);
        Vector3 pa = ca != null ? ClosestPoint(ca, aim) : aim;
        Vector3 pb = cb != null ? ClosestPoint(cb, aim) : aim;
        Vector3 outward = hitNormal.sqrMagnitude > 0.01f ? hitNormal.normalized : Vector3.up;
        if (ca != null)
        {
            Vector3 fromCenter = aim - ca.bounds.center;
            if (fromCenter.sqrMagnitude > 0.0001f && Vector3.Dot(outward, fromCenter) < 0f)
                outward = fromCenter.normalized;
        }
        pos = aim + outward * 0.08f;
        Vector3 through = pb - pa;
        if (through.sqrMagnitude > 0.00025f)
            dir = through.normalized;
        else
            dir = -outward;
    }

    public static void SnapTogether(BoatPiece a, BoatPiece b, Vector3 aim)
    {
        if (a == null || b == null || a == b)
            return;
        Rigidbody ra = a.IslandRootBody() ?? a.Body;
        Rigidbody rb = b.IslandRootBody() ?? b.Body;
        if (ra != null && ra == rb)
        {
            StopMotion(ra);
            return;
        }
        var ca = SolidCollider(a);
        var cb = SolidCollider(b);
        if (ca == null || cb == null)
            return;

        var moveIsland = new System.Collections.Generic.List<BoatPiece>(16);
        for (int pass = 0; pass < 6; pass++)
        {
            Physics.SyncTransforms();
            if (Physics.ComputePenetration(
                    ca, a.transform.position, a.transform.rotation,
                    cb, b.transform.position, b.transform.rotation,
                    out _, out float overlap)
                && overlap > 0.0004f)
                break;

            Vector3 pA = ClosestPoint(ca, aim);
            Vector3 pB = ClosestPoint(cb, pA);
            pA = ClosestPoint(ca, pB);
            pB = ClosestPoint(cb, pA);
            Vector3 gap = pA - pB;
            float dist = gap.magnitude;
            if (dist < 0.0015f || dist > 0.85f)
                break;

            BoatPiece mover = b;
            Vector3 delta = gap;
            if (a.Kind == BoatPieceKind.Oar)
            {
                mover = a;
                delta = -gap;
            }
            else if (b.Kind == BoatPieceKind.Oar)
            {
                mover = b;
                delta = gap;
            }
            else if (b.IsLockedInBoat() && !a.IsLockedInBoat())
            {
                mover = a;
                delta = -gap;
            }
            else if (a.IsLockedInBoat() && b.IsLockedInBoat())
            {
                a.CollectIsland(moveIsland);
                int na = moveIsland.Count;
                b.CollectIsland(moveIsland);
                int nb = moveIsland.Count;
                if (nb < na)
                {
                    mover = b;
                    delta = gap;
                }
                else
                {
                    mover = a;
                    delta = -gap;
                }
            }

            mover.CollectIsland(moveIsland);
            bool stayInIsland = false;
            BoatPiece stay = mover == a ? b : a;
            for (int i = 0; i < moveIsland.Count; i++)
            {
                if (moveIsland[i] == stay)
                {
                    stayInIsland = true;
                    break;
                }
            }
            if (stayInIsland || mover.Kind == BoatPieceKind.Oar)
                mover.transform.position += delta;
            else
                MoveCluster(moveIsland, delta);
        }

        StopMotion(a.Body);
        StopMotion(b.Body);
        if (a.Kind == BoatPieceKind.Oar)
            a.transform.localScale = Vector3.one;
        if (b.Kind == BoatPieceKind.Oar)
            b.transform.localScale = Vector3.one;
        Physics.SyncTransforms();
    }

    public static void SnapTogether(BoatPiece a, BoatPiece b)
    {
        if (a == null || b == null)
            return;
        Vector3 aim = (a.transform.position + b.transform.position) * 0.5f;
        SnapTogether(a, b, aim);
    }

    public static void SoftPlace(BoatPiece piece, Collider support)
    {
        if (piece == null)
            return;
        ClearIgnoreWithBoat(piece);
        Settle(piece, support);
        var list = new System.Collections.Generic.List<BoatPiece>(1) { piece };
        NudgeClusterFromActors(list);
        IgnoreActorsBriefly(list, 1.8f);
        var rb = piece.Body;
        if (rb != null)
        {
            rb.maxDepenetrationVelocity = 0.28f;
            StopMotion(rb);
        }
    }

    public static void Settle(BoatPiece piece, Collider support)
    {
        if (piece == null)
            return;

        var rb = piece.Body;
        if (rb != null)
        {
            rb.maxDepenetrationVelocity = 0.45f;
            StopMotion(rb);
        }

        var colA = SolidCollider(piece);
        if (colA != null && support != null
            && Physics.ComputePenetration(
                colA, piece.transform.position, piece.transform.rotation,
                support, support.transform.position, support.transform.rotation,
                out Vector3 dir, out float dist)
            && dist > 0.0001f)
        {
            piece.transform.position += dir.normalized * (dist + 0.025f);
        }

        Physics.SyncTransforms();
        if (colA != null)
        {
            Vector3 origin = colA.bounds.center + Vector3.up * 0.8f;
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit ground, 4f, ~0, QueryTriggerInteraction.Ignore)
                && ground.collider != null
                && !ground.collider.transform.IsChildOf(piece.transform)
                && ground.collider.GetComponentInParent<BoatPiece>() != piece)
            {
                float bottom = colA.bounds.min.y;
                if (bottom < ground.point.y - 0.002f)
                    piece.transform.position += Vector3.up * (ground.point.y - bottom + 0.02f);
            }
        }

        Unstuck(piece);

        float floorY = piece.transform.position.y - ProjectExtent(piece.transform.rotation, piece.PieceSize, Vector3.up);
        if (TryBlockedSupportY(piece.transform.position, floorY, piece.transform.rotation, piece.PieceSize, piece.gameObject, out float top))
        {
            float need = top + ProjectExtent(piece.transform.rotation, piece.PieceSize, Vector3.up) + 0.02f;
            if (piece.transform.position.y < need)
            {
                Vector3 p = piece.transform.position;
                p.y = need;
                piece.transform.position = p;
            }
        }

        if (support != null)
        {
            var otherRb = support.GetComponentInParent<Rigidbody>();
            if (otherRb != null)
                StopMotion(otherRb);
        }
    }

    public static void SettleCluster(System.Collections.Generic.List<BoatPiece> pieces)
    {
        if (pieces == null || pieces.Count == 0)
            return;

        bool any = false;
        Bounds b = default;
        for (int i = 0; i < pieces.Count; i++)
        {
            var p = pieces[i];
            if (p == null)
                continue;
            var box = p.GetComponent<BoxCollider>();
            Bounds pb = box != null ? box.bounds : new Bounds(p.transform.position, p.PieceSize);
            if (!any)
            {
                b = pb;
                any = true;
            }
            else
                b.Encapsulate(pb);
        }
        if (!any)
            return;

        Vector3 origin = b.center + Vector3.up * 1.6f;
        var hits = Physics.RaycastAll(origin, Vector3.down, 8f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.PositiveInfinity;
        Vector3 ground = origin;
        bool found = false;
        for (int i = 0; i < hits.Length; i++)
        {
            var col = hits[i].collider;
            if (col == null)
                continue;
            var hitPiece = col.GetComponentInParent<BoatPiece>();
            bool own = false;
            for (int p = 0; p < pieces.Count; p++)
            {
                if (pieces[p] != null && (hitPiece == pieces[p] || col.transform.IsChildOf(pieces[p].transform)))
                {
                    own = true;
                    break;
                }
            }
            if (own)
                continue;
            if (hits[i].distance < best)
            {
                best = hits[i].distance;
                ground = hits[i].point;
                found = true;
            }
        }
        if (found)
        {
            float lift = ground.y + 0.02f - b.min.y;
            if (lift > 0.001f)
            {
                Vector3 delta = Vector3.up * lift;
                for (int i = 0; i < pieces.Count; i++)
                {
                    if (pieces[i] != null)
                        pieces[i].transform.position += delta;
                }
            }
        }
        Physics.SyncTransforms();
        UnstuckCluster(pieces);
        NudgeClusterFromActors(pieces);
    }

    public static bool IsActorCollider(Collider col)
    {
        if (col == null)
            return false;
        return col.GetComponentInParent<CharacterController>() != null;
    }

    public static void EnsureCollideWithActors(Collider col)
    {
        if (col == null)
            return;
        var actors = Object.FindObjectsByType<CharacterController>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < actors.Length; i++)
        {
            if (actors[i] == null)
                continue;
            var cols = actors[i].GetComponentsInChildren<Collider>(true);
            for (int c = 0; c < cols.Length; c++)
            {
                if (cols[c] == null)
                    continue;
                Physics.IgnoreCollision(col, cols[c], false);
            }
        }
    }

    public static void ClearIgnoreWithBoat(BoatPiece piece)
    {
        if (piece == null)
            return;
        var self = piece.GetComponentsInChildren<Collider>(true);
        var others = Object.FindObjectsByType<BoatPiece>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < others.Length; i++)
        {
            var other = others[i];
            if (other == null || other == piece)
                continue;
            var cols = other.GetComponentsInChildren<Collider>(true);
            for (int s = 0; s < self.Length; s++)
            {
                if (self[s] == null)
                    continue;
                for (int c = 0; c < cols.Length; c++)
                {
                    if (cols[c] == null)
                        continue;
                    Physics.IgnoreCollision(self[s], cols[c], false);
                }
            }
        }
        EnsureCollideWithActors(SolidCollider(piece));
    }

    public static void SetActorIgnoreIsland(CharacterController actor, BoatPiece piece, bool ignore)
    {
        if (actor == null || piece == null)
            return;
        var island = new System.Collections.Generic.List<BoatPiece>(16);
        piece.CollectIsland(island);
        for (int i = 0; i < island.Count; i++)
        {
            if (island[i] == null)
                continue;
            var cols = island[i].GetComponentsInChildren<Collider>(true);
            for (int c = 0; c < cols.Length; c++)
            {
                if (cols[c] == null || cols[c].isTrigger)
                    continue;
                Physics.IgnoreCollision(actor, cols[c], ignore);
            }
        }
    }

    public static void MoveCluster(System.Collections.Generic.IList<BoatPiece> pieces, Vector3 delta)
    {
        if (pieces == null || delta.sqrMagnitude < 0.0000001f)
            return;
        for (int i = 0; i < pieces.Count; i++)
        {
            if (pieces[i] != null)
                pieces[i].transform.position += delta;
        }
    }

    public static void UnstuckCluster(System.Collections.Generic.IList<BoatPiece> pieces)
    {
        if (pieces == null || pieces.Count == 0)
            return;
        for (int pass = 0; pass < 6; pass++)
        {
            Physics.SyncTransforms();
            Vector3 pushDir = Vector3.up;
            float best = 0f;
            for (int p = 0; p < pieces.Count; p++)
            {
                var piece = pieces[p];
                if (piece == null)
                    continue;
                var colA = SolidCollider(piece);
                if (colA == null)
                    continue;
                Vector3 center = colA.bounds.center;
                Vector3 half = colA.bounds.extents;
                int n = Physics.OverlapBoxNonAlloc(
                    center, half, UnstuckScratch, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < n; i++)
                {
                    var col = UnstuckScratch[i];
                    if (col == null || col == colA || IsActorCollider(col))
                        continue;
                    if (col.transform == piece.transform || col.transform.IsChildOf(piece.transform))
                        continue;
                    var other = col.GetComponentInParent<BoatPiece>();
                    if (other == piece)
                        continue;
                    bool sibling = false;
                    for (int s = 0; s < pieces.Count; s++)
                    {
                        if (pieces[s] == other)
                        {
                            sibling = true;
                            break;
                        }
                    }
                    if (sibling)
                        continue;
                    if (!Physics.ComputePenetration(
                            colA, piece.transform.position, piece.transform.rotation,
                            col, col.transform.position, col.transform.rotation,
                            out Vector3 dir, out float dist)
                        || dist < 0.0008f)
                        continue;
                    if (dist > best)
                    {
                        best = dist;
                        pushDir = dir;
                    }
                }
            }
            if (best <= 0f)
                return;
            MoveCluster(pieces, pushDir.normalized * (best + 0.014f));
        }
    }

    public static void NudgeClusterFromActors(System.Collections.Generic.IList<BoatPiece> pieces)
    {
        if (pieces == null || pieces.Count == 0)
            return;
        for (int pass = 0; pass < 5; pass++)
        {
            Physics.SyncTransforms();
            Vector3 push = Vector3.zero;
            float best = 0f;
            for (int p = 0; p < pieces.Count; p++)
            {
                var piece = pieces[p];
                if (piece == null)
                    continue;
                var box = piece.GetComponent<BoxCollider>();
                if (box == null || !box.enabled)
                    continue;
                Vector3 center = piece.transform.TransformPoint(box.center);
                Vector3 half = box.size * 0.5f;
                int n = Physics.OverlapBoxNonAlloc(
                    center, half, UnstuckScratch, piece.transform.rotation, ~0, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < n; i++)
                {
                    var col = UnstuckScratch[i];
                    if (col == null || col == box || !IsActorCollider(col))
                        continue;
                    if (!Physics.ComputePenetration(
                            box, piece.transform.position, piece.transform.rotation,
                            col, col.transform.position, col.transform.rotation,
                            out Vector3 dir, out float dist)
                        || dist < 0.0008f)
                        continue;
                    if (dist > best)
                    {
                        best = dist;
                        push = dir;
                    }
                }
            }
            if (best <= 0f)
                return;
            if (push.y < -0.15f)
                push.y = 0f;
            if (push.sqrMagnitude < 0.0001f)
                push = Vector3.up;
            MoveCluster(pieces, push.normalized * (best + 0.14f));
        }
    }

    public static void IgnoreActorsBriefly(System.Collections.Generic.IList<BoatPiece> pieces, float seconds)
    {
        if (pieces == null || pieces.Count == 0)
            return;
        BoatPiece host = null;
        var self = new System.Collections.Generic.List<Collider>(16);
        for (int i = 0; i < pieces.Count; i++)
        {
            var p = pieces[i];
            if (p == null)
                continue;
            if (host == null)
                host = p;
            var cols = p.GetComponentsInChildren<Collider>(true);
            for (int c = 0; c < cols.Length; c++)
                if (cols[c] != null && cols[c].enabled)
                    self.Add(cols[c]);
        }
        if (host == null || self.Count == 0)
            return;

        var actors = Object.FindObjectsByType<CharacterController>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        var other = new System.Collections.Generic.List<Collider>(8);
        for (int i = 0; i < actors.Length; i++)
        {
            if (actors[i] == null)
                continue;
            var cols = actors[i].GetComponentsInChildren<Collider>(true);
            for (int c = 0; c < cols.Length; c++)
                if (cols[c] != null)
                    other.Add(cols[c]);
        }
        if (other.Count == 0)
            return;

        var driver = host.GetComponent<BoatIgnoreActors>();
        if (driver == null)
            driver = host.gameObject.AddComponent<BoatIgnoreActors>();
        driver.Run(self.ToArray(), other.ToArray(), seconds);
    }

    static readonly Collider[] UnstuckScratch = new Collider[64];

    public static void Unstuck(BoatPiece piece, System.Collections.Generic.IList<BoatPiece> skipTogether = null)
    {
        if (piece == null)
            return;
        var colA = SolidCollider(piece);
        if (colA == null)
            return;

        for (int pass = 0; pass < 8; pass++)
        {
            Physics.SyncTransforms();
            Vector3 center = colA.bounds.center;
            Vector3 half = colA.bounds.extents;
            int n = Physics.OverlapBoxNonAlloc(
                center, half, UnstuckScratch, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
            float best = 0f;
            Vector3 push = Vector3.zero;
            for (int i = 0; i < n; i++)
            {
                var col = UnstuckScratch[i];
                if (col == null || col == colA)
                    continue;
                if (IsActorCollider(col))
                    continue;
                if (col.transform == piece.transform || col.transform.IsChildOf(piece.transform))
                    continue;
                var other = col.GetComponentInParent<BoatPiece>();
                if (other == piece)
                    continue;
                if (skipTogether != null && other != null)
                {
                    bool skip = false;
                    for (int s = 0; s < skipTogether.Count; s++)
                    {
                        if (skipTogether[s] == other)
                        {
                            skip = true;
                            break;
                        }
                    }
                    if (skip)
                        continue;
                }
                if (!Physics.ComputePenetration(
                        colA, piece.transform.position, piece.transform.rotation,
                        col, col.transform.position, col.transform.rotation,
                        out Vector3 dir, out float dist)
                    || dist < 0.0008f)
                    continue;
                if (dist > best)
                {
                    best = dist;
                    if (other != null)
                    {
                        push = Vector3.up;
                        best = Mathf.Min(dist, 0.035f);
                    }
                    else
                        push = dir;
                }
            }
            if (best <= 0f)
                return;
            piece.transform.position += push.normalized * (best + 0.02f);
        }
    }

    public static void TickPlaceRotate(ref float yaw, ref float pitch, ref float roll, ref float qHeld, ref float eHeld, float dt)
    {
        var kb = Keyboard.current;
        const float tap = 5f;
        const float holdDelay = 0.16f;
        const float holdDeg = 120f;
        bool shift = kb != null && (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed);
        float tapStep = tap;
        float holdSpeed = holdDeg;

        if (kb != null && kb.qKey.wasPressedThisFrame)
        {
            pitch -= tapStep;
            qHeld = 0f;
        }
        if (kb != null && kb.eKey.wasPressedThisFrame)
        {
            pitch += tapStep;
            eHeld = 0f;
        }
        if (kb != null && kb.qKey.isPressed)
        {
            qHeld += dt;
            if (qHeld > holdDelay)
                pitch -= holdSpeed * dt;
        }
        else
            qHeld = 0f;
        if (kb != null && kb.eKey.isPressed)
        {
            eHeld += dt;
            if (eHeld > holdDelay)
                pitch += holdSpeed * dt;
        }
        else
            eHeld = 0f;
        if (kb != null && kb.rKey.wasPressedThisFrame)
            roll += 90f;

        var mouse = Mouse.current;
        if (mouse != null)
        {
            float scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f)
            {
                float step = scroll > 0f ? tapStep : -tapStep;
                if (shift)
                    roll += step;
                else
                    yaw += step;
            }
        }
    }

    public static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

    public static void StopMotion(Rigidbody rb)
    {
        if (rb == null || rb.isKinematic)
            return;
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
    }

    public static void SetMotion(Rigidbody rb, Vector3 linear, Vector3 angular)
    {
        if (rb == null || rb.isKinematic)
            return;
        rb.linearVelocity = linear;
        rb.angularVelocity = angular;
    }

    public static float ProjectExtent(Quaternion rot, Vector3 size, Vector3 n)
    {
        Vector3 w = Abs(rot * size);
        return 0.5f * Vector3.Dot(w, Abs(n.normalized));
    }
}

class BoatIgnoreActors : MonoBehaviour
{
    Collider[] _self;
    Collider[] _other;

    public void Run(Collider[] self, Collider[] other, float seconds)
    {
        Restore();
        _self = self;
        _other = other;
        SetIgnore(true);
        StopAllCoroutines();
        StartCoroutine(ClearAfter(seconds));
    }

    System.Collections.IEnumerator ClearAfter(float seconds)
    {
        yield return new WaitForSeconds(seconds);
        Restore();
        Destroy(this);
    }

    void OnDestroy()
    {
        Restore();
    }

    void Restore()
    {
        SetIgnore(false);
        _self = null;
        _other = null;
    }

    void SetIgnore(bool ignore)
    {
        if (_self == null || _other == null)
            return;
        for (int i = 0; i < _self.Length; i++)
        {
            var a = _self[i];
            if (a == null)
                continue;
            for (int j = 0; j < _other.Length; j++)
            {
                var b = _other[j];
                if (b == null)
                    continue;
                Physics.IgnoreCollision(a, b, ignore);
            }
        }
    }
}
