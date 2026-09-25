using System;
using System.Collections.Generic;
using AnimatedDrawingsWorld.Logic.Behavior;
using AnimatedDrawingsWorld.Logic.Scene;
using UnityEngine;

namespace AnimatedDrawingsWorld.Runtime
{
    // Immediate-mode UI: toolbar (backgrounds, characters, analysis overlay, speed), a panel
    // for the selected character, speech/emote bubbles and the event log.
    [RequireComponent(typeof(GameController))]
    public sealed class GameUI : MonoBehaviour
    {
        private GameController game;
        private readonly RuntimeFileBrowser browser = new();
        private readonly List<Rect> uiRects = new();
        private enum Popup { None, Backgrounds, Characters, Url }
        private Popup popup;
        private bool urlForBackground;
        private string url = "https://";
        private Texture2D white;
        private GUIStyle bubble, small, header, panelBox;
        private float scale = 1f;

        private static readonly Activity[] Commands =
        {
            Activity.Walk, Activity.Run, Activity.Wave, Activity.Dance, Activity.Jump, Activity.Talk,
            Activity.Climb, Activity.Sit, Activity.Sleep, Activity.Swim, Activity.Smell, Activity.Hide,
            Activity.Scare, Activity.Surprised,
        };

        private void Awake()
        {
            game = GetComponent<GameController>();
            game.IsPointerOverUi = IsOverUi;
            white = Texture2D.whiteTexture;
        }

        private bool IsOverUi(Vector2 screen)
        {
            var gui = new Vector2(screen.x, Screen.height - screen.y) / scale;
            if (browser.Visible || popup == Popup.Url) return true;
            foreach (var r in uiRects) if (r.Contains(gui)) return true;
            return false;
        }

        private Rect Track(Rect r)
        {
            uiRects.Add(r);
            return r;
        }

        private void EnsureStyles()
        {
            if (bubble != null) return;
            bubble = new GUIStyle(GUI.skin.box) { wordWrap = true, fontSize = 13, alignment = TextAnchor.MiddleCenter, richText = true };
            bubble.normal.textColor = Color.black;
            bubble.normal.background = MakeTexture(new Color(1f, 1f, 1f, 0.92f));
            small = new GUIStyle(GUI.skin.label) { fontSize = 12, richText = true, wordWrap = true };
            header = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold, richText = true };
            panelBox = new GUIStyle(GUI.skin.box);
            panelBox.normal.background = MakeTexture(new Color(0.12f, 0.12f, 0.16f, 0.85f));
        }

        private static Texture2D MakeTexture(Color c)
        {
            var t = new Texture2D(1, 1);
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }

        private void OnGUI()
        {
            EnsureStyles();
            // scale the UI on high-DPI screens
            scale = Mathf.Max(1f, Screen.dpi > 0 ? Screen.dpi / 120f : 1f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            if (Event.current.type == EventType.Layout) uiRects.Clear();

            if (game.World != null)
            {
                if (game.ShowAnalysis) DrawAnalysis();
                DrawBubbles();
            }

            DrawToolbar();
            DrawSelectedPanel();
            DrawLog();
            DrawPopups();
            browser.OnGUI();
        }

        // ------------------------------------------------------------------ toolbar

        private void DrawToolbar()
        {
            var width = Screen.width / scale;
            var bar = Track(new Rect(0, 0, width, 34));
            GUI.Box(bar, GUIContent.none, panelBox);
            GUILayout.BeginArea(new Rect(6, 5, width - 12, 26));
            GUILayout.BeginHorizontal();

            if (GUILayout.Button("Background...", GUILayout.Width(110))) popup = popup == Popup.Backgrounds ? Popup.None : Popup.Backgrounds;
            if (GUILayout.Button("Add character...", GUILayout.Width(125))) popup = popup == Popup.Characters ? Popup.None : Popup.Characters;

            var kindLabel = game.NewCharacterKind.HasValue ? game.NewCharacterKind.Value.ToString() : "Auto";
            if (GUILayout.Button("New is: " + kindLabel, GUILayout.Width(120)))
                game.NewCharacterKind = game.NewCharacterKind switch
                {
                    null => CharacterKind.Human,
                    CharacterKind.Human => CharacterKind.Monster,
                    CharacterKind.Monster => CharacterKind.Animal,
                    _ => null,
                };

            game.ShowAnalysis = GUILayout.Toggle(game.ShowAnalysis, " Show analysis", GUILayout.Width(115));
            if (GUILayout.Button(game.Paused ? "Play" : "Pause", GUILayout.Width(80))) game.Paused = !game.Paused;
            if (GUILayout.Button($"Speed {game.Speed:0.#}x", GUILayout.Width(85))) game.Speed = game.Speed >= 3f ? 0.5f : game.Speed * 2f;

            GUILayout.Label((game.Busy ? "[working] " : "") + game.Status, small);
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        private void DrawPopups()
        {
            if (popup == Popup.Backgrounds)
            {
                var items = game.Samples.Backgrounds;
                var rect = Track(new Rect(6, 36, 260, 30 + (items.Count + 2) * 26));
                GUI.Box(rect, "Change the background", panelBox);
                GUILayout.BeginArea(new Rect(rect.x + 6, rect.y + 24, rect.width - 12, rect.height - 28));
                foreach (var b in items)
                    if (GUILayout.Button(b.Name)) { game.LoadSampleBackground(b); popup = Popup.None; }
                if (GUILayout.Button("Open image file...")) { OpenFile("Choose a background drawing", game.LoadBackgroundFromPathOrUrl); popup = Popup.None; }
                if (GUILayout.Button("From a web address...")) { urlForBackground = true; popup = Popup.Url; }
                GUILayout.EndArea();
            }
            else if (popup == Popup.Characters)
            {
                var items = game.Samples.Characters;
                var rect = Track(new Rect(120, 36, 280, 30 + (items.Count + 2) * 26));
                GUI.Box(rect, "Add a drawn character", panelBox);
                GUILayout.BeginArea(new Rect(rect.x + 6, rect.y + 24, rect.width - 12, rect.height - 28));
                foreach (var c in items)
                    if (GUILayout.Button($"{c.Name} ({c.Kind.ToString().ToLowerInvariant()})")) { game.LoadSampleCharacter(c); popup = Popup.None; }
                if (GUILayout.Button("Open image file...")) { OpenFile("Choose a character drawing", game.LoadCharacterFromPathOrUrl); popup = Popup.None; }
                if (GUILayout.Button("From a web address...")) { urlForBackground = false; popup = Popup.Url; }
                GUILayout.EndArea();
            }
            else if (popup == Popup.Url)
            {
                var rect = Track(new Rect((Screen.width / scale - 460) * 0.5f, 120, 460, 96));
                GUI.Box(rect, urlForBackground ? "Background image URL" : "Character drawing URL", panelBox);
                url = GUI.TextField(new Rect(rect.x + 10, rect.y + 28, rect.width - 20, 24), url);
                if (GUI.Button(new Rect(rect.x + 10, rect.y + 60, 100, 26), "Load"))
                {
                    if (urlForBackground) game.LoadBackgroundFromPathOrUrl(url);
                    else game.LoadCharacterFromPathOrUrl(url);
                    popup = Popup.None;
                }
                if (GUI.Button(new Rect(rect.x + 120, rect.y + 60, 100, 26), "Cancel")) popup = Popup.None;
            }
        }

        private void OpenFile(string title, Action<string> picked)
        {
#if UNITY_EDITOR
            var path = UnityEditor.EditorUtility.OpenFilePanel(title, "", "png,jpg,jpeg");
            if (!string.IsNullOrEmpty(path)) picked(path);
#else
            browser.Open(title, picked);
#endif
        }

        // ------------------------------------------------------------------ selected character

        private void DrawSelectedPanel()
        {
            var view = game.Selected;
            if (view == null) return;
            var agent = view.Agent;
            var width = 250f;
            var rect = Track(new Rect(Screen.width / scale - width - 6, 40, width, 330));
            GUI.Box(rect, GUIContent.none, panelBox);
            GUILayout.BeginArea(new Rect(rect.x + 8, rect.y + 6, rect.width - 16, rect.height - 12));
            GUILayout.Label($"<color=white>{agent.Name}</color>", header);

            GUILayout.BeginHorizontal();
            GUILayout.Label($"<color=#ddd>is a</color>", small, GUILayout.Width(30));
            foreach (CharacterKind kind in Enum.GetValues(typeof(CharacterKind)))
            {
                var on = GUILayout.Toggle(agent.Kind == kind, kind.ToString(), GUI.skin.button);
                if (on && agent.Kind != kind) game.SetKind(view, kind);
            }
            GUILayout.EndHorizontal();

            GUILayout.Label($"<color=#ddd>{agent.Activity} — {agent.Thought}</color>", small);
            Bar("Energy", agent.Energy, new Color(0.4f, 0.8f, 0.4f));
            Bar("Friendly", agent.Sociability, new Color(0.9f, 0.6f, 0.8f));
            Bar("Curious", agent.Curiosity, new Color(0.5f, 0.7f, 1f));

            GUILayout.Space(4);
            for (var i = 0; i < Commands.Length; i += 3)
            {
                GUILayout.BeginHorizontal();
                for (var k = i; k < Math.Min(i + 3, Commands.Length); k++)
                    if (GUILayout.Button(Commands[k].ToString())) game.World.Command(agent, Commands[k]);
                GUILayout.EndHorizontal();
            }
            GUILayout.Space(4);
            GUILayout.Label("<color=#aaa>Drag to move • right-click the ground to walk there</color>", small);
            if (GUILayout.Button("Remove from drawing")) game.Remove(view);
            GUILayout.EndArea();
        }

        private void Bar(string label, float value, Color color)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<color=#ddd>{label}</color>", small, GUILayout.Width(60));
            var r = GUILayoutUtility.GetRect(100, 12, GUILayout.ExpandWidth(true));
            GUI.color = new Color(1, 1, 1, 0.2f);
            GUI.DrawTexture(r, white);
            GUI.color = color;
            GUI.DrawTexture(new Rect(r.x, r.y, r.width * Mathf.Clamp01(value), r.height), white);
            GUI.color = Color.white;
            GUILayout.EndHorizontal();
        }

        // ------------------------------------------------------------------ overlays

        private Vector2 ToGui(Vector3 world)
        {
            var s = game.Camera.WorldToScreenPoint(world);
            return new Vector2(s.x, Screen.height - s.y) / scale;
        }

        private void DrawBubbles()
        {
            foreach (var view in game.Views)
            {
                var agent = view.Agent;
                if (agent.Opacity < 0.2f) continue;
                var head = ToGui(view.HeadPosition + Vector3.up * 0.25f);
                string text = null;
                if (agent.EmoteTimer > 0f) text = $"<size=18><b>{agent.Emote}</b></size>";
                else if (agent.Activity == Activity.Sleep) text = "<size=16><b>Zzz</b></size>";
                if (view == game.Selected && !string.IsNullOrEmpty(agent.Thought))
                    text = (text != null ? text + "  " : "") + agent.Thought;
                if (text == null) continue;
                var content = new GUIContent(text);
                var size = bubble.CalcSize(content);
                size.x = Mathf.Min(size.x + 8, 220);
                size.y = bubble.CalcHeight(content, size.x);
                GUI.Box(new Rect(head.x - size.x * 0.5f, head.y - size.y - 4, size.x, size.y), content, bubble);
            }
        }

        private void DrawAnalysis()
        {
            var layout = game.Layout;
            var world = game.World;
            foreach (var o in layout.Objects)
            {
                var r = world.ToWorld(o.Bounds);
                var a = ToGui(new Vector3(r.XMin, r.YMax));
                var b = ToGui(new Vector3(r.XMax, r.YMin));
                var color = ColorFor(o.Kind);
                Frame(new Rect(a.x, a.y, b.x - a.x, b.y - a.y), color, 2f);
                var label = $"{o.Label} {(int)(o.Confidence * 100)}%";
                var labelRect = new Rect(a.x, a.y - 18, Mathf.Max(60, GUI.skin.label.CalcSize(new GUIContent(label)).x + 8), 18);
                GUI.color = color;
                GUI.DrawTexture(labelRect, white);
                GUI.color = Color.white;
                GUI.Label(labelRect, $"<color=black>{label}</color>", small);
            }

            // the walkable ground line and band
            var samples = 64;
            GUI.color = new Color(1f, 0f, 1f, 0.9f);
            for (var i = 0; i <= samples; i++)
            {
                var x = world.Width * i / samples;
                var top = ToGui(new Vector3(x, world.GroundTop(x)));
                var bottom = ToGui(new Vector3(x, world.BandBottom(x)));
                GUI.DrawTexture(new Rect(top.x - 2, top.y - 2, 4, 4), white);
                GUI.DrawTexture(new Rect(bottom.x - 1, bottom.y - 1, 2, 2), white);
            }
            GUI.color = Color.white;
        }

        private void Frame(Rect r, Color color, float t)
        {
            GUI.color = color;
            GUI.DrawTexture(new Rect(r.x, r.y, r.width, t), white);
            GUI.DrawTexture(new Rect(r.x, r.yMax - t, r.width, t), white);
            GUI.DrawTexture(new Rect(r.x, r.y, t, r.height), white);
            GUI.DrawTexture(new Rect(r.xMax - t, r.y, t, r.height), white);
            GUI.color = Color.white;
        }

        private static Color ColorFor(SceneObjectKind kind) => kind switch
        {
            SceneObjectKind.Sky => new Color(0.3f, 0.6f, 1f),
            SceneObjectKind.Grass => new Color(0.2f, 0.8f, 0.2f),
            SceneObjectKind.Water => new Color(0.1f, 0.3f, 0.9f),
            SceneObjectKind.Tree => new Color(0.55f, 0.3f, 0.05f),
            SceneObjectKind.Bush => new Color(0.3f, 0.6f, 0.2f),
            SceneObjectKind.Sun => new Color(1f, 0.7f, 0f),
            SceneObjectKind.Cloud => new Color(0.6f, 0.6f, 1f),
            SceneObjectKind.House => new Color(0.9f, 0.1f, 0.1f),
            SceneObjectKind.Flower => new Color(1f, 0.2f, 0.8f),
            SceneObjectKind.Rock => new Color(0.45f, 0.45f, 0.45f),
            SceneObjectKind.Mountain => new Color(0.5f, 0.2f, 0.7f),
            _ => new Color(0.1f, 0.1f, 0.1f),
        };

        private void DrawLog()
        {
            var lines = Math.Min(6, game.Log.Count);
            if (lines == 0) return;
            var rect = new Rect(6, Screen.height / scale - lines * 17 - 12, 460, lines * 17 + 8);
            GUI.color = new Color(1, 1, 1, 0.85f);
            GUI.Box(rect, GUIContent.none, panelBox);
            GUI.color = Color.white;
            for (var i = 0; i < lines; i++)
                GUI.Label(new Rect(rect.x + 6, rect.y + 4 + i * 17, rect.width - 12, 18), "<color=#eee>" + game.Log[game.Log.Count - lines + i] + "</color>", small);
        }
    }
}
