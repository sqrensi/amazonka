using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Склеивает связанный остров в один rigidbody. При разрыве шва снова даёт кускам свои тела.
/// Склейку нельзя делать из OnCollision — очередь на конец кадра.
/// </summary>
public static class BoatIsland
{
    static readonly List<BoatPiece> Tmp = new List<BoatPiece>(32);
    static readonly HashSet<BoatPiece> Pending = new HashSet<BoatPiece>();
    static Pump _pump;

    public static void Refresh(BoatPiece any)
    {
        if (any == null)
            return;
        Pending.Add(any);
        if (_pump != null)
            return;
        var go = new GameObject("BoatIslandPump");
        Object.DontDestroyOnLoad(go);
        go.hideFlags = HideFlags.HideAndDontSave;
        _pump = go.AddComponent<Pump>();
    }

    internal static void Flush()
    {
        if (Pending.Count == 0)
            return;
        var wait = new List<BoatPiece>(Pending);
        Pending.Clear();
        for (int i = 0; i < wait.Count; i++)
        {
            if (wait[i] == null)
                continue;
            wait[i].CollectIsland(Tmp);
            Weld(Tmp);
        }
    }

    static void Weld(List<BoatPiece> island)
    {
        if (island == null || island.Count == 0)
            return;
        for (int i = island.Count - 1; i >= 0; i--)
        {
            if (island[i] == null)
                island.RemoveAt(i);
        }
        if (island.Count == 0)
            return;

        if (island.Count == 1)
        {
            FreeStrayChildren(island[0], island);
            island[0].MakeFreeBody();
            BoatPiece.SyncCraft(island, island[0]);
            BoatRope.RetargetToRoots(island);
            return;
        }

        BoatPiece lead = BoatPiece.IslandLeaderFrom(island);
        if (lead == null)
            return;
        FreeStrayChildren(lead, island);

        bool dirty = lead.Body == null || lead.IsWeldSlave;
        for (int i = 0; i < island.Count; i++)
        {
            var p = island[i];
            if (p == null || p == lead)
                continue;
            if (!p.IsWeldSlave || p.transform.parent != lead.transform)
            {
                dirty = true;
                break;
            }
        }
        if (!dirty)
        {
            if (!BoatPiece.WaterSimLive())
                lead.WakeForWater();
            BoatPiece.SyncCraft(island, lead);
            return;
        }

        if (AttachNewcomers(lead, island))
            return;

        Vector3 lin = Vector3.zero;
        Vector3 ang = Vector3.zero;
        float w = 0f;
        for (int i = 0; i < island.Count; i++)
        {
            Rigidbody rb = island[i].Body;
            if (rb == null || rb.isKinematic)
                continue;
            float m = rb.mass;
            lin += rb.linearVelocity * m;
            ang += rb.angularVelocity * m;
            w += m;
        }
        if (w > 0.001f)
        {
            lin /= w;
            ang /= w;
        }

        for (int i = 0; i < island.Count; i++)
            island[i].BreakNailJoints();

        BoatRope.DropJointsOnIsland(island);

        lead.MakeFreeBody();
        for (int i = 0; i < island.Count; i++)
        {
            if (island[i] != null && island[i] != lead)
                island[i].AttachAsWeldChild(lead);
        }

        float mass = 0f;
        for (int i = 0; i < island.Count; i++)
            mass += island[i].OwnMass();
        lead.SetWeldMass(mass);
        Rigidbody body = lead.Body;
        if (body != null)
        {
            if (!BoatPiece.WaterSimLive())
                lead.WakeForWater();
            else if (!body.isKinematic)
            {
                body.linearVelocity = lin;
                body.angularVelocity = ang;
            }
        }

        BoatRope.RetargetToRoots(island);
        BoatPiece.SyncCraft(island, lead);
        Physics.SyncTransforms();
    }

    static bool AttachNewcomers(BoatPiece lead, List<BoatPiece> island)
    {
        if (lead == null || lead.Body == null || lead.IsWeldSlave)
            return false;
        var add = new List<BoatPiece>(8);
        for (int i = 0; i < island.Count; i++)
        {
            var p = island[i];
            if (p == null || p == lead)
                continue;
            if (p.IsWeldSlave && p.transform.parent == lead.transform)
                continue;
            if (p.IsWeldSlave)
                return false;
            add.Add(p);
        }
        if (add.Count == 0)
            return false;

        Rigidbody body = lead.Body;
        BoatBuildUtil.StopMotion(body);
        if (body != null)
        {
            body.maxDepenetrationVelocity = 0.08f;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }
        Vector3 keepPos = lead.transform.position;
        Quaternion keepRot = lead.transform.rotation;
        for (int i = 0; i < add.Count; i++)
        {
            var p = add[i];
            if (p == null)
                continue;
            BoatBuildUtil.StopMotion(p.Body);
            p.AttachAsWeldChild(lead);
        }
        lead.transform.SetPositionAndRotation(keepPos, keepRot);
        if (body != null)
        {
            BoatBuildUtil.StopMotion(body);
            body.maxDepenetrationVelocity = 0.08f;
        }
        float mass = 0f;
        for (int i = 0; i < island.Count; i++)
        {
            if (island[i] != null)
                mass += island[i].OwnMass();
        }
        lead.SetWeldMass(mass);
        if (body != null && !body.isKinematic)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }
        BoatRope.RetargetToRoots(island);
        BoatPiece.SyncCraft(island, lead);
        return true;
    }

    static void FreeStrayChildren(BoatPiece lead, List<BoatPiece> island)
    {
        if (lead == null || island == null)
            return;
        for (int i = lead.transform.childCount - 1; i >= 0; i--)
        {
            var p = lead.transform.GetChild(i).GetComponent<BoatPiece>();
            if (p == null || island.Contains(p))
                continue;
            if (p.Kind == BoatPieceKind.Oar)
            {
                p.ReleaseOarLatch();
                BoatOarStation.AbortIfIsland(p, "Oar came off");
            }
            else
                p.MakeFreeBody();
        }
    }

    class Pump : MonoBehaviour
    {
        void LateUpdate()
        {
            Flush();
        }
    }
}
