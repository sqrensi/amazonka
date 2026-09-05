using System.Collections;
using UnityEngine;

/// <summary>
/// Возрождает игрока после смерти: ждёт задержку, переносит на точку спавна,
/// восстанавливает здоровье и возвращает управление. Сбрасывает шум ночи.
/// </summary>
[RequireComponent(typeof(PlayerHealth))]
public class PlayerRespawn : MonoBehaviour
{
    [Tooltip("Точка спавна. Если не задана — берётся стартовая позиция игрока.")]
    [SerializeField] Transform spawnPoint;
    [SerializeField] float respawnDelay = 2.5f;
    [SerializeField] bool resetNoiseOnRespawn = true;

    PlayerHealth _health;
    CharacterController _controller;
    HorrorFirstPersonController _fpController;

    Vector3 _spawnPos;
    Quaternion _spawnRot;

    void Awake()
    {
        _health = GetComponent<PlayerHealth>();
        _controller = GetComponent<CharacterController>();
        _fpController = GetComponent<HorrorFirstPersonController>();

        if (spawnPoint != null)
        {
            _spawnPos = spawnPoint.position;
            _spawnRot = spawnPoint.rotation;
        }
        else
        {
            _spawnPos = transform.position;
            _spawnRot = transform.rotation;
        }
    }

    void OnEnable() => _health.OnDeath += HandleDeath;
    void OnDisable() => _health.OnDeath -= HandleDeath;

    void HandleDeath() => StartCoroutine(RespawnRoutine());

    IEnumerator RespawnRoutine()
    {
        yield return new WaitForSeconds(respawnDelay);

        TeleportToSpawn();
        _health.Revive(true);

        if (resetNoiseOnRespawn && NoiseManager.Instance != null)
            NoiseManager.Instance.SetNoise(0f);

        // Возвращаем управление и захват курсора.
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void TeleportToSpawn()
    {
        // CharacterController нужно временно выключить, иначе он "сопротивляется" телепорту.
        bool ccWasEnabled = _controller != null && _controller.enabled;
        if (_controller != null)
            _controller.enabled = false;

        transform.SetPositionAndRotation(_spawnPos, _spawnRot);

        if (_controller != null)
            _controller.enabled = ccWasEnabled || true;
    }

    /// <summary>Задать новую точку спавна во время игры (например, чекпоинт).</summary>
    public void SetSpawn(Vector3 position, Quaternion rotation)
    {
        _spawnPos = position;
        _spawnRot = rotation;
    }
}
