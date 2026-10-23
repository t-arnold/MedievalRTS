using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// On-screen debug info (IMGUI, no setup): FPS, unit counts, controls, the player's
/// health/stamina/poise/state, starting-enemy tier, duel tester status and history, a combat log
/// of hits involving the player, and bars + tier labels over nearby units.
/// Added automatically by TestArena. Toggle with 1.
/// </summary>
public class DebugOverlay : MonoBehaviour
{
    public bool show = true;
    public int maxUnitBars = 60;

    TestArena arena;
    DuelTester duel;
    float fps = 60f;
    GUIStyle label, small;
    readonly Queue<string> log = new Queue<string>();
    readonly List<string> lines = new List<string>();
    CombatStateMachine logged;

    void Awake()
    {
        arena = GetComponent<TestArena>();
    }

    void Update()
    {
        fps = Mathf.Lerp(fps, 1f / Mathf.Max(0.0001f, Time.unscaledDeltaTime), 0.05f);
        if (!duel) duel = GetComponent<DuelTester>();

        // Hook the combat log onto the current player (re-hooks after a reset).
        var p = arena ? arena.Player : null;
        if (p && p != logged)
        {
            logged = p;
            p.OnHitLanded += r => Log($"You → {r.defender.name} [{TierOf(r.defender)}] ({r.attack.name}): {r.type}");
            p.OnHitReceived += r => Log($"{r.attacker.name} [{TierOf(r.attacker)}] → you ({r.attack.name}): {r.type}");
        }
    }

    static string TierOf(CombatStateMachine c)
    {
        var ai = c ? c.GetComponent<SoldierAI>() : null;
        return ai && ai.skill ? ai.skill.tierName : "?";
    }

    void Log(string line)
    {
        log.Enqueue($"{Time.time:0.0}s  {line}");
        while (log.Count > 8) log.Dequeue();
    }

    void OnGUI()
    {
        if (!show) return;

        float scale = Mathf.Clamp(Screen.height / 900f, 1f, 3f);
        label ??= new GUIStyle(GUI.skin.label);
        small ??= new GUIStyle(GUI.skin.label);
        label.fontSize = Mathf.RoundToInt(13 * scale);
        small.fontSize = Mathf.RoundToInt(10 * scale);
        label.normal.textColor = small.normal.textColor = Color.white;

        float x = 10 * scale, y = 10 * scale, w = 640 * scale, line = 18 * scale;

        int allies = 0, enemies = 0;
        foreach (var c in CombatStateMachine.All)
        {
            if (c.State == CombatState.Dead) continue;
            if (c.team == 0) allies++; else enemies++;
        }

        // ---- text lines ----
        lines.Clear();
        lines.Add($"FPS {fps:0}   units: {allies} blue / {enemies} red   timeScale {Time.timeScale:0.##}");
        lines.Add("WASD move · mouse aim · LMB light (tap again = combo) · E heavy · RMB block (cyan = parry) · Space dodge · Shift sprint");
        lines.Add("1 overlay · 2 slow-mo · 3 skirmish · 4 big battle · 5 god mode · 9 enemy tier · R reset");
        if (arena) lines.Add($"Starting enemies: {arena.StartingEnemyTierName} (press 9 to change)");
        if (duel)
        {
            lines.Add($"Duels: {duel.NameA} vs {duel.NameB}   ·   6 {(duel.Running ? "STOP" : "start")} · 7 change A · 8 change B");
            if (duel.Running) lines.Add("  " + duel.Status);
            foreach (var h in duel.History) lines.Add("  done: " + h);
        }

        var p = arena ? arena.Player : null;
        int barRows = p ? 4 : 0;

        GUI.color = new Color(0, 0, 0, 0.6f);
        GUI.DrawTexture(new Rect(x - 5, y - 5, w, line * (lines.Count + barRows + 1) + line * 0.8f * log.Count + 10), Texture2D.whiteTexture);
        GUI.color = Color.white;

        foreach (var l in lines) Text(ref y, x, line, l);
        y += line * 0.5f;

        if (p)
        {
            Text(ref y, x, line, $"State: {p.State}  ({p.StateTime:0.00}s)  {(arena.PlayerInvulnerable ? "[GOD MODE]" : "")}{(p.State == CombatState.Dead ? "   YOU DIED — press R" : "")}");
            float bw = 360 * scale;
            Bar(ref y, x, bw, line, "HP", p.Stats.Health, p.Stats.maxHealth, new Color(0.85f, 0.2f, 0.2f));
            Bar(ref y, x, bw, line, "Stamina", p.Stats.Stamina, p.Stats.maxStamina, new Color(0.3f, 0.8f, 0.3f));
            Bar(ref y, x, bw, line, "Poise", p.Stats.Poise, p.Stats.maxPoise, new Color(0.9f, 0.75f, 0.2f));
        }

        y += line * 0.5f;
        foreach (var l in log) { GUI.Label(new Rect(x, y, w, line), l, small); y += line * 0.8f; }

        DrawUnitBars(scale);
    }

    void DrawUnitBars(float scale)
    {
        var cam = Camera.main;
        if (!cam) return;
        Vector3 centre = arena && arena.Player ? arena.Player.transform.position : cam.transform.position;
        float radius = cam.orthographicSize * 1.6f; // roughly what's on screen

        int drawn = 0;
        foreach (var c in CombatStateMachine.All)
        {
            if (c == (arena ? arena.Player : null) || c.State == CombatState.Dead) continue;
            if (drawn >= maxUnitBars) break;
            if ((c.transform.position - centre).sqrMagnitude > radius * radius) continue;
            drawn++;

            Vector3 sp = cam.WorldToScreenPoint(c.transform.position + Vector3.up * 1.0f);
            if (sp.z < 0) continue;
            float bw = 40 * scale, bh = 4 * scale;
            float bx = sp.x - bw / 2, by = Screen.height - sp.y;

            GUI.color = Color.black;
            GUI.DrawTexture(new Rect(bx - 1, by - 1, bw + 2, bh * 2 + 3), Texture2D.whiteTexture);
            GUI.color = new Color(0.85f, 0.2f, 0.2f);
            GUI.DrawTexture(new Rect(bx, by, bw * c.Stats.Health / c.Stats.maxHealth, bh), Texture2D.whiteTexture);
            GUI.color = new Color(0.9f, 0.75f, 0.2f);
            GUI.DrawTexture(new Rect(bx, by + bh + 1, bw * c.Stats.Poise / c.Stats.maxPoise, bh), Texture2D.whiteTexture);
            GUI.color = Color.white;

            string tag = TierOf(c);
            if (c.State != CombatState.Idle) tag += " · " + c.State;
            GUI.Label(new Rect(bx, by - 14 * scale, 160 * scale, 16 * scale), tag, small);
        }
    }

    void Text(ref float y, float x, float line, string s)
    {
        GUI.Label(new Rect(x, y, 3000, line), s, label);
        y += line;
    }

    void Bar(ref float y, float x, float w, float line, string name, float v, float max, Color col)
    {
        float labelW = 70 * (line / 18f);
        GUI.Label(new Rect(x, y, labelW, line), name, label);
        float bx = x + labelW, bw = w - labelW, bh = line * 0.6f, by = y + line * 0.2f;
        GUI.color = new Color(0.15f, 0.15f, 0.15f);
        GUI.DrawTexture(new Rect(bx, by, bw, bh), Texture2D.whiteTexture);
        GUI.color = col;
        GUI.DrawTexture(new Rect(bx, by, bw * Mathf.Clamp01(v / max), bh), Texture2D.whiteTexture);
        GUI.color = Color.white;
        y += line;
    }
}
