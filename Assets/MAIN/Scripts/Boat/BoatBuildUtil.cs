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

    public static bool Aim(GameObject owner, float dist, out RaycastHit hit, bool preferPieces = false, bool allowPieceSteal = true)
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
    /// Верх тех деталей, в которые бокс реально врезается на этой высоте.
    /// Рядом стоящие, но не пересекающиеся куски высоту не задают.
    /// </summary>
    public static bool TryBlockedSupportY(Vector3 xz, float floorY, Quaternion rot, Vector3 size, GameObject ignore, out float topY)
    {
        topY = float.NegativeInfinity;
        float halfUp = ProjectExtent(rot, size, Vector3.up);
        Vector3 pos = new Vector3(xz.x, floorY + halfUp + 0.02f, xz.z);
        Vector3 half = size * 0.5f;
        half.x = Mathf.Max(0.01f, half.x * 0.94f);
        half.z = Mathf.Max(0.01f, half.z * 0.94f);
        int n = Physics.OverlapBoxNonAlloc(pos, half, SupportScratch, rot, ~0, QueryTriggerInteraction.Ignore);
        bool any = false;
        for (int i = 0; i < n; i++)
        {
            var col = SupportScratch[i];
            if (col == null || col.isTrigger)
                continue;
            if (ignore != null && (col.transform == ignore.transform || col.transform.IsChildOf(ignore.transform)))
                continue;
            if (!IsBoatTarget(col))
                continue;
            if (col.bounds.max.y <= floorY + 0.005f)
                continue;
            if (!any || col.bounds.max.y > topY)
            {
                topY = col.bounds.max.y;
                any = true;
            }
        }
        return any;
    }

    public static bool TryPlacePose(GameObject owner, float dist, Vector3 size, Quaternion rot, out Vector3 pos)
    {
        pos = default;
        if (!Aim(owner, dist, out RaycastHit hit, preferPieces: false, allowPieceSteal: false))
            return false;
        BoatMaterialItem.PromoteAround(hit.point, Mathf.Max(0.6f, Mathf.Max(size.x, size.z) * 0.35f));
        float lift = ProjectExtent(rot, size, Vector3.up) + 0.04f;
        float y = hit.point.y;
        if (TryBlockedSupportY(hit.point, y, rot, size, owner, out float supportY))
            y = supportY;
        pos = new Vector3(hit.point.x, y + lift, hit.point.z);
        return true;
    }

    public static BoatNail SpawnNail(BoatPiece a, BoatPiece b, Vector3 pos, Vector3 dir)
    {
        var go = new GameObject("Nail");
        go.transform.SetParent(a.transform, true);
        go.transform.position = pos;
        go.transform.rotation = Quaternion.identity;
        BoatVisuals.Attach(go.transform, PrimitiveType.Cylinder, new Vector3(0.022f, 0.07f, 0.022f), BoatVisuals.Metal);
        var box = go.AddComponent<BoxCollider>();
        box.size = new Vector3(0.045f, 0.16f, 0.045f);
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
            {
                otherRb.linearVelocity = Vector3.zero;
                otherRb.angularVelocity = Vector3.zero;
            }
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
                    if (col == null || col == box || IsActorCollider(col))
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
                            box, piece.transform.position, piece.transform.rotation,
                            col, col.transform.position, col.transform.rotation,
                            out Vector3 dir, out float dist)
                        || dist < 0.0008f)
                        continue;
                    if (dir.y < 0.2f)
                        dir = Vector3.up;
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
            MoveCluster(pieces, push.normalized * (best + 0.05f));
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

    static readonly Collider[] UnstuckScratch = new Collider[24];

    public static void Unstuck(BoatPiece piece, System.Collections.Generic.IList<BoatPiece> skipTogether = null)
    {
        if (piece == null)
            return;
        var box = piece.GetComponent<BoxCollider>();
        if (box == null || !box.enabled)
            return;

        for (int pass = 0; pass < 6; pass++)
        {
            Physics.SyncTransforms();
            Vector3 center = piece.transform.TransformPoint(box.center);
            Vector3 half = box.size * 0.5f;
            int n = Physics.OverlapBoxNonAlloc(
                center, half, UnstuckScratch, piece.transform.rotation, ~0, QueryTriggerInteraction.Ignore);
            float best = 0f;
            for (int i = 0; i < n; i++)
            {
                var col = UnstuckScratch[i];
                if (col == null || col == box)
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
                        box, piece.transform.position, piece.transform.rotation,
                        col, col.transform.position, col.transform.rotation,
                        out Vector3 dir, out float dist)
                    || dist < 0.0008f)
                    continue;
                if (dir.y < 0.2f)
                    dir = Vector3.up;
                if (dist > best)
                    best = dist;
            }
            if (best <= 0f)
                return;
            piece.transform.position += Vector3.up * (best + 0.014f);
        }
    }

    public static void TickPlaceRotate(ref float yaw, ref float pitch, ref float roll, ref float qHeld, ref float eHeld, float dt)
    {
        var kb = Keyboard.current;
        const float tap = 5f;
        const float holdDelay = 0.16f;
        const float holdDeg = 120f;
        bool shift = kb != null && (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed);
        float tapStep = shift ? 2f : tap;
        float holdSpeed = shift ? holdDeg * 0.4f : holdDeg;

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
                yaw += scroll > 0f ? tapStep : -tapStep;
        }
    }

    public static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

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
