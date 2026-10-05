using System.IO;
using UnityEngine;

namespace BusSim.Editor
{
    /// <summary>
    /// Writes harness reports to the console and to Logs/Harness/, because the console tool shows only the
    /// first line of a multi-line message over MCP.
    /// </summary>
    internal static class HarnessLog
    {
        private const string Folder = "Logs/Harness";

        public static void Write(string label, string text)
        {
            Debug.Log(label + "\n" + text);
            string directory = Path.Combine(Directory.GetParent(Application.dataPath).FullName, Folder);
            Directory.CreateDirectory(directory);
            foreach (char invalid in Path.GetInvalidFileNameChars())
            {
                label = label.Replace(invalid, '_');
            }
            File.WriteAllText(Path.Combine(directory, label + ".txt"), text);
        }
    }
}
