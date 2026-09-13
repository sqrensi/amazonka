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
            add(r.PieceOf(r.BodyA));
            add(r.PieceOf(r.BodyB));
        }
    }

    BoatPiece PieceOf(Rigidbody body)
    {
        return body != null ? body.GetComponent<BoatPiece>() : null;
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
        BodyA = a.Body;
        BodyB = b.Body;
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
            if (r.BodyA == piece.Body && r.AnchorA != null)
                p = r.AnchorA.position;
            else if (r.BodyB == piece.Body && r.AnchorB != null)
                p = r.AnchorB.position;
            else
                continue;
            if (t.InverseTransformPoint(p)[axis] < cut)
                leftScore++;
            else
                rightScore++;
        }
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
            if (r.BodyA == from.Body && r.AnchorA != null)
            {
                float a = t.InverseTransformPoint(r.AnchorA.position)[axis];
                bool onKeep = keepLeft ? a < cut : a > cut;
                if (!onKeep)
                {
                    r.BodyA = offcut.Body;
                    r.AnchorA.SetParent(offcut.transform, true);
                    changed = true;
                }
            }
            if (r.BodyB == from.Body && r.AnchorB != null)
            {
                float b = t.InverseTransformPoint(r.AnchorB.position)[axis];
                bool onKeep = keepLeft ? b < cut : b > cut;
                if (!onKeep)
                {
                    r.BodyB = offcut.Body;
                    r.AnchorB.SetParent(offcut.transform, true);
                    changed = true;
                }
            }
            if (changed)
                r.RebuildJoint();
        }
    }

    void RebuildJoint()
    {
        if (_joint != null)
            Destroy(_joint);
        _joint = null;
        BuildJoint();
    }

    void BuildJoint()
    {
        if (BodyA == null || BodyB == null)
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

    void BuildLine()
    {
        _line = gameObject.AddComponent<LineRenderer>();
        _line.positionCount = 2;
        _line.startWidth = 0.028f;
        _line.endWidth = 0.028f;
        _line.material = BoatVisuals.Rope;
        _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    void LateUpdate()
    {
        if (_line == null || AnchorA == null || AnchorB == null)
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
