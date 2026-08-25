using System;
using System.Collections.Generic;
using System.IO;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading.Tasks;

namespace Ow2ServerPicker
{
    /// <summary>
    /// Finds an ICMP-responsive address inside each datacenter and writes it back to
    /// servers.json as "pingTarget", so the app's ping column measures the real
    /// datacenter instead of a nearby proxy.
    ///
    /// Candidates come from each datacenter's EXCLUSIVE space first - its own ranges
    /// minus every other datacenter's - because the catalog's ranges genuinely overlap
    /// and an address drawn from shared space cannot be attributed to one datacenter.
    /// Only if that finds nothing does it fall back to the datacenter's full ranges,
    /// and it says so, rather than silently reporting an ambiguous probe as exact.
    ///
    /// Reuses IpMath rather than reimplementing interval subtraction: there is one
    /// tested implementation of that logic and it should stay that way.
    ///
    /// Build and run with tools\find-probes.cmd.
    /// </summary>
    internal static class FindProbes
    {
        private const int MaxCandidates = 28;
        private const int TimeoutMs = 1500;

        /// <summary>
        /// Exclusive space smaller than this is not a representative sample of a
        /// datacenter. Taiwan, for instance, has only 256 exclusive addresses, and
        /// probing them returned 209 ms when its neighbours Tokyo and Singapore measure
        /// 133 and 121 - the sliver is simply not in Taipei. Below this threshold the
        /// datacenter's full range set is the better estimate despite the ambiguity.
        /// </summary>
        private const long MinExclusiveAddresses = 4096;

        private static readonly int[] Offsets = { 1, 2, 3, 65, 129, 130, 193, 257 };

        private static int Main(string[] args)
        {
            string path = args.Length > 0
                ? args[0]
                : Path.Combine(Directory.GetCurrentDirectory(), "data", "servers.json");
            bool write = Array.IndexOf(args, "--write") >= 0;

            if (!File.Exists(path)) { Console.WriteLine("not found: " + path); return 1; }

            string json = File.ReadAllText(path);
            Catalog cat = Catalog.Parse(json);
            Console.WriteLine("catalog: {0} datacenters\n", cat.Datacenters.Count);

            Dictionary<string, string> found = new Dictionary<string, string>();
            List<string> cleared = new List<string>();
            List<string> report = new List<string>();

            foreach (Datacenter dc in cat.Datacenters)
            {
                List<Interval> others = new List<Interval>();
                foreach (Datacenter o in cat.Datacenters)
                    if (o.Code != dc.Code) others.AddRange(o.Ranges);

                List<Interval> exclusive = IpMath.Subtract(dc.Ranges, others);
                long exclusiveCount = IpMath.TotalAddresses(exclusive);

                Console.Write("{0,-6} exclusive {1,12:N0}  ", dc.Code, exclusiveCount);

                bool trustExclusive = exclusiveCount >= MinExclusiveAddresses;
                Probe best = trustExclusive ? Sweep(exclusive) : null;
                string quality = best != null ? "exclusive" : null;

                if (best == null)
                {
                    // Either there is no attributable space worth sampling, or nothing in
                    // it answered. A probe from shared space still beats no reading, as
                    // long as it is labelled rather than passed off as exact.
                    best = Sweep(dc.Ranges);
                    quality = trustExclusive ? "shared" : "shared (exclusive too small)";
                }

                if (best == null)
                {
                    // Clear rather than leave whatever was there. A stale address that no
                    // longer answers renders as "n/a", which implies a real target was
                    // tried; null renders as "-", which honestly says we have none.
                    Console.WriteLine("-> silent (clearing any existing target)");
                    report.Add(string.Format("{0,-6} {1,-26} no ICMP responder", dc.Code, dc.Name));
                    cleared.Add(dc.Code);
                    continue;
                }

                Console.WriteLine("-> {0,-16} {1,4} ms  ({2})", best.Ip, best.Rtt, quality);
                found[dc.Code] = best.Ip;
                report.Add(string.Format("{0,-6} {1,-26} {2,-16} {3,4} ms  {4}",
                    dc.Code, dc.Name, best.Ip, best.Rtt, quality));
            }

            Console.WriteLine();
            foreach (string line in report) Console.WriteLine("  " + line);
            Console.WriteLine("\nresponders: {0} of {1}", found.Count, cat.Datacenters.Count);

            if (!write)
            {
                Console.WriteLine("\n(dry run - pass --write to update servers.json)");
                return 0;
            }

            int changed = Rewrite(path, json, found, cleared);
            Console.WriteLine("\nupdated {0} pingTarget value(s) in {1}", changed, path);
            return 0;
        }

        private sealed class Probe
        {
            public string Ip;
            public long Rtt;
        }

        private static List<string> Candidates(List<Interval> space)
        {
            List<string> list = new List<string>();
            foreach (Interval iv in space)
            {
                if (list.Count >= MaxCandidates) break;
                long size = iv.Count;
                foreach (int off in Offsets)
                {
                    if (off >= size) continue;
                    string ip = IpMath.Format((uint)(iv.Start + off));
                    if (!list.Contains(ip)) list.Add(ip);
                    if (list.Count >= MaxCandidates) break;
                }
            }
            return list;
        }

        private static Probe Sweep(List<Interval> space)
        {
            List<string> cands = Candidates(space);
            if (cands.Count == 0) return null;

            List<Ping> pings = new List<Ping>();
            List<Task<PingReply>> tasks = new List<Task<PingReply>>();
            foreach (string ip in cands)
            {
                Ping p = new Ping();
                pings.Add(p);
                tasks.Add(p.SendPingAsync(ip, TimeoutMs));
            }

            // All pings run concurrently with the same timeout, so waiting much beyond
            // that timeout just stalls the sweep on stragglers that will never answer.
            try { Task.WaitAll(tasks.ToArray(), TimeoutMs + 750); }
            catch { /* individual failures are read below */ }

            Probe best = null;
            for (int i = 0; i < tasks.Count; i++)
            {
                Task<PingReply> t = tasks[i];
                if (!t.IsCompleted || t.IsFaulted || t.IsCanceled) continue;
                PingReply r = t.Result;
                if (r == null || r.Status != IPStatus.Success) continue;
                if (best == null || r.RoundtripTime < best.Rtt)
                    best = new Probe { Ip = cands[i], Rtt = r.RoundtripTime };
            }

            // Disposing a Ping with a send still in flight can block, so only reclaim
            // the ones that have actually finished; the rest are left to the GC.
            for (int i = 0; i < pings.Count; i++)
                if (tasks[i].IsCompleted) { try { pings[i].Dispose(); } catch { } }
            return best;
        }

        /// <summary>
        /// Rewrites only the pingTarget lines, leaving the rest of the file byte-identical.
        /// A full re-serialise would reorder keys and reflow all 322 ranges, burying the
        /// actual change in an unreviewable diff.
        /// </summary>
        private static int Rewrite(string path, string json,
                                   Dictionary<string, string> found, List<string> cleared)
        {
            string[] lines = json.Replace("\r\n", "\n").Split('\n');
            string currentCode = null;
            int changed = 0;

            for (int i = 0; i < lines.Length; i++)
            {
                string trimmed = lines[i].Trim();

                if (trimmed.StartsWith("\"code\""))
                {
                    int a = trimmed.IndexOf(':');
                    currentCode = trimmed.Substring(a + 1).Trim().Trim(',').Trim('"');
                    continue;
                }

                if (!trimmed.StartsWith("\"pingTarget\"") || currentCode == null) continue;

                bool hit = found.ContainsKey(currentCode);
                bool clear = cleared.Contains(currentCode);
                if (!hit && !clear) continue;

                string indent = lines[i].Substring(0, lines[i].Length - lines[i].TrimStart().Length);
                bool comma = trimmed.EndsWith(",");
                string value = hit ? "\"" + found[currentCode] + "\"" : "null";
                string replacement = indent + "\"pingTarget\": " + value + (comma ? "," : "");
                if (lines[i] != replacement) { lines[i] = replacement; changed++; }
            }

            File.WriteAllText(path, string.Join(Environment.NewLine, lines), new UTF8Encoding(false));
            return changed;
        }
    }
}
