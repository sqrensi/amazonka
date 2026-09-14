using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Ручное весло в слоте. Зажатый ЛКМ гребёт, Q/E рулит.
/// </summary>
public class OarItem : HeldItem
{
    const float StrokeSeconds = 0.7f;
    static readonly string[] GroundProbes = { "Blade", "Shaft", "Neck", "Handle" };

    bool _holding;
    float _strokeT = 99f;
    float _groundPitch;
    float _groundPitchVel;

    public override Transform GetAnchor()
    {
        return BoatVisuals.EnsurePromptAnchor(transform, new Vector3(0f, 0.04f, 0.98f));
    }

    public override void OnEquip()
    {
        base.OnEquip();
        SetRenderersHidden(false);
        BoatBuildHud.Hint("Hold LMB to paddle   Q/E steer", 3f);
    }

    public override void OnUnequip()
    {
        _holding = false;
        base.OnUnequip();
    }

    public override void ApplyHeldPose()
    {
        var mouse = Mouse.current;
        _holding = IsEquipped
            && BoatOarStation.Active == null
            && mouse != null
            && mouse.leftButton.isPressed
            && Cursor.lockState == CursorLockMode.Locked
            && !IsUseBlocked;

        if (_holding)
        {
            _strokeT += Time.deltaTime;
            if (_strokeT >= StrokeSeconds)
                Stroke(true);
        }
        else if (_strokeT < 90f)
            _strokeT += Time.deltaTime;

        BoatPaddle.TickSteer(Time.deltaTime);
        base.ApplyHeldPose();
        float u = StrokeWeight();
        if (u > 0.001f)
        {
            transform.localPosition += new Vector3(0.006f, -0.025f, -0.035f) * u;
            transform.localRotation = Quaternion.Euler(9f * u, 2f * u, -3f * u) * transform.localRotation;
        }
        transform.localRotation = Quaternion.Euler(0f, BoatPaddle.Steer * 0.7f, 0f) * transform.localRotation;
        ClearGround(Time.deltaTime);
    }

    public override void OnUseStart()
    {
        if (IsUseBlocked)
            return;
        _holding = true;
        Stroke(false);
    }

    public override void OnUseStop()
    {
        _holding = false;
    }

    void Stroke(bool repeat)
    {
        if (!repeat && _strokeT < StrokeSeconds * 0.85f && _strokeT < 90f)
            return;
        Camera cam = BoatBuildUtil.Cam(Owner);
        Vector3 grip = transform.position;
        Vector3 blade = grip + Vector3.down;
        Transform bladeTf = transform.Find("Blade");
        if (bladeTf != null)
            blade = bladeTf.position;

        if (!BoatPaddle.ReachesWater(grip, blade))
        {
            if (!repeat)
                BoatBuildHud.Hint("Blade not in water");
            return;
        }

        Rigidbody boat = BoatPaddle.BoatUnder(Owner);
        if (boat == null)
        {
            if (!repeat)
                BoatBuildHud.Hint("Stand on a boat");
            return;
        }

        _strokeT = 0f;
        Vector3 fwd = cam != null ? cam.transform.forward : transform.forward;
        BoatPaddle.Push(boat, BoatPaddle.SteerDir(fwd), blade, 48f, ForceMode.Impulse);
    }

    float StrokeWeight()
    {
        float t = Mathf.Clamp01(_strokeT / StrokeSeconds);
        if (t >= 1f)
            return 0f;
        float rise = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 0.36f));
        float fall = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - 0.36f) / 0.64f));
        return t < 0.36f ? rise : fall;
    }

    void ClearGround(float dt)
    {
        float need = 0f;
        for (int i = 0; i < GroundProbes.Length; i++)
        {
            Transform p = transform.Find(GroundProbes[i]);
            if (p == null)
                continue;
            Vector3 pt = p.position;
            if (!Physics.Raycast(pt + Vector3.up * 0.5f, Vector3.down, out RaycastHit hit, 1.8f, ~0, QueryTriggerInteraction.Ignore))
                continue;
            if (hit.collider == null)
                continue;
            if (Owner != null && (hit.transform == Owner.transform || hit.transform.IsChildOf(Owner.transform)))
                continue;
            if (hit.collider.GetComponentInParent<BoatWater>() != null)
                continue;
            if (hit.collider.GetComponentInParent<BoatPiece>() != null)
                continue;
            float keep = hit.point.y + 0.05f;
            if (pt.y < keep)
                need = Mathf.Max(need, keep - pt.y);
        }

        float pitchNeed = Mathf.Clamp(need * 85f, 0f, 48f);
        float smooth = pitchNeed > _groundPitch ? 0.07f : 0.16f;
        _groundPitch = Mathf.SmoothDamp(_groundPitch, pitchNeed, ref _groundPitchVel, smooth, 90f, dt);
        if (_groundPitch > 0.05f)
            transform.localRotation = Quaternion.Euler(_groundPitch, 0f, 0f) * transform.localRotation;
    }
}
