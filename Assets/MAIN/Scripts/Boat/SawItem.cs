using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Пила: линия реза по прицелу, удержание ЛКМ пилит ровно там.
/// </summary>
public class SawItem : HeldItem
{
    const float SawTime = 0.55f;
    LineRenderer _line;
    BoatPiece _target;
    Vector3 _cutPoint;
    float _progress;

    public override void OnEquip()
    {
        base.OnEquip();
        BoatBuildHud.Hint("Aim at the cut  ·  hold LMB to saw", 4f);
    }

    public override void OnUnequip()
    {
        ClearPreview();
        _progress = 0f;
        _target = null;
        base.OnUnequip();
    }

    public override void OnUseStart() { }

    public override void OnUseStop()
    {
        _progress = 0f;
    }

    void Update()
    {
        if (!IsEquipped)
        {
            ClearPreview();
            return;
        }
        if (Cursor.lockState != CursorLockMode.Locked)
        {
            HidePreview();
            _progress = 0f;
            return;
        }

        bool aim = TryCut(out BoatPiece piece, out Vector3 point);
        if (aim)
        {
            ShowPreview(piece, point);
            BoatBuildHud.Hint(_progress > 0.05f
                ? $"Sawing  {Mathf.RoundToInt(Mathf.Clamp01(_progress / SawTime) * 100f)}%"
                : "Hold LMB to saw here", 0.15f);
        }
        else
            HidePreview();

        bool held = !IsUseBlocked && Mouse.current != null && Mouse.current.leftButton.isPressed;
        if (!held || !aim)
        {
            _progress = 0f;
            _target = aim ? piece : null;
            _cutPoint = point;
            return;
        }

        if (piece != _target || (point - _cutPoint).sqrMagnitude > 0.04f * 0.04f)
        {
            _progress = 0f;
            _target = piece;
            _cutPoint = point;
            return;
        }

        _progress += Time.deltaTime;
        if (_progress < SawTime)
            return;

        _progress = 0f;
        _target = null;
        if (piece.TrySaw(point))
            BoatBuildHud.Hint("Cut");
        else
            BoatBuildHud.Hint("Cut closer to the middle of the piece");
    }

    bool TryCut(out BoatPiece piece, out Vector3 point)
    {
        piece = null;
        point = default;
        if (!BoatBuildUtil.Aim(Owner, 4.2f, out RaycastHit hit, true))
            return false;
        piece = hit.collider.GetComponentInParent<BoatPiece>();
        if (piece == null)
        {
            var wood = hit.collider.GetComponentInParent<BoatMaterialItem>();
            if (wood != null)
                piece = wood.TryBecomeWorldPiece();
        }
        if (piece == null)
            return false;
        return piece.TryGetCut(hit.point, out point);
    }

    void ShowPreview(BoatPiece piece, Vector3 point)
    {
        EnsureLine();
        _line.enabled = true;
        int axis = BoatVisuals.LengthAxis(piece.Kind);
        Vector3 size = piece.PieceSize;
        Vector3 center = piece.ColliderCenter;
        Vector3 local = piece.transform.InverseTransformPoint(point);
        local[axis] = Mathf.Clamp(local[axis], center[axis] - size[axis] * 0.5f + 0.01f, center[axis] + size[axis] * 0.5f - 0.01f);

        int count = piece.Kind == BoatPieceKind.Plank ? 16 : 28;
        _line.positionCount = count;
        _line.loop = true;

        Vector3 planeCenter = center;
        planeCenter[axis] = local[axis];
        Vector3 worldCenter = piece.transform.TransformPoint(planeCenter);
        Camera cam = BoatBuildUtil.Cam(Owner);
        Vector3 toCam = cam != null ? (cam.transform.position - worldCenter).normalized : Vector3.up;
        Vector3 axisWorld = piece.transform.TransformDirection(AxisUnit(axis));

        for (int i = 0; i < count; i++)
        {
            Vector3 lp = OutlinePoint(piece.Kind, size, center, axis, local[axis], i, count);
            Vector3 wp = piece.transform.TransformPoint(lp);
            Vector3 radial = Vector3.ProjectOnPlane(wp - worldCenter, axisWorld);
            if (radial.sqrMagnitude < 0.0001f)
                radial = Vector3.ProjectOnPlane(toCam, axisWorld);
            wp += radial.normalized * 0.018f + toCam * 0.03f;
            _line.SetPosition(i, wp);
        }
    }

    static Vector3 AxisUnit(int axis)
    {
        var v = Vector3.zero;
        v[axis] = 1f;
        return v;
    }

    static Vector3 OutlinePoint(BoatPieceKind kind, Vector3 size, Vector3 center, int axis, float cut, int i, int n)
    {
        var p = center;
        p[axis] = cut;
        if (kind == BoatPieceKind.Plank)
        {
            int u = axis == 2 ? 0 : (axis == 1 ? 0 : 1);
            int v = axis == 0 ? 2 : (axis == 1 ? 2 : 1);
            float hu = size[u] * 0.5f;
            float hv = size[v] * 0.5f;
            float per = hu * 4f + hv * 4f;
            float d = per * (i / (float)n);
            float e0 = hu * 2f;
            float e1 = e0 + hv * 2f;
            float e2 = e1 + hu * 2f;
            if (d < e0)
            {
                p[u] = -hu + d;
                p[v] = hv;
            }
            else if (d < e1)
            {
                p[u] = hu;
                p[v] = hv - (d - e0);
            }
            else if (d < e2)
            {
                p[u] = hu - (d - e1);
                p[v] = -hv;
            }
            else
            {
                p[u] = -hu;
                p[v] = -hv + (d - e2);
            }
            return p;
        }

        float ang = (i / (float)n) * Mathf.PI * 2f;
        float rx = size.x * 0.5f;
        float ry = axis == 1 ? size.z * 0.5f : size.y * 0.5f;
        if (axis == 2)
        {
            p.x = center.x + Mathf.Cos(ang) * rx;
            p.y = center.y + Mathf.Sin(ang) * ry;
        }
        else if (axis == 1)
        {
            p.x = center.x + Mathf.Cos(ang) * rx;
            p.z = center.z + Mathf.Sin(ang) * ry;
        }
        else
        {
            p.y = center.y + Mathf.Cos(ang) * ry;
            p.z = center.z + Mathf.Sin(ang) * rx;
        }
        return p;
    }

    void EnsureLine()
    {
        if (_line != null)
            return;
        var go = new GameObject("SawPreview");
        BoatVisuals.SetIgnoreRaycast(go);
        _line = go.AddComponent<LineRenderer>();
        _line.positionCount = 16;
        _line.loop = true;
        _line.startWidth = 0.042f;
        _line.endWidth = 0.042f;
        _line.useWorldSpace = true;
        _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _line.material = BoatVisuals.SawMark;
        _line.numCapVertices = 4;
        _line.numCornerVertices = 4;
        _line.textureMode = LineTextureMode.Stretch;
    }

    void HidePreview()
    {
        if (_line != null)
            _line.enabled = false;
    }

    void ClearPreview()
    {
        if (_line != null)
            Destroy(_line.gameObject);
        _line = null;
    }

    void OnDestroy()
    {
        ClearPreview();
    }
}
