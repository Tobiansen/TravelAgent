using System;
using System.Collections;
using System.IO;

namespace TravelAgent.Integrations.Photos
{
    public sealed class EditorPhotoService : IPhotoService
    {
        public IEnumerator Select(Action<PhotoSelection> completed)
        {
            var result = new PhotoSelection();
#if UNITY_EDITOR
            string path = UnityEditor.EditorUtility.OpenFilePanelWithFilters("Choose a condition photo", "", new[] { "Images", "png,jpg,jpeg" });
            if (string.IsNullOrEmpty(path)) result.cancelled = true;
            else
            {
                try
                {
                    var info = new FileInfo(path);
                    if (!info.Exists || info.Length > 20L * 1024 * 1024) result.error = "Choose an image smaller than 20 MB.";
                    else
                    {
                        byte[] header = new byte[8];
                        using (var stream = File.OpenRead(path))
                        {
                            if (stream.Read(header, 0, header.Length) < header.Length) result.error = "The selected file is not a valid image.";
                        }
                        bool png = header[0] == 137 && header[1] == 80 && header[2] == 78 && header[3] == 71 && header[4] == 13 && header[5] == 10 && header[6] == 26 && header[7] == 10;
                        bool jpeg = header[0] == 255 && header[1] == 216 && header[2] == 255;
                        if (string.IsNullOrEmpty(result.error) && !png && !jpeg) result.error = "Choose a PNG or JPEG image.";
                        if (string.IsNullOrEmpty(result.error)) { result.path = path; result.origin = "local imported image"; }
                    }
                }
                catch (Exception) { result.error = "The selected image could not be read. Please retry."; }
            }
#else
            result.error = "The desktop file picker is available in the Unity Editor.";
#endif
            if (!string.IsNullOrEmpty(result.path) && string.IsNullOrEmpty(result.error))
                yield return PhotoPreview.Prepare(result.path, preview => { result.previewPath = preview.previewPath; result.error = preview.error; });
            completed?.Invoke(result);
            yield break;
        }
    }
}
