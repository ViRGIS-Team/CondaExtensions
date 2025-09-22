using UnityEngine;
using UnityEditor;
using System.IO;
using System.Diagnostics;
using System;
using Debug = UnityEngine.Debug;
using System.Net;
using System.Linq;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.Text.RegularExpressions;


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

        public new string ToString() {
            return name;
        }
    }

    [Serializable]
    public class CondaInfo
    {
        public CondaItem[] Items;
    }

    [Serializable]
    public class CondaEnvs
    {
        public string[] envs;
    }

    public class PathEqualityComparer : IEqualityComparer<string>
    {
        private readonly StringComparison _comparison;

        public PathEqualityComparer()
        {
            // Windows + macOS default to case-insensitive, Linux is case-sensitive
            _comparison = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ||
                          RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
        }

        public bool Equals(string x, string y)
        {
            if (x is null || y is null) return x == y;

            string fullX = Path.GetFullPath(x).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string fullY = Path.GetFullPath(y).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            return string.Equals(fullX, fullY, _comparison);
        }

        public int GetHashCode(string obj)
        {
            if (obj is null) return 0;

            string full = Path.GetFullPath(obj).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            return _comparison == StringComparison.OrdinalIgnoreCase
                ? StringComparer.OrdinalIgnoreCase.GetHashCode(full)
                : StringComparer.Ordinal.GetHashCode(full);
        }
    }

    public class Conda
    {

        public static string Install(string install_string)
        {

            Debug.Log($"Starting Install for {install_string}");
            string condaPath = Path.Combine(Application.dataPath, "Conda");
            string pluginPath = Path.Combine(condaPath, "Env");
            string response;
            string url, mambaApp, platform, target;
            Architecture processArch = RuntimeInformation.ProcessArchitecture;
#if UNITY_EDITOR_WIN
            url = "https://github.com/mamba-org/micromamba-releases/releases/latest/download/micromamba-win-64";
            mambaApp = Path.Combine(condaPath, "micromamba.exe");
            platform = "win-64";
#elif UNITY_EDITOR_OSX
            mambaApp = Path.Combine(condaPath, "micromamba");
            Debug.Log("Unity Editor Process Architecture: " + processArch);

            if (processArch == Architecture.Arm64)
            {
                url =  "https://github.com/mamba-org/micromamba-releases/releases/latest/download/micromamba-osx-arm64";
                platform = "osx-arm64";
            }
            else
            {
                url =  "https://github.com/mamba-org/micromamba-releases/releases/latest/download/micromamba-osx-64";
                platform = "osx-64";
            }
#elif UNITY_EDITOR_LINUX
            url =  "https://github.com/mamba-org/micromamba-releases/releases/latest/download/micromamba-linux-64";
            mambaApp = Path.Combine(condaPath, "micromamba");
#endif
            if (Environment.GetEnvironmentVariable("CONDA_ARCH_OVERRIDE") == null){
                target = platform;
            } else {
                target = Environment.GetEnvironmentVariable("CONDA_ARCH_OVERRIDE");
                Debug.Log($"Conda Architecture Override set to {target}");
            }
            //Check the Conda environment exists and if it does not not - initialize it
            if (!Directory.Exists(condaPath))
            {
                Directory.CreateDirectory(condaPath);
            };
            if (!System.IO.File.Exists(mambaApp))
            {
                // need to install micromamba which is totally stand-alone

                using (WebClient client = new WebClient())
                {
                    Debug.Log($"Downloading ${url}");
                    client.DownloadFile(url, mambaApp);
#if UNITY_EDITOR_OSX
                    using (Process compiler = new Process()) {
                        compiler.StartInfo.FileName = "/bin/bash";
                        compiler.StartInfo.Arguments = $"-c \"chmod 766 {mambaApp} && xattr -d \"com.apple.quarantine\" {mambaApp} \"";
                        compiler.StartInfo.UseShellExecute = false;
                        compiler.StartInfo.RedirectStandardOutput = true;
                        compiler.StartInfo.CreateNoWindow = true;
                        compiler.StartInfo.WorkingDirectory = condaPath;
                        compiler.Start();
                        compiler.WaitForExit();
                    }
#endif
                    Debug.Log($"File downloaded to: ${mambaApp}");
                }
            };

            //CHeck if there is a working Conda Env 
            if (!Envs().envs.Contains(pluginPath, new PathEqualityComparer()))
            {
                //run the Mamba create env command
                using (Process compiler = new Process())
                {
#if UNITY_EDITOR_WIN
                    compiler.StartInfo.FileName = "powershell.exe";
                    compiler.StartInfo.Arguments = $"-ExecutionPolicy Bypass {mambaApp} create -c conda-forge -p {pluginPath} -y";
#else
                    compiler.StartInfo.FileName = "/bin/bash";
                    compiler.StartInfo.Arguments = $"-c '{mambaApp} create -c conda-forge -p {pluginPath} -y --platform {target}'";
#endif
                    compiler.StartInfo.UseShellExecute = false;
                    compiler.StartInfo.RedirectStandardOutput = true;
                    compiler.StartInfo.CreateNoWindow = true;
                    compiler.StartInfo.WorkingDirectory = condaPath;
                    compiler.Start();

                    response = compiler.StandardOutput.ReadToEnd();

                    compiler.WaitForExit();
                }
                Debug.Log(response);
            }

            //Run the Mamba install process using the package specific install script
            using (Process compiler = new Process())
            {
#if UNITY_EDITOR_WIN
                compiler.StartInfo.FileName = "powershell.exe";
                compiler.StartInfo.Arguments = $"-ExecutionPolicy Bypass {mambaApp} install -c conda-forge -p {pluginPath} --copy {install_string} -y -v *>&1";
#else
                compiler.StartInfo.FileName = "/bin/bash";
                compiler.StartInfo.Arguments = $" -c \"'{mambaApp}' install -c conda-forge/{target} --strict-channel-priority -p '{pluginPath}' '{install_string}' -y --json \" ";
#endif
                compiler.StartInfo.UseShellExecute = false;
                compiler.StartInfo.RedirectStandardOutput = true;
                compiler.StartInfo.CreateNoWindow = true;
                compiler.StartInfo.WorkingDirectory = condaPath;
                compiler.Start();

                response = compiler.StandardOutput.ReadToEnd();

                compiler.WaitForExit();
            }
            Debug.Log(response);
            return response;
        }


        public static CondaInfo Info()
        {
            string pluginPath = Path.Combine(Application.dataPath, "Conda", "Env");
            string mambaApp = Path.Combine(Application.dataPath, "Conda", "micromamba");
            string response;
            EditorUtility.DisplayProgressBar("Conda Package Manager", "Getting Package List", .5f);
            using (Process compiler = new Process())
            {
#if UNITY_EDITOR_WIN
                compiler.StartInfo.FileName = "powershell.exe";
                compiler.StartInfo.Arguments = $" -ExecutionPolicy Bypass {mambaApp} list -p '{pluginPath}' --json ";
#else
                compiler.StartInfo.FileName = "/bin/bash";
                compiler.StartInfo.Arguments = $" -c '{mambaApp} list -p \"{pluginPath}\" --json ' ";
#endif
                compiler.StartInfo.UseShellExecute = false;
                compiler.StartInfo.RedirectStandardOutput = true;
                compiler.StartInfo.CreateNoWindow = true;
                compiler.StartInfo.WorkingDirectory = Application.dataPath;
                compiler.Start();

                response = compiler.StandardOutput.ReadToEnd();

                compiler.WaitForExit();
            }
            EditorUtility.ClearProgressBar();
            return response == "" ? default : JsonUtility.FromJson<CondaInfo>($"{{\"Items\":{response}}}");
        }

        public static bool IsInstalled(string name, string packageVersion)
        {
            CondaItem[] items;
            try {
                items = Info().Items;
            } catch {
                return false;
            }
            if (items.Length == 0) return false;
            Architecture processArch = RuntimeInformation.ProcessArchitecture;
            string platform = "";
#if UNITY_EDITOR_WIN
            platform = "win-64";
#elif UNITY_EDITOR_OSX
            if (processArch == Architecture.X64)
                platform = "osx-64";
            else 
                platform = "osx-arm64";
#endif
            return Array.Exists( items, item => item.name == name && item.version == packageVersion && item.platform == platform);
        }

        public static CondaEnvs Envs()
        {
            string mambaApp = Path.Combine(Application.dataPath, "Conda", "micromamba");
            string response;
            EditorUtility.DisplayProgressBar("Conda Package Manager", "Getting Envs List", .5f);
            using (Process compiler = new Process())
            {
#if UNITY_EDITOR_WIN
                compiler.StartInfo.FileName = "powershell.exe";
                compiler.StartInfo.Arguments = $" -ExecutionPolicy Bypass {mambaApp} info --envs  --json ";
#else
                compiler.StartInfo.FileName = "/bin/bash";
                compiler.StartInfo.Arguments = $" -c '{mambaApp} info --envs  --json ' ";
#endif
                compiler.StartInfo.UseShellExecute = false;
                compiler.StartInfo.RedirectStandardOutput = true;
                compiler.StartInfo.CreateNoWindow = true;
                compiler.StartInfo.WorkingDirectory = Path.Combine(Application.dataPath, "Conda");
                compiler.Start();

                response = compiler.StandardOutput.ReadToEnd();

                compiler.WaitForExit();
            }
            EditorUtility.ClearProgressBar();
            return response == "" ? default : JsonUtility.FromJson<CondaEnvs>(response);
        }

        public static void TreeShake()
        {
            string path = Path.Combine(Application.dataPath, "Conda", "Env");
#if UNITY_EDITOR_WIN
            RecurseAndClean(path, new Regex[] {
                    new Regex("^\\..*"),
                    new Regex("^conda-meta$"),
                    new Regex("\\.meta$"),
                    new Regex("^Library$"),
                    new Regex("\\.txt$"),
                });
            path = Path.Combine(path, "Library");
            RecurseAndClean(path, new Regex[]{
                new Regex("^bin$")
            });
            path = Path.Combine(path, "bin");
            RecurseAndClean(path, new Regex[] {
                new Regex("\\.dll$"),
                new Regex("\\.exe$"),
                new Regex("\\.json$"),
                new Regex("\\.txt$"),
                new Regex("\\.meta$"),
                }, new Regex[] {
                    new Regex("^api-"),
                    new Regex("^vcr"),
                    new Regex("^msvcp"),
                });
#else
            RecurseAndClean(path, new Regex[] {
                    new Regex("^\\..*"),
                    new Regex("^conda-meta$"),
                    new Regex("\\.meta$"),
                    new Regex("^bin$"),
                    new Regex("^lib$"),
                });
            path = Path.Combine(path, "lib");
            RecurseAndClean(path, new Regex[] {
                new Regex("\\.lib$"),
                new Regex("\\.dylib$"),
                new Regex("\\.meta$"),
                });
            foreach (var dir in Directory.GetDirectories(path)){
                try
                {
                    Directory.Delete(dir,true);
                    Console.WriteLine($"Deleted directory: {dir}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to delete directory {dir}: {ex.Message}");
                }
            }
#endif
        }

        public static void RecurseAndClean(string path, Regex[] excludes, Regex[] includes = null)
        {
            // Process files
            foreach (var file in Directory.GetFiles(path))
            {
                string fileName = Path.GetFileName(file);
                bool keep = excludes.Any(rx => rx.IsMatch(fileName));
                if (includes != null & keep)
                {
                    keep = ! includes.Any(rx => rx.IsMatch(fileName));
                }

                if (!keep)
                {
                    try
                    {
                        File.Delete(file);
                        Console.WriteLine($"Deleted file: {file}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Failed to delete file {file}: {ex.Message}");
                    }
                }
            }

            // Process subdirectories
            foreach (var dir in Directory.GetDirectories(path))
            {
               string dirName= Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

                bool keep = excludes.Any(rx => rx.IsMatch(dirName));

                if (!keep)
                {
                    try
                    {
                        Directory.Delete(dir,true);
                        Console.WriteLine($"Deleted directory: {dir}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Failed to delete directory {dir}: {ex.Message}");
                    }
                }
            }
        }
    }

    public class CondaMenus
    {
        [MenuItem("Conda/List Packages")]
        static void ListPackages()
        {
            ListWindow window = (ListWindow)EditorWindow.GetWindow(typeof(ListWindow));
            try
            {
                window.list = Conda.Info();
            }
            catch (Exception e)
            {
                Debug.LogError($"Conda Error: {e.ToString()}");
                window.list = default;
            }
            window.titleContent.text = "Installed Conda Packages";
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
            if (list != null)
                foreach (CondaItem item in list.Items)
                {
                    GUILayout.Label($"{item.name}\t\t:\t{item.version}:\t{item.platform}");
                }
            GUILayout.EndScrollView();
        }
    }
}
#endif
