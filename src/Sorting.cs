using System;
using System.Collections.Generic;

namespace Ow2ServerPicker
{
    internal enum SortKey { None, Name, Code, Ping, Ranges }

    /// <summary>
    /// What the sorter needs from a row. Kept as an interface with no UI types so the
    /// ordering rules can be tested without constructing WinForms controls.
    /// </summary>
    internal interface ISortableRow
    {
        string SortName { get; }
        string SortCode { get; }
        int SortRanges { get; }
        long SortPing { get; }
        bool HasPing { get; }
    }

    internal static class Sorting
    {
        /// <summary>
        /// Orders rows for the given column.
        ///
        /// Rows with no ping reading always sink to the bottom, in both directions. An
        /// unmeasured datacenter is unknown, not slow, so letting one head a descending
        /// sort would assert something the data does not support. Name is the tiebreak
        /// everywhere, which also makes the order stable across repeated sorts.
        /// </summary>
        public static int Compare(ISortableRow a, ISortableRow b, SortKey key, bool descending)
        {
            if (key == SortKey.Ping)
            {
                if (!a.HasPing || !b.HasPing)
                {
                    if (a.HasPing) return -1;
                    if (b.HasPing) return 1;
                    return ByName(a, b);
                }
                int p = a.SortPing.CompareTo(b.SortPing);
                if (descending) p = -p;
                return p != 0 ? p : ByName(a, b);
            }

            int c;
            switch (key)
            {
                case SortKey.Code:   c = string.Compare(a.SortCode, b.SortCode, StringComparison.OrdinalIgnoreCase); break;
                case SortKey.Ranges: c = a.SortRanges.CompareTo(b.SortRanges); break;
                case SortKey.Name:   c = ByName(a, b); break;
                default:             return 0;
            }
            if (descending) c = -c;
            return c != 0 ? c : ByName(a, b);
        }

        private static int ByName(ISortableRow a, ISortableRow b)
        {
            return string.Compare(a.SortName, b.SortName, StringComparison.OrdinalIgnoreCase);
        }

        public static void Sort<T>(List<T> rows, SortKey key, bool descending) where T : ISortableRow
        {
            rows.Sort(delegate(T x, T y) { return Compare(x, y, key, descending); });
        }
    }
}
