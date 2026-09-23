using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Сборка из нескольких сбитых/связанных деталей: подбор, поворот и установка как у доски.
/// </summary>
public class BoatClusterItem : HeldItem
{
    public override float WaterLift() => 48f;
    public override float WaterCurrent() => 1.1f;
    GameObject _ghost;
    Vector3 _ghostPos;
    Vector3 _ghostPosVel;
    Quaternion _ghostRot;
    bool _ghostFollow;
    float _yaw;
    float _pitch;
    float _roll;
    float _qHeld;
    float _eHeld;
    Quaternion _poseRot = Quaternion.identity;
    Vector3 _localSize = Vector3.one;
    readonly List<BoatPiece> _parts = new List<BoatPiece>();
    readonly List<Vector3> _localPos = new List<Vector3>();
    readonly List<Quaternion> _localRot = new List<Quaternion>();
    bool _released;

    public static bool TryPickup(List<BoatPiece> parts, PlayerInventory inv)
    {
        if (parts == null || parts.Count < 2 || inv == null)
            return false;

        Bounds world = default;
        bool any = false;
        for (int i = 0; i < parts.Count; i++)
        {
            var p = parts[i];
            if (p == null)
                continue;
            var box = p.GetComponent<BoxCollider>();
            Bounds b = box != null ? box.bounds : new Bounds(p.transform.position, p.PieceSize);
            if (!any)
            {
                world = b;
                any = true;
            }
            else
                world.Encapsulate(b);
        }
        if (!any)
            return false;

        var root = new GameObject("BoatAssembly");
        root.transform.SetPositionAndRotation(world.center, parts[0].transform.rotation);

        BoatRope.ParentOwning(parts, root.transform);

        for (int i = 0; i < parts.Count; i++)
        {
            var p = parts[i];
            if (p == null)
                continue;
            p.SleepForCarry();
            p.transform.SetParent(root.transform, true);
        }

        var item = root.AddComponent<BoatClusterItem>();
        item.Capture(parts, root.transform.rotation);
        if (!inv.PickupExisting(item))
        {
            item.ReleaseToWorld(root.transform.position, root.transform.rotation, Vector3.zero);
            return false;
        }
        return true;
    }

    void Capture(List<BoatPiece> parts, Quaternion worldRot)
    {
        _parts.Clear();
        _localPos.Clear();
        _localRot.Clear();
        for (int i = 0; i < parts.Count; i++)
        {
            var p = parts[i];
            if (p == null)
                continue;
            _parts.Add(p);
            _localPos.Add(p.transform.localPosition);
            _localRot.Add(p.transform.localRotation);
        }
        _poseRot = worldRot;
        _localSize = LocalSize();
        SetDisplayName(_parts.Count > 2 ? "Boat" : "Assembly");
    }

    Vector3 LocalSize()
    {
        var filters = GetComponentsInChildren<MeshFilter>(true);
        bool any = false;
        Bounds b = new Bounds(Vector3.zero, Vector3.zero);
        for (int i = 0; i < filters.Length; i++)
        {
            var f = filters[i];
            if (f == null || f.sharedMesh == null)
                continue;
            if (!f.gameObject.activeInHierarchy)
                continue;
            if (f.GetComponentInParent<BoatNail>() != null)
                continue;
            Bounds mb = f.sharedMesh.bounds;
            Vector3[] corners =
            {
                mb.min, mb.max,
                new Vector3(mb.min.x, mb.min.y, mb.max.z),
                new Vector3(mb.min.x, mb.max.y, mb.min.z),
                new Vector3(mb.max.x, mb.min.y, mb.min.z),
                new Vector3(mb.max.x, mb.max.y, mb.min.z),
                new Vector3(mb.max.x, mb.min.y, mb.max.z),
                new Vector3(mb.min.x, mb.max.y, mb.max.z)
            };
            for (int c = 0; c < corners.Length; c++)
            {
                Vector3 local = transform.InverseTransformPoint(f.transform.TransformPoint(corners[c]));
                if (!any)
                {
                    b = new Bounds(local, Vector3.zero);
                    any = true;
                }
                else
                    b.Encapsulate(local);
            }
        }
        Vector3 size = any ? b.size : Vector3.one;
        size.x = Mathf.Max(0.2f, size.x);
        size.y = Mathf.Max(0.08f, size.y);
        size.z = Mathf.Max(0.2f, size.z);
        return size;
    }

    public override void OnEquip()
    {
        base.OnEquip();
        HideHeldMesh();
        _ghostFollow = false;
        BoatBuildHud.Hint("LMB place   Wheel yaw   Shift+Wheel roll   hold Q/E tilt   G drop", 4f);
    }

    public override void OnUnequip()
    {
        ClearGhost();
        BoatBuildHud.Clear();
        base.OnUnequip();
    }

    protected override void OnRetractChanged(float retract)
    {
        HideHeldMesh();
    }

    public override void OnUseStart()
    {
        if (IsUseBlocked)
            return;
        if (!TryPose(out Vector3 pos, out Quaternion rot))
        {
            BoatBuildHud.Hint("Aim at ground or a piece");
            return;
        }
        Place(pos, rot);
    }

    void Update()
    {
        if (!IsEquipped)
        {
            ClearGhost();
            return;
        }

        BoatBuildUtil.TickPlaceRotate(ref _yaw, ref _pitch, ref _roll, ref _qHeld, ref _eHeld, Time.deltaTime);
        if (!TryPose(out Vector3 pos, out Quaternion rot))
        {
            if (_ghost != null)
                _ghost.SetActive(false);
            _ghostFollow = false;
            HideHeldMesh();
            return;
        }
        EnsureGhost();
        _ghost.SetActive(true);
        BoatBuildUtil.FollowGhost(ref _ghostFollow, ref _ghostPos, ref _ghostPosVel, ref _ghostRot, pos, rot);
        _ghost.transform.SetPositionAndRotation(_ghostPos, _ghostRot);
        HideHeldMesh();
    }

    bool TryPose(out Vector3 pos, out Quaternion rot)
    {
        rot = _poseRot * Quaternion.Euler(_pitch, _yaw, _roll);
        return BoatBuildUtil.TryPlacePose(Owner, 6f, _localSize, Vector3.zero, rot, out pos, spanNeighbors: true);
    }

    void Place(Vector3 pos, Quaternion rot)
    {
        if (_ghost != null && _ghost.activeInHierarchy)
        {
            pos = _ghost.transform.position;
            rot = _ghost.transform.rotation;
        }
        BoatBuildHud.Clear();
        ClearGhost();
        Inventory?.ClearEquippedKeepObject();
        ReleaseToWorld(pos, rot, Vector3.zero);
    }

    protected override void OnDropped()
    {
        base.OnDropped();
        Invoke(nameof(ReleaseAfterDrop), 0f);
    }

    void ReleaseAfterDrop()
    {
        if (_released || this == null)
            return;
        Vector3 vel = Vector3.zero;
        var rb = GetComponent<Rigidbody>();
        if (rb != null)
            vel = rb.linearVelocity;
        ReleaseToWorld(transform.position, transform.rotation, vel);
    }

    void ReleaseToWorld(Vector3 pos, Quaternion rot, Vector3 velocity)
    {
        if (_released)
            return;
        _released = true;
        enabled = false;
        ClearGhost();
        transform.SetParent(null, true);
        transform.SetPositionAndRotation(pos, rot);
        for (int i = 0; i < _parts.Count && i < _localPos.Count; i++)
        {
            var p = _parts[i];
            if (p == null)
                continue;
            p.transform.localPosition = _localPos[i];
            p.transform.localRotation = _localRot[i];
        }
        gameObject.SetActive(true);

        var pieces = new List<BoatPiece>(_parts);
        for (int i = 0; i < pieces.Count; i++)
        {
            var p = pieces[i];
            if (p == null)
                continue;
            p.transform.SetParent(null, true);
            p.WakeInWorld(false);
        }
        BoatRope.UnparentOwned(pieces);

        Physics.SyncTransforms();
        BoatBuildUtil.SettleCluster(pieces);
        Physics.SyncTransforms();

        for (int i = 0; i < pieces.Count; i++)
        {
            if (pieces[i] == null)
                continue;
            var nails = pieces[i].Nails;
            for (int n = 0; n < nails.Count; n++)
                nails[n]?.RebuildJoint();
        }
        BoatRope.RebuildOwned(pieces);
        BoatBuildUtil.NudgeClusterFromActors(pieces);
        BoatBuildUtil.IgnoreActorsBriefly(pieces, 1.15f);
        Physics.SyncTransforms();

        for (int i = 0; i < pieces.Count; i++)
        {
            if (pieces[i] != null)
                pieces[i].ResumePhysics(velocity);
        }

        var rb = GetComponent<Rigidbody>();
        if (rb != null)
            rb.isKinematic = true;
        var cols = GetComponents<Collider>();
        for (int i = 0; i < cols.Length; i++)
            if (cols[i] != null)
                cols[i].enabled = false;
        Destroy(gameObject);
    }

    void EnsureGhost()
    {
        if (_ghost != null)
            return;
        _ghost = new GameObject("Ghost");
        _ghost.transform.SetPositionAndRotation(transform.position, transform.rotation);
        var filters = GetComponentsInChildren<MeshFilter>(true);
        for (int i = 0; i < filters.Length; i++)
        {
            var f = filters[i];
            if (f == null || f.sharedMesh == null)
                continue;
            if (f.transform == transform)
                continue;
            if (!f.gameObject.activeInHierarchy)
                continue;
            if (f.GetComponentInParent<BoatNail>() != null)
                continue;
            var go = new GameObject(f.name);
            go.transform.SetParent(_ghost.transform, false);
            go.transform.localPosition = transform.InverseTransformPoint(f.transform.position);
            go.transform.localRotation = Quaternion.Inverse(transform.rotation) * f.transform.rotation;
            Vector3 parentScale = transform.lossyScale;
            Vector3 worldScale = f.transform.lossyScale;
            go.transform.localScale = new Vector3(
                SafeDiv(worldScale.x, parentScale.x),
                SafeDiv(worldScale.y, parentScale.y),
                SafeDiv(worldScale.z, parentScale.z));
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = f.sharedMesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = BoatVisuals.Ghost;
        }
        BoatVisuals.SetIgnoreRaycast(_ghost);
        _ghost.SetActive(false);
    }

    static float SafeDiv(float a, float b) => Mathf.Abs(b) < 0.0001f ? a : a / b;

    void HideHeldMesh()
    {
        var rs = GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < rs.Length; i++)
        {
            if (rs[i] == null)
                continue;
            if (_ghost != null && rs[i].transform.IsChildOf(_ghost.transform))
                continue;
            if (rs[i] is LineRenderer)
                continue;
            if (rs[i].GetComponentInParent<BoatRope>() != null)
                continue;
            rs[i].enabled = false;
        }
    }

    void ClearGhost()
    {
        if (_ghost != null)
            Destroy(_ghost);
        _ghost = null;
        _ghostFollow = false;
    }

    void OnDestroy()
    {
        ClearGhost();
    }
}
