using System.IO;
using System.Xml;
using UnityEditor.Android;
using UnityEngine;

namespace Sygnet.Editor
{
    /// <summary>
    /// SYGNET działa w 100% offline i nie może prosić o uprawnienie INTERNET (CLIENT_UNITY.md §1, §8).
    /// Unity dodaje je np. w buildach deweloperskich albo po wykryciu UnityWebRequest, więc po wygenerowaniu
    /// projektu Gradle dopisujemy do manifestu launchera <c>tools:node="remove"</c>. Launcher ma najwyższy
    /// priorytet w manifest mergerze, więc uprawnienie znika ze wszystkich bibliotek (unityLibrary itd.).
    /// </summary>
    public class AndroidNoInternetPermission : IPostGenerateGradleAndroidProject
    {
        const string AndroidNs = "http://schemas.android.com/apk/res/android";
        const string ToolsNs = "http://schemas.android.com/tools";
        static readonly string[] Removed = { "android.permission.INTERNET" };

        public int callbackOrder => 1000;

        public void OnPostGenerateGradleAndroidProject(string unityLibraryPath)
        {
            var manifestPath = Path.Combine(unityLibraryPath, "..", "launcher", "src", "main", "AndroidManifest.xml");
            if (!File.Exists(manifestPath))
            {
                Debug.LogError("[SYGNET] Nie znaleziono manifestu launchera: " + manifestPath);
                return;
            }

            var doc = new XmlDocument();
            doc.Load(manifestPath);
            var manifest = doc.DocumentElement;
            if (manifest.GetAttribute("xmlns:tools") == "")
                manifest.SetAttribute("xmlns:tools", ToolsNs);

            foreach (var perm in Removed)
            {
                var el = doc.CreateElement("uses-permission");
                el.SetAttribute("name", AndroidNs, perm);
                el.SetAttribute("node", ToolsNs, "remove");
                manifest.PrependChild(el);
            }

            doc.Save(manifestPath);
            Debug.Log("[SYGNET] Usunięto z manifestu: " + string.Join(", ", Removed));
        }
    }
}
