using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Прочность собранной лодки: стыки, материал, износ в воде, удары, затопление.
/// </summary>
public static class BoatHull
{
    static readonly List<BoatPiece> Scratch = new List<BoatPiece>(32);
    static readonly Collider[] Hits = new Collider[16];

    public static void Tick(BoatPiece piece, float dt)
    {
        if (piece == null || dt <= 0f)
            return;
        var lead = piece.IslandLeader();
        if (lead == null)
            return;
        if (piece == lead)
            Simulate(lead, dt);
    }

    static float PushGuardUntil;

    public static void GuardPush(float seconds)
    {
        PushGuardUntil = Time.time + Mathf.Max(0.2f, seconds);
    }

    public static void Impact(BoatPiece piece, Collision collision)
    {
        if (piece == null || collision == null)
            return;
        if (Time.time < PushGuardUntil)
            return;
        var other = collision.collider != null
            ? collision.collider.GetComponentInParent<BoatPiece>()
            : null;
        if (other != null)
        {
            piece.CollectIsland(Scratch);
            for (int i = 0; i < Scratch.Count; i++)
            {
                if (Scratch[i] == other)
                    return;
            }
        }
        if (BoatBuildUtil.IsActorCollider(collision.collider))
            return;
        if (collision.collider != null && collision.collider.GetComponentInParent<BoatWater>() != null)
            return;

        float speed = collision.relativeVelocity.magnitude;
        float impulse = collision.impulse.magnitude;
        float hit = Mathf.Max(speed * 0.028f, impulse * 0.008f);
        if (speed > 6.5f || hit > 0.16f)
            BoatOarStation.AbortIfIsland(piece, "Thrown from the oar");
        if (hit < 0.045f)
            return;
        hit = Mathf.Clamp(hit, 0.045f, 0.22f);

        piece.Strain = Mathf.Clamp01(piece.Strain + hit * 0.35f);
        var lead = piece.IslandLeader();
        if (lead != null && lead != piece)
            lead.Strain = Mathf.Clamp01(lead.Strain + hit * 0.12f);
        if (lead != null && hit > 0.17f)
            lead.HullFlood = Mathf.Clamp01(lead.HullFlood + (hit - 0.17f) * 0.12f);

        if (hit > 0.19f)
            FailWeakestNail(piece, 0.28f);
        if (IsWood(piece.Kind))
            WoundWood(piece, hit);
        if (hit > 0.2f)
            BoatBuildHud.Hint("Impact — seams opening", 1.6f);
        else if (hit > 0.11f)
            BoatBuildHud.Hint("Hull shudders", 1.2f);
    }

    public static void JointBroke(BoatPiece host)
    {
        if (host == null)
            return;
        var nails = host.Nails;
        for (int i = nails.Count - 1; i >= 0; i--)
        {
            var n = nails[i];
            if (n == null || !n.Driven || n.Joint != null)
                continue;
            Breach(n);
            return;
        }
    }

    public static string PlayerStatus(GameObject player)
    {
        if (player == null)
            return "";
        var piece = PieceUnder(player);
        if (piece == null && BoatOarStation.Active != null)
            piece = BoatOarStation.Active.Oar;
        if (piece == null)
            return "";
        var lead = piece.IslandLeader();
        if (lead == null || !lead.HullIsCraft)
            return "";
        int s = Mathf.RoundToInt(lead.HullStrength * 100f);
        int f = Mathf.RoundToInt(lead.HullFlood * 100f);
        if (f < 4)
            return $"Hull {s}%";
        if (f < 40)
            return $"Hull {s}%   taking water {f}%";
        if (f < 75)
            return $"Hull {s}%   flooding {f}%";
        return $"Hull {s}%   sinking {f}%";
    }

    static BoatPiece PieceUnder(GameObject player)
    {
        Vector3 origin = player.transform.position + Vector3.up * 0.35f;
        int n = Physics.OverlapSphereNonAlloc(origin, 0.55f, Hits, ~0, QueryTriggerInteraction.Ignore);
        BoatPiece best = null;
        float bestY = float.MaxValue;
        for (int i = 0; i < n; i++)
        {
            var p = Hits[i] != null ? BoatPart.FromCollider(Hits[i]) : null;
            if (p == null)
                continue;
            float y = Mathf.Abs(BoatBuildUtil.ClosestPoint(Hits[i], origin).y - player.transform.position.y);
            if (y < bestY)
            {
                bestY = y;
                best = p;
            }
        }
        return best;
    }

    static void Simulate(BoatPiece lead, float dt)
    {
        lead.CollectIsland(Scratch);
        int hull = 0;
        int nails = 0;
        int ropes = 0;
        float mat = 0f;
        float strain = 0f;
        bool wet = false;
        Vector3 centroid = Vector3.zero;
        Vector3 vel = Vector3.zero;
        float ang = 0f;
        int velN = 0;

        for (int i = 0; i < Scratch.Count; i++)
        {
            var p = Scratch[i];
            if (p == null)
                continue;
            if (p.Kind != BoatPieceKind.Oar)
            {
                hull++;
                mat += MaterialScore(p.Kind);
                strain += p.Strain;
                centroid += p.transform.position;
            }
            else
                strain += p.Strain * 0.35f;
            for (int n = 0; n < p.Nails.Count; n++)
            {
                if (p.Nails[n] != null && p.Nails[n].Driven)
                    nails++;
            }
            if (BoatRope.IsTied(p))
                ropes++;
            if (BoatWater.TryHeight(p.transform.position, out float wy) && p.transform.position.y < wy + 0.45f)
                wet = true;
            if (p.Body != null)
            {
                vel += p.Body.linearVelocity;
                ang += p.Body.angularVelocity.magnitude;
                velN++;
            }
        }
        nails /= 2;
        if (velN > 0)
        {
            vel /= velN;
            ang /= velN;
        }
        if (hull <= 0)
            hull = 1;

        float nailNeed = Mathf.Max(1f, hull * 0.9f);
        float joints = Mathf.Clamp01((nails + ropes * 0.35f) / nailNeed);
        float material = mat / hull;
        if (hull > 1)
            centroid /= hull;
        float spread = 0f;
        int spreadN = 0;
        for (int i = 0; i < Scratch.Count; i++)
        {
            var p = Scratch[i];
            if (p == null || p.Kind == BoatPieceKind.Oar)
                continue;
            spread += (p.transform.position - centroid).sqrMagnitude;
            spreadN++;
        }
        float rms = spreadN > 0 ? Mathf.Sqrt(spread / spreadN) : 0f;
        float compact = Mathf.Clamp01(1.2f - rms / 2.4f);

        bool craft = hull >= 1;
        lead.HullIsCraft = craft;
        float build = hull >= 2
            ? Mathf.Clamp01(0.55f * joints + 0.25f * material + 0.2f * compact)
            : 0.42f * material;

        float avgStrain = strain / Mathf.Max(1, Scratch.Count);
        float strength = Mathf.Clamp01(build * (1f - avgStrain * 0.55f));

        float speed2 = vel.sqrMagnitude;
        float wear = dt * (0.000002f + 0.000012f * speed2 + 0.000008f * ang * ang);
        wear *= Mathf.Lerp(1.35f, 0.7f, joints);
        if (!wet)
            wear *= 0.08f;
        wear *= Mathf.Lerp(1.2f, 0.85f, material);
        if (hull <= 1)
            wear *= 3.2f;

        for (int i = 0; i < Scratch.Count; i++)
        {
            var p = Scratch[i];
            if (p == null)
                continue;
            float k = p.Kind == BoatPieceKind.Oar ? 0.4f : 1f;
            if (p.HullCracked)
                k *= 1.6f;
            p.Strain = Mathf.Clamp01(p.Strain + wear * k);
        }

        float leak = LeakRate(strength, lead.HullFlood);
        if (hull <= 1)
            leak += wet ? (WoodLeak(Scratch) + 0.0016f) : 0f;
        else if (hull == 2)
            leak += wet ? 0.00055f * (1.1f - joints) : 0f;
        if (AnyCracked(Scratch))
            leak += wet ? 0.0014f : 0f;
        if (wet && craft)
            lead.HullFlood = Mathf.Clamp01(lead.HullFlood + leak * dt);
        else if (!wet && lead.HullFlood > 0f)
            lead.HullFlood = Mathf.Max(0f, lead.HullFlood - dt * 0.02f);

        float lift = 1f - lead.HullFlood * 0.92f;
        float sink = lead.HullFlood * 16f;
        for (int i = 0; i < Scratch.Count; i++)
        {
            var p = Scratch[i];
            if (p == null)
                continue;
            p.HullLift = lift;
            p.HullSink = sink;
            p.HullStrength = strength;
            p.HullFlood = lead.HullFlood;
            p.HullIsCraft = craft;
        }

        float breakF = Mathf.Lerp(1600f, 18000f, strength);
        float breakT = Mathf.Lerp(400f, 5200f, strength);
        for (int i = 0; i < Scratch.Count; i++)
        {
            var p = Scratch[i];
            if (p == null)
                continue;
            for (int n = 0; n < p.Nails.Count; n++)
                p.Nails[n]?.SetBreakLimit(breakF, breakT);
        }

        if (craft && wet && strength < 0.22f && Random.value < dt * 0.025f)
            FailWeakestNail(lead, 1f);
        if (craft && lead.HullFlood > 0.75f && Random.value < dt * 0.04f)
            FailWeakestNail(lead, 1f);
    }

    public static bool IsRuined(BoatPiece lead)
    {
        if (lead == null)
            return true;
        if (lead.HullFlood >= 0.88f)
            return true;
        lead.CollectIsland(Scratch);
        int hull = 0;
        for (int i = 0; i < Scratch.Count; i++)
        {
            var p = Scratch[i];
            if (p != null && p.Kind != BoatPieceKind.Oar)
                hull++;
        }
        return hull < 1;
    }

    public static int HullBodyCount(BoatPiece lead)
    {
        if (lead == null)
            return 0;
        lead.CollectIsland(Scratch);
        int n = 0;
        for (int i = 0; i < Scratch.Count; i++)
        {
            if (Scratch[i] != null && Scratch[i].Kind != BoatPieceKind.Oar)
                n++;
        }
        return n;
    }

    public static int HullPieceCount(BoatPiece lead)
    {
        if (lead == null)
            return 0;
        lead.CollectIsland(Scratch);
        int n = 0;
        for (int i = 0; i < Scratch.Count; i++)
        {
            if (Scratch[i] != null)
                n++;
        }
        return n;
    }

    static float LeakRate(float strength, float flood)
    {
        if (strength >= 0.52f)
            return 0f;
        float fail = Mathf.InverseLerp(0.52f, 0.08f, strength);
        float rate = Mathf.Pow(fail, 2.4f) * 0.002f;
        if (flood > 0.2f && strength < 0.32f)
            rate += flood * 0.0009f;
        return rate;
    }

    static float MaterialScore(BoatPieceKind kind)
    {
        switch (kind)
        {
            case BoatPieceKind.Log: return 1f;
            case BoatPieceKind.Barrel: return 0.82f;
            case BoatPieceKind.Oar: return 0.4f;
            default: return 0.64f;
        }
    }

    static void FailWeakestNail(BoatPiece around, float chance)
    {
        if (around == null || Random.value > chance)
            return;
        around.CollectIsland(Scratch);
        BoatNail worst = null;
        float worstStrain = -1f;
        for (int i = 0; i < Scratch.Count; i++)
        {
            var p = Scratch[i];
            if (p == null)
                continue;
            for (int n = 0; n < p.Nails.Count; n++)
            {
                var nail = p.Nails[n];
                if (nail == null || !nail.Driven)
                    continue;
                float s = p.Strain;
                if (nail.B != null)
                    s = Mathf.Max(s, nail.B.Strain);
                if (s >= worstStrain)
                {
                    worstStrain = s;
                    worst = nail;
                }
            }
        }
        if (worst != null)
            Breach(worst);
    }

    static void Breach(BoatNail nail)
    {
        if (nail == null)
            return;
        var a = nail.A;
        var lead = a != null ? a.IslandLeader() : null;
        if (lead != null)
        {
            lead.HullFlood = Mathf.Clamp01(lead.HullFlood + 0.012f);
            lead.Strain = Mathf.Clamp01(lead.Strain + 0.04f);
        }
        if (a != null)
            a.Strain = Mathf.Clamp01(a.Strain + 0.06f);
        BoatBuildHud.Hint("Hull breaking up", 1.8f);
        Hurt(0.32f);
        nail.SplitSeam();
    }

    public static void SharkBite(BoatPiece craft, Vector3 from, float power)
    {
        if (craft == null)
            return;
        var lead = craft.IslandLeader() ?? craft;
        lead.CollectIsland(Scratch);
        BoatPiece pick = null;
        float best = float.PositiveInfinity;
        int hull = 0;
        for (int i = 0; i < Scratch.Count; i++)
        {
            var p = Scratch[i];
            if (p == null || p.Kind == BoatPieceKind.Oar)
                continue;
            hull++;
            if (p == lead && Scratch.Count > 2)
                continue;
            if (p.HoldsOar() && Scratch.Count > 2)
                continue;
            float d = (p.transform.position - from).sqrMagnitude;
            if (d < best)
            {
                best = d;
                pick = p;
            }
        }
        if (pick == null)
        {
            for (int i = 0; i < Scratch.Count; i++)
            {
                var p = Scratch[i];
                if (p == null || p.Kind == BoatPieceKind.Oar)
                    continue;
                float d = (p.transform.position - from).sqrMagnitude;
                if (d < best)
                {
                    best = d;
                    pick = p;
                }
            }
        }
        if (pick == null)
            return;
        float hit = Mathf.Clamp(power, 0.05f, 0.35f);
        pick.Strain = Mathf.Clamp01(pick.Strain + hit * 5.5f);
        lead.Strain = Mathf.Clamp01(lead.Strain + hit * 2.8f);
        FailWeakestNail(lead, 0.7f);
        if (pick.Strain < 0.92f)
        {
            if (hull <= 3)
                lead.HullFlood = Mathf.Clamp01(lead.HullFlood + 0.008f);
            return;
        }
        RipPiece(pick, from);
        if (hull <= 2)
            lead.HullFlood = Mathf.Clamp01(lead.HullFlood + 0.02f);
    }

    public static void RipPiece(BoatPiece piece, Vector3 from)
    {
        if (piece == null)
            return;
        var lead = piece.IslandLeader() ?? piece;
        lead.CollectIsland(Scratch);
        BoatPiece stay = null;
        for (int i = 0; i < Scratch.Count; i++)
        {
            var p = Scratch[i];
            if (p == null || p == piece)
                continue;
            if (p.Kind != BoatPieceKind.Oar)
                stay = p;
        }
        var nails = new List<BoatNail>(piece.Nails);
        for (int i = 0; i < nails.Count; i++)
        {
            if (nails[i] != null)
                nails[i].SplitSeam();
        }
        piece.MakeFreeBody();
        if (piece.Body != null)
        {
            BoatRope.DropJointsOn(piece.Body);
            Vector3 away = piece.transform.position - from;
            away.y = 0.15f;
            if (away.sqrMagnitude < 0.01f)
                away = Vector3.up + Random.onUnitSphere;
            piece.Body.AddForce(away.normalized * 3.2f + Vector3.down * 0.4f, ForceMode.VelocityChange);
            piece.Body.AddTorque(Random.onUnitSphere * 1.4f, ForceMode.VelocityChange);
        }
        if (stay != null)
            BoatIsland.Refresh(stay.IslandLeader() ?? stay);
        BoatIsland.Refresh(piece);
        Hurt(0.28f);
        BoatBuildHud.Hint("Shark took a piece", 1.8f);
    }

    static void WoundWood(BoatPiece piece, float hit)
    {
        if (piece == null || !IsWood(piece.Kind))
            return;
        var lead = piece.IslandLeader() ?? piece;
        lead.CollectIsland(Scratch);
        int hull = 0;
        for (int i = 0; i < Scratch.Count; i++)
        {
            if (Scratch[i] != null && Scratch[i].Kind != BoatPieceKind.Oar)
                hull++;
        }
        float plank = piece.Kind == BoatPieceKind.Plank ? 1.45f : 1f;
        float small = hull <= 1 ? 1.7f : hull == 2 ? 1.2f : 1f;
        piece.Strain = Mathf.Clamp01(piece.Strain + hit * 0.85f * plank);
        lead.HullFlood = Mathf.Clamp01(lead.HullFlood + hit * 0.12f * plank * small);
        if (hit < 0.08f && piece.Strain < 0.48f)
            return;
        bool first = !piece.HullCracked;
        piece.HullCracked = true;
        if (first)
        {
            Hurt(0.3f);
            BoatBuildHud.Hint(piece.Kind == BoatPieceKind.Log ? "Log stove in — taking water" : "Plank split — taking water", 1.8f);
            FailWeakestNail(piece, 0.55f);
            var rb = piece.Body;
            if (rb != null && !rb.isKinematic)
                rb.AddTorque(Random.onUnitSphere * (2.4f * plank), ForceMode.VelocityChange);
        }
        if (piece.Strain >= 0.78f)
        {
            lead.HullFlood = Mathf.Clamp01(lead.HullFlood + 0.05f * plank);
            Hurt(0.36f);
        }
    }

    static bool IsWood(BoatPieceKind kind)
    {
        return kind == BoatPieceKind.Plank || kind == BoatPieceKind.Log;
    }

    static bool AnyCracked(List<BoatPiece> island)
    {
        for (int i = 0; i < island.Count; i++)
        {
            if (island[i] != null && island[i].HullCracked)
                return true;
        }
        return false;
    }

    static float WoodLeak(List<BoatPiece> island)
    {
        float extra = 0f;
        for (int i = 0; i < island.Count; i++)
        {
            var p = island[i];
            if (p == null || !IsWood(p.Kind))
                continue;
            extra += p.Kind == BoatPieceKind.Plank ? 0.0012f : 0.00085f;
            extra += p.Strain * 0.0022f;
            if (p.HullCracked)
                extra += 0.0024f;
        }
        return extra;
    }

    static void Hurt(float amount)
    {
        if (BoatRaceMode.Current == null || BoatRaceMode.Current.CurrentPhase != BoatRaceMode.Phase.Race)
            return;
        if (BoatRaceHud.Active != null)
            BoatRaceHud.Active.PulseHurt(amount);
    }
}
