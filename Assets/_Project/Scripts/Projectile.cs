using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A flying projectile (arrow now, siege shot later). Spawned by CombatStateMachine when a ranged
/// attack goes active. Arrows don't pick sides: anyone at the landing point can be hit, including
/// the shooter's own team (scaled by the attack's allyDamageMultiplier).
/// Draws itself with a placeholder streak and ground shadow.
/// </summary>
public class Projectile : MonoBehaviour
{
    static readonly List<Collider2D> overlap = new List<Collider2D>(32);
    static readonly List<RaycastHit2D> castHits = new List<RaycastHit2D>(16);
    static readonly List<CombatStateMachine> candidates = new List<CombatStateMachine>(16);

    public static int Live { get; private set; }

    CombatStateMachine owner;
    int ownerTeam;
    AttackData attack;
    ProjectileData data;
    float damageMult;

    Vector2 start, end, dir;
    float flightTime, t, totalDist, stuckTimer;
    bool landed;
    int hitsLeft;
    ContactFilter2D filter;
    readonly HashSet<CombatStateMachine> alreadyHit = new HashSet<CombatStateMachine>();

    LineRenderer line;
    SpriteRenderer shadow;

    public static Projectile Launch(CombatStateMachine owner, AttackData attack, float damageMult,
                                    Vector2 from, Vector2 direction, float aimDistance)
    {
        var go = new GameObject(attack.projectile.name);
        var p = go.AddComponent<Projectile>();
        p.Init(owner, attack, damageMult, from, direction, aimDistance);
        return p;
    }

    void Init(CombatStateMachine owner, AttackData attack, float damageMult, Vector2 from, Vector2 direction, float aimDistance)
    {
        this.owner = owner;
        ownerTeam = owner ? owner.team : -1;
        this.attack = attack;
        this.damageMult = damageMult;
        data = attack.projectile;
        filter = new ContactFilter2D { useTriggers = true };

        start = from;
        dir = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.right;
        if (data.mode == ProjectileMode.Arc)
        {
            float d = Mathf.Clamp(aimDistance, data.minRange, data.maxRange);
            end = start + dir * d + Random.insideUnitCircle * (d * data.scatter);
            dir = (end - start).normalized;
        }
        else
        {
            end = start + dir * data.maxRange;
        }
        totalDist = Mathf.Max(0.1f, Vector2.Distance(start, end));
        flightTime = totalDist / Mathf.Max(0.1f, data.speed);
        hitsLeft = data.maxTargets <= 0 ? int.MaxValue : data.maxTargets;
        Live++;

        BuildVisual();
        UpdateVisual(0f);
    }

    void OnDestroy() => Live--;

    void Update()
    {
        float dt = Time.deltaTime;
        if (landed)
        {
            stuckTimer -= dt;
            if (stuckTimer <= 0f) Destroy(gameObject);
            return;
        }

        float prevT = t;
        t = Mathf.Min(flightTime, t + dt);

        if (data.mode == ProjectileMode.Direct) DirectStep(prevT, t);
        if (landed) return;

        if (t >= flightTime)
        {
            if (data.mode == ProjectileMode.Arc && Impact(end)) { Destroy(gameObject); return; }
            Land(end);
            return;
        }
        UpdateVisual(t / flightTime);
    }

    // ================= Hitting =================

    /// <summary>Arc: everything within hitRadius of the landing point, closest first.</summary>
    bool Impact(Vector2 point)
    {
        candidates.Clear();
        int n = Physics2D.OverlapCircle(point, data.hitRadius, filter, overlap);
        for (int i = 0; i < n; i++)
        {
            var c = overlap[i].GetComponentInParent<CombatStateMachine>();
            if (c && c.State != CombatState.Dead && !candidates.Contains(c)) candidates.Add(c);
        }
        candidates.Sort((a, b) =>
            ((Vector2)a.transform.position - point).sqrMagnitude.CompareTo(((Vector2)b.transform.position - point).sqrMagnitude));

        bool any = false;
        foreach (var c in candidates)
        {
            if (TryHit(c)) { any = true; if (--hitsLeft <= 0) break; }
        }
        return any;
    }

    /// <summary>Direct: sweep the path flown this frame; walls stop it.</summary>
    void DirectStep(float prevT, float nowT)
    {
        Vector2 a = Vector2.Lerp(start, end, prevT / flightTime);
        Vector2 b = Vector2.Lerp(start, end, nowT / flightTime);
        float len = (b - a).magnitude;
        if (len < 0.0001f) return;

        int n = Physics2D.CircleCast(a, data.hitRadius, dir, filter, castHits, len);
        castHits.Sort((x, y) => x.distance.CompareTo(y.distance));
        for (int i = 0; i < n; i++)
        {
            var hit = castHits[i];
            var c = hit.collider.GetComponentInParent<CombatStateMachine>();
            if (!c)
            {
                if (!hit.collider.isTrigger && hit.rigidbody == null) { Land(hit.point); return; } // wall
                continue;
            }
            if (c == owner || c.State == CombatState.Dead || alreadyHit.Contains(c)) continue;
            if (TryHit(c) && --hitsLeft <= 0) { Destroy(gameObject); landed = true; return; }
        }
    }

    bool TryHit(CombatStateMachine c)
    {
        if (!owner || alreadyHit.Contains(c)) return false; // shooter removed from the game: arrow is harmless
        float mult = c.team == ownerTeam ? attack.allyDamageMultiplier : 1f;
        if (mult <= 0f) return false;

        alreadyHit.Add(c);
        Vector2 from = (Vector2)c.transform.position - dir; // the direction it flew in, for shield blocks
        var r = c.ReceiveHit(owner, attack, mult * damageMult, from);
        if (r.type == HitType.Dodged || r.type == HitType.Ignored) return false;
        owner.ReportHitLanded(r);
        return true;
    }

    void Land(Vector2 at)
    {
        landed = true;
        stuckTimer = data.stuckTime;
        if (shadow) shadow.enabled = false;
        // Stuck in the ground: short, drawn under units.
        line.SetPosition(0, at - dir * data.length * 0.55f);
        line.SetPosition(1, at);
        int order = -Mathf.RoundToInt(at.y * 100f) - 5;
        line.sortingOrder = order;
        var c = line.startColor; c.a = 0.75f;
        line.startColor = line.endColor = c;
    }

    // ================= Placeholder visual =================

    void BuildVisual()
    {
        line = gameObject.AddComponent<LineRenderer>();
        line.sharedMaterial = PlaceholderArt.Unlit;
        line.useWorldSpace = true;
        line.positionCount = 2;
        line.widthMultiplier = data.width;
        line.startColor = line.endColor = data.color;
        line.numCapVertices = 1;
        line.sortingOrder = 32000;

        if (data.mode == ProjectileMode.Arc)
        {
            shadow = PlaceholderArt.MakeRenderer(transform, "Shadow", PlaceholderArt.Circle, new Color(0f, 0f, 0f, 0.35f), new Vector2(0.14f, 0.07f));
            shadow.sortingOrder = 31999;
        }
    }

    void UpdateVisual(float u)
    {
        Vector2 ground = Vector2.Lerp(start, end, u);
        transform.position = ground;

        if (data.mode == ProjectileMode.Direct)
        {
            line.SetPosition(0, ground - dir * data.length);
            line.SetPosition(1, ground);
            return;
        }

        // Top-down 3/4 view: height is drawn as a +y offset; the shadow stays on the ground.
        float peak = data.arcHeight * totalDist / 10f;
        float tailU = Mathf.Max(0f, u - data.length / totalDist);
        Vector2 head = ground + Vector2.up * (peak * 4f * u * (1f - u));
        Vector2 tail = Vector2.Lerp(start, end, tailU) + Vector2.up * (peak * 4f * tailU * (1f - tailU));
        if ((head - tail).sqrMagnitude < 0.0001f) tail = head - dir * data.length;
        line.SetPosition(0, tail);
        line.SetPosition(1, head);
        if (shadow) shadow.transform.position = ground;
    }
}
