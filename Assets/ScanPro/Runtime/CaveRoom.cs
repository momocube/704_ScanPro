using System;
using System.Collections.Generic;
using UnityEngine;

namespace ScanPro704
{
    public sealed class CaveRoom : IDisposable
    {
        public readonly GameObject Root;
        public readonly Camera Camera;
        public readonly RenderTexture Image;
        readonly Material material;
        readonly List<Mesh> meshes = new List<Mesh>();
        readonly Vector3 orbitTarget;
        // Keep a third-person overview while viewing the left wall more obliquely
        // and reducing the steep downward angle that made it look compressed.
        readonly Vector3 homeOffset = new Vector3(9, 20, -37);
        float yaw, pitch;
        public CaveRoom(Texture texture, Shader shader, int width = 1000, int height = 700)
        {
            Root = new GameObject("704 CAVE spatial preview") { hideFlags = HideFlags.HideAndDontSave };
            // Put preview away from any user-authored scene geometry.
            Root.transform.position = new Vector3(10000, 10000, 10000);
            material = new Material(shader) { mainTexture = texture, hideFlags = HideFlags.HideAndDontSave };
            Surface("Floor", new[] { new Vector3(-16,0,11),new Vector3(16,0,11),new Vector3(16,0,-11),new Vector3(-16,0,-11) },
                new[] { new Vector2(100,100),new Vector2(420,100),new Vector2(420,320),new Vector2(100,320) }, new Color(.055f,.07f,.095f,1));
            Surface("Back wall", new[] { new Vector3(-16,10,11),new Vector3(16,10,11),new Vector3(16,0,11),new Vector3(-16,0,11) },
                new[] { new Vector2(100,0),new Vector2(420,0),new Vector2(420,100),new Vector2(100,100) }, new Color(.105f,.12f,.16f,1));
            Surface("Left wall", new[] { new Vector3(-16,10,11),new Vector3(-16,0,11),new Vector3(-16,0,-11),new Vector3(-16,10,-11) },
                new[] { new Vector2(0,100),new Vector2(100,100),new Vector2(100,320),new Vector2(0,320) }, new Color(.16f,.105f,.09f,1));
            Surface("Right wall", new[] { new Vector3(16,0,11),new Vector3(16,10,11),new Vector3(16,10,-11),new Vector3(16,0,-11) },
                new[] { new Vector2(420,100),new Vector2(520,100),new Vector2(520,320),new Vector2(420,320) }, new Color(.085f,.125f,.16f,1));
            // Near wall is a short cutaway strip; all of it remains in the atlas output.
            Surface("Front cutaway", new[] { new Vector3(-16,0,-11),new Vector3(16,0,-11),new Vector3(16,1.5f,-11),new Vector3(-16,1.5f,-11) },
                new[] { new Vector2(100,320),new Vector2(420,320),new Vector2(420,335),new Vector2(100,335) }, new Color(.12f,.12f,.13f,1));
            var cameraObject = new GameObject("CAVE preview camera"); cameraObject.transform.SetParent(Root.transform, false);
            Camera = cameraObject.AddComponent<Camera>(); Camera.enabled = false;
            Camera.clearFlags = CameraClearFlags.SolidColor; Camera.backgroundColor = new Color(.035f,.045f,.065f);
            Camera.nearClipPlane = .1f; Camera.farClipPlane = 100; Camera.fieldOfView = 52;
            orbitTarget = Root.transform.position + new Vector3(0, 3, 0);
            ResetOrbit();
            Image = new RenderTexture(width, height, 24) { name = "704 CAVE preview" }; Image.Create(); Camera.targetTexture = Image;
        }
        void Surface(string name, Vector3[] vertices, Vector2[] pixels, Color emptySurfaceTint)
        {
            var mesh = new Mesh { vertices = vertices, uv = Array.ConvertAll(pixels, p => new Vector2(p.x / 520f, 1 - p.y / 420f)), triangles = new[] { 0,1,2,0,2,3 } };
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); meshes.Add(mesh);
            var go = new GameObject(name); go.transform.SetParent(Root.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var meshRenderer = go.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = material;
            var properties = new MaterialPropertyBlock();
            properties.SetColor("_SurfaceTint", emptySurfaceTint);
            meshRenderer.SetPropertyBlock(properties);
        }
        public void SetTexture(Texture texture)
        {
            if (material.mainTexture != texture) material.mainTexture = texture;
        }
        public void Render()
        {
            var previous = RenderTexture.active;
            try { Camera.Render(); }
            finally { RenderTexture.active = previous; }
        }
        public void Orbit(float horizontalDelta, float verticalDelta)
        {
            yaw = Mathf.Repeat(yaw + horizontalDelta * .35f, 360f);
            pitch = Mathf.Clamp(pitch - verticalDelta * .25f, -12f, 68f);
            ApplyOrbit();
        }
        public void ResetOrbit()
        {
            yaw = 0; pitch = 0; ApplyOrbit();
        }
        void ApplyOrbit()
        {
            Camera.transform.position = orbitTarget + Quaternion.Euler(pitch, yaw, 0) * homeOffset;
            Camera.transform.LookAt(orbitTarget);
        }
        public void Dispose()
        {
            Image.Release(); UnityEngine.Object.DestroyImmediate(Image);
            UnityEngine.Object.DestroyImmediate(Root); UnityEngine.Object.DestroyImmediate(material);
            foreach (var mesh in meshes) UnityEngine.Object.DestroyImmediate(mesh);
        }
    }
}
