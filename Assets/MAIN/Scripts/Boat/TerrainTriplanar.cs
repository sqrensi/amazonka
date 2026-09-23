using UnityEngine;

/// <summary>
/// Террейн UV идёт сверху, поэтому отвесные склоны растягивают splat.
/// Этот шейдер семплирует слои triplanar (XZ / XY / ZY) по мировой нормали.
/// </summary>
[ExecuteAlways]
[DefaultExecutionOrder(-40)]
[DisallowMultipleComponent]
[RequireComponent(typeof(Terrain))]
public class TerrainTriplanar : MonoBehaviour
{
    [SerializeField] float sharpness = 4.2f;

    Terrain _terrain;
    static Material _mat;

    void OnEnable()
    {
        _terrain = GetComponent<Terrain>();
        Apply();
    }

    void LateUpdate()
    {
        Apply();
    }

    void Apply()
    {
        if (_terrain == null)
            _terrain = GetComponent<Terrain>();
        if (_terrain == null || _terrain.terrainData == null)
            return;

        Vector3 size = _terrain.terrainData.size;
        Shader.SetGlobalVector("_TerrainWorldSize", size);
        Shader.SetGlobalFloat("_TerrainTriplanarSharp", sharpness);

        if (_mat == null)
        {
            Shader shader = Shader.Find("Horror/Terrain Lit Triplanar");
            if (shader != null)
                _mat = new Material(shader);
        }
        if (_mat != null && _terrain.materialTemplate != _mat)
            _terrain.materialTemplate = _mat;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Hook()
    {
        var terrains = Object.FindObjectsByType<Terrain>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < terrains.Length; i++)
        {
            if (terrains[i] != null && terrains[i].GetComponent<TerrainTriplanar>() == null)
                terrains[i].gameObject.AddComponent<TerrainTriplanar>();
        }
    }
}
