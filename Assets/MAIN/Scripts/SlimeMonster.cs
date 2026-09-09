using System.Collections;
using UnityEngine;

/// <summary>
/// Слайм: прыгает по местности, иногда останавливается и «разглядывает».
/// Умирает от попадания мухомором.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class SlimeMonster : MonoBehaviour, IDamageable
{
    [SerializeField] string displayName = "Slime";
    [SerializeField] AudioClip hitClip;
    [SerializeField] float wanderRadius = 11f;
    [SerializeField] float hopHeight = 0.55f;
    [SerializeField] float hopSpeed = 2.35f;
    [SerializeField] float gravity = -22f;
    [SerializeField] float inspectChance = 0.38f;
    [SerializeField] Vector2 hopPause = new Vector2(0.35f, 1.1f);
    [SerializeField] Vector2 inspectTime = new Vector2(1.6f, 3.2f);
    [SerializeField] float turnSpeed = 7f;

    CharacterController _cc;
    Animator _anim;
    Vector3 _home;
    Vector3 _hopVel;
    bool _dead;
    bool _hopping;
    string _animState = "";
    Coroutine _brain;

    public bool IsDead => _dead;
    public string DisplayName => displayName;

    void Awake()
    {
        _cc = GetComponent<CharacterController>();
        _anim = GetComponentInChildren<Animator>();
        if (_anim != null)
            _anim.applyRootMotion = false;
        _home = transform.position;
    }

    void OnEnable()
    {
        if (!_dead)
            _brain = StartCoroutine(Brain());
    }

    void OnDisable()
    {
        if (_brain != null)
            StopCoroutine(_brain);
        _brain = null;
    }

    public void TakeDamage(float amount, Vector3 hitPoint, Vector3 hitDirection)
    {
        if (_dead)
            return;
        PlayAnim("GetHit", 0.08f);
    }

    public void HitByMushroom(MushroomItem mushroom)
    {
        if (_dead || mushroom == null || !mushroom.IsPlayerThrowHit)
            return;

        if (hitClip != null)
            AudioSource.PlayClipAtPoint(hitClip, transform.position, 1f);

        Vector3 from = PlayerOrigin();
        float distance = Vector3.Distance(from, transform.position);
        Die();
        KillNoticeHUD.Show(new KillNoticeHUD.Report
        {
            killIndex = KillNoticeHUD.NextKillIndex(),
            target = displayName,
            weapon = string.IsNullOrEmpty(mushroom.DisplayName) ? "Fly Agaric" : mushroom.DisplayName,
            distanceMeters = distance
        });
    }

    public void Die()
    {
        if (_dead)
            return;
        _dead = true;
        if (_brain != null)
        {
            StopCoroutine(_brain);
            _brain = null;
        }
        _hopVel = Vector3.zero;
        if (_cc != null)
            _cc.enabled = false;
        PlayAnim("Die", 0.05f);
        StartCoroutine(Despawn());
    }

    void Update()
    {
        if (_dead || _cc == null || !_cc.enabled || _hopping)
            return;
        float dt = Time.deltaTime;
        if (_cc.isGrounded && _hopVel.y < 0f)
            _hopVel.y = -2f;
        _hopVel.x = 0f;
        _hopVel.z = 0f;
        _hopVel.y += gravity * dt;
        _cc.Move(_hopVel * dt);
    }

    IEnumerator Despawn()
    {
        yield return new WaitForSeconds(4.2f);
        Destroy(gameObject);
    }

    IEnumerator Brain()
    {
        PlayAnim("IdleNormal", 0.2f);
        yield return new WaitForSeconds(Random.Range(0.4f, 1.2f));

        while (!_dead)
        {
            if (Random.value < inspectChance)
                yield return Inspect();
            else
                yield return HopTo(RandomGroundPoint());
        }
    }

    IEnumerator Inspect()
    {
        PlayAnim("IdleNormal", 0.15f);
        Vector3 look = transform.position + new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f)).normalized;
        float spin = Random.Range(0.35f, 0.8f);
        float t = 0f;
        while (t < spin && !_dead)
        {
            Face(look, Time.deltaTime);
            t += Time.deltaTime;
            yield return null;
        }

        PlayAnim(Random.value > 0.5f ? "SenseSomethingST" : "SenseSomethingRPT", 0.12f);
        float wait = Random.Range(inspectTime.x, inspectTime.y);
        t = 0f;
        while (t < wait && !_dead)
        {
            t += Time.deltaTime;
            yield return null;
        }

        if (Random.value < 0.35f)
        {
            PlayAnim("Taunt", 0.12f);
            yield return new WaitForSeconds(1.1f);
        }

        PlayAnim("IdleNormal", 0.15f);
        yield return new WaitForSeconds(Random.Range(0.2f, 0.6f));
    }

    IEnumerator HopTo(Vector3 dest)
    {
        _hopping = true;
        PlayAnim("WalkFWD", 0.12f);
        float giveUp = 6.5f;
        float elapsed = 0f;
        while (!_dead && elapsed < giveUp)
        {
            Vector3 flat = dest - transform.position;
            flat.y = 0f;
            if (flat.sqrMagnitude < 0.45f * 0.45f)
                break;

            Face(dest, Time.deltaTime);
            TickHop(flat.normalized, Time.deltaTime);
            elapsed += Time.deltaTime;
            yield return null;
        }

        _hopVel.x = 0f;
        _hopVel.z = 0f;
        _hopping = false;
        PlayAnim("IdleNormal", 0.12f);
        yield return new WaitForSeconds(Random.Range(hopPause.x, hopPause.y));
    }

    void TickHop(Vector3 dir, float dt)
    {
        bool grounded = _cc != null && _cc.isGrounded;
        if (grounded && _hopVel.y < 0f)
            _hopVel.y = -2f;

        if (grounded)
        {
            _hopVel.x = dir.x * hopSpeed;
            _hopVel.z = dir.z * hopSpeed;
            _hopVel.y = Mathf.Sqrt(hopHeight * -2f * gravity);
        }

        _hopVel.y += gravity * dt;
        _cc.Move(_hopVel * dt);
    }

    void Face(Vector3 worldPoint, float dt)
    {
        Vector3 d = worldPoint - transform.position;
        d.y = 0f;
        if (d.sqrMagnitude < 0.001f)
            return;
        Quaternion want = Quaternion.LookRotation(d.normalized, Vector3.up);
        transform.rotation = Quaternion.Slerp(transform.rotation, want, 1f - Mathf.Exp(-turnSpeed * dt));
    }

    Vector3 RandomGroundPoint()
    {
        Vector3 p = _home + Random.insideUnitSphere * wanderRadius;
        p.y = _home.y + 6f;
        if (Physics.Raycast(p, Vector3.down, out RaycastHit hit, 18f, ~0, QueryTriggerInteraction.Ignore))
            return hit.point;
        p.y = _home.y;
        return p;
    }

    void PlayAnim(string state, float fade)
    {
        if (_anim == null || _dead && state != "Die")
            return;
        if (_animState == state && state != "Die")
            return;
        _animState = state;
        if (fade <= 0.01f)
            _anim.Play(state, 0, 0f);
        else
            _anim.CrossFadeInFixedTime(state, fade, 0, 0f);
    }

    void OnControllerColliderHit(ControllerColliderHit hit)
    {
        if (hit.collider == null)
            return;
        var shroom = hit.collider.GetComponentInParent<MushroomItem>();
        if (shroom != null && shroom.IsPlayerThrowHit)
            HitByMushroom(shroom);
    }

    static Vector3 PlayerOrigin()
    {
        var player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
            return player.transform.position;
        if (Camera.main != null)
            return Camera.main.transform.position;
        return Vector3.zero;
    }
}
