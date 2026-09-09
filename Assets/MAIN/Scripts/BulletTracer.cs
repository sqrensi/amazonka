using UnityEngine;

/// <summary>
/// Визуальный "трассер" выстрела: короткий яркий отрезок (LineRenderer), быстро летящий
/// от дула к точке попадания — читается как летящая пуля. Живёт доли секунды и самоуничтожается.
/// Создаётся целиком из кода (см. <see cref="Spawn"/>), не требует префаба.
/// </summary>
[RequireComponent(typeof(LineRenderer))]
public class BulletTracer : MonoBehaviour
{
    LineRenderer _line;
    Vector3 _start;
    Vector3 _dir;
    float _distance;
    float _speed;
    float _streakLength;
    float _traveled;

    public static void Spawn(Vector3 start, Vector3 end, float speed, float streakLength, Color color, float width)
    {
        var go = new GameObject("BulletTracer");
        var tracer = go.AddComponent<BulletTracer>();
        tracer.Init(start, end, speed, streakLength, color, width);
    }

    void Init(Vector3 start, Vector3 end, float speed, float streakLength, Color color, float width)
    {
        _line = GetComponent<LineRenderer>();
        _line.useWorldSpace = true;
        _line.positionCount = 2;
        _line.numCapVertices = 2;
        _line.startWidth = width;
        _line.endWidth = width;
        _line.startColor = color;
        _line.endColor = new Color(color.r, color.g, color.b, 0f);
        _line.textureMode = LineTextureMode.Stretch;
        _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _line.receiveShadows = false;

        // Простой неосвещённый материал, работающий и в URP (Sprites/Default учитывает vertex color).
        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null)
            shader = Shader.Find("Unlit/Color");
        _line.material = new Material(shader);

        _start = start;
        Vector3 delta = end - start;
        _distance = delta.magnitude;
        _dir = _distance > 0.0001f ? delta / _distance : Vector3.forward;
        _speed = Mathf.Max(1f, speed);
        _streakLength = Mathf.Max(0.1f, streakLength);
        _traveled = 0f;

        UpdatePositions(0f);
    }

    void Update()
    {
        _traveled += _speed * Time.deltaTime;
        UpdatePositions(_traveled);

        // Хвост дошёл до цели — трассер отработал.
        if (_traveled - _streakLength >= _distance)
            Destroy(gameObject);
    }

    void UpdatePositions(float traveled)
    {
        float headDist = Mathf.Min(traveled, _distance);
        float tailDist = Mathf.Clamp(traveled - _streakLength, 0f, _distance);

        Vector3 head = _start + _dir * headDist;
        Vector3 tail = _start + _dir * tailDist;

        _line.SetPosition(0, tail);
        _line.SetPosition(1, head);
    }
}
