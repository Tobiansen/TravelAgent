namespace TravelAgent.Integrations.Import
{
    public sealed class EditorIncomingShareService : IIncomingShareService
    {
        public IncomingShare Peek() { return null; }
        public bool Acknowledge(string requestId) { return false; }
    }
}
