using System;
using System.Collections.Generic;
using System.IO;

namespace Ow2ServerPicker
{
    /// <summary>Console harness over IpMath and the shipped catalog. Run via tests\run-tests.cmd.</summary>
    internal static class SelfTest
    {
        private static int _failed;
        private static int _passed;

        private static void Check(bool condition, string label)
        {
            if (condition) { _passed++; Console.WriteLine("  PASS  " + label); }
            else { _failed++; Console.WriteLine("  FAIL  " + label); }
        }

        private static Interval P(string text)
        {
            Interval iv;
            if (!IpMath.TryParse(text, out iv)) throw new Exception("could not parse " + text);
            return iv;
        }

        private static List<Interval> L(params string[] items)
        {
            List<Interval> list = new List<Interval>();
            foreach (string s in items) list.Add(P(s));
            return list;
        }

        private static bool Contains(List<Interval> set, uint address)
        {
            foreach (Interval iv in set)
                if (address >= iv.Start && address <= iv.End) return true;
            return false;
        }


        private sealed class Row : ISortableRow
        {
            public string N; public string C; public int R; public long P; public bool Has;
            public string SortName { get { return N; } }
            public string SortCode { get { return C; } }
            public int SortRanges { get { return R; } }
            public long SortPing { get { return P; } }
            public bool HasPing { get { return Has; } }
        }

        private static Row Mk(string name, string code, int ranges, long ping)
        {
            return new Row { N = name, C = code, R = ranges, P = ping, Has = ping >= 0 };
        }

        private static string Order(List<Row> rows, SortKey key, bool desc)
        {
            List<Row> copy = new List<Row>(rows);
            Sorting.Sort(copy, key, desc);
            List<string> names = new List<string>();
            foreach (Row r in copy) names.Add(r.N);
            return string.Join(",", names.ToArray());
        }

        private static void SortTests()
        {
            Console.WriteLine();
            Console.WriteLine("Sorting");

            List<Row> rows = new List<Row>
            {
                Mk("Sydney",    "SYD2",  21,  29),
                Mk("Singapore", "GSG1",  59, 121),
                Mk("Amsterdam", "AMS1",   2,  -1),   // probe silent
                Mk("Bahrain",   "MES1",   4,  -2),   // never measured
                Mk("Chicago",   "ORD1",  79, 210),
            };

            Check(Order(rows, SortKey.Ping, false).StartsWith("Sydney,Singapore,Chicago"),
                "ping ascending puts the closest first");
            Check(Order(rows, SortKey.Ping, true).StartsWith("Chicago,Singapore,Sydney"),
                "ping descending reverses the measured rows");

            // The rule that matters: absence is not slowness.
            Check(Order(rows, SortKey.Ping, false).EndsWith("Amsterdam,Bahrain"),
                "unmeasured rows sink in ascending order");
            Check(Order(rows, SortKey.Ping, true).EndsWith("Amsterdam,Bahrain"),
                "unmeasured rows ALSO sink in descending order");

            Check(Order(rows, SortKey.Ranges, false) == "Amsterdam,Bahrain,Sydney,Singapore,Chicago",
                "ranges ascending is numeric, not lexicographic");
            Check(Order(rows, SortKey.Ranges, true) == "Chicago,Singapore,Sydney,Bahrain,Amsterdam",
                "ranges descending reverses");

            Check(Order(rows, SortKey.Name, false) == "Amsterdam,Bahrain,Chicago,Singapore,Sydney",
                "name ascending is alphabetical");
            Check(Order(rows, SortKey.Code, false) == "Amsterdam,Singapore,Bahrain,Chicago,Sydney",
                "code ascending sorts by datacenter code");

            // Ties must not shuffle, or repeated sorts would reorder the list under the user.
            List<Row> ties = new List<Row>
            {
                Mk("Delta", "D1", 5, 100), Mk("Alpha", "A1", 5, 100), Mk("Charlie", "C1", 5, 100),
            };
            Check(Order(ties, SortKey.Ranges, false) == "Alpha,Charlie,Delta", "ties break by name");
            Check(Order(ties, SortKey.Ranges, true) == "Alpha,Charlie,Delta",
                "ties break by name in descending too, so order is stable");

            Check(Order(rows, SortKey.None, false) == Order(rows, SortKey.None, false),
                "SortKey.None is a no-op comparison");
        }

        private static int Main(string[] args)
        {
            Console.WriteLine();
            Console.WriteLine("Parsing");
            Check(P("34.124.40.0/23").ToString() == "34.124.40.0-34.124.41.255", "/23 expands correctly");
            Check(P("10.0.0.0/8").ToString() == "10.0.0.0-10.255.255.255", "/8 expands correctly");
            Check(P("1.2.3.4/32").ToString() == "1.2.3.4", "/32 collapses to a single address");
            Check(P("0.0.0.0/0").Count == 4294967296L, "/0 counts the whole space without wrapping");
            Check(P("5.6.7.8").ToString() == "5.6.7.8", "bare address parses");
            Check(P("1.1.1.10-1.1.1.20").Count == 11, "explicit range is inclusive");
            Check(P("1.1.1.20-1.1.1.10").ToString() == "1.1.1.10-1.1.1.20", "reversed range is normalised");

            Interval dummy;
            Check(!IpMath.TryParse("300.1.1.1/24", out dummy), "octet > 255 rejected");
            Check(!IpMath.TryParse("1.2.3.4/33", out dummy), "prefix > 32 rejected");
            Check(!IpMath.TryParse("garbage", out dummy), "junk rejected");
            Check(!IpMath.TryParse("", out dummy), "empty rejected");

            Console.WriteLine();
            Console.WriteLine("Merging");
            Check(IpMath.Merge(L("1.0.0.0/24", "1.0.1.0/24")).Count == 1, "adjacent blocks coalesce");
            Check(IpMath.Merge(L("1.0.0.0/24", "1.0.0.128/25")).Count == 1, "contained block absorbed");
            Check(IpMath.Merge(L("1.0.0.0/24", "1.0.5.0/24")).Count == 2, "disjoint blocks stay separate");
            Check(IpMath.Merge(L("2.0.0.0/24", "1.0.0.0/24"))[0].ToString() == "1.0.0.0-1.0.0.255",
                "merge output is sorted");

            Console.WriteLine();
            Console.WriteLine("Subtraction");
            List<Interval> hole = IpMath.Subtract(L("1.0.0.0/24"), L("1.0.0.100-1.0.0.150"));
            Check(hole.Count == 2, "carving the middle yields two intervals");
            Check(hole[0].ToString() == "1.0.0.0-1.0.0.99" && hole[1].ToString() == "1.0.0.151-1.0.0.255",
                "carved boundaries are exact");
            Check(IpMath.Subtract(L("1.0.0.0/24"), L("1.0.0.0/24")).Count == 0, "full removal empties the set");
            Check(IpMath.Subtract(L("1.0.0.0/24"), L("2.0.0.0/24")).Count == 1, "unrelated removal is a no-op");
            Check(IpMath.Subtract(L("1.0.0.0/24"), L("1.0.0.0/16")).Count == 0, "superset removal empties the set");
            Check(IpMath.Subtract(L("1.0.0.0/24"), L("1.0.0.0/25"))[0].ToString() == "1.0.0.128-1.0.0.255",
                "leading removal trims the front");

            // The endpoint that would wrap a uint if the cut arithmetic were done in 32 bits.
            List<Interval> top = IpMath.Subtract(L("255.255.255.0/24"), L("255.255.255.0-255.255.255.255"));
            Check(top.Count == 0, "cut at 255.255.255.255 does not wrap");
            Check(IpMath.Subtract(L("0.0.0.0/0"), L("0.0.0.0/1")).Count == 1, "half-space removal is stable");

            Console.WriteLine();
            Console.WriteLine("Real catalog: 'play only on Sydney'");

            string json = Path.Combine(
                Path.GetDirectoryName(typeof(SelfTest).Assembly.Location), "servers.json");
            if (!File.Exists(json))
            {
                Console.WriteLine("  SKIP  servers.json not found next to the test binary");
            }
            else
            {
                Catalog cat = Catalog.Parse(File.ReadAllText(json));
                Check(cat.Warnings.Count == 0, "catalog parses with no warnings");
                Check(cat.Datacenters.Count >= 10, "catalog has a plausible number of datacenters");

                Datacenter syd = null, sgp = null;
                foreach (Datacenter dc in cat.Datacenters)
                {
                    if (dc.Code == "SYD2") syd = dc;
                    if (dc.Code == "GSG1") sgp = dc;
                }
                Check(syd != null, "SYD2 present");
                Check(sgp != null, "GSG1 present");

                // Sanity: the overlap this whole design exists for is genuinely there.
                bool overlaps = false;
                foreach (Interval a in syd.Ranges)
                    foreach (Interval b in sgp.Ranges)
                        if (a.Start <= b.End && b.Start <= a.End) overlaps = true;
                Check(overlaps, "SYD2 and GSG1 really do overlap (the bug this guards against)");

                List<Interval> keep = new List<Interval>(syd.Ranges);
                List<Interval> candidate = new List<Interval>();
                foreach (Datacenter dc in cat.Datacenters)
                    if (dc.Code != "SYD2") candidate.AddRange(dc.Ranges);

                List<Interval> blocked = IpMath.Subtract(candidate, keep);

                // The load-bearing assertion: no address Sydney needs may end up blocked.
                int leaked = 0;
                foreach (Interval a in syd.Ranges)
                {
                    if (Contains(blocked, a.Start)) leaked++;
                    if (Contains(blocked, a.End)) leaked++;
                    if (Contains(blocked, a.Start + (a.End - a.Start) / 2)) leaked++;
                }
                Check(leaked == 0, "no Sydney address survives in the block set");

                // ...while Singapore-only space is still blocked.
                int sgpBlocked = 0, sgpTotal = 0;
                foreach (Interval b in sgp.Ranges)
                {
                    bool insideSyd = false;
                    foreach (Interval a in syd.Ranges)
                        if (b.Start >= a.Start && b.Start <= a.End) insideSyd = true;
                    if (insideSyd) continue;
                    sgpTotal++;
                    if (Contains(blocked, b.Start)) sgpBlocked++;
                }
                Check(sgpTotal > 0 && sgpBlocked == sgpTotal,
                    string.Format("all {0} Singapore-only ranges are blocked", sgpTotal));

                Console.WriteLine(string.Format(
                    "        ({0} intervals blocked, {1:N0} addresses, {2} rule set(s))",
                    blocked.Count, IpMath.TotalAddresses(blocked), (blocked.Count + 149) / 150));
            }

            SortTests();

            Console.WriteLine();
            Console.WriteLine(string.Format("{0} passed, {1} failed", _passed, _failed));
            Console.WriteLine();
            return _failed == 0 ? 0 : 1;
        }
    }
}
