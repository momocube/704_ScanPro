using UnityEngine;

namespace ScanPro704
{
    [RequireComponent(typeof(Camera))]
    public sealed class VenueOutputPresenter : MonoBehaviour
    {
        public Shader presentationShader;
        public RenderTexture Source { get; set; }
        public float HorizontalOffsetFraction;
        Material presentationMaterial;
        public Rect ContentRect { get; private set; }
        public int PresentationWidth { get; private set; }
        public int PresentationHeight { get; private set; }

        public static Rect FitRect(int sourceWidth, int sourceHeight, int targetWidth, int targetHeight, float horizontalOffsetFraction = 0)
        {
            var scale = Mathf.Min(targetWidth / (float)sourceWidth, targetHeight / (float)sourceHeight);
            var width = sourceWidth * scale / targetWidth;
            var height = sourceHeight * scale / targetHeight;
            var horizontalSlack = 1 - width;
            var offset = Mathf.Clamp(horizontalOffsetFraction, -.5f, .5f) * horizontalSlack;
            return new Rect(horizontalSlack / 2 + offset, (1 - height) / 2, width, height);
        }

        public void Present(Texture texture, RenderTexture destination)
        {
            var camera = GetComponent<Camera>();
            Present(texture, destination, destination != null ? destination.width : camera.pixelWidth,
                destination != null ? destination.height : camera.pixelHeight);
        }

        void Present(Texture texture, RenderTexture destination, int width, int height)
        {
            if (presentationMaterial == null)
            {
                if (presentationShader == null) presentationShader = Shader.Find("704/Fullscreen Fit");
                if (presentationShader == null || !presentationShader.isSupported)
                    throw new System.InvalidOperationException("704 fullscreen aspect-fit shader is missing or unsupported.");
                presentationMaterial = new Material(presentationShader);
            }
            var resized = PresentationWidth != width || PresentationHeight != height;
            PresentationWidth = width;
            PresentationHeight = height;
            ContentRect = FitRect(texture.width, texture.height, width, height, HorizontalOffsetFraction);
            presentationMaterial.SetVector("_ContentRect", new Vector4(ContentRect.x, ContentRect.y, ContentRect.width, ContentRect.height));
#if !UNITY_EDITOR
            if (resized)
                Debug.Log($"SCANPRO_PRESENTATION: display={GetComponent<Camera>().targetDisplay}; frame={width}x{height}; operator={Screen.width}x{Screen.height}; source={texture.width}x{texture.height}; content={ContentRect}");
#endif
            OutputCompositor.Blit(texture, destination, presentationMaterial);
        }

        void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            // A null destination is this camera's display backbuffer. Screen describes the
            // primary operator window, which can have a completely different aspect ratio.
            var frame = destination != null ? destination : source;
            Present(Source != null ? Source : source, destination, frame.width, frame.height);
        }

        void OnDestroy() { if (presentationMaterial != null) Destroy(presentationMaterial); }
    }
}
