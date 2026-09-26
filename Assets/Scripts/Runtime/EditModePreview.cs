using System.IO;
using AnimatedDrawingsWorld.Logic;
using UnityEngine;

namespace AnimatedDrawingsWorld.Runtime
{
    // The game builds everything at runtime, so outside Play mode the scene would look empty.
    // This shows the first sample background in the Scene/Game view while editing; it is not
    // saved with the scene and disappears when Play starts (the game then takes over).
    [ExecuteAlways]
    public sealed class EditModePreview : MonoBehaviour
    {
        [SerializeField] private float worldHeight = 10f;

        private GameObject preview;
        private Texture2D texture;
        private Material material;

        private void OnEnable()
        {
            if (!Application.isPlaying) Show();
        }

        private void Start()
        {
            if (Application.isPlaying) Hide();
        }

        private void OnDisable() => Hide();

        private void Show()
        {
            Hide();
            try
            {
                var manifest = SamplesManifest.Parse(File.ReadAllText(StreamingAssetsIO.PathFor("Samples/samples.json")));
                if (manifest.Backgrounds.Count == 0) return;
                var bytes = File.ReadAllBytes(StreamingAssetsIO.PathFor(manifest.Backgrounds[0].Path));
                texture = TextureConversion.Decode(bytes, "Preview");
                if (texture == null) return;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("EditModePreview: " + e.Message);
                return;
            }

            var width = worldHeight * texture.width / texture.height;
            preview = GameObject.CreatePrimitive(PrimitiveType.Quad);
            preview.name = "Background preview (edit mode only)";
            preview.hideFlags = HideFlags.DontSave | HideFlags.NotEditable;
            DestroyImmediate(preview.GetComponent<Collider>());
            preview.transform.SetParent(transform, false);
            preview.transform.position = new Vector3(width * 0.5f, worldHeight * 0.5f, 1f);
            preview.transform.localScale = new Vector3(width, worldHeight, 1f);
            material = DrawingMaterials.Create(texture);
            material.hideFlags = HideFlags.DontSave;
            preview.GetComponent<MeshRenderer>().sharedMaterial = material;

            var cam = Camera.main;
            if (cam != null)
            {
                cam.orthographic = true;
                cam.orthographicSize = Mathf.Max(worldHeight * 0.56f, width * 0.51f / Mathf.Max(0.1f, cam.aspect));
                cam.transform.position = new Vector3(width * 0.5f, worldHeight * 0.52f, -10f);
            }
        }

        private void Hide()
        {
            if (preview != null) DestroyImmediate(preview);
            if (material != null) DestroyImmediate(material);
            if (texture != null) DestroyImmediate(texture);
            preview = null;
            material = null;
            texture = null;
        }
    }
}
