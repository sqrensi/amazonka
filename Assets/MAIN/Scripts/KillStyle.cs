using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Стиль убийства в духе How to Fish: спин, флик, прыжок. Очки раунда.
/// </summary>
public class KillStyle : MonoBehaviour
{
    public struct Tag
    {
        public string name;
        public int points;
        public Color color;
    }

    public struct Result
    {
        public Tag[] tags;
        public int points;
    }

    const int MaxSamples = 48;
    const float Window = 0.95f;

    static KillStyle _live;
    static readonly List<Tag> RoundLog = new List<Tag>(16);
    static float _lastKillAt = -99f;

    readonly float[] _yaw = new float[MaxSamples];
    readonly float[] _at = new float[MaxSamples];
    int _count;
    int _head;
    float _peakFlick;

    public static int RoundPoints { get; private set; }

    public static IReadOnlyList<Tag> RoundTags => RoundLog;

    public static void ResetRound()
    {
        RoundPoints = 0;
        RoundLog.Clear();
        _lastKillAt = -99f;
        if (_live != null)
            _live._peakFlick = 0f;
    }

    public static KillStyle On(HorrorFirstPersonController player)
    {
        if (player == null)
            return null;
        var s = player.GetComponent<KillStyle>();
        if (s == null)
            s = player.gameObject.AddComponent<KillStyle>();
        _live = s;
        return s;
    }

    void OnEnable()
    {
        _live = this;
    }

    void LateUpdate()
    {
        var move = GetComponent<HorrorFirstPersonController>();
        if (move == null)
            return;
        float now = Time.unscaledTime;
        Push(move.YawDegrees, now);
        float flick = Mathf.Abs(move.LookVelocity.x);
        if (flick > _peakFlick)
            _peakFlick = flick;
        _peakFlick = Mathf.MoveTowards(_peakFlick, flick, Time.unscaledDeltaTime * 420f);
    }

    void Push(float yaw, float now)
    {
        _yaw[_head] = yaw;
        _at[_head] = now;
        _head = (_head + 1) % MaxSamples;
        if (_count < MaxSamples)
            _count++;
        while (_count > 2)
        {
            int oldest = Oldest();
            if (now - _at[oldest] <= Window)
                break;
            _count--;
        }
    }

    int Oldest()
    {
        return (_head - _count + MaxSamples) % MaxSamples;
    }

    float NetSpin()
    {
        if (_count < 2)
            return 0f;
        float sum = 0f;
        int n = _count;
        for (int i = 1; i < n; i++)
        {
            int a = (Oldest() + i - 1) % MaxSamples;
            int b = (Oldest() + i) % MaxSamples;
            sum += Mathf.DeltaAngle(_yaw[a], _yaw[b]);
        }
        return Mathf.Abs(sum);
    }

    public static Result Evaluate(RaceActor actor, RiverShark shark)
    {
        Vector3 pos = shark != null ? shark.transform.position : Vector3.zero;
        return Evaluate(actor, pos);
    }

    public static Result Evaluate(RaceActor actor, Vector3 targetPos)
    {
        var tags = new List<Tag>(6);
        var move = actor != null ? actor.GetComponent<HorrorFirstPersonController>() : null;
        var style = move != null ? On(move) : _live;
        float dist = 0f;
        if (actor != null)
            dist = Vector3.Distance(actor.AimOrigin, targetPos);

        if (style != null)
        {
            float spin = style.NetSpin();
            if (spin >= 300f)
                tags.Add(new Tag { name = "360", points = 400, color = new Color(1f, 0.82f, 0.2f) });
            else if (spin >= 155f)
                tags.Add(new Tag { name = "180", points = 180, color = new Color(1f, 0.9f, 0.45f) });
            if (style._peakFlick >= 260f)
                tags.Add(new Tag { name = "FLICK", points = 120, color = new Color(1f, 0.55f, 0.85f) });
        }

        if (move != null && move.AirborneNow)
            tags.Add(new Tag { name = "AIR", points = 150, color = new Color(0.55f, 0.9f, 1f) });

        if (BoatOarStation.Active != null)
            tags.Add(new Tag { name = "FROM THE OAR", points = 110, color = new Color(0.7f, 1f, 0.55f) });

        if (dist > 0.05f && dist < 3.4f)
            tags.Add(new Tag { name = "POINT BLANK", points = 80, color = new Color(1f, 0.45f, 0.35f) });
        else if (dist >= 26f)
            tags.Add(new Tag { name = "LONG SHOT", points = 150, color = new Color(0.55f, 0.75f, 1f) });

        float now = Time.unscaledTime;
        if (now - _lastKillAt < 2.4f)
            tags.Add(new Tag { name = "DOUBLE", points = 220, color = new Color(1f, 0.35f, 0.45f) });
        _lastKillAt = now;

        if (KillNoticeHUD.KillCount == 0)
            tags.Add(new Tag { name = "FIRST BLOOD", points = 50, color = new Color(1f, 0.7f, 0.35f) });

        int pts = 40;
        for (int i = 0; i < tags.Count; i++)
            pts += tags[i].points;
        for (int i = 0; i < tags.Count; i++)
            RoundLog.Add(tags[i]);
        RoundPoints += pts;
        if (style != null)
            style._peakFlick *= 0.25f;
        return new Result { tags = tags.ToArray(), points = pts };
    }
}
