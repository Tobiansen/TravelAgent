using System;

namespace TravelAgent.Integrations.Import
{
    [Serializable]
    public sealed class IncomingShare
    {
        public string requestId = "";
        public string text = "";
        public string origin = "";
        public string receivedAtUtc = "";
        public string error = "";
    }

    public interface IIncomingShareService
    {
        // Peek never consumes or confirms the user-provided text. Null means no pending item.
        IncomingShare Peek();
        // Acknowledge only after explicit discard or a successfully saved reviewed import.
        bool Acknowledge(string requestId);
    }
}
