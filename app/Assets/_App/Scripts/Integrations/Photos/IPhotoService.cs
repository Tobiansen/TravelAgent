using System;
using System.Collections;

namespace TravelAgent.Integrations.Photos
{
    [Serializable]
    public sealed class PhotoSelection
    {
        public string path = "";
        public string previewPath = "";
        public string previewWarning = "";
        public string error = "";
        public string requestId = "";
        public string context = "";
        public string origin = "";
        public string captureRequestedAtUtc = "";
        public string captureReturnedAtUtc = "";
        public bool cancelled;
        public bool recovered;
        public bool pending;
        public string status = "";
    }

    public interface IPhotoService
    {
        IEnumerator Select(Action<PhotoSelection> completed);
    }

    public interface ICameraService
    {
        IEnumerator Capture(string context, Action<PhotoSelection> completed);
        // Null means no retained request. A pending result is not usable evidence.
        IEnumerator Recover(Action<PhotoSelection> completed);
        // Call only after copying original and any available preview into journey storage, or explicit discard.
        void Release(string requestId);
    }
}
