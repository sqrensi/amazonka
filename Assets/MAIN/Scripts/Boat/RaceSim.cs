using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Точка симуляции гонки. Сейчас всё локально (host = этот клиент).
/// Позже: host/server применяет те же методы по RPC, клиенты только шлют запросы.
///
/// 4–5 игроков: у каждого RaceActor.Id из лобби. Спавн акулы, HP, укусы —
/// только Authority. Оружие шлёт RequestDamage(attackerId, targetId, ...).
/// Финиш и drown — per-actor, таймер общий. Seed раунда общий, чтобы
/// «акула в этом заезде есть/нет» совпало у всех без отдельного сообщения.
/// </summary>
public static class RaceSim
{
    public static bool HasAuthority { get; set; } = true;
    public static int RoundSeed { get; private set; }
    public static float RaceElapsed { get; set; }
    public static ushort NextNetId { get; private set; } = 1;

    public static void BeginRound(int seed)
    {
        RoundSeed = seed == 0 ? 1 : seed;
        RaceElapsed = 0f;
        NextNetId = 1;
        RaceRoster.Clear();
        KillNoticeHUD.ResetKills();
        KillStyle.ResetRound();
    }

    public static ushort AllocId()
    {
        ushort id = NextNetId;
        NextNetId++;
        return id;
    }

    public static void RequestDamage(ushort attackerId, IDamageable target, float amount, Vector3 point, Vector3 dir, string weapon)
    {
        if (target == null || target.IsDead)
            return;
        if (!HasAuthority)
            return;
        ApplyDamage(attackerId, target, amount, point, dir, weapon);
    }

    public static void ApplyDamage(ushort attackerId, IDamageable target, float amount, Vector3 point, Vector3 dir, string weapon)
    {
        if (target == null || target.IsDead)
            return;
        target.TakeDamage(amount, point, dir);
        PickupPromptHUD.MarkHit();
        if (target.IsDead)
            AnnounceKill(attackerId, target, weapon);
    }

    public static void AnnounceKill(ushort attackerId, IDamageable target, string weapon)
    {
        if (target == null)
            return;
        var actor = RaceRoster.Find(attackerId);
        if (actor == null)
            actor = RaceRoster.Local();
        string name = actor != null && actor.WeaponName != null ? actor.WeaponName : weapon;
        Vector3 pos = Vector3.zero;
        var comp = target as Component;
        if (comp != null)
            pos = comp.transform.position;
        float dist = actor != null ? Vector3.Distance(actor.AimOrigin, pos) : 0f;
        var style = KillStyle.Evaluate(actor, pos);
        KillNoticeHUD.Show(new KillNoticeHUD.Report
        {
            killIndex = KillNoticeHUD.NextKillIndex(),
            target = KillName(target),
            weapon = string.IsNullOrEmpty(name) ? weapon : name,
            distanceMeters = dist,
            tags = style.tags,
            points = style.points
        });
    }

    static string KillName(IDamageable target)
    {
        if (target is RiverShark shark)
            return shark.DisplayName;
        if (target is HouseBombBird)
            return "Bird";
        if (target is HouseBombEgg)
            return "Egg";
        return "Target";
    }
}

/// <summary>
/// Участники заезда. Локально один игрок; в мультиплеере 4–5 записей.
/// </summary>
public static class RaceRoster
{
    static readonly List<RaceActor> Actors = new List<RaceActor>(8);

    public static IReadOnlyList<RaceActor> All => Actors;

    public static void Clear()
    {
        Actors.Clear();
    }

    public static void Register(RaceActor actor)
    {
        if (actor == null || Actors.Contains(actor))
            return;
        Actors.Add(actor);
    }

    public static void Unregister(RaceActor actor)
    {
        Actors.Remove(actor);
    }

    public static RaceActor Find(ushort id)
    {
        for (int i = 0; i < Actors.Count; i++)
        {
            if (Actors[i] != null && Actors[i].Id == id)
                return Actors[i];
        }
        return null;
    }

    public static RaceActor Local()
    {
        for (int i = 0; i < Actors.Count; i++)
        {
            if (Actors[i] != null && Actors[i].IsLocal)
                return Actors[i];
        }
        return Actors.Count > 0 ? Actors[0] : null;
    }

    public static RaceActor PreyNear(Vector3 world)
    {
        RaceActor best = null;
        float bestD = float.PositiveInfinity;
        for (int i = 0; i < Actors.Count; i++)
        {
            var a = Actors[i];
            if (a == null || a.AimOrigin == Vector3.zero)
                continue;
            float d = (a.AimOrigin - world).sqrMagnitude;
            if (d < bestD)
            {
                bestD = d;
                best = a;
            }
        }
        return best;
    }
}

public class RaceActor : MonoBehaviour
{
    public ushort Id { get; private set; }
    public bool IsLocal { get; private set; }
    public BoatPiece Craft { get; set; }
    public string WeaponName { get; set; }

    public Vector3 AimOrigin
    {
        get
        {
            var cam = GetComponentInChildren<Camera>();
            if (cam != null)
                return cam.transform.position;
            return transform.position + Vector3.up * 1.4f;
        }
    }

    public static RaceActor BindLocal(HorrorFirstPersonController player)
    {
        if (player == null)
            return null;
        var actor = player.GetComponent<RaceActor>();
        if (actor == null)
            actor = player.gameObject.AddComponent<RaceActor>();
        actor.IsLocal = true;
        if (actor.Id == 0)
            actor.Id = RaceSim.AllocId();
        KillStyle.On(player);
        RaceRoster.Register(actor);
        return actor;
    }

    void OnDestroy()
    {
        RaceRoster.Unregister(this);
    }
}
