using System;
using System.Collections;

namespace TravelAgent.Integrations.Photos
{
    public sealed class EditorCameraService : ICameraService
    {
        public IEnumerator Capture(string context, Action<PhotoSelection> completed)
        {
            completed?.Invoke(new PhotoSelection { context = context, error = "Camera capture is available on an Android phone. Choose an existing photo in the Editor." });
            yield break;
        }
        public IEnumerator Recover(Action<PhotoSelection> completed) { completed?.Invoke(null); yield break; }
        public void Release(string requestId) { }
    }
}
