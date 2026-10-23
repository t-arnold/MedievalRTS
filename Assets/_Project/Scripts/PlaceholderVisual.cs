using UnityEngine;

/// <summary>Generates simple sprites and an unlit material at runtime, so testing needs no art.</summary>
public static class PlaceholderArt
{
    static Sprite circle, square;
    static Material unlit;

    public static Sprite Circle => circle ? circle : circle = MakeSprite(64, true);
    public static Sprite Square => square ? square : square = MakeSprite(8, false);

    public static Material Unlit
    {
        get
        {
            if (unlit) return unlit;
            var shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
            if (!shader) shader = Shader.Find("Sprites/Default");
            unlit = new Material(shader);
            return unlit;
        }
    }

    static Sprite MakeSprite(int size, bool round)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
        float r = size / 2f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                bool inside = !round || new Vector2(x + 0.5f - r, y + 0.5f - r).magnitude <= r;
                tex.SetPixel(x, y, inside ? Color.white : Color.clear);
            }
        tex.Apply();
        // pixelsPerUnit = size, so the sprite is exactly 1 world unit wide.
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }

    public static SpriteRenderer MakeRenderer(Transform parent, string name, Sprite sprite, Color color, Vector2 scale)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localScale = new Vector3(scale.x, scale.y, 1f);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.color = color;
        sr.sharedMaterial = Unlit;
        return sr;
    }
}

/// <summary>
/// Stand-in visuals for a combatant: team-coloured body, facing pointer, state tint,
/// attack-arc preview (telegraph), swing smear, hit flash, unblockable warning, block shield.
/// Delete this component once real sprites exist; it only listens to CombatStateMachine events.
/// </summary>
[RequireComponent(typeof(CombatStateMachine))]
public class PlaceholderVisual : MonoBehaviour
{
    public Color teamColor = new Color(0.3f, 0.5f, 1f);
    [Tooltip("Seconds a corpse stays before being removed (never removes the player).")]
    public float corpseLifetime = 6f;

    CombatStateMachine csm;
    SpriteRenderer body, pointer, shield, warning;
    readonly SpriteRenderer[] pips = new SpriteRenderer[5];
    LineRenderer arc;
    float flashTimer, deadTimer, promoteTimer;
    SoldierAI ai;
    bool aiLooked;
    int lastPips = -1;

    static readonly Color WindupTint = new Color(1f, 0.85f, 0.3f);
    static readonly Color RecoveryTint = new Color(0.45f, 0.45f, 0.45f);
    static readonly Color StaggerTint = new Color(0.7f, 0.3f, 0.9f);
    static readonly Color DeadTint = new Color(0.25f, 0.25f, 0.25f);

    void Awake()
    {
        csm = GetComponent<CombatStateMachine>();

        body = PlaceholderArt.MakeRenderer(transform, "Body", PlaceholderArt.Circle, teamColor, new Vector2(0.7f, 0.7f));
        body.transform.localPosition = new Vector3(0f, 0.35f, 0f); // pivot at feet, body above

        pointer = PlaceholderArt.MakeRenderer(transform, "Facing", PlaceholderArt.Square, Color.white, new Vector2(0.12f, 0.45f));
        shield = PlaceholderArt.MakeRenderer(transform, "Shield", PlaceholderArt.Square, new Color(0.8f, 0.8f, 0.9f), new Vector2(0.7f, 0.12f));
        shield.enabled = false;

        warning = PlaceholderArt.MakeRenderer(transform, "UnblockableFlag", PlaceholderArt.Circle, new Color(1f, 0.1f, 0.1f), new Vector2(0.25f, 0.25f));
        warning.transform.localPosition = new Vector3(0f, 1.05f, 0f);
        warning.enabled = false;

        // Rank pips above the head: one gold square per experience tier above Levy.
        for (int i = 0; i < pips.Length; i++)
        {
            pips[i] = PlaceholderArt.MakeRenderer(transform, "Pip" + i, PlaceholderArt.Square, new Color(1f, 0.82f, 0.25f), new Vector2(0.09f, 0.09f));
            pips[i].transform.localPosition = new Vector3((i - 2) * 0.13f, 0.85f, 0f);
            pips[i].enabled = false;
        }

        var arcGo = new GameObject("AttackArc");
        arcGo.transform.SetParent(transform, false);
        arc = arcGo.AddComponent<LineRenderer>();
        arc.sharedMaterial = PlaceholderArt.Unlit;
        arc.useWorldSpace = true;
        arc.loop = true;
        arc.positionCount = 0;
        arc.numCapVertices = 2;
    }

    void OnEnable()
    {
        csm.OnHitReceived += HandleHit;
        csm.OnStateChanged += HandleState;
    }

    void OnDisable()
    {
        csm.OnHitReceived -= HandleHit;
        csm.OnStateChanged -= HandleState;
    }

    public void SetTeamColor(Color c)
    {
        teamColor = c;
        if (body) body.color = c;
    }

    void HandleHit(HitResult r)
    {
        if (r.type == HitType.Hit || r.type == HitType.CounterHit || r.type == HitType.GuardBroken)
            flashTimer = 0.08f;
    }

    void HandleState(CombatState from, CombatState to)
    {
        warning.enabled = to == CombatState.Windup && csm.CurrentAttack && csm.CurrentAttack.telegraph == TelegraphType.Unblockable;

        if (to == CombatState.Windup && csm.CurrentAttack && csm.CurrentAttack.telegraphSound)
            AudioSource.PlayClipAtPoint(csm.CurrentAttack.telegraphSound, transform.position);

        if (to == CombatState.Dead)
        {
            foreach (var c in GetComponentsInChildren<Collider2D>()) c.enabled = false;
            var rb = GetComponent<Rigidbody2D>();
            if (rb) rb.simulated = false;
        }
    }

    void LateUpdate()
    {
        Vector2 f = csm.Facing;
        float angle = Mathf.Atan2(f.y, f.x) * Mathf.Rad2Deg - 90f;

        pointer.transform.localPosition = new Vector3(f.x * 0.45f, 0.35f + f.y * 0.45f, 0f);
        pointer.transform.localRotation = Quaternion.Euler(0, 0, angle);

        shield.enabled = csm.State == CombatState.Blocking;
        shield.transform.localPosition = new Vector3(f.x * 0.55f, 0.35f + f.y * 0.55f, 0f);
        shield.transform.localRotation = Quaternion.Euler(0, 0, angle);
        shield.color = csm.StateTime <= csm.parryWindow ? Color.cyan : new Color(0.8f, 0.8f, 0.9f); // cyan = parry window

        // ---- body tint by state ----
        Color c = csm.State switch
        {
            CombatState.Windup => Color.Lerp(teamColor, WindupTint, 0.6f),
            CombatState.Active => Color.Lerp(teamColor, Color.white, 0.5f),
            CombatState.Recovery => Color.Lerp(teamColor, RecoveryTint, 0.6f),
            CombatState.Staggered => StaggerTint,
            CombatState.Dead => DeadTint,
            CombatState.Dodging => new Color(teamColor.r, teamColor.g, teamColor.b, 0.45f),
            _ => teamColor,
        };
        if (flashTimer > 0f) { flashTimer -= Time.deltaTime; c = Color.white; }

        // ---- rank pips + gold flash on promotion ----
        if (!aiLooked) { ai = GetComponent<SoldierAI>(); aiLooked = true; }
        int rank = ai && ai.skill && csm.State != CombatState.Dead ? ai.skill.rankPips : 0;
        if (lastPips >= 0 && rank > lastPips) promoteTimer = 0.5f;
        if (csm.State != CombatState.Dead) lastPips = rank;
        for (int i = 0; i < pips.Length; i++) pips[i].enabled = i < rank;
        if (promoteTimer > 0f) { promoteTimer -= Time.deltaTime; c = new Color(1f, 0.85f, 0.2f); }

        body.color = c;

        DrawArc();

        // ---- y-sorting: lower on screen = drawn in front ----
        int order = -Mathf.RoundToInt(transform.position.y * 100f);
        body.sortingOrder = order;
        pointer.sortingOrder = shield.sortingOrder = order + 1;
        warning.sortingOrder = order + 2;
        foreach (var p in pips) p.sortingOrder = order + 2;
        arc.sortingOrder = order - 1;

        if (csm.State == CombatState.Dead && !GetComponent<PlayerController>())
        {
            deadTimer += Time.deltaTime;
            if (deadTimer > corpseLifetime) Destroy(gameObject);
        }
    }

    void DrawArc()
    {
        var atk = csm.CurrentAttack;
        bool show = atk && (csm.State == CombatState.Windup || csm.State == CombatState.Active);
        if (!show) { arc.positionCount = 0; return; }

        bool active = csm.State == CombatState.Active;
        Color col = atk.telegraph == TelegraphType.Unblockable ? new Color(1f, 0.15f, 0.15f) : new Color(1f, 0.8f, 0.3f);
        col = active ? Color.white : new Color(col.r, col.g, col.b, 0.35f);
        arc.startColor = arc.endColor = col;
        arc.widthMultiplier = active ? 0.12f : 0.04f; // active arc doubles as the swing smear

        const int segments = 14;
        arc.positionCount = segments + 2;
        Vector3 o = transform.position;
        arc.SetPosition(0, o);
        float half = atk.arcAngle * 0.5f;
        float baseAngle = Mathf.Atan2(csm.Facing.y, csm.Facing.x) * Mathf.Rad2Deg;
        for (int i = 0; i <= segments; i++)
        {
            float a = (baseAngle - half + atk.arcAngle * i / segments) * Mathf.Deg2Rad;
            arc.SetPosition(i + 1, o + new Vector3(Mathf.Cos(a), Mathf.Sin(a)) * atk.reach);
        }
    }
}
