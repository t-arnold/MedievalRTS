using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// On-screen debug info (IMGUI, no setup): FPS, unit counts, controls, the player's
/// health/stamina/poise/state, a combat log of hits involving the player, and bars over nearby units.
/// Added automatically by TestArena. Toggle with 1.
/// </summary>
public class DebugOverlay : MonoBehaviour
{
    public bool show = true;
    public int maxUnitBars = 60;

    TestArena arena;
    float fps = 60f;
    GUIStyle label, small;
    readonly Queue<string> log = new Queue<string>();
    CombatStateMachine logged;

    void Awake() => arena = GetComponent<TestArena>();

    void Update()
    {
        fps = Mathf.Lerp(fps, 1f / Mathf.Max(0.0001f, Time.unscaledDeltaTime), 0.05f);

        // Hook the combat log onto the current player (re-hooks after a reset).
        var p = arena ? arena.Player : null;
        if (p && p != logged)
        {
            logged = p;
            p.OnHitLanded += r => Log($"You → {r.defender.name} ({r.attack.name}): {r.type}");
            p.OnHitReceived += r => Log($"{r.attacker.name} → you ({r.attack.name}): {r.type}");
        }
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

        float x = 10 * scale, y = 10 * scale, w = 360 * scale, line = 18 * scale;

        int allies = 0, enemies = 0;
        foreach (var c in CombatStateMachine.All)
        {
            if (c.State == CombatState.Dead) continue;
            if (c.team == 0) allies++; else enemies++;
        }

        GUI.color = new Color(0, 0, 0, 0.6f);
        GUI.DrawTexture(new Rect(x - 5, y - 5, w, line * 17), Texture2D.whiteTexture);
        GUI.color = Color.white;

        Text(ref y, x, line, $"FPS {fps:0}   units: {allies} blue / {enemies} red   timeScale {Time.timeScale:0.##}");
        Text(ref y, x, line, "WASD move · mouse aim · LMB light (tap again = combo) · E heavy");
        Text(ref y, x, line, "RMB block (cyan = parry window) · Space dodge · Shift sprint");
        Text(ref y, x, line, "1 overlay · 2 slow-mo · 3 skirmish · 4 big battle · 5 god mode · R reset");
        y += line * 0.5f;

        var p = arena ? arena.Player : null;
        if (p)
        {
            Text(ref y, x, line, $"State: {p.State}  ({p.StateTime:0.00}s)  {(arena.PlayerInvulnerable ? "[GOD MODE]" : "")}");
            Bar(ref y, x, w - 20 * scale, line, "HP", p.Stats.Health, p.Stats.maxHealth, new Color(0.85f, 0.2f, 0.2f));
            Bar(ref y, x, w - 20 * scale, line, "Stamina", p.Stats.Stamina, p.Stats.maxStamina, new Color(0.3f, 0.8f, 0.3f));
            Bar(ref y, x, w - 20 * scale, line, "Poise", p.Stats.Poise, p.Stats.maxPoise, new Color(0.9f, 0.75f, 0.2f));
            if (p.State == CombatState.Dead) Text(ref y, x, line, "YOU DIED — press R");
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

        int drawn = 0;
        foreach (var c in CombatStateMachine.All)
        {
            if (c == (arena ? arena.Player : null) || c.State == CombatState.Dead) continue;
            if (drawn >= maxUnitBars) break;
            if ((c.transform.position - centre).sqrMagnitude > 100f) continue; // within 10 units of player
            drawn++;

            Vector3 sp = cam.WorldToScreenPoint(c.transform.position + Vector3.up * 0.95f);
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
            if (c.State != CombatState.Idle)
                GUI.Label(new Rect(bx, by - 14 * scale, 120 * scale, 16 * scale), c.State.ToString(), small);
        }
    }

    void Text(ref float y, float x, float line, string s)
    {
        GUI.Label(new Rect(x, y, 2000, line), s, label);
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
