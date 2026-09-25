using System.Collections.Generic;
using AnimatedDrawingsWorld.Logic;
using AnimatedDrawingsWorld.Logic.Animation;
using AnimatedDrawingsWorld.Logic.Behavior;
using AnimatedDrawingsWorld.Logic.Characters;
using UnityEngine;

namespace AnimatedDrawingsWorld.Runtime
{
    // Draws one character: its drawing as a skinned mesh deformed every frame by the Meta-style
    // rig (CharacterAnimator), placed where the simulation (Agent) says.
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class CharacterView : MonoBehaviour
    {
        public Agent Agent { get; private set; }
        public BuiltCharacter Character { get; private set; }
        public CharacterAnimator Animator { get; private set; }
        public Texture2D Texture { get; private set; }
        public Bounds WorldBounds { get; private set; }
        public Vector3 HeadPosition { get; private set; }

        private GameWorld world;
        private Mesh mesh;
        private MeshRenderer meshRenderer;
        private MaterialPropertyBlock block;
        private Vector3[] vertices;
        private Color[] colors;
        private int[] triangles;
        private bool highlighted;

        public void Initialize(GameWorld gameWorld, Agent agent, BuiltCharacter character, MotionLibrary motions)
        {
            world = gameWorld;
            Agent = agent;
            Character = character;
            Animator = new CharacterAnimator(character.Rig, character.Mesh, motions);
            Texture = TextureConversion.ToTexture(character.Annotation.Texture);
            Texture.name = agent.Name;

            mesh = new Mesh { name = agent.Name };
            mesh.MarkDynamic();
            var n = character.Mesh.VertexCount;
            vertices = new Vector3[n];
            colors = new Color[n];
            var uvs = new Vector2[n];
            for (var i = 0; i < n; i++)
            {
                uvs[i] = new Vector2(character.Mesh.UVs[i].X, character.Mesh.UVs[i].Y);
                colors[i] = Color.white;
            }
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.colors = colors;
            triangles = new int[character.Mesh.Triangles.Length];
            mesh.triangles = character.Mesh.Triangles;
            GetComponent<MeshFilter>().sharedMesh = mesh;

            meshRenderer = GetComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = DrawingMaterials.Create(Texture);
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            block = new MaterialPropertyBlock();
            transform.position = Vector3.zero;
            Tick(0f);
        }

        public void SetHighlighted(bool on) => highlighted = on;

        public void Tick(float deltaTime)
        {
            if (Agent == null) return;
            Animator.Update(Agent.Activity, deltaTime, Agent.AnimationSpeed, Agent.ClipOverride);

            var toWorld = Placement.RigToWorld(Agent, Animator, world.PerspectiveScale(Agent));
            var deformed = Animator.DeformedVertices;
            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);
            for (var i = 0; i < deformed.Length; i++)
            {
                var p = toWorld.Apply(deformed[i]);
                vertices[i] = new Vector3(p.X, p.Y, 0f);
                min = Vector2.Min(min, vertices[i]);
                max = Vector2.Max(max, vertices[i]);
            }
            mesh.vertices = vertices;

            // painter's order of body parts changes with the motion (arm in front of / behind body)
            System.Array.Copy(Animator.DrawOrder, triangles, triangles.Length);
            mesh.SetTriangles(triangles, 0, false);
            mesh.bounds = new Bounds((min + max) * 0.5f, new Vector3(max.x - min.x, max.y - min.y, 1f));
            WorldBounds = mesh.bounds;

            var neck = Animator.Rig.IndexOf("neck");
            var head = neck >= 0 ? toWorld.Apply(Animator.Pose.Positions[neck]) : new V2((min.x + max.x) * 0.5f, max.y);
            HeadPosition = new Vector3(head.X, Mathf.Max(head.Y, max.y), 0f);

            // nearer (lower) characters draw on top; climbers are in front of the scenery
            meshRenderer.sortingOrder = 100 + Mathf.RoundToInt((1f - Placement.SortDepth(Agent, world.Height)) * 1000f);
            var tint = highlighted ? new Color(1f, 0.93f, 0.75f, Agent.Opacity) : new Color(1f, 1f, 1f, Agent.Opacity);
            block.SetColor("_Color", tint);
            meshRenderer.SetPropertyBlock(block);
            meshRenderer.enabled = Agent.Opacity > 0.01f;
        }

        // precise hit test against the deformed triangles (bounds alone would grab empty corners)
        public bool Contains(Vector2 worldPoint)
        {
            if (!WorldBounds.Contains(new Vector3(worldPoint.x, worldPoint.y, 0f))) return false;
            var tris = Character.Mesh.Triangles;
            for (var t = 0; t < tris.Length; t += 3)
            {
                Vector2 a = vertices[tris[t]], b = vertices[tris[t + 1]], c = vertices[tris[t + 2]];
                var d = (b.y - c.y) * (a.x - c.x) + (c.x - b.x) * (a.y - c.y);
                if (Mathf.Abs(d) < 1e-8f) continue;
                var w0 = ((b.y - c.y) * (worldPoint.x - c.x) + (c.x - b.x) * (worldPoint.y - c.y)) / d;
                var w1 = ((c.y - a.y) * (worldPoint.x - c.x) + (a.x - c.x) * (worldPoint.y - c.y)) / d;
                if (w0 >= 0f && w1 >= 0f && w0 + w1 <= 1f) return true;
            }
            return false;
        }

        private void OnDestroy()
        {
            if (mesh != null) Destroy(mesh);
            if (Texture != null) Destroy(Texture);
            if (meshRenderer != null && meshRenderer.sharedMaterial != null) Destroy(meshRenderer.sharedMaterial);
        }
    }
}
