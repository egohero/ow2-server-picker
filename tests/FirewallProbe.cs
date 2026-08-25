using System;
using System.Collections.Generic;
using System.IO;

namespace Ow2ServerPicker
{
    /// <summary>
    /// Validates the firewall rule contract WITHOUT writing anything to the system.
    /// Building an HNetCfg.FWRule and assigning its properties needs no elevation, and the
    /// COM object validates RemoteAddresses on assignment - so this proves the address
    /// format and every property name/type is accepted. Only Rules.Add() would persist,
    /// and this probe deliberately never calls it.
    /// </summary>
    internal static class FirewallProbe
    {
        [STAThread]
        private static int Main()
        {
            int failed = 0;
            try
            {
                string json = Path.Combine(
                    Path.GetDirectoryName(typeof(FirewallProbe).Assembly.Location), "servers.json");
                Catalog cat = Catalog.Parse(File.ReadAllText(json));

                List<Interval> keep = new List<Interval>();
                List<Interval> candidate = new List<Interval>();
                foreach (Datacenter dc in cat.Datacenters)
                {
                    if (dc.Code == "SYD2") keep.AddRange(dc.Ranges);
                    else candidate.AddRange(dc.Ranges);
                }
                List<Interval> blocked = IpMath.Subtract(candidate, keep);
                Console.WriteLine("computed block set: {0} intervals", blocked.Count);

                Type ruleType = Type.GetTypeFromProgID("HNetCfg.FWRule");
                if (ruleType == null) { Console.WriteLine("FAIL: HNetCfg.FWRule unavailable"); return 1; }

                dynamic rule = Activator.CreateInstance(ruleType);
                rule.Name = "OW2ServerPicker-probe";
                rule.Description = "probe - never added to the firewall";
                rule.Protocol = 17;
                rule.Direction = 2;
                rule.Action = 0;
                rule.Profiles = 0x7FFFFFFF;
                rule.Enabled = true;
                rule.ApplicationName = @"C:\Program Files (x86)\Overwatch\_retail_\Overwatch.exe";

                // The real payload, in the exact chunk size the app uses.
                int chunk = Math.Min(150, blocked.Count);
                string[] parts = new string[chunk];
                for (int i = 0; i < chunk; i++) parts[i] = blocked[i].ToString();
                string joined = string.Join(",", parts);
                rule.RemoteAddresses = joined;

                Console.WriteLine("assigned {0} intervals ({1} chars)", chunk, joined.Length);

                string readBack = rule.RemoteAddresses as string;
                Console.WriteLine("read back  {0} chars", readBack == null ? -1 : readBack.Length);

                if (string.IsNullOrEmpty(readBack)) { Console.WriteLine("FAIL: RemoteAddresses did not persist"); failed++; }
                if ((int)rule.Protocol != 17) { Console.WriteLine("FAIL: Protocol not retained"); failed++; }
                if ((int)rule.Direction != 2) { Console.WriteLine("FAIL: Direction not retained"); failed++; }
                if ((int)rule.Action != 0) { Console.WriteLine("FAIL: Action not retained"); failed++; }
                if (rule.Enabled != true) { Console.WriteLine("FAIL: Enabled not retained"); failed++; }

                // Windows normalises single addresses and may reformat ranges; confirm the
                // count of comma-separated entries survives the round trip.
                int outCount = readBack.Split(',').Length;
                Console.WriteLine("entries in : {0}", chunk);
                Console.WriteLine("entries out: {0}", outCount);
                if (outCount != chunk) { Console.WriteLine("WARN: entry count changed on round trip"); }

                Console.WriteLine(failed == 0 ? "PROBE OK (nothing was written to the firewall)" : "PROBE FAILED");
                return failed == 0 ? 0 : 1;
            }
            catch (Exception ex)
            {
                Console.WriteLine("PROBE FAILED: " + ex.GetType().Name + ": " + ex.Message);
                return 1;
            }
        }
    }
}
