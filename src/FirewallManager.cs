using System;
using System.Collections;
using System.Collections.Generic;

namespace Ow2ServerPicker
{
    /// <summary>
    /// Creates and removes the Windows Firewall rules, via the COM firewall API
    /// (HNetCfg.FwPolicy2) rather than netsh - no child processes, no console flashes,
    /// no dependence on the localised text netsh prints, and rule enumeration is exact.
    /// </summary>
    internal static class FirewallManager
    {
        public const string RulePrefix = "OW2ServerPicker";

        // NET_FW_ACTION_BLOCK = 0, NET_FW_RULE_DIR_OUT = 2, IPPROTO_UDP = 17
        private const int ActionBlock = 0;
        private const int DirectionOut = 2;
        private const int ProtocolUdp = 17;
        private const int ProfileAll = 0x7FFFFFFF;

        // Each rule holds a slice of the block set. The firewall caps how long a single
        // RemoteAddresses string may be, so large selections are split across rules.
        private const int IntervalsPerRule = 150;

        private static dynamic CreatePolicy()
        {
            Type t = Type.GetTypeFromProgID("HNetCfg.FwPolicy2");
            if (t == null)
                throw new InvalidOperationException(
                    "Windows Firewall COM API is unavailable on this system.");
            return Activator.CreateInstance(t);
        }

        public static List<string> ListOurRuleNames()
        {
            List<string> names = new List<string>();
            dynamic rules = CreatePolicy().Rules;
            foreach (object o in (IEnumerable)rules)
            {
                try
                {
                    dynamic r = o;
                    string name = r.Name as string;
                    if (!string.IsNullOrEmpty(name) &&
                        name.StartsWith(RulePrefix, StringComparison.OrdinalIgnoreCase))
                        names.Add(name);
                }
                catch
                {
                    // A malformed third-party rule should not stop us reading our own.
                }
            }
            return names;
        }

        public static int RemoveAll()
        {
            dynamic rules = CreatePolicy().Rules;
            List<string> names = ListOurRuleNames();
            int removed = 0;
            foreach (string name in names)
            {
                try { rules.Remove(name); removed++; }
                catch { }
            }
            return removed;
        }

        /// <summary>
        /// Replaces every rule this app owns with a fresh set blocking <paramref name="blocked"/>.
        /// Pass a non-null programPath to confine the block to that executable, which keeps the
        /// rules from touching anything else on the machine that shares those addresses.
        /// </summary>
        public static int Apply(List<Interval> blocked, string programPath, string summary)
        {
            RemoveAll();
            if (blocked == null || blocked.Count == 0) return 0;

            dynamic rules = CreatePolicy().Rules;
            Type ruleType = Type.GetTypeFromProgID("HNetCfg.FWRule");
            if (ruleType == null)
                throw new InvalidOperationException("Cannot create firewall rule objects.");

            int created = 0;
            for (int offset = 0; offset < blocked.Count; offset += IntervalsPerRule)
            {
                int count = Math.Min(IntervalsPerRule, blocked.Count - offset);
                string[] parts = new string[count];
                for (int i = 0; i < count; i++) parts[i] = blocked[offset + i].ToString();

                dynamic rule = Activator.CreateInstance(ruleType);
                rule.Name = string.Format("{0}-{1:D2}", RulePrefix, created + 1);
                rule.Description = summary;
                rule.Protocol = ProtocolUdp;
                rule.Direction = DirectionOut;
                rule.Action = ActionBlock;
                rule.RemoteAddresses = string.Join(",", parts);
                rule.Profiles = ProfileAll;
                rule.Enabled = true;
                if (!string.IsNullOrEmpty(programPath))
                    rule.ApplicationName = programPath;

                rules.Add(rule);
                created++;
            }
            return created;
        }
    }
}
