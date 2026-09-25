using UnityEngine;

namespace AnimatedDrawingsWorld.Runtime
{
    public static class DrawingMaterials
    {
        private static Shader shader;

        public static Material Create(Texture texture)
        {
            if (shader == null)
            {
                shader = Shader.Find("AnimatedDrawingsWorld/DrawingUnlit");
                if (shader == null) shader = Shader.Find("Sprites/Default");
                if (shader == null) shader = Shader.Find("Unlit/Transparent");
            }
            return new Material(shader) { mainTexture = texture };
        }
    }
}
