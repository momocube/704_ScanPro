using UnityEngine;

namespace ScanPro704
{
    public static class OutputCompositor
    {
        public static void Blit(Texture source, RenderTexture destination, Material material)
        {
            var previous = RenderTexture.active;
            try { Graphics.Blit(source, destination, material); }
            finally { RenderTexture.active = previous; }
        }
    }
}
