using System;
using System.Collections;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;

namespace TravelAgent.Integrations.Claims
{
    [Serializable]
    public sealed class AirlineResolution
    {
        public string schemaVersion, airline, iataCode, officialSite, sourceUrl, iataSourceUrl, retrievedAtUtc, origin, status, error;
        public bool IsVerified
        {
            get
            {
                if(schemaVersion!="1" || status!="resolved" || (origin!="live" && origin!="cached"))return false;
                string domain=airline=="Finnair" && iataCode=="AY" ? "finnair.com" : airline=="Lufthansa" && iataCode=="LH" ? "lufthansa.com" : null;
                return domain!=null && Official(officialSite,domain) && Official(sourceUrl,domain) &&
                    (string.IsNullOrEmpty(iataSourceUrl) || iataSourceUrl==(airline=="Finnair" ? "https://www.iata.org/en/about/members/airline-list/finnair/86/" : "https://www.iata.org/en/about/members/airline-list/lufthansa/115/")) &&
                    Regex.IsMatch(retrievedAtUtc ?? "",@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|\+00:00)$") &&
                    DateTimeOffset.TryParse(retrievedAtUtc,CultureInfo.InvariantCulture,DateTimeStyles.None,out var time) &&
                    time.Offset==TimeSpan.Zero && time<=DateTimeOffset.UtcNow.AddMinutes(5);
            }
        }
        private static bool Official(string value,string domain) => Uri.TryCreate(value,UriKind.Absolute,out var uri) &&
            uri.Scheme=="https" && uri.Port==443 && string.IsNullOrEmpty(uri.UserInfo) &&
            (uri.Host==domain || uri.Host.EndsWith("."+domain,StringComparison.OrdinalIgnoreCase));
        public static AirlineResolution Failure(string message) => new AirlineResolution {schemaVersion="1",status="error",error=message};
    }
    public interface IAirlineResolver { IEnumerator Resolve(string candidate, Action<AirlineResolution> completed); }

    public sealed class HttpAirlineResolver : IAirlineResolver
    {
        [Serializable] private sealed class Request { public string candidate; }
        private readonly string address, token;
        public HttpAirlineResolver(string address,string token) {this.address=(address??"").TrimEnd('/');this.token=token??"";}
        public IEnumerator Resolve(string candidate,Action<AirlineResolution> completed)
        {
            UnityWebRequest call=null; UnityWebRequestAsyncOperation operation=null; AirlineResolution result=null;
            try
            {
                if(!Uri.TryCreate(address,UriKind.Absolute,out var endpoint) || (endpoint.Scheme!="http" && endpoint.Scheme!="https") || !string.IsNullOrEmpty(endpoint.UserInfo))
                    result=AirlineResolution.Failure("Set the laptop gateway address in Research settings, then retry.");
                else
                {
                    call=new UnityWebRequest(address+"/api/airlines/resolve",UnityWebRequest.kHttpVerbPOST);
                    call.uploadHandler=new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonUtility.ToJson(new Request {candidate=candidate})));
                    call.downloadHandler=new DownloadHandlerBuffer();call.SetRequestHeader("Content-Type","application/json");
                    if(!string.IsNullOrEmpty(token))call.SetRequestHeader("Authorization","Bearer "+token);
                    call.timeout=60;operation=call.SendWebRequest();
                }
            }
            catch(Exception) { result=AirlineResolution.Failure("Could not start Exa. Check the gateway address and connection, then retry."); }
            if(result==null)
            {
                try
                {
                    yield return operation;
                    result=Read(call);
                }
                finally {call?.Dispose();call=null;}
            }
            call?.Dispose();
            completed?.Invoke(result);
        }
        private static AirlineResolution Read(UnityWebRequest call)
        {
            try
            {
                string raw=call.downloadHandler?.text;
                if(call.result!=UnityWebRequest.Result.Success || string.IsNullOrEmpty(raw) || raw.Length>65536)
                    return AirlineResolution.Failure("Exa could not be reached. Check the laptop gateway, then retry. No airline has been selected.");
                var result=JsonUtility.FromJson<AirlineResolution>(raw);
                if(result==null || result.schemaVersion!="1" || (result.status!="resolved" && result.status!="needsConfirmation" && result.status!="error"))
                    return AirlineResolution.Failure("The airline lookup returned an unreadable response. Retry.");
                if(result.status=="resolved" && !result.IsVerified)return AirlineResolution.Failure("The airline source could not be verified. Retry or enter the airline manually.");
                return result;
            }
            catch(Exception) {return AirlineResolution.Failure("The airline result could not be read. No airline has been selected.");}
        }
    }
}
