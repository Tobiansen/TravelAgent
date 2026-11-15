using System;
using System.Collections.Generic;

namespace TravelAgent.Core
{
    /// <summary>Reconnect replay is at-least-once. Preserve originals and remove duplicate identities.</summary>
    public sealed class EventLedger
    {
        private readonly HashSet<string> identities = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<BagEvent> events = new List<BagEvent>();
        public IReadOnlyList<BagEvent> Events { get { return events; } }

        public EventLedger() { }
        public EventLedger(IEnumerable<BagEvent> initial)
        {
            if (initial == null) return;
            foreach (BagEvent item in initial) Add(item);
        }

        public bool Add(BagEvent item)
        {
            if (item == null) return false;
            // Length prefixes avoid accidental collisions when identifiers themselves contain separators.
            string device = item.deviceId ?? "";
            string boot = item.bootId ?? "";
            string identity = device.Length + ":" + device + boot.Length + ":" + boot + ":" + item.sequence;
            if (!identities.Add(identity)) return false;
            events.Add(item.Copy());
            return true;
        }

        public void Clear() { identities.Clear(); events.Clear(); }
    }
}
