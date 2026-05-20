using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;

namespace Most.L10n
{
    public static class HookManager
    {
        public static Harmony harmony = new Harmony("com.Windofxy.Most-L10n-Hook.Tanki");

        public const string FallbackLanguageName = "ru-RU";
        public const string AppXamlPath = "/Most.L10n.Hook;component/Resources/font.xaml";
#if zh_CN
        public const string ResourceDictionaryPath = "/Most.L10n.Hook;component/Resources/Tanki.Most.exe/resources/lang.zh-cn.xaml";
        public const string CountryFlagPath = "/Most.L10n.Hook;component/Resources/Lesta.Application.dll/resources/cn.normal.png";
#endif

        public static Dictionary<string, string> Language_DisplayName_Name_Map = new Dictionary<string, string> { { "简体中文", "zh-CN" } };

        public static void InitHook()
        {
            Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri(AppXamlPath, UriKind.Relative)
            });

            var languages = Lesta.Application.Language.Languages.Take(1).ToList();
            languages.Add(
                new Lesta.Application.Model.Language("be-BY", "简体中文", new ResourceDictionary
                {
                    Source = new Uri(ResourceDictionaryPath, UriKind.Relative)
                })
            );
            Lesta.Application.Language.Languages = languages;
            Lesta.Application.Language.LanguageImages["be-BY"] =
                Lesta.Control.Helpers.ImageItemHelper.Load(CountryFlagPath);

            foreach (var language in languages)
            {
                if (language.Name != "ru-RU" && Language_DisplayName_Name_Map.ContainsKey(language.DisplayName))
                {
                    string languageName = Language_DisplayName_Name_Map[language.DisplayName];
                    if (!Most_Configuration_Model_Localizations_Select.CustomLocalizationMap.ContainsKey(languageName))
                    {
                        Most_Configuration_Model_Localizations_Select.CustomLocalizationMap.Add(languageName, new Dictionary<string, string>());
                        try
                        {
                            foreach (JToken translation in (JsonConvert.DeserializeObject(File.ReadAllText($".\\Localization\\localization_{languageName}.json")) as JObject)["translation"].Values<JToken>())
                            {
                                try
                                {
                                    Most_Configuration_Model_Localizations_Select.CustomLocalizationMap[languageName].Add(translation[0].ToString(), translation[1].ToString());
                                }
                                catch (ArgumentException) { }
                            }
                        }
                        catch (Exception) { }
                    }
                }
            }

            harmony.PatchAll();
        }
    }

    [HarmonyPatch(typeof(Most.Configuration.Model.Localizations), nameof(Most.Configuration.Model.Localizations.Select))]
    class Most_Configuration_Model_Localizations_Select
    {
        public static Dictionary<string, Dictionary<string, string>> CustomLocalizationMap { get; set; } = new Dictionary<string, Dictionary<string, string>>();

        static bool Prefix(ref string language, ref string name, ref string url, ref string movie, ref string author, ref string description, ref string preview, ref string attention, ref string hyperlink)
        {
            language = HookManager.FallbackLanguageName;
            return true;
        }

        static void Postfix(string language, ref string name, ref string url, ref string movie, ref string author, ref string description, ref string preview, ref string attention, ref string hyperlink)
        {
            string languageName = "";
            bool hasLocalization = HookManager.Language_DisplayName_Name_Map.TryGetValue(Lesta.Application.Language.Current.DisplayName, out languageName) && CustomLocalizationMap.ContainsKey(languageName);
            if (!hasLocalization)
            {
                return;
            }
            string temp = "";
            if (!string.IsNullOrWhiteSpace(name) && CustomLocalizationMap[languageName].TryGetValue(name, out temp))
            {
                name = temp;
            }
            if (!string.IsNullOrWhiteSpace(url) && CustomLocalizationMap[languageName].TryGetValue(url, out temp))
            {
                url = temp;
            }
            if (!string.IsNullOrWhiteSpace(movie) && CustomLocalizationMap[languageName].TryGetValue(movie, out temp))
            {
                movie = temp;
            }
            if (!string.IsNullOrWhiteSpace(description) && CustomLocalizationMap[languageName].TryGetValue(description, out temp))
            {
                description = temp;
            }
            if (!string.IsNullOrWhiteSpace(attention) && CustomLocalizationMap[languageName].TryGetValue(attention, out temp))
            {
                attention = temp;
            }
            if (!string.IsNullOrWhiteSpace(hyperlink) && CustomLocalizationMap[languageName].TryGetValue(hyperlink, out temp))
            {
                hyperlink = temp;
            }
            if (!string.IsNullOrWhiteSpace(preview) && CustomLocalizationMap[languageName].TryGetValue(preview, out temp))
            {
                preview = temp;
            }
        }
    }

    [HarmonyPatch(typeof(Most.UI.ViewModel.UpdatesViewModel), nameof(Most.UI.ViewModel.UpdatesViewModel.HTMLWelcome), MethodType.Getter)]
    class Most_UI_ViewModel_UpdatesViewModel_HTMLWelcome_Getter_Patch
    {
        static bool Prefix(Most.UI.ViewModel.UpdatesViewModel __instance, ref string __result)
        {
            Assembly executingAssembly = Assembly.GetExecutingAssembly();
            string languageName = "";
            string welcomePage;
            if (HookManager.Language_DisplayName_Name_Map.TryGetValue(Lesta.Application.Language.Current.DisplayName, out languageName))
            {
                languageName = languageName.Split(new char[] { '-' })[1];
                var uri = new Uri(
                    "/Most.L10n.Hook;component/Resources/Most.UI.dll/Most.UI.Resources.Settings.welcome_" + languageName + ".html",
                    UriKind.Relative
                );
                var info = Application.GetResourceStream(uri);
                if (info == null)
                {
                    __result = "";
                    return false;
                }
                using (var reader = new StreamReader(info.Stream, Encoding.UTF8))
                {
                    welcomePage = reader.ReadToEnd();
                }
                if (!(__instance.WindowOptions.LastExitTime < DateTime.Now.AddYears(-10)))
                {
                    __result = "";
                    return false;
                }
            }
            else
            {
                languageName = Lesta.Application.Language.Current.Name.Split(new char[] { '-' })[1];
                using (Stream manifestResourceStream = executingAssembly.GetManifestResourceStream("Most.UI.Resources.Settings.welcome_" + languageName + ".html"))
                {
                    using (StreamReader streamReader = new StreamReader(manifestResourceStream))
                    {
                        welcomePage = streamReader.ReadToEnd();
                    }
                }
                if (!(__instance.WindowOptions.LastExitTime < DateTime.Now.AddYears(-10)))
                {
                    __result = "";
                    return false;
                }
            }
            __result = welcomePage;
            return false;
        }
    }

    [HarmonyPatch(typeof(Most.Configuration.Model.Item), nameof(Most.Configuration.Model.Item.LocalizedName), MethodType.Getter)]
    class Most_Configuration_Model_Item_LocalizedName_Getter_Patch
    {
        static bool Prefix(ref Most.Configuration.Model.Item __instance, ref string __result)
        {
            Most.Configuration.Model.Localizations localization = __instance.Localization;
            if (localization == null)
            {
                __result = null;
                return false;
            }
            string original = localization.GetLocalizedName(HookManager.FallbackLanguageName);
            bool hasLocalization = HookManager.Language_DisplayName_Name_Map.TryGetValue(Lesta.Application.Language.Current.DisplayName, out string languageName) && Most_Configuration_Model_Localizations_Select.CustomLocalizationMap.ContainsKey(languageName);
            if (hasLocalization && !string.IsNullOrWhiteSpace(original) && Most_Configuration_Model_Localizations_Select.CustomLocalizationMap[languageName].TryGetValue(original, out string temp))
            {
                __result = temp;
            }
            else { __result = original; }
            return false;
        }
    }
}
