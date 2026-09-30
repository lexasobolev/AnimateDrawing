using System;
using AnimatedDrawingsWorld.Logic.Audio;
using AnimatedDrawingsWorld.Logic.Behavior;
using UnityEngine;

namespace AnimatedDrawingsWorld.Runtime
{
    // Deliberately minimal UI: big round buttons and, for a clicked character, one readable card.
    //   top-left      monster       add a character    } each opens two choices:
    //   top-right     house + sun   change the background } camera (take a photo) / gallery (pick one)
    //   bottom        cross         remove all characters     speaker   sound on/off
    //   right side    card of the clicked character: kind, mood, needs, things to do
    // Drag characters around; tap (or right-click) the ground to send the selected character there.
    [RequireComponent(typeof(GameController))]
    public sealed class GameUI : MonoBehaviour
    {
        private GameController game;
        private readonly RuntimeFileBrowser browser = new();
        private Texture2D characterIcon, backgroundIcon, clearIcon, soundOnIcon, soundOffIcon, cameraIcon, galleryIcon, white;
        private CameraCapture cameraCapture;
        // which big button's choices (camera / gallery) are open
        private Button chooser;
        private GUIStyle bubble, toast, cardTitle, cardText, cardButton, cardToggle, card;
        private enum Button { None, Character, Background, Clear, Sound, TakePhoto, PickPicture }
        private Button busyButton;
        private Rect cardRect;

        private static readonly Activity[] Commands =
        {
            Activity.Walk, Activity.Run, Activity.Wave, Activity.Dance, Activity.Jump, Activity.Talk,
            Activity.Climb, Activity.Sit, Activity.Sleep, Activity.Swim, Activity.Smell, Activity.Hide,
            Activity.Scare, Activity.Surprised,
        };

        private void Awake()
        {
            game = GetComponent<GameController>();
            game.IsPointerOverUi = screen =>
            {
                var gui = new Vector2(screen.x, Screen.height - screen.y);
                return browser.Visible || cameraCapture.Visible || chooser != Button.None || HitButton(gui) != Button.None ||
                       (game.Selected != null && cardRect.Contains(gui));
            };
            characterIcon = IconPainter.AddCharacter();
            backgroundIcon = IconPainter.AddBackground();
            clearIcon = IconPainter.Clear();
            soundOnIcon = IconPainter.SoundOn();
            soundOffIcon = IconPainter.SoundOff();
            cameraIcon = IconPainter.CameraChoice();
            galleryIcon = PhotoSource.HasPhoneGallery ? IconPainter.GalleryChoice() : IconPainter.FolderChoice();
            cameraCapture = GetComponent<CameraCapture>();
            if (cameraCapture == null) cameraCapture = gameObject.AddComponent<CameraCapture>();
            white = Texture2D.whiteTexture;
        }

        private void OnDestroy()
        {
            foreach (var t in new[] { characterIcon, backgroundIcon, clearIcon, soundOnIcon, soundOffIcon, cameraIcon, galleryIcon }) Destroy(t);
        }

        // big enough for a child's finger: ~16% of the short screen side
        private float ButtonSize => Mathf.Clamp(Mathf.Min(Screen.width, Screen.height) * 0.16f, 72f, 180f);
        private float Margin => ButtonSize * 0.15f;
        private bool SoundMuted => game.Sounds != null && game.Sounds.Muted;

        private Rect RectOf(Button b)
        {
            var safe = CameraCapture.SafeAreaGui();
            var s = ButtonSize;
            var m = Margin;
            var small = s * 0.75f;
            var bottom = safe.yMax - m - small;
            switch (b)
            {
                case Button.Character: return new Rect(safe.x + m, safe.y + m, s, s);
                case Button.Background: return new Rect(safe.xMax - m - s, safe.y + m, s, s);
                case Button.Clear: return new Rect(safe.center.x - small - m * 0.5f, bottom, small, small);
                case Button.Sound: return new Rect(safe.center.x + m * 0.5f, bottom, small, small);
                case Button.TakePhoto:
                case Button.PickPicture:
                {
                    // the two choices line up beside the button that opened them, toward the middle
                    var owner = RectOf(chooser);
                    var option = s * 0.8f;
                    var index = b == Button.TakePhoto ? 0 : 1;
                    var y = owner.center.y - option * 0.5f;
                    return chooser == Button.Character
                        ? new Rect(owner.xMax + m + index * (option + m), y, option, option)
                        : new Rect(owner.x - m - option - index * (option + m), y, option, option);
                }
                default: return Rect.zero;
            }
        }

        private Button HitButton(Vector2 gui)
        {
            foreach (Button b in new[] { Button.TakePhoto, Button.PickPicture, Button.Character, Button.Background, Button.Clear, Button.Sound })
            {
                if (b == Button.Clear && game.Views.Count == 0) continue;
                if ((b == Button.TakePhoto || b == Button.PickPicture) && chooser == Button.None) continue;
                if (b == Button.TakePhoto && !CameraCapture.HasCamera) continue;
                var r = RectOf(b);
                if ((gui - r.center).magnitude <= r.width * 0.5f) return b;
            }
            return Button.None;
        }

        private void Update()
        {
            // Android back button / Escape: close the innermost thing that is open
            if (!Input.GetKeyDown(KeyCode.Escape) || cameraCapture.Visible) return;
            if (chooser != Button.None) chooser = Button.None;
            else if (game.Selected != null) game.Select(null);
        }

        private void OnGUI()
        {
            if (cameraCapture.Visible) return; // the camera screen covers everything
            EnsureStyles();
            if (game.World != null) DrawBubbles();

            DrawButton(Button.Character, characterIcon);
            DrawButton(Button.Background, backgroundIcon);
            if (game.Views.Count > 0) DrawButton(Button.Clear, clearIcon);
            DrawButton(Button.Sound, SoundMuted ? soundOffIcon : soundOnIcon);
            if (chooser != Button.None)
            {
                if (CameraCapture.HasCamera) DrawButton(Button.TakePhoto, cameraIcon);
                DrawButton(Button.PickPicture, galleryIcon);
            }
            DrawCharacterCard();

            if (!game.Busy) busyButton = Button.None;
            DrawErrorToast();
            browser.OnGUI();

            if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && !browser.Visible)
            {
                var hit = HitButton(Event.current.mousePosition);
                if (hit == Button.None)
                {
                    // tapping anywhere else just closes the choices
                    if (chooser != Button.None) chooser = Button.None;
                    return;
                }
                if (game.Busy && (hit == Button.Character || hit == Button.Background)) return;
                Event.current.Use();
                Press(hit);
            }
        }

        private void Click()
        {
            if (game.Sounds != null) game.Sounds.PlayUi(SoundKind.Click);
        }

        private void Press(Button b)
        {
            switch (b)
            {
                case Button.Character:
                case Button.Background:
                    Click();
                    chooser = chooser == b ? Button.None : b;
                    break;
                case Button.TakePhoto:
                {
                    Click();
                    var forCharacter = chooser == Button.Character;
                    chooser = Button.None;
                    cameraCapture.Open(photo => UsePhoto(photo, forCharacter, "Photo"));
                    break;
                }
                case Button.PickPicture:
                {
                    Click();
                    var forCharacter = chooser == Button.Character;
                    chooser = Button.None;
                    PickPicture(forCharacter);
                    break;
                }
                case Button.Clear:
                    game.ClearCharacters();
                    break;
                case Button.Sound:
                    if (game.Sounds == null) break;
                    game.Sounds.SetMuted(!game.Sounds.Muted);
                    Click();
                    break;
            }
        }

        private void UsePhoto(Texture2D photo, bool forCharacter, string name)
        {
            if (photo == null) return;
            busyButton = forCharacter ? Button.Character : Button.Background;
            if (forCharacter) game.AddCharacterFromPhoto(photo, name);
            else game.LoadBackgroundFromTexture(photo, name);
        }

        private void PickPicture(bool forCharacter)
        {
            var title = forCharacter ? "Choose a drawing of a character" : "Choose a drawing for the background";
            if (PhotoSource.HasPhoneGallery)
            {
                PhotoSource.PickFromGallery(title, picture => UsePhoto(picture, forCharacter, "Picture"));
                return;
            }
            PickImage(title, forCharacter ? "Samples/Characters" : "Samples/Backgrounds", path =>
            {
                busyButton = forCharacter ? Button.Character : Button.Background;
                if (forCharacter) game.LoadCharacterFromPathOrUrl(path);
                else game.LoadBackgroundFromPathOrUrl(path);
            });
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
            var hover = (Event.current.mousePosition - r.center).magnitude <= r.width * 0.5f;
            var scale = hover ? 1.06f : 1f;
            // a gentle pulse while this button's drawing is being processed
            if (busyButton == b && game.Busy) scale = 1f + 0.06f * Mathf.Sin(Time.unscaledTime * 8f);
            var size = r.width * scale;
            var drawn = new Rect(r.center.x - size * 0.5f, r.center.y - size * 0.5f, size, size);
            var dimmed = game.Busy && busyButton != b && (b == Button.Character || b == Button.Background);
            GUI.color = dimmed ? new Color(1f, 1f, 1f, 0.5f) : Color.white;
            GUI.DrawTexture(drawn, icon, ScaleMode.ScaleToFit, true);
            GUI.color = Color.white;
        }

        // ------------------------------------------------------------------ character card

        private void DrawCharacterCard()
        {
            var view = game.Selected;
            if (view == null)
            {
                cardRect = Rect.zero;
                return;
            }

            // text scales with the screen, and shrinks only if the card wouldn't fit
            var safe = CameraCapture.SafeAreaGui();
            var top = safe.y + Margin * 2f + ButtonSize;
            var available = safe.yMax - top - ButtonSize - Margin * 2f;
            var font = Mathf.Clamp(Screen.height / 34f, 18f, 34f);
            font = Mathf.Max(13f, Mathf.Min(font, available / 22f));
            var fontSize = Mathf.RoundToInt(font);
            cardTitle.fontSize = Mathf.RoundToInt(font * 1.45f);
            cardText.fontSize = fontSize;
            cardButton.fontSize = Mathf.RoundToInt(font * 0.9f);
            cardToggle.fontSize = Mathf.RoundToInt(font * 0.9f);
            var row = font * 1.75f;

            var width = Mathf.Clamp(font * 17f, 280f, safe.width * 0.45f);
            var height = Mathf.Min(available, font * 22f);
            cardRect = new Rect(safe.xMax - Margin - width, top, width, height);
            GUI.Box(cardRect, GUIContent.none, card);

            var agent = view.Agent;
            var pad = font * 0.7f;
            GUILayout.BeginArea(new Rect(cardRect.x + pad, cardRect.y + pad * 0.6f, cardRect.width - pad * 2f, cardRect.height - pad * 1.2f));

            GUILayout.BeginHorizontal();
            GUILayout.Label(agent.Name, cardTitle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("X", cardButton, GUILayout.Width(row), GUILayout.Height(row))) game.Select(null);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            foreach (CharacterKind kind in Enum.GetValues(typeof(CharacterKind)))
            {
                var on = GUILayout.Toggle(agent.Kind == kind, kind.ToString(), cardToggle, GUILayout.Height(row));
                if (on && agent.Kind != kind) game.SetKind(view, kind);
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(font * 0.3f);
            GUILayout.Label($"{Describe(agent.Activity)}{(string.IsNullOrEmpty(agent.Thought) ? "" : " — " + agent.Thought)}", cardText, GUILayout.MinHeight(font * 2.6f));

            Bar("Energy", agent.Energy, new Color(0.35f, 0.8f, 0.35f), font);
            Bar("Friendly", agent.Sociability, new Color(0.95f, 0.55f, 0.75f), font);
            Bar("Curious", agent.Curiosity, new Color(0.45f, 0.65f, 1f), font);

            GUILayout.Space(font * 0.4f);
            for (var i = 0; i < Commands.Length; i += 3)
            {
                GUILayout.BeginHorizontal();
                for (var k = i; k < Math.Min(i + 3, Commands.Length); k++)
                    if (GUILayout.Button(Commands[k].ToString(), cardButton, GUILayout.Height(row))) game.World.Command(agent, Commands[k]);
                GUILayout.EndHorizontal();
            }
            GUILayout.Space(font * 0.3f);
            if (GUILayout.Button("Remove " + agent.Name, cardButton, GUILayout.Height(row))) game.Remove(view);
            GUILayout.EndArea();
        }

        private static string Describe(Activity a) => a switch
        {
            Activity.Idle => "Standing",
            Activity.Walk => "Walking",
            Activity.Run => "Running",
            Activity.Climb => "Climbing",
            Activity.Sit => "Sitting",
            Activity.Sleep => "Sleeping",
            Activity.Yawn => "Yawning",
            Activity.Wave => "Waving",
            Activity.Surprised => "Surprised",
            Activity.Dance => "Dancing",
            Activity.Jump => "Jumping",
            Activity.Swim => "Swimming",
            Activity.Smell => "Smelling",
            Activity.Scare => "Scaring",
            Activity.Fall => "Flying",
            Activity.Hide => "Hiding",
            Activity.Talk => "Chatting",
            _ => a.ToString(),
        };

        private void Bar(string label, float value, Color color, float font)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, cardText, GUILayout.Width(font * 5f));
            var r = GUILayoutUtility.GetRect(100, font * 0.8f, GUILayout.ExpandWidth(true));
            r.y += font * 0.35f;
            GUI.color = new Color(1, 1, 1, 0.18f);
            GUI.DrawTexture(r, white);
            GUI.color = color;
            GUI.DrawTexture(new Rect(r.x, r.y, r.width * Mathf.Clamp01(value), r.height), white);
            GUI.color = Color.white;
            GUILayout.EndHorizontal();
        }

        // ------------------------------------------------------------------ styles & overlays

        private void EnsureStyles()
        {
            if (bubble != null) return;
            bubble = new GUIStyle(GUI.skin.box) { fontSize = 20, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            bubble.normal.textColor = Color.black;
            bubble.normal.background = SolidTexture(new Color(1f, 1f, 1f, 0.9f));
            toast = new GUIStyle(GUI.skin.box) { fontSize = 16, wordWrap = true, alignment = TextAnchor.MiddleCenter };
            toast.normal.textColor = Color.white;
            toast.normal.background = SolidTexture(new Color(0.6f, 0.1f, 0.1f, 0.9f));

            card = new GUIStyle(GUI.skin.box);
            card.normal.background = SolidTexture(new Color(0.1f, 0.1f, 0.14f, 0.88f));
            cardTitle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, wordWrap = false };
            cardTitle.normal.textColor = Color.white;
            cardText = new GUIStyle(GUI.skin.label) { wordWrap = true };
            cardText.normal.textColor = new Color(0.92f, 0.92f, 0.92f);
            cardButton = new GUIStyle(GUI.skin.button) { wordWrap = false };
            cardToggle = new GUIStyle(GUI.skin.button);
            cardToggle.onNormal.background = SolidTexture(new Color(1f, 0.62f, 0.26f));
            cardToggle.onNormal.textColor = Color.black;
            cardToggle.onHover.background = cardToggle.onNormal.background;
            cardToggle.onHover.textColor = Color.black;
        }

        private static Texture2D SolidTexture(Color c)
        {
            var t = new Texture2D(1, 1);
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }

        // moods only — no text to read in the scene itself
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
            GUI.Box(new Rect((Screen.width - w) * 0.5f, Screen.height - ButtonSize - 80f, w, 48f), game.LastError, toast);
        }
    }
}
