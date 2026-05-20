using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public class AssemblyResourcesConverter
{
    public class ConversionResult
    {
        public int FileCount { get; set; }
        public int ItemCount { get; set; }
        public List<string> Errors { get; } = new List<string>();
    }

    private string ApplyLocaleCasing(string sourcePattern, string inputLocale)
    {
        if (string.IsNullOrEmpty(sourcePattern) || string.IsNullOrEmpty(inputLocale)) return inputLocale;
        // Split by - or _ and apply casing from source pieces to input pieces
        var srcParts = Regex.Split(sourcePattern, "[-_]");
        var inParts = Regex.Split(inputLocale, "[-_]");
        for (int i = 0; i < inParts.Length; i++)
        {
            if (i < srcParts.Length)
            {
                var sp = srcParts[i];
                var ip = inParts[i];
                // If source is upper (e.g., RU) make input upper; if title (Ru) capitalize; else lower
                if (sp == sp.ToUpperInvariant()) inParts[i] = ip.ToUpperInvariant();
                else if (char.IsUpper(sp.FirstOrDefault())) inParts[i] = char.ToUpperInvariant(ip[0]) + ip.Substring(1).ToLowerInvariant();
                else inParts[i] = ip.ToLowerInvariant();
            }
            else
            {
                inParts[i] = inParts[i].ToLowerInvariant();
            }
        }
        return string.Join("-", inParts);
    }

    public ConversionResult ConvertFromParaTranz(string paraTranzOutputRoot, string localeRaw)
    {
        var result = new ConversionResult();
        if (!Directory.Exists(paraTranzOutputRoot))
        {
            result.Errors.Add("ParaTranzOutput not found: " + paraTranzOutputRoot);
            return result;
        }

        // find json files in ParaTranz output
        var jsonFiles = Directory.GetFiles(paraTranzOutputRoot, "*.json", SearchOption.AllDirectories)
            .Where(p => p.EndsWith(".xaml.json", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".xml.json", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".html.json", StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var jf in jsonFiles)
        {
            try
            {
                var dir = Path.GetDirectoryName(jf);
                var baseName = Path.GetFileName(jf);
                // original resource file (without appended .json)
                var origName = baseName.Substring(0, baseName.Length - ".json".Length);
                var origPath = Path.Combine(dir, origName);

                var list = JsonConvert.DeserializeObject<List<JObject>>(File.ReadAllText(jf, Encoding.UTF8));
                if (list == null) continue;

                if (origName.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
                {
                    if (!File.Exists(origPath))
                    {
                        result.Errors.Add($"Missing XAML file: {origPath}");
                        continue;
                    }

                    var doc = XDocument.Load(origPath);
                    XNamespace xns = "http://schemas.microsoft.com/winfx/2006/xaml";
                    bool changed = false;
                    foreach (var obj in list)
                    {
                        var key = obj.Value<string>("key");
                        var orig = obj.Value<string>("original");
                        var translation = obj.Value<string>("translation") ?? string.Empty;
                        var elem = doc.Descendants().FirstOrDefault(e => (string)e.Attribute(xns + "Key") == key);
                        if (elem != null)
                        {
                            elem.Value = string.IsNullOrWhiteSpace(translation) ? orig : translation;
                            changed = true;
                            result.ItemCount++;
                        }
                    }

                    if (changed)
                    {
                        // save as lang.{locale}.xaml: if origName starts with lang., replace middle
                        var fileName = Path.GetFileName(origPath);
                        string newName;
                        if (fileName.StartsWith("lang."))
                        {
                            // extract source locale pattern
                            var m = Regex.Match(fileName, "^lang\\.(.+)\\.xaml$", RegexOptions.IgnoreCase);
                            var sourcePattern = m.Success ? m.Groups[1].Value : null;
                            var transformed = sourcePattern != null ? ApplyLocaleCasing(sourcePattern, localeRaw) : localeRaw;
                            newName = "lang." + transformed + ".xaml";
                        }
                        else
                        {
                            // use source file's pattern if present (try to detect existing locale in filename)
                            var m2 = Regex.Match(fileName, "\\.(?<loc>[A-Za-z0-9_-]+)\\.xaml$");
                            string transformed = localeRaw;
                            if (m2.Success)
                            {
                                transformed = ApplyLocaleCasing(m2.Groups["loc"].Value, localeRaw);
                            }
                            newName = fileName + "." + transformed + ".xaml";
                        }
                        var newPath = Path.Combine(dir, newName);
                        doc.Save(newPath);
                        result.FileCount++;
                    }
                }
                else if (origName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        // baseName is defined earlier in this scope
                        // handle Localizations.xml.json produced by ConvertToParaTranz
                        if (string.Equals(baseName, "Localizations.xml.json", StringComparison.OrdinalIgnoreCase))
                        {
                            var transArr = new JArray();
                            foreach (var obj in list)
                            {
                                var orig = obj.Value<string>("original");
                                var trans = obj.Value<string>("translation") ?? string.Empty;
                                transArr.Add(new JArray(orig, string.IsNullOrWhiteSpace(trans) ? orig : trans));
                                result.ItemCount++;
                            }

                            string FormatLocale(string loc)
                            {
                                if (string.IsNullOrEmpty(loc)) return loc ?? string.Empty;
                                var parts = Regex.Split(loc, "[-_]");
                                if (parts.Length == 1) return parts[0].ToLowerInvariant();
                                var first = parts[0].ToLowerInvariant();
                                var second = parts[1].ToUpperInvariant();
                                return first + "-" + second;
                            }

                            var formatted = FormatLocale(localeRaw ?? string.Empty);
                            var outName = string.IsNullOrEmpty(formatted) ? "localization.json" : "localization_" + formatted + ".json";
                            var outObj = new JObject(new JProperty("translation", transArr));
                            var outPath = Path.Combine(dir ?? string.Empty, outName);
                            File.WriteAllText(outPath, outObj.ToString(Newtonsoft.Json.Formatting.Indented), Encoding.UTF8);
                            result.FileCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        result.Errors.Add($"Failed to process Localizations json: {jf}: {ex.Message}");
                    }
                }
                else if (origName.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
                {
                    if (!File.Exists(origPath))
                    {
                        result.Errors.Add($"Missing HTML file: {origPath}");
                        continue;
                    }

                    var htmlDoc = new HtmlAgilityPack.HtmlDocument();
                    htmlDoc.Load(origPath, Encoding.UTF8);
                    bool changed = false;

                    foreach (var obj in list)
                    {
                        var key = obj.Value<string>("key");
                        var translation = obj.Value<string>("translation") ?? string.Empty;
                        if (string.IsNullOrEmpty(translation)) continue;
                        var node = FindNodeBySelector(htmlDoc.DocumentNode, key);
                        if (node != null)
                        {
                            node.InnerHtml = System.Net.WebUtility.HtmlEncode(translation);
                            changed = true;
                            result.ItemCount++;
                        }
                    }

                    if (changed)
                    {
                        // derive second part from localeRaw using source pattern casing
                        var baseFile = Path.GetFileName(origPath);
                        var idx = baseFile.IndexOf("welcome_");
                        string newName;
                        if (idx >= 0)
                        {
                            var suffix = baseFile.Substring(idx + "welcome_".Length);
                            var sourcePattern = Path.GetFileNameWithoutExtension(suffix); // may be RU or ru-RU
                            string regionTransformed = localeRaw;
                            // if sourcePattern exists, use its casing to transform the region part
                            if (!string.IsNullOrEmpty(sourcePattern))
                            {
                                // sourcePattern might include both parts; take last part
                                var spParts = Regex.Split(sourcePattern, "[-_]");
                                var srcRegion = spParts.Length > 0 ? spParts[spParts.Length - 1] : sourcePattern;
                                // input region
                                var inParts = Regex.Split(localeRaw, "[-_]");
                                var inRegion = inParts.Length > 1 ? inParts[inParts.Length - 1] : inParts[0];
                                regionTransformed = ApplyLocaleCasing(srcRegion, inRegion);
                            }

                            var prefix = baseFile.Substring(0, idx);
                            newName = prefix + "welcome_" + regionTransformed + ".html";
                        }
                        else
                        {
                            newName = Path.GetFileNameWithoutExtension(baseFile) + "." + localeRaw + ".html";
                        }
                        var newPath = Path.Combine(dir, newName);
                        htmlDoc.Save(newPath, Encoding.UTF8);
                        result.FileCount++;
                    }
                }
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Apply failed for {jf}: {ex.Message}");
            }
        }

        return result;
    }

    // Convert lang.ru-ru.xaml files under assemblyOutputRoot into ParaTranz JSON under paraTranzOutputRoot
    public ConversionResult ConvertToParaTranz(string assemblyOutputRoot, string modCacheRoot, string paraTranzOutputRoot)
    {
        var result = new ConversionResult();
        if (!Directory.Exists(assemblyOutputRoot))
        {
            result.Errors.Add("AssemblyResourcesOutput not found: " + assemblyOutputRoot);
            return result;
        }

        var filesXaml = Directory.GetFiles(assemblyOutputRoot, "lang.ru-ru.xaml", SearchOption.AllDirectories);
        var filesHtml = Directory.GetFiles(assemblyOutputRoot, "*welcome_RU.html", SearchOption.AllDirectories);
        var files = filesXaml.Concat(filesHtml).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        result.FileCount = files.Length;

        foreach (var f in files)
        {
            try
            {
                var rel = f.Substring(assemblyOutputRoot.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var destDir = Path.Combine(paraTranzOutputRoot, Path.GetDirectoryName(rel) ?? string.Empty);
                if (!Directory.Exists(destDir)) Directory.CreateDirectory(destDir);
                var destFile = Path.Combine(destDir, Path.GetFileName(f) + ".json");

                var items = new List<(string Key, string Value)>();

                if (f.EndsWith("lang.ru-ru.xaml", StringComparison.OrdinalIgnoreCase))
                {
                    var doc = XDocument.Load(f);
                    XNamespace xns = "http://schemas.microsoft.com/winfx/2006/xaml";

                    items.AddRange(doc.Descendants()
                        .Where(elem => elem.Name.LocalName == "String" && elem.Attribute(xns + "Key") != null)
                        .Select(elem => (Key: elem.Attribute(xns + "Key")?.Value, Value: elem.Value))
                        .Where(i => !string.IsNullOrEmpty(i.Key))
                    );
                }
                else if (f.EndsWith("welcome_RU.html", StringComparison.OrdinalIgnoreCase))
                {
                    // Use HtmlAgilityPack for robust HTML parsing
                    try
                    {
                        var htmlDoc = new HtmlAgilityPack.HtmlDocument();
                        htmlDoc.OptionFixNestedTags = true;
                        htmlDoc.Load(f, Encoding.UTF8);

                        var body = htmlDoc.DocumentNode.SelectSingleNode("//body");
                        if (body != null)
                        {
                            var textNodes = body.DescendantsAndSelf()
                                .Where(n => n.NodeType == HtmlAgilityPack.HtmlNodeType.Text)
                                .Select(n => new { Node = n, Text = n.InnerText })
                                .Where(x => !string.IsNullOrWhiteSpace(x.Text))
                                .Select(x => new { x.Node, Text = System.Text.RegularExpressions.Regex.Unescape(x.Text).Trim() })
                                .Where(x => !string.IsNullOrWhiteSpace(x.Text))
                                .ToList();

                            // normalize and group/merge by parent nodes sequence
                            var grouped = textNodes
                                .Select(x => new { Parent = x.Node.ParentNode, Text = System.Text.RegularExpressions.Regex.Replace(x.Text, "\\s+", " ").Trim() })
                                .Where(x => x.Parent != null && x.Parent.Name != null && x.Parent.Name.ToLowerInvariant() != "script" && x.Parent.Name.ToLowerInvariant() != "style")
                                .ToList();

                            var baseKey = Path.GetFileNameWithoutExtension(f).Replace('.', '_');
                            var usedKeys = new Dictionary<string, int>(StringComparer.Ordinal);
                            int idx = 1;
                            foreach (var g in grouped)
                            {
                                var selector = BuildCssSelector(g.Parent);
                                var key = selector;
                                // sanitize key for filenames/JSON keys: replace ' > ' with ' > ' kept but also keep as selector; also create safeKey for filesystem
                                var safeKey = System.Text.RegularExpressions.Regex.Replace(key, "\\s*>\\s*", "__gt__");
                                safeKey = System.Text.RegularExpressions.Regex.Replace(safeKey, @"[^a-zA-Z0-9_\-\.#]", "_");
                                if (usedKeys.TryGetValue(key, out var count))
                                {
                                    count++;
                                    usedKeys[key] = count;
                                    key = key + "_" + count.ToString();
                                }
                                else
                                {
                                    usedKeys[key] = 1;
                                }

                                // final key used in JSON is the selector; but ensure it's safe to use as property by leaving it string
                                items.Add((Key: key, Value: g.Text));
                                idx++;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        result.Errors.Add($"HTML parse failed: {f}: {ex.Message}");
                    }
                }

                // Load existing translations if file exists
                var existing = new Dictionary<int, string>();
                if (File.Exists(destFile))
                {
                    try
                    {
                        var old = File.ReadAllText(destFile, Encoding.UTF8);
                        existing = ParseExistingJson(old);
                    }
                    catch
                    {
                        // ignore parse errors and proceed with empty existing
                    }
                }
                var outList = new List<object>();
                foreach (var it in items)
                {
                    var hashCode = it.Key.GetHashCode() ^ it.Value.GetHashCode();
                    string translation = string.Empty;
                    if (existing.TryGetValue(hashCode, out var oldTrans)) translation = oldTrans;
                    outList.Add(new { key = it.Key, original = it.Value, translation = translation });
                    result.ItemCount++;
                }

                var jsonOut = JsonConvert.SerializeObject(outList, Formatting.Indented);
                File.WriteAllText(destFile, jsonOut, Encoding.UTF8);

                // Also copy the original source file into the same output directory so ConvertFromParaTranz can use it
                try
                {
                    var srcFile = f; // original file path
                    var copyDest = Path.Combine(destDir, Path.GetFileName(srcFile));
                    File.Copy(srcFile, copyDest, true);
                }
                catch (Exception ex)
                {
                    // non-fatal: record error but continue
                    result.Errors.Add($"Failed to copy source file for {f}: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                result.Errors.Add($"{f}: {ex.Message}");
            }
        }

        // If mod cache path provided, locate .Configurations, pick highest Version and copy .Localizations
        try
        {
            if (!string.IsNullOrEmpty(modCacheRoot) && Directory.Exists(modCacheRoot))
            {
                var configsPath = Path.Combine(modCacheRoot, ".Configurations");
                if (File.Exists(configsPath))
                {
                    try
                    {
                        var cfgDoc = XDocument.Load(configsPath);
                        var cfgs = cfgDoc.Root?.Elements("Configuration").Where(e => e.Attribute("Version") != null).ToList();
                        Version bestVersion = null;
                        string bestVersionStr = null;
                        if (cfgs != null)
                        {
                            foreach (var c in cfgs)
                            {
                                var vstr = c.Attribute("Version")?.Value;
                                if (string.IsNullOrEmpty(vstr)) continue;
                                if (!Regex.IsMatch(vstr, "^\\d+(?:\\.\\d+)*$")) continue;
                                try
                                {
                                    var v = new Version(vstr);
                                    if (bestVersion == null || v.CompareTo(bestVersion) > 0)
                                    {
                                        bestVersion = v;
                                        bestVersionStr = vstr;
                                    }
                                }
                                catch { }
                            }
                        }

                        if (!string.IsNullOrEmpty(bestVersionStr))
                        {
                            var versionDir = Path.Combine(modCacheRoot, bestVersionStr);
                            if (Directory.Exists(versionDir))
                            {
                                var locPath = Path.Combine(versionDir, ".Localizations");
                                if (File.Exists(locPath))
                                {
                                    try
                                    {
                                        var outDir = Path.Combine(paraTranzOutputRoot, "Localization");
                                        if (!Directory.Exists(outDir)) Directory.CreateDirectory(outDir);
                                        var copyDest = Path.Combine(outDir, "Localizations.xml");
                                        File.Copy(locPath, copyDest, true);
                                        var attrs = File.GetAttributes(copyDest);
                                        if ((attrs & FileAttributes.Hidden) == FileAttributes.Hidden)
                                        {
                                            attrs &= ~FileAttributes.Hidden;
                                            File.SetAttributes(copyDest, attrs);
                                        }
                                        result.FileCount++;

                                        // Parse Localizations XML and export ru-RU entries to JSON
                                        try
                                        {
                                            var locDoc = XDocument.Load(locPath);
                                            var entries = new List<object>();

                                            var jsonPath = Path.Combine(outDir, "Localizations.xml.json");
                                            var existingLoc = new Dictionary<int, string>();
                                            if (File.Exists(jsonPath))
                                            {
                                                try
                                                {
                                                    var oldJson = File.ReadAllText(jsonPath, Encoding.UTF8);
                                                    existingLoc = ParseExistingJson(oldJson);
                                                }
                                                catch { /* ignore parse errors */ }
                                            }

                                            // Items may be under Configuration/Items/Item
                                            var itemsRoot = locDoc.Root?.Element("Items");
                                            var items = itemsRoot != null ? itemsRoot.Elements("Item") : locDoc.Descendants("Item");
                                            foreach (var itemElem in items)
                                            {
                                                var itemName = itemElem.Attribute("Name")?.Value;
                                                if (string.IsNullOrEmpty(itemName)) continue;

                                                var localizationsElem = itemElem.Element("Localizations");
                                                if (localizationsElem == null) continue;

                                                var locs = localizationsElem.Elements("Localization")
                                                    .Where(l => string.Equals((string)l.Attribute("Language"), "ru-RU", StringComparison.OrdinalIgnoreCase));

                                                foreach (var loc in locs)
                                                {
                                                    var nameVal = loc.Attribute("Name")?.Value ?? string.Empty;
                                                    // Use Item's Name attribute as key base
                                                    var keyBase = itemName;

                                                    var keyName = keyBase + "_Name";
                                                    var nameHashCode = keyName.GetHashCode() ^ nameVal.GetHashCode();
                                                    var transName = string.Empty;
                                                    if (!string.IsNullOrEmpty(nameVal) && existingLoc.TryGetValue(nameHashCode, out var prev)) transName = prev;
                                                    entries.Add(new { key = keyName, original = nameVal, translation = transName });
                                                    result.ItemCount++;

                                                    var descVal = loc.Attribute("Description")?.Value ?? string.Empty;
                                                    if (!string.IsNullOrEmpty(descVal))
                                                    {
                                                        var keyDesc = keyBase + "_Description";
                                                        var descHashCode = keyDesc.GetHashCode() ^ descVal.GetHashCode();
                                                        var transDesc = string.Empty;
                                                        if (!string.IsNullOrEmpty(descVal) && existingLoc.TryGetValue(descHashCode, out var prevDesc)) transDesc = prevDesc;
                                                        entries.Add(new { key = keyDesc, original = descVal, translation = transDesc });
                                                        result.ItemCount++;
                                                    }

                                                    var attentionVal = loc.Attribute("Attention")?.Value;
                                                    if (!string.IsNullOrEmpty(attentionVal))
                                                    {
                                                        var keyAtt = keyBase + "_Attention";
                                                        var attHashCode = keyAtt.GetHashCode() ^ attentionVal.GetHashCode();
                                                        var transAtt = string.Empty;
                                                        if (existingLoc.TryGetValue(attHashCode, out var prevAtt)) transAtt = prevAtt;
                                                        entries.Add(new { key = keyAtt, original = attentionVal, translation = transAtt });
                                                        result.ItemCount++;
                                                    }
                                                }
                                            }

                                            if (entries.Count > 0)
                                            {
                                                var jsonOut2 = JsonConvert.SerializeObject(entries, Formatting.Indented);
                                                File.WriteAllText(jsonPath, jsonOut2, Encoding.UTF8);
                                            }
                                        }
                                        catch (Exception ex)
                                        {
                                            result.Errors.Add($"Failed to parse .Localizations xml: {ex.Message}");
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        result.Errors.Add($"Failed to copy .Localizations: {ex.Message}");
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        result.Errors.Add($"Failed to parse .Configurations: {ex.Message}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            result.Errors.Add($"Mod cache processing failed: {ex.Message}");
        }

        return result;
    }

    private Dictionary<int, string> ParseExistingJson(string json)
    {
        var map = new Dictionary<int, string>();
        if (string.IsNullOrWhiteSpace(json)) return map;

        try
        {
            var list = JsonConvert.DeserializeObject<List<JObject>>(json);
            if (list == null) return map;

            foreach (var obj in list)
            {
                var k = obj.Value<string>("key") ?? string.Empty;
                var o = obj.Value<string>("original") ?? string.Empty;
                var t = obj.Value<string>("translation") ?? string.Empty;
                if (string.IsNullOrWhiteSpace(k) || string.IsNullOrWhiteSpace(o) || string.IsNullOrWhiteSpace(t))
                    continue;
                var hashCode = k.GetHashCode() ^ o.GetHashCode();
                if (!map.ContainsKey(hashCode)) map[hashCode] = t;
            }
        }
        catch
        {
            // ignore parse errors and return empty map
        }

        return map;
    }

    private string BuildCssSelector(HtmlAgilityPack.HtmlNode node)
    {
        var parts = new List<string>();
        var cur = node;
        while (cur != null && !string.Equals(cur.Name, "body", StringComparison.OrdinalIgnoreCase))
        {
            if (cur.NodeType != HtmlAgilityPack.HtmlNodeType.Element)
            {
                cur = cur.ParentNode;
                continue;
            }

            var tag = cur.Name.ToLowerInvariant();

            // compute nth-of-type among element siblings
            int position = 1;
            if (cur.ParentNode != null)
            {
                foreach (var sib in cur.ParentNode.ChildNodes)
                {
                    if (sib == cur) break;
                    if (sib.NodeType == HtmlAgilityPack.HtmlNodeType.Element && string.Equals(sib.Name, cur.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        position++;
                    }
                }
            }

            var part = tag + $":nth-of-type({position})";

            var id = cur.GetAttributeValue("id", null);
            if (!string.IsNullOrEmpty(id))
            {
                part += "#" + id;
            }

            var cls = cur.GetAttributeValue("class", null);
            if (!string.IsNullOrEmpty(cls))
            {
                // include full class list
                var classes = string.Join("", cls.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(c => "." + c));
                if (!string.IsNullOrEmpty(classes)) part += classes;
            }

            parts.Insert(0, part);
            cur = cur.ParentNode;
        }
        if (parts.Count == 0) return node.Name.ToLowerInvariant();
        var selector = string.Join(" > ", parts);

        // sanitize selector for use as a key: collapse whitespace and trim
        selector = System.Text.RegularExpressions.Regex.Replace(selector.Trim(), "\\s+", " ");
        return selector;
    }

    private string UnescapeJson(string s)
    {
        if (s == null) return null;
        // handle common escapes
        return s.Replace("\\r", "\r").Replace("\\n", "\n").Replace("\\\"", "\"").Replace("\\\\", "\\");
    }

    private HtmlAgilityPack.HtmlNode FindNodeBySelector(HtmlAgilityPack.HtmlNode root, string selector)
    {
        if (root == null || string.IsNullOrEmpty(selector)) return null;
        var body = root.SelectSingleNode("//body") ?? root;
        var parts = selector.Split(new[] { " > " }, StringSplitOptions.None);
        var cur = body;
        foreach (var part in parts)
        {
            var p = part.Trim();
            if (string.IsNullOrEmpty(p)) continue;

            // parse tag and nth-of-type and optional id/classes
            var tag = p;
            int nth = 1;
            string id = null;
            var classes = new List<string>();

            var m = System.Text.RegularExpressions.Regex.Match(p, @"^(?<tag>[^:]+):nth-of-type\((?<n>\d+)\)(?<rest>.*)$");
            if (m.Success)
            {
                tag = m.Groups["tag"].Value;
                nth = int.Parse(m.Groups["n"].Value);
                var rest = m.Groups["rest"].Value;
                var idm = System.Text.RegularExpressions.Regex.Match(rest, @"#(?<id>[^\.\#]+)");
                if (idm.Success) id = idm.Groups["id"].Value;
                foreach (Match cm in System.Text.RegularExpressions.Regex.Matches(rest, @"\.(?<c>[^\.\#]+)"))
                    classes.Add(cm.Groups["c"].Value);
            }
            else
            {
                var idm = System.Text.RegularExpressions.Regex.Match(p, @"#(?<id>[^\.\#]+)");
                if (idm.Success) id = idm.Groups["id"].Value;
                foreach (Match cm in System.Text.RegularExpressions.Regex.Matches(p, @"\.(?<c>[^\.\#]+)"))
                    classes.Add(cm.Groups["c"].Value);
                tag = System.Text.RegularExpressions.Regex.Replace(p, @"[#\.].*$", "");
            }

            // find nth occurrence of tag among children
            int count = 0;
            HtmlAgilityPack.HtmlNode found = null;
            foreach (var child in cur.ChildNodes)
            {
                if (child.NodeType != HtmlAgilityPack.HtmlNodeType.Element) continue;
                if (!string.Equals(child.Name, tag, StringComparison.OrdinalIgnoreCase)) continue;
                count++;
                if (count == nth)
                {
                    // verify id and classes
                    if (id != null)
                    {
                        var cid = child.GetAttributeValue("id", null);
                        if (!string.Equals(cid, id, StringComparison.Ordinal)) { found = null; break; }
                    }
                    var ok = true;
                    foreach (var c in classes)
                    {
                        var cls = child.GetAttributeValue("class", null);
                        if (string.IsNullOrEmpty(cls) || !cls.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Contains(c)) { ok = false; break; }
                    }
                    if (!ok) { found = null; break; }
                    found = child;
                    break;
                }
            }

            if (found == null) return null;
            cur = found;
        }

        return cur;
    }
}
