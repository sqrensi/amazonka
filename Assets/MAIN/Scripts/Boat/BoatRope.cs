using UnityEngine;

/// <summary>
/// Верёвка между двумя деталями (пружина + линия). Живёт на отдельном объекте,
/// якоря — дочерние трансформы тел, поэтому подбор доски не отвязывает канат.
/// </summary>
public class BoatRope : MonoBehaviour
{
    static readonly System.Collections.Generic.List<BoatRope> All = new System.Collections.Generic.List<BoatRope>();

    public Rigidbody BodyA;
    public Rigidbody BodyB;
    public Transform AnchorA;
    public Transform AnchorB;
    SpringJoint _joint;
    LineRenderer _line;

    public static bool IsTied(Component piece)
    {
        if (piece == null)
            return false;
        Transform t = piece.transform;
        for (int i = All.Count - 1; i >= 0; i--)
        {
            var r = All[i];
            if (r == null)
            {
                All.RemoveAt(i);
                continue;
            }
            if (r.Owns(t))
                return true;
        }
        return false;
    }

    public static void ForEachLinked(Transform t, System.Action<BoatPiece> add)
    {
        if (t == null || add == null)
            return;
        for (int i = All.Count - 1; i >= 0; i--)
        {
            var r = All[i];
            if (r == null)
            {
                All.RemoveAt(i);
                continue;
            }
            if (!r.Owns(t))
                continue;
            add(PieceFromAnchor(r.AnchorA));
            add(PieceFromAnchor(r.AnchorB));
        }
    }

    static BoatPiece PieceFromAnchor(Transform a)
    {
        return a != null ? a.GetComponentInParent<BoatPiece>() : null;
    }

    BoatPiece PieceOf(Rigidbody body)
    {
        if (body == null)
            return null;
        var p = body.GetComponent<BoatPiece>();
        if (p != null)
            return p;
        return body.GetComponentInChildren<BoatPiece>();
    }

    public bool Owns(Transform t)
    {
        if (t == null)
            return false;
        if (BodyA != null && (t == BodyA.transform || t.IsChildOf(BodyA.transform) || BodyA.transform.IsChildOf(t)))
            return true;
        if (BodyB != null && (t == BodyB.transform || t.IsChildOf(BodyB.transform) || BodyB.transform.IsChildOf(t)))
            return true;
        return false;
    }

    public void Bind(BoatPiece a, Vector3 worldA, BoatPiece b, Vector3 worldB)
    {
        if (a != null && a.Body == null)
            a.MakeFreeBody();
        if (b != null && b.Body == null)
            b.MakeFreeBody();
        BodyA = a != null ? a.IslandRootBody() : null;
        BodyB = b != null ? b.IslandRootBody() : null;
        AnchorA = CreateAnchor(a.transform, worldA, "RopeA");
        AnchorB = CreateAnchor(b.transform, worldB, "RopeB");
        BuildJoint();
        BuildLine();
    }

    void OnEnable()
    {
        if (!All.Contains(this))
            All.Add(this);
    }

    void OnDisable()
    {
        All.Remove(this);
    }

    public static void Retarget(BoatPiece from, BoatPiece to)
    {
        if (from == null || to == null)
            return;
        for (int i = 0; i < All.Count; i++)
        {
            var r = All[i];
            if (r == null)
                continue;
            if (r.BodyA == from.Body)
            {
                r.BodyA = to.Body;
                if (r.AnchorA != null)
                    r.AnchorA.SetParent(to.transform, true);
                r.RebuildJoint();
            }
            if (r.BodyB == from.Body)
            {
                r.BodyB = to.Body;
                if (r.AnchorB != null)
                    r.AnchorB.SetParent(to.transform, true);
                r.RebuildJoint();
            }
        }
    }

    public static void ScoreCut(BoatPiece piece, int axis, float cut, ref int leftScore, ref int rightScore)
    {
        if (piece == null)
            return;
        Transform t = piece.transform;
        for (int i = 0; i < All.Count; i++)
        {
            var r = All[i];
            if (r == null || !r.Owns(t))
                continue;
            Vector3 p = Vector3.zero;
            if (PieceFromAnchor(r.AnchorA) == piece && r.AnchorA != null)
                p = r.AnchorA.position;
            else if (PieceFromAnchor(r.AnchorB) == piece && r.AnchorB != null)
                p = r.AnchorB.position;
            else
                continue;
            if (t.InverseTransformPoint(p)[axis] < cut)
                leftScore++;
            else
                rightScore++;
        }
    }

    public static void ParentOwning(System.Collections.Generic.IList<BoatPiece> parts, Transform root)
    {
        if (parts == null || root == null)
            return;
        for (int i = 0; i < All.Count; i++)
        {
            var r = All[i];
            if (r == null)
                continue;
            if (!OwnsParts(r, parts))
                continue;
            r.transform.SetParent(root, true);
        }
    }

    public static void UnparentOwned(System.Collections.Generic.IList<BoatPiece> parts)
    {
        if (parts == null)
            return;
        for (int i = 0; i < All.Count; i++)
        {
            var r = All[i];
            if (r == null)
                continue;
            if (!OwnsParts(r, parts))
                continue;
            r.transform.SetParent(null, true);
        }
    }

    public static void RebuildOwned(System.Collections.Generic.IList<BoatPiece> parts)
    {
        if (parts == null)
            return;
        for (int i = 0; i < All.Count; i++)
        {
            var r = All[i];
            if (r == null || !OwnsParts(r, parts))
                continue;
            r.RebuildJoint();
        }
    }

    static bool OwnsParts(BoatRope r, System.Collections.Generic.IList<BoatPiece> parts)
    {
        bool a = false;
        bool b = false;
        for (int i = 0; i < parts.Count; i++)
        {
            var p = parts[i];
            if (p == null)
                continue;
            if (PieceFromAnchor(r.AnchorA) == p)
                a = true;
            if (PieceFromAnchor(r.AnchorB) == p)
                b = true;
        }
        return a && b;
    }

    public static void SplitForCut(BoatPiece from, BoatPiece offcut, int axis, float cut, bool keepLeft)
    {
        if (from == null || offcut == null)
            return;
        Transform t = from.transform;
        for (int i = 0; i < All.Count; i++)
        {
            var r = All[i];
            if (r == null)
                continue;
            bool changed = false;
            if (PieceFromAnchor(r.AnchorA) == from && r.AnchorA != null)
            {
                float a = t.InverseTransformPoint(r.AnchorA.position)[axis];
                bool onKeep = keepLeft ? a < cut : a > cut;
                if (!onKeep)
                {
                    r.AnchorA.SetParent(offcut.transform, true);
                    r.BodyA = offcut.IslandRootBody() != null ? offcut.IslandRootBody() : offcut.Body;
                    changed = true;
                }
            }
            if (PieceFromAnchor(r.AnchorB) == from && r.AnchorB != null)
            {
                float b = t.InverseTransformPoint(r.AnchorB.position)[axis];
                bool onKeep = keepLeft ? b < cut : b > cut;
                if (!onKeep)
                {
                    r.AnchorB.SetParent(offcut.transform, true);
                    r.BodyB = offcut.IslandRootBody() != null ? offcut.IslandRootBody() : offcut.Body;
                    changed = true;
                }
            }
            if (changed)
                r.RebuildJoint();
        }
    }

    public static void RetargetToRoots(System.Collections.Generic.IList<BoatPiece> parts)
    {
        if (parts == null)
            return;
        for (int i = 0; i < All.Count; i++)
        {
            var r = All[i];
            if (r == null)
                continue;
            var pa = PieceFromAnchor(r.AnchorA);
            var pb = PieceFromAnchor(r.AnchorB);
            bool touch = false;
            for (int p = 0; p < parts.Count; p++)
            {
                if (parts[p] == pa || parts[p] == pb)
                {
                    touch = true;
                    break;
                }
            }
            if (!touch)
                continue;
            r.DropJoint();
            if (pa != null)
                r.BodyA = pa.IslandRootBody();
            if (pb != null)
                r.BodyB = pb.IslandRootBody();
            r.RebuildJoint();
        }
    }

    public static void DropJointsOn(Rigidbody body)
    {
        if (body == null)
            return;
        for (int i = 0; i < All.Count; i++)
        {
            var r = All[i];
            if (r == null)
                continue;
            if (r.BodyA == body || r.BodyB == body || (r._joint != null && (r._joint.connectedBody == body || r._joint.gameObject == body.gameObject)))
                r.DropJoint();
        }
    }

    public static void DropJointsOnIsland(System.Collections.Generic.IList<BoatPiece> parts)
    {
        if (parts == null)
            return;
        for (int i = 0; i < parts.Count; i++)
        {
            if (parts[i] != null)
                DropJointsOn(parts[i].Body);
        }
    }

    void DropJoint()
    {
        if (_joint != null)
            Object.DestroyImmediate(_joint);
        _joint = null;
    }

    void RebuildJoint()
    {
        DropJoint();
        BuildJoint();
        RestoreVisual();
    }

    void BuildJoint()
    {
        if (BodyA == null || BodyB == null || AnchorA == null || AnchorB == null)
            return;
        if (BodyA == BodyB)
            return;
        _joint = BodyA.gameObject.AddComponent<SpringJoint>();
        _joint.connectedBody = BodyB;
        _joint.autoConfigureConnectedAnchor = false;
        _joint.anchor = BodyA.transform.InverseTransformPoint(AnchorA.position);
        _joint.connectedAnchor = BodyB.transform.InverseTransformPoint(AnchorB.position);
        float dist = Vector3.Distance(AnchorA.position, AnchorB.position);
        _joint.minDistance = dist * 0.85f;
        _joint.maxDistance = dist * 1.08f;
        _joint.spring = 220f;
        _joint.damper = 16f;
        _joint.tolerance = 0.02f;
        _joint.enableCollision = true;
    }

    public void RestoreVisual()
    {
        if (transform.parent == null)
            transform.localScale = Vector3.one;
        EnsureLine();
        if (_line == null)
            return;
        bool carried = false;
        var held = GetComponentInParent<HeldItem>();
        if (held != null && held.IsCarried)
            carried = true;
        _line.enabled = !carried;
        if (carried || AnchorA == null || AnchorB == null)
            return;
        _line.useWorldSpace = true;
        _line.SetPosition(0, AnchorA.position);
        _line.SetPosition(1, AnchorB.position);
    }

    void EnsureLine()
    {
        if (_line == null)
            _line = GetComponent<LineRenderer>();
        if (_line == null)
            BuildLine();
        else
            ApplyLineSettings(_line);
    }

    void BuildLine()
    {
        _line = gameObject.AddComponent<LineRenderer>();
        ApplyLineSettings(_line);
    }

    static void ApplyLineSettings(LineRenderer line)
    {
        line.positionCount = 2;
        line.startWidth = 0.032f;
        line.endWidth = 0.032f;
        line.useWorldSpace = true;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.numCapVertices = 4;
        line.alignment = LineAlignment.View;
        line.textureMode = LineTextureMode.Stretch;
        line.material = BoatVisuals.RopeLine;
        line.enabled = true;
    }

    void LateUpdate()
    {
        RestoreVisual();
        if (_line == null || !_line.enabled || AnchorA == null || AnchorB == null)
            return;
        _line.SetPosition(0, AnchorA.position);
        _line.SetPosition(1, AnchorB.position);

        if (_joint == null && BodyA != null && BodyB != null && BodyA.gameObject.activeInHierarchy && BodyB.gameObject.activeInHierarchy)
            BuildJoint();
    }

    void OnDestroy()
    {
        All.Remove(this);
        if (_joint != null)
            Destroy(_joint);
    }

    static Transform CreateAnchor(Transform parent, Vector3 world, string name)
    {
        var t = new GameObject(name).transform;
        t.SetParent(parent, true);
        t.position = world;
        return t;
    }
}
