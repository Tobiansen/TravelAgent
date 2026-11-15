using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace TravelAgent.Integrations.Claims
{
    public sealed class HttpClaimService : IClaimService
    {
        private readonly string baseUrl;
        private readonly string demoToken;
        private readonly Func<UnityWebRequest, UnityWebRequestAsyncOperation> sendRequest;

        public HttpClaimService(string baseUrl, string token = "",
            Func<UnityWebRequest, UnityWebRequestAsyncOperation> sendRequest = null)
        {
            this.baseUrl = (baseUrl ?? "").TrimEnd('/');
            demoToken = token ?? "";
            this.sendRequest = sendRequest ?? (call => call.SendWebRequest());
        }

        public IEnumerator Research(ClaimResearchRequest request, Action<GuidanceResponse> completed)
        {
            // Unity can throw synchronously when its HTTP policy rejects the URL,
            // before an AsyncOperation exists. Keep preparation/send outside the
            // yielding block so that failure still reaches the UI exactly once.
            var response = TryStart(request, out var call, out var operation);
            if (response == null)
            {
                try
                {
                    yield return operation;
                    response = ReadResponse(call, request.airline);
                }
                finally { DisposeRequest(call); }
            }
            // Completion is deliberately outside exception handling: an exception
            // in the caller must never cause a second completion attempt.
            completed?.Invoke(response);
        }

        private GuidanceResponse TryStart(ClaimResearchRequest request, out UnityWebRequest call,
            out UnityWebRequestAsyncOperation operation)
        {
            call = null;
            operation = null;
            try
            {
                if (request == null)
                    return GuidanceResponse.Failure("Enter the journey context before researching airline requirements.");
                if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var endpoint) ||
                    (endpoint.Scheme != "http" && endpoint.Scheme != "https") || !string.IsNullOrEmpty(endpoint.UserInfo))
                    return GuidanceResponse.Failure("Enter a valid laptop service address in Journey → Prepare your report.", request.airline);

                call = new UnityWebRequest(baseUrl + "/api/claims/research", UnityWebRequest.kHttpVerbPOST);
                call.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonUtility.ToJson(request)));
                call.downloadHandler = new DownloadHandlerBuffer();
                call.SetRequestHeader("Content-Type", "application/json");
                if (!string.IsNullOrEmpty(demoToken)) call.SetRequestHeader("Authorization", "Bearer " + demoToken);
                call.timeout = 100;
                operation = sendRequest(call);
                if (operation == null) throw new InvalidOperationException("No request operation was created.");
                return null;
            }
            catch (Exception error)
            {
                DisposeRequest(call);
                call = null;
                // Do not forward exception text: it may contain the configured
                // address, request headers or a session credential.
                bool blockedHttp = error.Message != null && error.Message.IndexOf("insecure", StringComparison.OrdinalIgnoreCase) >= 0;
                return GuidanceResponse.Failure(blockedHttp
                    ? "This build blocks the laptop's HTTP connection. Install the development build that allows the demo connection, or use an HTTPS service. Your evidence is saved locally."
                    : "Could not start the research request. Check the service address and this build's networking support, then retry. Your evidence is saved locally.", request?.airline ?? "");
            }
        }

        private static GuidanceResponse ReadResponse(UnityWebRequest call, string airline)
        {
            try
            {
                GuidanceResponse reply = null;
                var raw = call.downloadHandler?.text;
                if (!string.IsNullOrEmpty(raw) && raw.Length <= 262144)
                {
                    try { reply = JsonUtility.FromJson<GuidanceResponse>(raw); }
                    catch (ArgumentException) { /* Show a usable failure below. */ }
                }
                if (reply == null || reply.schemaVersion != "1" ||
                    (reply.status != "ready" && reply.status != "needsConfirmation" && reply.status != "error"))
                {
                    return GuidanceResponse.Failure(call.result == UnityWebRequest.Result.Success
                        ? "The backend returned an unreadable response. Retry; no fixture was substituted."
                        : "Could not reach the laptop backend. Check its address and connection, then retry. Your evidence is saved locally.", airline);
                }
                reply.items ??= Array.Empty<GuidanceItem>();
                reply.sources ??= Array.Empty<GuidanceSource>();
                reply.missingContext ??= Array.Empty<string>();
                return reply;
            }
            catch (Exception)
            {
                return GuidanceResponse.Failure("Could not read the research result. Retry; your evidence is saved locally and no fixture was substituted.", airline);
            }
        }

        private static void DisposeRequest(UnityWebRequest call)
        {
            try { call?.Dispose(); }
            catch (Exception) { /* Cleanup must not suppress the UI's failure response. */ }
        }
    }
}
