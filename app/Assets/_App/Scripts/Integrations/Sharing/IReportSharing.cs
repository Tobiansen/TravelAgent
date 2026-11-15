using System;
using System.Collections;

namespace TravelAgent.Integrations.Sharing
{
    [Serializable]
    public sealed class ReportShareResult
    {
        public string type = "";
        public string archivePath = "";
        public string error = "";
        public bool sheetOpened;
    }

    public interface IReportSharing
    {
        IEnumerator Share(string completedExportFolder, Action<ReportShareResult> completed, Action<string> progress = null);
    }
}
