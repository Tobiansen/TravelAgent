using System;
using System.Collections;
using System.IO;
using System.Security.Cryptography;
using UnityEngine;

namespace TravelAgent.Integrations.Photos
{
    public static class PhotoPreview
    {
        public const int MaximumEdge = 1280;
        public const long MaximumEditorPixels = 16000000;
        public static IEnumerator Prepare(string originalPath, Action<PhotoSelection> completed)
        {
            PhotoSelection result = null;
#if UNITY_ANDROID && !UNITY_EDITOR
            AndroidJavaClass bridge = null;
            string id = "";
            try
            {
                bridge = new AndroidJavaClass("com.travelagent.ble.PhotoPreviewBridge");
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity")) id = bridge.CallStatic<string>("start", activity, originalPath);
                if (string.IsNullOrEmpty(id)) result = new PhotoSelection { path = originalPath, error = "Other photo previews are still being prepared. Please retry." };
            }
            catch (Exception) { result = new PhotoSelection { path = originalPath, error = "The display preview could not start. The original is retained." }; }
            float started = Time.realtimeSinceStartup;
            while (result == null)
            {
                try
                {
                    string json = bridge.CallStatic<string>("poll", id);
                    if (!string.IsNullOrEmpty(json)) result = JsonUtility.FromJson<PhotoSelection>(json);
                }
                catch (Exception) { result = new PhotoSelection { path = originalPath, error = "The display preview could not be read. The original is retained." }; }
                if (result == null && Time.realtimeSinceStartup - started > 30) result = new PhotoSelection { path = originalPath, error = "Preview preparation timed out. The original is retained." };
                if (result == null) yield return new WaitForSecondsRealtime(.1f);
            }
            bridge?.Dispose();
#else
            try { result = PrepareEditor(originalPath); }
            catch (Exception) { result = new PhotoSelection { path = originalPath, error = "The display preview could not be prepared. Choose a PNG/JPEG smaller than 20 MB and 16 megapixels; the original is retained." }; }
#endif
            completed?.Invoke(result);
            yield break;
        }
#if !UNITY_ANDROID || UNITY_EDITOR
        private static PhotoSelection PrepareEditor(string path)
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > 20L * 1024 * 1024) throw new IOException("Image unavailable or too large.");
            byte[] bytes = File.ReadAllBytes(path);
            var header = PhotoHeader.Read(bytes);
            if ((long)header.width * header.height > MaximumEditorPixels) throw new IOException("Editor decode pixel budget exceeded.");
            string key;
            using (var sha = SHA256.Create()) key = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
            string directory = Path.Combine(Application.temporaryCachePath, "PhotoPreviews"); Directory.CreateDirectory(directory);
            string preview = Path.Combine(directory, key + ".jpg");
            if (File.Exists(preview))
            {
                Texture2D cached = null;
                try
                {
                    if (new FileInfo(preview).Length <= 4L * 1024 * 1024)
                    {
                        byte[] cachedBytes = File.ReadAllBytes(preview);
                        var existing = PhotoHeader.Read(cachedBytes);
                        if (Math.Max(existing.width, existing.height) <= MaximumEdge)
                        {
                            cached = new Texture2D(2, 2, TextureFormat.RGB24, false);
                            if (cached.LoadImage(cachedBytes, true) && Math.Max(cached.width, cached.height) <= MaximumEdge)
                                return new PhotoSelection { path = path, previewPath = preview };
                        }
                    }
                }
                catch (Exception) { /* A broken derivative is regenerated from the retained original below. */ }
                finally { Destroy(cached); }
            }
            Texture2D source = null, small = null, oriented = null;
            RenderTexture temporary = null, previous = RenderTexture.active;
            try
            {
                source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!source.LoadImage(bytes, true)) throw new IOException("Image decode failed.");
                float ratio = Mathf.Min(1, MaximumEdge / (float)Math.Max(source.width, source.height));
                int width = Math.Max(1, Mathf.RoundToInt(source.width * ratio)), height = Math.Max(1, Mathf.RoundToInt(source.height * ratio));
                temporary = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
                Graphics.Blit(source, temporary); RenderTexture.active = temporary;
                small = new Texture2D(width, height, TextureFormat.RGB24, false);
                small.ReadPixels(new Rect(0, 0, width, height), 0, 0); small.Apply();
                bool swap = header.orientation >= 5 && header.orientation <= 8;
                int outWidth = swap ? height : width, outHeight = swap ? width : height;
                var input = small.GetPixels32(); var output = new Color32[input.Length];
                for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
                {
                    int top = height - 1 - y, ox = x, oy = top;
                    switch (header.orientation)
                    {
                        case 2: ox = width - 1 - x; break;
                        case 3: ox = width - 1 - x; oy = height - 1 - top; break;
                        case 4: oy = height - 1 - top; break;
                        case 5: ox = top; oy = x; break;
                        case 6: ox = height - 1 - top; oy = x; break;
                        case 7: ox = height - 1 - top; oy = width - 1 - x; break;
                        case 8: ox = top; oy = width - 1 - x; break;
                    }
                    output[(outHeight - 1 - oy) * outWidth + ox] = input[y * width + x];
                }
                oriented = new Texture2D(outWidth, outHeight, TextureFormat.RGB24, false); oriented.SetPixels32(output); oriented.Apply();
                string partial = preview + "." + Guid.NewGuid().ToString("N") + ".partial";
                try { File.WriteAllBytes(partial, oriented.EncodeToJPG(88)); if (File.Exists(preview)) File.Delete(preview); File.Move(partial, preview); }
                finally { if (File.Exists(partial)) File.Delete(partial); }
                return new PhotoSelection { path = path, previewPath = preview };
            }
            finally
            {
                RenderTexture.active = previous;
                if (temporary) RenderTexture.ReleaseTemporary(temporary);
                Destroy(source); Destroy(small); Destroy(oriented);
            }
        }
        private static void Destroy(UnityEngine.Object value)
        {
            if (!value) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(value); else UnityEngine.Object.DestroyImmediate(value);
        }
#endif
    }
}
