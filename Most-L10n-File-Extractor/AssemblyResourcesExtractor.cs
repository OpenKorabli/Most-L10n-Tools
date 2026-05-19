using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Resources;

public static class AssemblyResourcesExtractor
{
    // Target resource logical names to extract (case-insensitive)
    private static readonly string[] TargetNames = new[] {
        "lang.ru-RU.xaml",
        "lang_ru_RU",
        "ru_RU_NormalUri",
        "Most.UI.Resources.Settings.welcome_RU.html"
    };

    public static List<string> Extract(string appFolder, string outputRoot)
    {
        var outputs = new List<string>();

        if (!Directory.Exists(appFolder)) return outputs;

        // patterns: Most.*.dll, Lesta.dll, Lesta.*.dll
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in Directory.GetFiles(appFolder, "Most.*.dll")) files.Add(f);
        foreach (var f in Directory.GetFiles(appFolder, "Lesta.dll")) files.Add(f);
        foreach (var f in Directory.GetFiles(appFolder, "Lesta.*.dll")) files.Add(f);

        // Ensure output root
        Directory.CreateDirectory(outputRoot);

        foreach (var file in files)
        {
            try
            {
                var asm = Assembly.LoadFrom(file);
                var resources = asm.GetManifestResourceNames();
                foreach (var res in resources)
                {
                    // If this manifest resource is a .resources container, enumerate inner entries
                    if (res.EndsWith(".resources", StringComparison.OrdinalIgnoreCase))
                    {
                        using (var stream = asm.GetManifestResourceStream(res))
                        {
                            if (stream == null) continue;
                            try
                            {
                                using (var rr = new ResourceReader(stream))
                                {
                                    var enumerator = rr.GetEnumerator();
                                    while (enumerator.MoveNext())
                                    {
                                        var key = enumerator.Key as string;
                                        var value = enumerator.Value;
                                        if (string.IsNullOrEmpty(key)) continue;
                                        var keyNorm = key.Replace('\\', '/').ToLowerInvariant();
                                        foreach (var target in TargetNames)
                                        {
                                            var t = target.ToLowerInvariant();
                                            if (keyNorm.Equals(t) || keyNorm.EndsWith('/' + t) || keyNorm.Contains('/' + t))
                                            {
                                                // Build output path using key (preserve slashes as directories, do NOT split on '.')
                                                var relative = key.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
                                                var asmFolder = Path.Combine(outputRoot, Path.GetFileName(file));
                                                var dest = Path.Combine(asmFolder, relative);
                                                var destDir = Path.GetDirectoryName(dest);
                                                if (!Directory.Exists(destDir)) Directory.CreateDirectory(destDir);

                                                // Write value depending on its type
                                                if (value is byte[] ba)
                                                {
                                                    File.WriteAllBytes(dest, ba);
                                                }
                                                else if (value is string s)
                                                {
                                                    File.WriteAllText(dest, s, System.Text.Encoding.UTF8);
                                                }
                                                else if (value is System.IO.UnmanagedMemoryStream ums)
                                                {
                                                    using (var outFs = new FileStream(dest, FileMode.Create, FileAccess.Write))
                                                    {
                                                        ums.CopyTo(outFs);
                                                    }
                                                }
                                                else if (value is System.IO.Stream stm)
                                                {
                                                    using (var outFs = new FileStream(dest, FileMode.Create, FileAccess.Write))
                                                    {
                                                        stm.CopyTo(outFs);
                                                    }
                                                }
                                                else
                                                {
                                                    // Fallback: serialize ToString
                                                    File.WriteAllText(dest, value?.ToString() ?? string.Empty, System.Text.Encoding.UTF8);
                                                }
                                                outputs.Add(dest);
                                                break;
                                            }
                                        }
                                    }
                                }
                            }
                            catch
                            {
                                // ignore resource reader failures
                            }
                        }
                    }
                    else
                    {
                        // Non-.resources manifest entries represent single resources; match by name
                        var resNorm = res.Replace('\\', '/').ToLowerInvariant();
                        foreach (var target in TargetNames)
                        {
                            var t = target.ToLowerInvariant();
                            if (resNorm.Equals(t) || resNorm.EndsWith(t) || resNorm.Contains('/' + t) || resNorm.Contains('.' + t))
                            {
                                using (var stream = asm.GetManifestResourceStream(res))
                                {
                                    if (stream == null) continue;
                                    var asmFolder = Path.Combine(outputRoot, Path.GetFileName(file));
                                    if (!Directory.Exists(asmFolder)) Directory.CreateDirectory(asmFolder);
                                    var dest = Path.Combine(asmFolder, res);
                                    // Ensure dest directory exists
                                    var destDir = Path.GetDirectoryName(dest);
                                    if (!Directory.Exists(destDir)) Directory.CreateDirectory(destDir);
                                    using (var fs = new FileStream(dest, FileMode.Create, FileAccess.Write))
                                    {
                                        stream.CopyTo(fs);
                                    }
                                    outputs.Add(dest);
                                }
                                break;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // ignore problematic assemblies
                outputs.Add($"ERROR:{file}:{ex.Message}");
            }
        }

        return outputs;
    }
}
