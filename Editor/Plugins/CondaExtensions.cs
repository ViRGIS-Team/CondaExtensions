using UnityEngine;
using UnityEditor;
using System.IO;
using System.Diagnostics;
using System;
using Debug = UnityEngine.Debug;

#if UNITY_EDITOR
namespace Conda
{
    [Serializable]
    public class CondaItem
    {
        public string base_url;
        public int build_number;
        public string build_string;
        public string channel;
        public string dist_name;
        public string name;
        public string platform;
        public string version;
    }

    [Serializable]
    public class CondaInfo
    {
        public CondaItem[] Items;
    }

    public class Conda
    {
#if UNITY_EDITOR_OSX
        public const string basharg = "-l";
#elif UNITY_EDITOR_LINUX
        public const string basharg = "-i";
#endif

        public static string Install(string install_string, string install_script)
        {

            string pluginPath = Path.Combine(Application.dataPath, "Conda");
            string response;
            using (Process compiler = new Process())
            {
#if UNITY_EDITOR_WIN
                compiler.StartInfo.FileName = "powershell.exe";
                compiler.StartInfo.Arguments = $"-ExecutionPolicy Bypass \"{install_script}\" " +
                                                    $"-install {install_string} " +
                                                    $"-destination '{pluginPath}' " +
                                                    $"-shared_assets '{Application.streamingAssetsPath}' ";
#else
                compiler.StartInfo.FileName = "/bin/bash";
                compiler.StartInfo.Arguments = $" {basharg} \"{install_script}\" " +
                                                $"-i {install_string} " +
                                                $"-d '{pluginPath}' " +
                                                $"-s '{Application.streamingAssetsPath}'  ";
#endif
                compiler.StartInfo.UseShellExecute = false;
                compiler.StartInfo.RedirectStandardOutput = true;
                compiler.StartInfo.CreateNoWindow = true;
                compiler.Start();

                response = compiler.StandardOutput.ReadToEnd();

                compiler.WaitForExit();
            }
            return response;
        }


        public static CondaInfo Info()
        {
            string pluginPath = Path.Combine(Application.dataPath, "Conda");
            string response;
            EditorUtility.DisplayProgressBar("Conda Package Manager", "Getting Package List", .5f);
            try
            {
                using (Process compiler = new Process())
                {
#if UNITY_EDITOR_WIN
                    compiler.StartInfo.FileName = "powershell.exe";
                    compiler.StartInfo.Arguments = $" -ExecutionPolicy Bypass conda list -p {pluginPath} --json ";
#else
                    compiler.StartInfo.FileName = "/bin/bash";
                    compiler.StartInfo.Arguments = $"{basharg} -c 'conda list -p {pluginPath} --json ' ";
#endif
                    compiler.StartInfo.UseShellExecute = false;
                    compiler.StartInfo.RedirectStandardOutput = true;
                    compiler.StartInfo.CreateNoWindow = true;
                    compiler.Start();

                    response = compiler.StandardOutput.ReadToEnd();

                    compiler.WaitForExit();
                }
                EditorUtility.ClearProgressBar();
                return JsonUtility.FromJson<CondaInfo>($"{{\"Items\":{response}}}");
            }
            catch (Exception e)
            {
                Debug.LogError($"Conda Error: {e.ToString()}");
                return default;
            }
        }
    }

    public class CondaMenus
    {
        [MenuItem("Conda/List Packages")]
        static void ListPackages()
        {
            ListWindow window = (ListWindow)EditorWindow.GetWindow(typeof(ListWindow));
            window.list = Conda.Info();
            window.title = "Installed Conda Packages";
            window.Show();
        }
    }

    public class ListWindow : EditorWindow
    {
        public CondaInfo list;
        public Vector2 svposition = Vector2.zero;

        private void OnGUI()
        {
            svposition = GUILayout.BeginScrollView(svposition);
            foreach (CondaItem item in list.Items)
            {
                GUILayout.Label($"{item.name}\t\t:\t{item.version}");
            }
            GUILayout.EndScrollView();
        }
    }
}
#endif