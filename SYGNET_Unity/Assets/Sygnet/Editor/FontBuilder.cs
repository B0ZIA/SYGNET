using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace Sygnet.Editor
{
    /// <summary>
    /// Buduje assety TextMeshPro z TTF w Assets/Sygnet/Fonts → Assets/Resources/Fonts (ładowane przez Theme).
    /// Dynamiczne atlasy (brakujące znaki dochodzą w locie), z góry wypełnione alfabetem polskim i znakami UI.
    /// </summary>
    public static class FontBuilder
    {
        const string Source = "Assets/Sygnet/Fonts/";
        const string Target = "Assets/Resources/Fonts/";

        static readonly string[] Fonts =
        {
            "Inter-Regular", "Inter-SemiBold", "Inter-Bold", "Inter-ExtraBold", "JetBrainsMono-Medium", "JetBrainsMono-Bold",
        };

        const string Charset =
            " !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~" +
            "ąćęłńóśźżĄĆĘŁŃÓŚŹŻ„”“‚’–—…·•→←°×";

        [MenuItem("SYGNET/Zbuduj fonty TMP")]
        public static string BuildAll()
        {
            Directory.CreateDirectory(Target);
            var fallback = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
                "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset");
            var report = "";
            foreach (var name in Fonts)
            {
                var font = AssetDatabase.LoadAssetAtPath<Font>(Source + name + ".ttf");
                if (font == null)
                {
                    report += name + ": brak TTF; ";
                    continue;
                }
                var path = Target + name + " SDF.asset";
                AssetDatabase.DeleteAsset(path);
                var fa = TMP_FontAsset.CreateFontAsset(font, 64, 6, GlyphRenderMode.SDFAA, 1024, 1024,
                    AtlasPopulationMode.Dynamic, false);
                fa.name = name + " SDF";
                fa.TryAddCharacters(Charset, out var missing);
                // statyczny atlas: nic nie dopisuje się w trakcie gry, więc asset nie zmienia się w gicie
                fa.atlasPopulationMode = AtlasPopulationMode.Static;
                AssetDatabase.CreateAsset(fa, path);
                fa.atlasTextures[0].name = name + " Atlas";
                AssetDatabase.AddObjectToAsset(fa.atlasTextures[0], fa);
                fa.material.name = name + " Material";
                AssetDatabase.AddObjectToAsset(fa.material, fa);
                if (fallback != null) fa.fallbackFontAssetTable = new System.Collections.Generic.List<TMP_FontAsset> { fallback };
                EditorUtility.SetDirty(fa);
                report += name + " (" + fa.characterTable.Count + " znaków)" +
                          (string.IsNullOrEmpty(missing) ? " OK; " : " brak: " + missing + "; ");
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[SYGNET] Fonty TMP: " + report);
            return report;
        }
    }
}
