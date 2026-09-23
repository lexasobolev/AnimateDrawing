using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Video;

namespace AnimatedDrawingsWorld.Core
{
    [RequireComponent(typeof(CharacterStateController))]
    public class MetaAnimatedDrawingPlayer : MonoBehaviour
    {
        [SerializeField] private string clipsRoot = "AnimatedDrawings/Character";
        [SerializeField] private bool hideSourceSprite = true;
        [SerializeField] private float videoScale = 1f;

        // must match CHROMA_KEY_RGB in Tools/AnimatedDrawings/unity_animate.py, which clears
        // the render background to this color since exported MP4 can't carry real alpha
        [SerializeField] private Color chromaKeyColor = new(1f, 0f, 1f, 1f);

        // fires each time the currently-playing clip completes a loop, so CharacterStateController
        // knows it's safe to switch to a different state without cutting the animation short
        public event Action AnimationLoopCompleted;

        private readonly Dictionary<BehaviorState, VideoPlayer> players = new();
        private CharacterStateController controller;
        private SpriteRenderer sourceRenderer;
        private GameObject videoRoot;
        private BehaviorState currentState;

        private void Awake()
        {
            controller = GetComponent<CharacterStateController>();
            sourceRenderer = GetComponent<SpriteRenderer>();
            currentState = controller.State;
        }

        private void OnEnable() => controller.StateChanged += OnStateChanged;

        private void OnDisable() => controller.StateChanged -= OnStateChanged;

        private void Start()
        {
            BuildPlayers();
            OnStateChanged(currentState);
        }

        public void Configure(string relativeClipsRoot)
        {
            clipsRoot = relativeClipsRoot.Trim('/');
        }

        private void BuildPlayers()
        {
            videoRoot = new GameObject("Meta Animated Drawing");
            videoRoot.transform.SetParent(transform, false);
            videoRoot.transform.localPosition = new Vector3(0f, 0f, -0.1f);
            videoRoot.transform.localScale = Vector3.one * videoScale;

            var mesh = GameObject.CreatePrimitive(PrimitiveType.Quad);
            mesh.name = "Meta Video Surface";
            mesh.transform.SetParent(videoRoot.transform, false);
            var collider = mesh.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            var meshRenderer = mesh.GetComponent<MeshRenderer>();
            var chromaKeyShader = Shader.Find("Custom/ChromaKeyUnlit");
            if (chromaKeyShader == null)
            {
                Debug.LogWarning($"{name}: Custom/ChromaKeyUnlit shader not found, falling back to opaque Unlit/Texture.", this);
                meshRenderer.material = new Material(Shader.Find("Unlit/Texture"));
            }
            else
            {
                meshRenderer.material = new Material(chromaKeyShader);
                meshRenderer.material.SetColor("_KeyColor", chromaKeyColor);
                // the shader's default _MainTex is Unity's built-in white texture, which the key
                // doesn't match — without this, the quad flashes solid white for the instant before
                // the VideoPlayer decodes its very first frame (e.g. right when the character spawns)
                meshRenderer.material.mainTexture = MakeSolidTexture(chromaKeyColor);
            }

            foreach (BehaviorState state in Enum.GetValues(typeof(BehaviorState)))
            {
                var playerObject = new GameObject($"Meta {state}");
                playerObject.transform.SetParent(videoRoot.transform, false);
                var player = playerObject.AddComponent<VideoPlayer>();
                player.playOnAwake = false;
                player.isLooping = true;
                player.renderMode = VideoRenderMode.MaterialOverride;
                player.targetMaterialRenderer = meshRenderer;
                player.targetMaterialProperty = "_MainTex";
                player.url = Path.Combine(Application.streamingAssetsPath, clipsRoot, state.ToString(), "video.mp4");
                playerObject.SetActive(false);
                players[state] = player;
            }

            if (hideSourceSprite && sourceRenderer != null)
                sourceRenderer.enabled = false;
        }

        private void OnStateChanged(BehaviorState state)
        {
            currentState = state;
            if (players.Count == 0) return;

            foreach (var player in players.Values)
            {
                player.loopPointReached -= HandleLoopPointReached;
                player.Stop();
                player.gameObject.SetActive(false);
            }

            if (!players.TryGetValue(state, out var activePlayer))
            {
                AnimationLoopCompleted?.Invoke(); // nothing to play — don't leave the controller waiting forever
                return;
            }
            if (!File.Exists(activePlayer.url))
            {
                Debug.LogWarning($"{name}: Meta clip not found: {activePlayer.url}. Using source sprite.", this);
                if (sourceRenderer != null) sourceRenderer.enabled = true;
                AnimationLoopCompleted?.Invoke();
                return;
            }

            if (sourceRenderer != null) sourceRenderer.enabled = false;
            activePlayer.gameObject.SetActive(true);
            activePlayer.loopPointReached += HandleLoopPointReached;
            activePlayer.Prepare();
            activePlayer.prepareCompleted += PlayPrepared;
        }

        private void HandleLoopPointReached(VideoPlayer player) => AnimationLoopCompleted?.Invoke();

        private static Texture2D MakeSolidTexture(Color color)
        {
            var texture = new Texture2D(1, 1);
            texture.SetPixel(0, 0, color);
            texture.Apply();
            return texture;
        }

        private static void PlayPrepared(VideoPlayer player)
        {
            player.prepareCompleted -= PlayPrepared;
            player.Play();
        }
    }
}
