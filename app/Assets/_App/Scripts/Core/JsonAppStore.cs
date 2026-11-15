using System;
using System.IO;
using System.Net;
using System.Text;

namespace TravelAgent.Core
{
    /// <summary>Versioned local storage. Unity provides JsonUtility delegates; the domain has no engine dependency.</summary>
    public sealed class JsonAppStore : IAppStore
    {
        public string RootDirectory { get; private set; }
        public string LastWarning { get; private set; } = "";
        private readonly string statePath;
        private readonly bool demo;
        private readonly Func<AppData, string> serialize;
        private readonly Func<string, AppData> deserialize;

        public JsonAppStore(string rootPath, bool demo, Func<AppData, string> serialize, Func<string, AppData> deserialize)
        {
            if (string.IsNullOrWhiteSpace(rootPath)) throw new ArgumentException("A local storage root is required.", nameof(rootPath));
            this.serialize = serialize ?? throw new ArgumentNullException(nameof(serialize));
            this.deserialize = deserialize ?? throw new ArgumentNullException(nameof(deserialize));
            this.demo = demo;
            RootDirectory = Path.Combine(Path.GetFullPath(rootPath), demo ? "Demo" : "Real");
            statePath = Path.Combine(RootDirectory, "app-state.json");
        }

        public AppData Load()
        {
            LastWarning = "";
            Directory.CreateDirectory(RootDirectory);
            if (!File.Exists(statePath)) return new AppData();
            try { return Read(statePath); }
            catch (Exception primary) when (primary is IOException || primary is ArgumentException || primary is FormatException)
            {
                if (File.Exists(statePath + ".bak"))
                {
                    try
                    {
                        AppData recovered = Read(statePath + ".bak");
                        string preserved = Path.Combine(RootDirectory, "unreadable-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6) + ".json");
                        File.Copy(statePath, preserved, false);
                        LastWarning = "Recovered the previous local save. The unreadable current file was preserved.";
                        return recovered;
                    }
                    catch (Exception backup) when (backup is IOException || backup is ArgumentException || backup is FormatException) { }
                }
                throw new InvalidDataException("Local evidence could not be read. The existing files were preserved.", primary);
            }
        }

        private AppData Read(string path)
        {
            AppData data = deserialize(File.ReadAllText(path, Encoding.UTF8));
            if (data == null) throw new InvalidDataException("The saved JSON did not contain app data.");
            // A newer schema is not corruption. Do not silently recover an old backup over newer evidence.
            if (data.schemaVersion != 1) throw new NotSupportedException("Unsupported evidence schema " + data.schemaVersion + "; migration is required.");
            if (data.journey == null) data.journey = new JourneyRecord();
            if (data.events == null) data.events = new System.Collections.Generic.List<BagEvent>();
            if (data.motionSamples == null) data.motionSamples = new System.Collections.Generic.List<MotionSample>();
            if (data.motionSamples.Count > 3000) data.motionSamples.RemoveRange(0, data.motionSamples.Count - 3000);
            if (data.journey.photos == null) data.journey.photos = new System.Collections.Generic.List<PhotoEvidence>();
            return data;
        }

        public void Save(AppData data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (data.schemaVersion != 1) throw new InvalidDataException("Refusing to overwrite an unsupported schema.");
            Directory.CreateDirectory(RootDirectory);
            string temporary = statePath + ".tmp";
            File.WriteAllText(temporary, serialize(data), new UTF8Encoding(false));
            if (!File.Exists(statePath)) File.Move(temporary, statePath);
            else
            {
                try { File.Replace(temporary, statePath, statePath + ".bak"); }
                catch (PlatformNotSupportedException)
                {
                    File.Copy(statePath, statePath + ".bak", true);
                    File.Copy(temporary, statePath, true);
                    File.Delete(temporary);
                }
            }
        }

        /// <summary>Presentation-only fresh trip in either provider; archives prior evidence and retains device configuration/original files.</summary>
        public AppData StartFreshDemoSession(AppData previous)
        {
            if (previous == null) throw new ArgumentNullException(nameof(previous));
            var fresh = new AppData {
                bagName = previous.bagName,
                confirmedArmSequence = previous.confirmedArmSequence,
                confirmedDisarmSequence = previous.confirmedDisarmSequence,
                confirmedConfigRevision = previous.confirmedConfigRevision,
                retiredTicketRequestId = !string.IsNullOrEmpty(previous.journey?.ticketImportRequestId)
                    ? previous.journey.ticketImportRequestId : previous.retiredTicketRequestId,
                journey = new JourneyRecord { stage = "Choose your airline" }
            };
            if (File.Exists(statePath))
            {
                string history = Path.Combine(RootDirectory, "JourneyHistory");
                Directory.CreateDirectory(history);
                File.WriteAllText(Path.Combine(history, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N") + ".json"), serialize(previous), new UTF8Encoding(false));
            }
            Save(fresh);
            return fresh;
        }

        /// <summary>Only demo-owned state is cleared. Original photo files, exports and real evidence remain untouched.</summary>
        public void Reset()
        {
            if (!demo) throw new InvalidOperationException("Demo reset cannot erase the real evidence store.");
            foreach (string suffix in new[] { "", ".bak", ".tmp" })
            {
                string file = statePath + suffix;
                if (File.Exists(file)) File.Delete(file);
            }
            LastWarning = "";
        }

        public string Export(AppData data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            string directory = Path.Combine(RootDirectory, "Exports", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6));
            Directory.CreateDirectory(directory);
            Directory.CreateDirectory(Path.Combine(directory, "images"));
            File.WriteAllText(Path.Combine(directory, "evidence.json"), serialize(data), new UTF8Encoding(false));
            JourneyRecord journey = data.journey ?? new JourneyRecord();
            var text = new StringBuilder();
            text.AppendLine("Suitcase evidence report");
            text.AppendLine(demo ? "DEMO STORE — sample content may be included; inspect each origin label." : "REAL STORE — inspect each event/photo origin and quality.");
            text.AppendLine("Exported UTC: " + DateTime.UtcNow.ToString("O"));
            text.AppendLine("Airline: " + journey.airline);
            text.AppendLine("Flight: " + journey.flight);
            text.AppendLine("Route: " + journey.route);
            text.AppendLine("Bag: " + journey.bagDescription);
            text.AppendLine("Baggage reference: " + journey.baggageReference);
            text.AppendLine("Collection date/context: " + journey.collectionDate);
            text.AppendLine("Journey stage: " + journey.stage);
            text.AppendLine("Damage confirmed by traveler: " + (journey.damageConfirmed ? "yes" : "no"));
            text.AppendLine("Traveler description: " + journey.damageDescription);
            text.AppendLine();
            text.AppendLine("DRAFT FOR USER REVIEW");
            text.AppendLine(journey.claimDraft);
            text.AppendLine();
            text.AppendLine("EVIDENCE FILE LIST");
            if (journey.photos != null)
            {
                int count = 0;
                foreach (PhotoEvidence photo in journey.photos)
                {
                    count++;
                    string safeExtension = Path.GetExtension(photo.path ?? "");
                    if (safeExtension.Length > 8 || safeExtension.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) safeExtension = ".image";
                    string fileName = "photo-" + count.ToString("D2") + safeExtension;
                    string copied = "missing original";
                    if (!string.IsNullOrWhiteSpace(photo.path) && File.Exists(photo.path))
                    {
                        File.Copy(photo.path, Path.Combine(directory, "images", fileName), false);
                        copied = "images/" + fileName;
                    }
                    text.AppendLine(photo.stage + " | " + photo.origin + " | " + photo.capturedAtUtc + " | " + copied);
                }
            }
            text.AppendLine();
            text.AppendLine("HANDLING TIMELINE");
            if (data.events != null)
                foreach (BagEvent item in data.events)
                    text.AppendLine(item.recordedAtUtc + " | " + item.type + " | " + item.summary + " | " + item.origin + " | " + item.quality + " | " + item.deviceId + "/" + item.bootId + "/" + item.sequence);
            text.AppendLine();
            text.AppendLine("Sensor events do not establish damage, cause or liability. A prepared draft or opened form is not a submitted claim.");
            if (!string.IsNullOrWhiteSpace(journey.submissionReference))
                text.AppendLine("User-recorded submission reference: " + journey.submissionReference + " at " + journey.submissionConfirmedAtUtc);
            File.WriteAllText(Path.Combine(directory, "report.txt"), text.ToString(), new UTF8Encoding(false));
            string html = "<!doctype html><html lang=\"en\"><meta charset=\"utf-8\"><title>Suitcase evidence report</title>" +
                "<style>body{font:16px/1.5 system-ui,sans-serif;max-width:900px;margin:40px auto;padding:0 24px;color:#18332f}pre{white-space:pre-wrap;overflow-wrap:anywhere}</style>" +
                "<h1>Suitcase evidence report</h1><pre>" + WebUtility.HtmlEncode(text.ToString()) + "</pre></html>";
            File.WriteAllText(Path.Combine(directory, "report.html"), html, new UTF8Encoding(false));
            return directory;
        }
    }
}
