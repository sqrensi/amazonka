using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;

/// <summary>
/// Общий толчок лодки веслом: только если лопасть достаёт воду.
/// </summary>
public static class BoatPaddle
{
    public static float Steer { get; private set; }

    public static void TickSteer(float dt, float axis)
    {
        float want = Mathf.Clamp(axis, -1f, 1f) * 22f;
        float speed = Mathf.Abs(axis) > 0.05f ? 70f : 50f;
        Steer = Mathf.MoveTowards(Steer, want, speed * dt);
    }

    public static Vector3 Flatten(Vector3 v)
    {
        v.y = 0f;
        return v.sqrMagnitude > 0.0001f ? v.normalized : Vector3.zero;
    }

    public static bool PieceBladeInWater(BoatPiece piece)
    {
        if (piece == null)
            return false;
        Transform blade = piece.transform.Find("Blade");
        if (blade == null)
            return false;
        var rend = blade.GetComponent<Renderer>();
        Vector3[] pts = new Vector3[5];
        if (rend != null)
        {
            Bounds b = rend.bounds;
            pts[0] = b.center;
            pts[1] = new Vector3(b.min.x, b.min.y, b.min.z);
            pts[2] = new Vector3(b.max.x, b.min.y, b.min.z);
            pts[3] = new Vector3(b.min.x, b.min.y, b.max.z);
            pts[4] = new Vector3(b.max.x, b.min.y, b.max.z);
        }
        else
            pts[0] = blade.position;
        for (int i = 0; i < pts.Length; i++)
        {
            if (rend == null && i > 0)
                break;
            if (!BoatWater.HeightAt(pts[i], out float y) && !BoatWater.TryHeight(pts[i], out y))
                continue;
            if (pts[i].y <= y + 0.42f)
                return true;
        }
        return false;
    }

    static readonly List<BoatPiece> Island = new List<BoatPiece>(32);

    public static void PushIsland(BoatPiece any, Vector3 worldForce, ForceMode mode = ForceMode.Force)
    {
        if (any == null || worldForce.sqrMagnitude < 0.0001f)
            return;
        Rigidbody rb = any.IslandRootBody();
        if (rb == null || rb.isKinematic)
            return;
        rb.AddForce(worldForce, mode);
    }

    public static void YawIsland(BoatPiece any, float yawAccel)
    {
        if (any == null || Mathf.Abs(yawAccel) < 0.01f)
            return;
        Rigidbody rb = any.IslandRootBody();
        if (rb == null || rb.isKinematic)
            return;
        rb.AddTorque(Vector3.up * yawAccel, ForceMode.Acceleration);
    }

    public static void CalmRock(BoatPiece any, float k)
    {
        if (any == null || k <= 0f)
            return;
        Rigidbody rb = any.IslandRootBody();
        if (rb == null || rb.isKinematic)
            return;
        Vector3 w = rb.angularVelocity;
        Vector3 yaw = Vector3.Project(w, Vector3.up);
        Vector3 rock = w - yaw;
        rb.AddTorque(-rock * k, ForceMode.Acceleration);
        if (rock.sqrMagnitude > 0.08f)
            rb.angularVelocity = yaw + rock * 0.82f;
    }

    public static void DampIslandYaw(BoatPiece any, float k)
    {
        if (any == null || k <= 0f)
            return;
        Rigidbody rb = any.IslandRootBody();
        if (rb == null || rb.isKinematic)
            return;
        Vector3 yaw = Vector3.Project(rb.angularVelocity, Vector3.up);
        rb.AddTorque(-yaw * k, ForceMode.Acceleration);
    }

    public static Vector3 SteerDir(Vector3 forward)
    {
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f)
            return Vector3.forward;
        return Quaternion.AngleAxis(Steer, Vector3.up) * forward.normalized;
    }

    public static bool ReachesWater(Vector3 a, Vector3 b)
    {
        for (int i = 0; i < 5; i++)
        {
            Vector3 p = Vector3.Lerp(a, b, 0.35f + i * 0.16f);
            if (BoatWater.TryHeight(p, out float y) && p.y <= y + 0.1f)
                return true;
        }
        return false;
    }

    public static BoatPiece HullUnder(GameObject player)
    {
        if (player == null)
            return null;
        Vector3 origin = player.transform.position + Vector3.up * 0.4f;
        var hits = Physics.SphereCastAll(origin, 0.28f, Vector3.down, 1.4f, ~0, QueryTriggerInteraction.Ignore);
        BoatPiece best = null;
        float bestD = float.MaxValue;
        for (int i = 0; i < hits.Length; i++)
        {
            var p = hits[i].collider != null ? BoatPart.FromCollider(hits[i].collider) : null;
            if (p == null || p.Kind == BoatPieceKind.Oar)
                continue;
            if (hits[i].distance < bestD)
            {
                bestD = hits[i].distance;
                best = p;
            }
        }
        return best;
    }

    public static Rigidbody BoatUnder(GameObject player)
    {
        var hull = HullUnder(player);
        return hull != null ? hull.IslandRootBody() : null;
    }

    public static void Push(Rigidbody boat, Vector3 worldDir, Vector3 at, float force, ForceMode mode = ForceMode.Force)
    {
        if (boat == null)
            return;
        worldDir.y = 0f;
        if (worldDir.sqrMagnitude < 0.0001f)
            return;
        worldDir.Normalize();
        Vector3 p = at;
        p.y = boat.worldCenterOfMass.y;
        boat.AddForceAtPosition(worldDir * force, p, mode);
    }
}
