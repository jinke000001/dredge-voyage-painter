using System;
using System.IO;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Jinke.DredgeSkins.NativeQA
{
    internal static class GpuExport
    {
        internal static Texture2D Read(Texture source)
        {
            RenderTexture previous = RenderTexture.active; bool srgb = GL.sRGBWrite;
            RenderTexture temp = null; Texture2D copy = null;
            try
            {
                temp = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                GL.sRGBWrite = QualitySettings.activeColorSpace == ColorSpace.Linear;
                Graphics.Blit(source, temp); RenderTexture.active = temp;
                copy = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false, false);
                copy.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0, false); copy.Apply(false, false);
                return copy;
            }
            catch { if (copy != null) Object.Destroy(copy); throw; }
            finally { RenderTexture.active = previous; GL.sRGBWrite = srgb; if (temp != null) RenderTexture.ReleaseTemporary(temp); }
        }

        internal static PreviewExport RenderMesh(Renderer source, string directory, string selection)
        {
            GameObject model = null, cameraObject = null; Mesh mesh = null; RenderTexture target = null; Texture2D image = null;
            RenderTexture previous = RenderTexture.active;
            bool previousSrgb = GL.sRGBWrite;
            try
            {
                // Build a visual-only mesh clone. Never clone a source GameObject, scripts, bones, or native hierarchy.
                var skinned = source as SkinnedMeshRenderer;
                if (skinned != null) { mesh = new Mesh(); skinned.BakeMesh(mesh); }
                else
                {
                    var sourceFilter = source.GetComponent<MeshFilter>();
                    if (sourceFilter == null || sourceFilter.sharedMesh == null) throw new InvalidOperationException("Source has no readable mesh.");
                    mesh = Object.Instantiate(sourceFilter.sharedMesh);
                }
                if (mesh.vertexCount == 0) throw new InvalidOperationException("Native flag mesh is empty.");
                mesh.RecalculateBounds(); Bounds bounds = mesh.bounds;
                float largest = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
                if (largest < 0.0001f) throw new InvalidOperationException("Native flag bounds are empty.");
                const int previewLayer = 31;
                // Large world translations with a very small near plane produced native frustum warnings.
                // This temporary clone is isolated by its camera's layer and destroyed before yielding.
                Vector3 center = Vector3.zero;
                model = new GameObject("QA-ONLY-FlagMesh"); model.hideFlags = HideFlags.DontSave; model.layer = previewLayer;
                model.transform.position = center - bounds.center / largest; model.transform.localScale = Vector3.one / largest;
                model.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = model.AddComponent<MeshRenderer>(); renderer.sharedMaterials = source.sharedMaterials;
                var block = new MaterialPropertyBlock(); source.GetPropertyBlock(block, 0); renderer.SetPropertyBlock(block, 0);
                cameraObject = new GameObject("QA-ONLY-PreviewCamera"); cameraObject.hideFlags = HideFlags.DontSave;
                var camera = cameraObject.AddComponent<Camera>(); camera.enabled = false; camera.cullingMask = 1 << previewLayer;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(0.07f, 0.09f, 0.12f, 1f);
                camera.orthographic = true; camera.orthographicSize = 0.64f; camera.nearClipPlane = 0.1f; camera.farClipPlane = 8f;
                camera.useOcclusionCulling = false;
                camera.allowHDR = false; camera.allowMSAA = false;
                Vector3 axis = bounds.size.x <= bounds.size.y && bounds.size.x <= bounds.size.z ? Vector3.right : bounds.size.y <= bounds.size.z ? Vector3.up : Vector3.forward;
                camera.transform.position = center + axis * 3f; camera.transform.LookAt(center, axis == Vector3.up ? Vector3.forward : Vector3.up);
                target = new RenderTexture(PreviewPixels.Width, PreviewPixels.Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB); target.Create(); camera.targetTexture = target;
                camera.Render(); RenderTexture.active = target;
                image = new Texture2D(PreviewPixels.Width, PreviewPixels.Height, TextureFormat.RGBA32, false, false); image.ReadPixels(new Rect(0, 0, PreviewPixels.Width, PreviewPixels.Height), 0, 0, false); image.Apply(false, false);
                Color32[] pixels = image.GetPixels32();
                string name = selection + "-native-flag-mesh.png"; string path = Path.Combine(directory, name);
                if (File.Exists(path)) throw new IOException("Preview already exists.");
                File.WriteAllBytes(path, ImageConversion.EncodeToPNG(image));
                return new PreviewExport { FileName = name, NonBackgroundPixels = PreviewPixels.CountNonBackground(pixels), TotalPixels = pixels.Length };
            }
            finally
            {
                RenderTexture.active = previous;
                GL.sRGBWrite = previousSrgb;
                if (cameraObject != null) Object.DestroyImmediate(cameraObject);
                if (model != null) Object.DestroyImmediate(model);
                if (mesh != null) Object.Destroy(mesh);
                if (target != null) { target.Release(); Object.Destroy(target); }
                if (image != null) Object.Destroy(image);
            }
        }
    }
}
