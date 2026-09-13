using UnityEngine;

/// <summary>
/// Водная поверхность для плавучести. Положи префаб-плоскость на уровень воды.
/// </summary>
public class BoatWater : MonoBehaviour
{
    static BoatWater _instance;
    [SerializeField] float surfaceY;

    void OnEnable()
    {
        _instance = this;
        if (Mathf.Abs(surfaceY) < 0.001f)
            surfaceY = transform.position.y;
    }

    void OnDisable()
    {
        if (_instance == this)
            _instance = null;
    }

    public static bool TryHeight(Vector3 world, out float y)
    {
        if (_instance == null)
        {
            y = 0f;
            return false;
        }
        y = _instance.surfaceY;
        return true;
    }
}
