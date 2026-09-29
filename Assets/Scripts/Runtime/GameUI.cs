using System;
using AnimatedDrawingsWorld.Logic.Behavior;
using UnityEngine;

namespace AnimatedDrawingsWorld.Runtime
{
    // Deliberately minimal UI: three big round buttons, nothing else on screen.
    //   top-left     monster       add a character (pick a drawing)
    //   top-right    house + sun   change the background (pick a drawing)
    //   bottom       cross         remove all characters
    // Everything else happens in the drawing itself: drag characters around, right-click the
    // ground to send the last touched character there. Emote bubbles (!, ♪, ♥, Zzz) show moods.
    [RequireComponent(typeof(GameController))]
    public sealed class GameUI : MonoBehaviour
    {
        private GameController game;
        private readonly RuntimeFileBrowser browser = new();
        private Texture2D characterIcon, backgroundIcon, clearIcon;
        private GUIStyle bubble, toast;
        private enum Button { None, Character, Background, Clear }
        private Button busyButton;

        private void Awake()
        {
            game = GetComponent<GameController>();
            game.IsPointerOverUi = screen => browser.Visible || HitButton(new Vector2(screen.x, Screen.height - screen.y)) != Button.None;
            characterIcon = IconPainter.AddCharacter();
            backgroundIcon = IconPainter.AddBackground();
            clearIcon = IconPainter.Clear();
        }

        private void OnDestroy()
        {
            Destroy(characterIcon);
            Destroy(backgroundIcon);
            Destroy(clearIcon);
        }

        // big enough for a child's finger: ~16% of the short screen side
        private float ButtonSize => Mathf.Clamp(Mathf.Min(Screen.width, Screen.height) * 0.16f, 72f, 180f);
        private float Margin => ButtonSize * 0.15f;

        private Rect RectOf(Button b)
        {
            var s = ButtonSize;
            var m = Margin;
            return b switch
            {
                Button.Character => new Rect(m, m, s, s),
                Button.Background => new Rect(Screen.width - m - s, m, s, s),
                Button.Clear => new Rect((Screen.width - s * 0.75f) * 0.5f, Screen.height - m - s * 0.75f, s * 0.75f, s * 0.75f),
                _ => Rect.zero,
            };
        }

        private Button HitButton(Vector2 gui)
        {
            foreach (Button b in new[] { Button.Character, Button.Background, Button.Clear })
            {
                if (b == Button.Clear && game.Views.Count == 0) continue;
                var r = RectOf(b);
                if ((gui - r.center).magnitude <= r.width * 0.5f) return b;
            }
            return Button.None;
        }

        private void OnGUI()
        {
            EnsureStyles();
            if (game.World != null) DrawBubbles();

            DrawButton(Button.Character, characterIcon);
            DrawButton(Button.Background, backgroundIcon);
            if (game.Views.Count > 0) DrawButton(Button.Clear, clearIcon);

            if (!game.Busy) busyButton = Button.None;
            DrawErrorToast();
            browser.OnGUI();

            if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && !browser.Visible && !game.Busy)
            {
                var hit = HitButton(Event.current.mousePosition);
                if (hit == Button.None) return;
                Event.current.Use();
                Press(hit);
            }
        }

        private void Press(Button b)
        {
            switch (b)
            {
                case Button.Character:
                    PickImage("Choose a drawing of a character", "Samples/Characters", path =>
                    {
                        busyButton = Button.Character;
                        game.LoadCharacterFromPathOrUrl(path);
                    });
                    break;
                case Button.Background:
                    PickImage("Choose a drawing for the background", "Samples/Backgrounds", path =>
                    {
                        busyButton = Button.Background;
                        game.LoadBackgroundFromPathOrUrl(path);
                    });
                    break;
                case Button.Clear:
                    game.ClearCharacters();
                    break;
            }
        }

        private void PickImage(string title, string samplesFolder, Action<string> picked)
        {
#if UNITY_EDITOR
            var path = UnityEditor.EditorUtility.OpenFilePanel(title, StreamingAssetsIO.PathFor(samplesFolder), "png,jpg,jpeg");
            if (!string.IsNullOrEmpty(path)) picked(path);
#else
            browser.Open(title, picked);
#endif
        }

        private void DrawButton(Button b, Texture2D icon)
        {
            var r = RectOf(b);
            var hover = r.Contains(Event.current.mousePosition) && (Event.current.mousePosition - r.center).magnitude <= r.width * 0.5f;
            var scale = hover ? 1.06f : 1f;
            // a gentle pulse while this button's drawing is being processed
            if (busyButton == b && game.Busy) scale = 1f + 0.06f * Mathf.Sin(Time.unscaledTime * 8f);
            var size = r.width * scale;
            var drawn = new Rect(r.center.x - size * 0.5f, r.center.y - size * 0.5f, size, size);
            GUI.color = game.Busy && busyButton != b ? new Color(1f, 1f, 1f, 0.5f) : Color.white;
            GUI.DrawTexture(drawn, icon, ScaleMode.ScaleToFit, true);
            GUI.color = Color.white;
        }

        private void EnsureStyles()
        {
            if (bubble != null) return;
            bubble = new GUIStyle(GUI.skin.box) { fontSize = 20, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            bubble.normal.textColor = Color.black;
            bubble.normal.background = SolidTexture(new Color(1f, 1f, 1f, 0.9f));
            toast = new GUIStyle(GUI.skin.box) { fontSize = 14, wordWrap = true, alignment = TextAnchor.MiddleCenter };
            toast.normal.textColor = Color.white;
            toast.normal.background = SolidTexture(new Color(0.6f, 0.1f, 0.1f, 0.9f));
        }

        private static Texture2D SolidTexture(Color c)
        {
            var t = new Texture2D(1, 1);
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }

        // moods only — no text to read
        private void DrawBubbles()
        {
            foreach (var view in game.Views)
            {
                var agent = view.Agent;
                if (agent.Opacity < 0.2f) continue;
                string text = null;
                if (agent.EmoteTimer > 0f) text = agent.Emote;
                else if (agent.Activity == Activity.Sleep) text = "Zzz";
                if (text == null) continue;
                var s = game.Camera.WorldToScreenPoint(view.HeadPosition + Vector3.up * 0.3f);
                var size = bubble.CalcSize(new GUIContent(text)) + new Vector2(12, 4);
                GUI.Box(new Rect(s.x - size.x * 0.5f, Screen.height - s.y - size.y, size.x, size.y), text, bubble);
            }
        }

        // errors are rare but must never look like a silent blank screen
        private void DrawErrorToast()
        {
            if (string.IsNullOrEmpty(game.LastError) || Time.unscaledTime - game.LastErrorTime > 8f) return;
            var w = Mathf.Min(560f, Screen.width - 40f);
            GUI.Box(new Rect((Screen.width - w) * 0.5f, Screen.height - ButtonSize - 70f, w, 44f), game.LastError, toast);
        }
    }
}
