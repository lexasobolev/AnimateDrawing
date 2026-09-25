using UnityEngine;

namespace AnimatedDrawingsWorld.Runtime
{
    // The background drawing, stretched over the world rectangle (0,0)-(width,height).
    public sealed class BackgroundView : MonoBehaviour
    {
        private MeshRenderer meshRenderer;
        private Mesh mesh;
        public Texture2D Texture { get; private set; }

        public void Show(Texture2D texture, float width, float height)
        {
            if (meshRenderer == null)
            {
                gameObject.AddComponent<MeshFilter>().sharedMesh = mesh = new Mesh { name = "Background" };
                meshRenderer = gameObject.AddComponent<MeshRenderer>();
                meshRenderer.sortingOrder = -100;
            }

            if (Texture != null && Texture != texture) Destroy(Texture);
            Texture = texture;
            mesh.Clear();
            mesh.vertices = new[] { new Vector3(0, 0, 1), new Vector3(width, 0, 1), new Vector3(width, height, 1), new Vector3(0, height, 1) };
            mesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            mesh.colors = new[] { Color.white, Color.white, Color.white, Color.white };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            mesh.RecalculateBounds();

            if (meshRenderer.sharedMaterial != null) Destroy(meshRenderer.sharedMaterial);
            meshRenderer.sharedMaterial = DrawingMaterials.Create(texture);
        }

        private void OnDestroy()
        {
            if (mesh != null) Destroy(mesh);
            if (Texture != null) Destroy(Texture);
        }
    }
}
