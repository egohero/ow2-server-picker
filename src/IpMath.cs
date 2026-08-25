using System;
using System.Collections.Generic;

namespace Ow2ServerPicker
{
    /// <summary>An inclusive IPv4 interval held as unsigned 32-bit endpoints.</summary>
    internal struct Interval
    {
        public readonly uint Start;
        public readonly uint End;

        public Interval(uint start, uint end)
        {
            Start = start;
            End = end;
        }

        /// <summary>long, not uint: a full 0.0.0.0/0 interval would wrap a 32-bit count to zero.</summary>
        public long Count
        {
            get { return (long)End - Start + 1; }
        }

        /// <summary>netsh accepts both "a.b.c.d" and "a.b.c.d-e.f.g.h" for remoteip.</summary>
        public override string ToString()
        {
            if (Start == End) return IpMath.Format(Start);
            return IpMath.Format(Start) + "-" + IpMath.Format(End);
        }
    }

    /// <summary>
    /// Interval arithmetic over IPv4 space.
    ///
    /// This exists because Overwatch datacenter ranges genuinely overlap - Singapore's
    /// 34.124.0.0-34.124.255.255 contains Sydney's 34.124.40.0/23, for example. Windows
    /// Firewall resolves block-vs-allow in favour of Block, so emitting one block rule per
    /// unwanted datacenter would silently take out the datacenter the user wanted to keep.
    /// The fix is to subtract the kept ranges from the blocked ones before writing any rule.
    /// </summary>
    internal static class IpMath
    {
        public static string Format(uint value)
        {
            return string.Format("{0}.{1}.{2}.{3}",
                (value >> 24) & 0xFF, (value >> 16) & 0xFF, (value >> 8) & 0xFF, value & 0xFF);
        }

        private static bool TryParseAddress(string text, out uint value)
        {
            value = 0;
            if (string.IsNullOrEmpty(text)) return false;
            string[] parts = text.Split('.');
            if (parts.Length != 4) return false;
            uint acc = 0;
            for (int i = 0; i < 4; i++)
            {
                int octet;
                if (!int.TryParse(parts[i], out octet)) return false;
                if (octet < 0 || octet > 255) return false;
                acc = (acc << 8) | (uint)octet;
            }
            value = acc;
            return true;
        }

        /// <summary>Parses "a.b.c.d", "a.b.c.d/n", or "a.b.c.d-e.f.g.h".</summary>
        public static bool TryParse(string text, out Interval result)
        {
            result = new Interval(0, 0);
            if (text == null) return false;
            text = text.Trim();
            if (text.Length == 0) return false;

            int slash = text.IndexOf('/');
            if (slash > 0)
            {
                uint baseAddr;
                int prefix;
                if (!TryParseAddress(text.Substring(0, slash), out baseAddr)) return false;
                if (!int.TryParse(text.Substring(slash + 1), out prefix)) return false;
                if (prefix < 0 || prefix > 32) return false;

                uint mask = prefix == 0 ? 0u : (uint)(0xFFFFFFFFL << (32 - prefix));
                uint start = baseAddr & mask;
                uint end = start | ~mask;
                result = new Interval(start, end);
                return true;
            }

            int dash = text.IndexOf('-');
            if (dash > 0)
            {
                uint lo, hi;
                if (!TryParseAddress(text.Substring(0, dash), out lo)) return false;
                if (!TryParseAddress(text.Substring(dash + 1), out hi)) return false;
                if (lo > hi) { uint t = lo; lo = hi; hi = t; }
                result = new Interval(lo, hi);
                return true;
            }

            uint single;
            if (!TryParseAddress(text, out single)) return false;
            result = new Interval(single, single);
            return true;
        }

        /// <summary>Sorts, then coalesces overlapping and adjacent intervals.</summary>
        public static List<Interval> Merge(IEnumerable<Interval> input)
        {
            List<Interval> list = new List<Interval>(input);
            list.Sort(delegate(Interval x, Interval y)
            {
                if (x.Start != y.Start) return x.Start.CompareTo(y.Start);
                return x.End.CompareTo(y.End);
            });

            List<Interval> result = new List<Interval>();
            foreach (Interval iv in list)
            {
                if (result.Count > 0)
                {
                    Interval last = result[result.Count - 1];
                    // Touching counts as mergeable: [1..5] and [6..9] become [1..9].
                    bool adjacent = last.End != uint.MaxValue && iv.Start == last.End + 1;
                    if (iv.Start <= last.End || adjacent)
                    {
                        if (iv.End > last.End) result[result.Count - 1] = new Interval(last.Start, iv.End);
                        continue;
                    }
                }
                result.Add(iv);
            }
            return result;
        }

        /// <summary>Returns (union of from) minus (union of remove).</summary>
        public static List<Interval> Subtract(IEnumerable<Interval> from, IEnumerable<Interval> remove)
        {
            List<Interval> a = Merge(from);
            List<Interval> b = Merge(remove);
            List<Interval> result = new List<Interval>();

            int j = 0;
            foreach (Interval cur in a)
            {
                // long avoids wrapping when a cut lands on 255.255.255.255.
                long start = cur.Start;

                // a is sorted and disjoint, so b entries ending before cur are dead for good.
                while (j < b.Count && b[j].End < cur.Start) j++;

                int k = j;
                while (k < b.Count && b[k].Start <= cur.End)
                {
                    if (b[k].Start > start)
                        result.Add(new Interval((uint)start, b[k].Start - 1));

                    long next = (long)b[k].End + 1;
                    if (next > start) start = next;
                    k++;
                }

                if (start <= cur.End)
                    result.Add(new Interval((uint)start, cur.End));
            }
            return result;
        }

        public static long TotalAddresses(IEnumerable<Interval> intervals)
        {
            long total = 0;
            foreach (Interval iv in intervals) total += iv.Count;
            return total;
        }
    }
}
