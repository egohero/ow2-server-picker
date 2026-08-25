using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Web.Script.Serialization;

namespace Ow2ServerPicker
{
    internal sealed class Datacenter
    {
        public string Code;
        public string Name;
        public string Region;
        public string PingTarget;
        public List<Interval> Ranges = new List<Interval>();

        /// <summary>
        /// Authoritative checked state. Held here rather than read back off the ListView,
        /// because ListView re-inserts its items when the native handle is created and
        /// briefly hands out items whose Tag is not yet wired up.
        /// </summary>
        public bool Selected = true;

        public string Display
        {
            get { return Name + "  (" + Code + ")"; }
        }
    }

    internal sealed class Catalog
    {
        public string Updated = "unknown";

        /// <summary>
        /// UDP ports the game server uses, as a Windows Firewall port list. Rules are scoped
        /// to these so the block cannot touch QUIC (443), voice STUN/SIP or DNS, which live on
        /// the same addresses but are not game traffic. Empty means every port, which is the
        /// old, blunter behaviour.
        /// </summary>
        public string GameUdpPorts = "";
        public List<Datacenter> Datacenters = new List<Datacenter>();
        public List<string> Warnings = new List<string>();

        /// <summary>
        /// Loads servers.json from beside the executable when present, otherwise falls back to
        /// the copy embedded at build time. The external file wins so users can pick up range
        /// changes without waiting for a new release.
        /// </summary>
        public static Catalog Load(out string source)
        {
            string exeDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string external = Path.Combine(exeDir, "servers.json");

            if (File.Exists(external))
            {
                source = external;
                return Parse(File.ReadAllText(external, Encoding.UTF8));
            }

            source = "built-in catalog";
            using (Stream s = Assembly.GetExecutingAssembly()
                       .GetManifestResourceStream("servers.json"))
            {
                if (s == null)
                    throw new FileNotFoundException(
                        "No servers.json beside the executable and none embedded in this build.");
                using (StreamReader r = new StreamReader(s, Encoding.UTF8))
                    return Parse(r.ReadToEnd());
            }
        }

        private static string Str(Dictionary<string, object> d, string key)
        {
            object v;
            if (!d.TryGetValue(key, out v) || v == null) return null;
            return Convert.ToString(v);
        }

        public static Catalog Parse(string json)
        {
            JavaScriptSerializer ser = new JavaScriptSerializer();
            ser.MaxJsonLength = int.MaxValue;
            Dictionary<string, object> root = ser.DeserializeObject(json) as Dictionary<string, object>;
            if (root == null) throw new InvalidDataException("servers.json is not a JSON object.");

            Catalog cat = new Catalog();
            string updated = Str(root, "updated");
            if (!string.IsNullOrEmpty(updated)) cat.Updated = updated;

            string ports = Str(root, "gameUdpPorts");
            if (!string.IsNullOrEmpty(ports)) cat.GameUdpPorts = ports.Trim();

            object dcsObj;
            if (!root.TryGetValue("datacenters", out dcsObj) || !(dcsObj is object[]))
                throw new InvalidDataException("servers.json has no 'datacenters' array.");

            foreach (object entry in (object[])dcsObj)
            {
                Dictionary<string, object> d = entry as Dictionary<string, object>;
                if (d == null) continue;

                Datacenter dc = new Datacenter();
                dc.Code = Str(d, "code");
                dc.Name = Str(d, "name");
                dc.Region = Str(d, "region");
                dc.PingTarget = Str(d, "pingTarget");

                if (string.IsNullOrEmpty(dc.Code) || string.IsNullOrEmpty(dc.Name))
                {
                    cat.Warnings.Add("Skipped a datacenter entry with no code or name.");
                    continue;
                }
                if (string.IsNullOrEmpty(dc.Region)) dc.Region = "Other";

                object rangesObj;
                if (d.TryGetValue("ranges", out rangesObj) && rangesObj is object[])
                {
                    foreach (object r in (object[])rangesObj)
                    {
                        string text = Convert.ToString(r);
                        Interval iv;
                        if (IpMath.TryParse(text, out iv)) dc.Ranges.Add(iv);
                        else cat.Warnings.Add(string.Format(
                            "{0}: ignored unparseable range '{1}'", dc.Code, text));
                    }
                }

                if (dc.Ranges.Count == 0)
                {
                    cat.Warnings.Add(dc.Code + ": no usable ranges, skipped.");
                    continue;
                }

                dc.Ranges = IpMath.Merge(dc.Ranges);
                cat.Datacenters.Add(dc);
            }

            if (cat.Datacenters.Count == 0)
                throw new InvalidDataException("servers.json contained no usable datacenters.");

            cat.Datacenters.Sort(delegate(Datacenter x, Datacenter y)
            {
                int c = string.Compare(x.Region, y.Region, StringComparison.OrdinalIgnoreCase);
                if (c != 0) return c;
                return string.Compare(x.Name, y.Name, StringComparison.OrdinalIgnoreCase);
            });
            return cat;
        }
    }
}
