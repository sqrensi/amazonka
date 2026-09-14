using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Общий толчок лодки веслом: только если лопасть достаёт воду.
/// </summary>
public static class BoatPaddle
{
    public static float Steer { get; private set; }

    public static void TickSteer(float dt)
    {
        var kb = Keyboard.current;
        float want = 0f;
        if (kb != null && kb.qKey.isPressed)
            want -= 40f;
        if (kb != null && kb.eKey.isPressed)
            want += 40f;
        float speed = want == 0f ? 70f : 95f;
        Steer = Mathf.MoveTowards(Steer, want, speed * dt);
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

    public static bool PieceBladeInWater(BoatPiece piece)
    {
        if (piece == null)
            return false;
        Transform blade = piece.transform.Find("Blade");
        if (blade != null)
        {
            Vector3 p = blade.position;
            if (BoatWater.TryHeight(p, out float y) && p.y <= y + 0.18f)
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
            var p = hits[i].collider != null ? hits[i].collider.GetComponentInParent<BoatPiece>() : null;
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
