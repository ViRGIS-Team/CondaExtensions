using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using static UnityEngine.GraphicsBuffer;
using Debug = UnityEngine.Debug;


#if UNITY_EDITOR
namespace Conda
{
    [Serializable]
    public class CondaItem
    {
        public string source;
        public string build;
        public string name;
        public bool is_explicit;
        public string version;

        public new string ToString() {
            return name;
        }
    }

    [Serializable]
    public class CondaList
    {
        public CondaItem[] Items;
    }

    [Serializable]
    class CondaInfo
    {
        public string platform;
        public string[] virtual_packages;
        public string version;
        public string cache_dir;
        public string cache_size;
    }

    class CondaEnv
    {
        public string name;
        public string[] features;
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
        private static string condaPath => Path.Combine(Application.dataPath, "Conda");
        private static string pluginPath => Path.Combine(condaPath, "Plugins");
        private static string condaDefault {
            get
            {
                switch (Environment.GetEnvironmentVariable("CONDA_ARCH_OVERRIDE"))
                {
                    case "osx-64":
                    case "win-64":
                    case "linux-64":
                        return Path.Combine(pluginPath, "x64" );
                    case "osx-arm64":
                        return Path.Combine(pluginPath, "arm64");
                    case "linux-aarch64":
                        return Path.Combine(pluginPath, "Android");
                    default :
                        return Path.Combine(pluginPath,
                            RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "x64"
                        );
                }
            }
        }
#if UNITY_EDITOR_WIN
        public static string condaLibrary => Path.Combine(condaDefault, "Library");
        public static string condaShared => Path.Combine(condaLibrary, "share");
        public static string condaBin => Path.Combine(condaLibrary, "bin");
        private static string  pixiApp => Path.Combine(condaPath, "pixi.exe");
#else
        public static string condaLibrary => Path.Combine(condaDefault, "lib");
        public static string condaShared => Path.Combine(condaDefault, "share");
        public static string condaBin => Path.Combine(condaDefault, "bin");
        private static string  pixiApp => Path.Combine(condaPath, "pixi");
#endif

        public static string Install(string install_string)
        {

            Debug.Log($"Starting Install for {install_string}");

            string response;
            string url, platform, target, syspkgs;
            Architecture processArch = RuntimeInformation.ProcessArchitecture;
            syspkgs = "";
#if UNITY_EDITOR_WIN
            url = "https://github.com/prefix-dev/pixi/releases/latest/download/pixi-x86_64-pc-windows-msvc.exe";
            platform = "win-64";
#elif UNITY_EDITOR_OSX
            if (processArch == Architecture.Arm64)
            {
                url =  "https://github.com/prefix-dev/pixi/releases/latest/download/pixi-aarch64-apple-darwin";
                platform = "osx-arm64";
            }
            else
            {
                url =  "https://github.com/prefix-dev/pixi/releases/latest/download/pixi-x86_64-apple-darwin";
                platform = "osx-64";
            }
#elif UNITY_EDITOR_LINUX
            url =  "https://github.com/mamba-org/micromamba-releases/releases/latest/download/micromamba-linux-64";
            platform = "linux-64";
#endif
            if (Environment.GetEnvironmentVariable("CONDA_ARCH_OVERRIDE") == null){
                target = platform;
            } else {
                target = Environment.GetEnvironmentVariable("CONDA_ARCH_OVERRIDE");
                Debug.Log($"Conda Architecture Override set to {target}");
                if (platform.Contains("osx") && target.Contains("linux")){
                    syspkgs = $"gcc_{target} gxx_{target} sysroot_{target} ";
                    Debug.Log($" syspkgs set to : {syspkgs}");
                }
            }
            //Check the Conda environment exists and if it does not not - initialize it
            if (!Directory.Exists(condaPath))
            {
                Directory.CreateDirectory(condaPath);
            };
            if (!Directory.Exists(pluginPath))
            {
                Directory.CreateDirectory(pluginPath);
            };
            Debug.Log($"Platform : {platform}");
            Debug.Log($"Target : {target}");
            if (!System.IO.File.Exists(pixiApp))
            {
                // need to install micromamba which is totally stand-alone

                using (WebClient client = new WebClient())
                {
                    Debug.Log($"Downloading {url}");
                    client.DownloadFile(url, pixiApp);
#if UNITY_EDITOR_OSX
                    using (Process compiler = new Process()) {
                        compiler.StartInfo.FileName = "/bin/bash";
                        compiler.StartInfo.Arguments = $"-c \"chmod 766 {pixiApp} && xattr -d \"com.apple.quarantine\" {pixiApp} \"";
                        compiler.StartInfo.UseShellExecute = false;
                        compiler.StartInfo.RedirectStandardOutput = true;
                        compiler.StartInfo.CreateNoWindow = true;
                        compiler.StartInfo.WorkingDirectory = condaPath;
                        compiler.Start();
                        compiler.WaitForExit();
                    }
#elif UNITY_EDITOR_LINUX
                    using (Process compiler = new Process()) {
                        compiler.StartInfo.FileName = "/bin/bash";
                        compiler.StartInfo.Arguments = $"-c \"chmod 766 {pixiApp}  \"";
                        compiler.StartInfo.UseShellExecute = false;
                        compiler.StartInfo.RedirectStandardOutput = true;
                        compiler.StartInfo.CreateNoWindow = true;
                        compiler.StartInfo.WorkingDirectory = condaPath;
                        compiler.Start();
                        compiler.WaitForExit();
                    }
#endif
                    Debug.Log($"File downloaded to: ${pixiApp}");
                }
            };

            //CHeck if there is a working Conda Env 
            //if (!Envs().envs.Contains(pluginPath, new PathEqualityComparer()))
            if (!File.Exists(Path.Combine(condaPath, "pixi.toml")))
            {
                //run the Mamba create env command
                using (Process compiler = new Process())
                {
#if UNITY_EDITOR_WIN
                    compiler.StartInfo.FileName = "powershell.exe";
                    compiler.StartInfo.Arguments = $"-ExecutionPolicy Bypass {pixiApp} init --platform {target} {condaPath}";
#else
                    compiler.StartInfo.FileName = "/bin/bash";
                    compiler.StartInfo.Arguments = $"-c '{pixiApp} init --platform {target} {condaPath}'";
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
                compiler.StartInfo.Arguments = $"-ExecutionPolicy Bypass {pixiApp} add --no-install  --platform {target} {install_string}";
#else
                compiler.StartInfo.FileName = "/bin/bash";
                compiler.StartInfo.Arguments = $" -c \"{pixiApp} add --no-install  --platform {target} {install_string}\" ";
#endif
                compiler.StartInfo.UseShellExecute = false;
                compiler.StartInfo.RedirectStandardOutput = true;
                compiler.StartInfo.CreateNoWindow = true;
                compiler.StartInfo.WorkingDirectory = condaPath;
                compiler.Start();

                response = compiler.StandardOutput.ReadToEnd();

                compiler.WaitForExit();
            }

            using (Process compiler = new Process())
            {
#if UNITY_EDITOR_WIN
                compiler.StartInfo.FileName = "powershell.exe";
                compiler.StartInfo.Arguments = $"-ExecutionPolicy Bypass {pixiApp} exec pixi-install-to-prefix --no-activation-scripts --platform {target} {condaDefault}";
#else
                    compiler.StartInfo.FileName = "/bin/bash";
                    compiler.StartInfo.Arguments = $" -c \"{pixiApp} exec pixi-install-to-prefix --no-activation-scripts --platform {target} {condaDefault}\" ";
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


        public static CondaList Info()
        {
            string response;
            string error;
            EditorUtility.DisplayProgressBar("Conda Package Manager", "Getting Package List", .5f);
            using (Process compiler = new Process())
            {
#if UNITY_EDITOR_WIN
                compiler.StartInfo.FileName = "powershell.exe";
                compiler.StartInfo.Arguments = $" -ExecutionPolicy Bypass {pixiApp} list --json ";
#else
                compiler.StartInfo.FileName = "/bin/bash";
                compiler.StartInfo.Arguments = $" -c '{pixiApp} list --json ' ";
#endif
                compiler.StartInfo.UseShellExecute = false;
                compiler.StartInfo.RedirectStandardOutput = true;
                compiler.StartInfo.RedirectStandardError = true;
                compiler.StartInfo.CreateNoWindow = true;
                compiler.StartInfo.WorkingDirectory = condaPath;
                compiler.Start();

                response = compiler.StandardOutput.ReadToEnd();
                error = compiler.StandardError.ReadToEnd();

                compiler.WaitForExit();
            }
            EditorUtility.ClearProgressBar();
            return response == "" ? default : JsonUtility.FromJson<CondaList>($"{{\"Items\":{response}}}");
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
            return Array.Exists( items, item => item.name == name && item.version == packageVersion );
        }

        private static CondaInfo Envs()
        {
            string pixiApp = Path.Combine(Application.dataPath, "Conda", "pixi");
            string response;
            EditorUtility.DisplayProgressBar("Conda Package Manager", "Getting Envs List", .5f);
            using (Process compiler = new Process())
            {
#if UNITY_EDITOR_WIN
                compiler.StartInfo.FileName = "powershell.exe";
                compiler.StartInfo.Arguments = $" -ExecutionPolicy Bypass {pixiApp}.exe info --json ";
#else
                compiler.StartInfo.FileName = "/bin/bash";
                compiler.StartInfo.Arguments = $" -c '{pixiApp}.exe info --json ' ";
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
            return response == "" ? default : JsonUtility.FromJson<CondaInfo>(response);
        }

        public static void TreeShake()
        {
            string target;
#if UNITY_EDITOR_WIN
            string platform = "win";
#else
            string platform = "unix";
#endif

            switch (Environment.GetEnvironmentVariable("CONDA_ARCH_OVERRIDE"))
            {
                case "osx-64":
                case "osx-arm64":
                case "linux-64":
                case "linux-aarch64":
                    target = "unix";
                    break;
                case "win-64":
                    target = "win";
                    break;
                default:
                    target = platform;
                    break;
            }
            if (target == "win")
            {
                RecurseAndClean(condaDefault, new Regex[] {
                    new Regex("^\\..*"),
                    new Regex("^conda-meta$"),
                    new Regex("\\.meta$"),
                    new Regex("^Library$"),
                    new Regex("\\.txt$"),
                });
                if (Directory.Exists(condaLibrary))
                    RecurseAndClean(condaLibrary, new Regex[]{
                    new Regex("^bin$")
                });
                if (Directory.Exists(condaBin))
                    RecurseAndClean(condaBin, new Regex[] {
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
            }
            else
            {
                RecurseAndClean(condaDefault, new Regex[] {
                    new Regex("^\\..*"),
                    new Regex("^conda-meta$"),
                    new Regex("\\.meta$"),
                    new Regex("^bin$"),
                    new Regex("^lib$"),
                });
                if (Directory.Exists(condaLibrary))
                {
                    RecurseAndClean(condaLibrary, new Regex[] {
                    new Regex("\\.lib$"),
                    new Regex("\\.dylib$"),
                    new Regex("\\.so$"),
                    new Regex("\\.meta$"),
                    });
                }
            }
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
        public CondaList list;
        public Vector2 svposition = Vector2.zero;

        private void OnGUI()
        {
            svposition = GUILayout.BeginScrollView(svposition);
            if (list != null)
                foreach (CondaItem item in list.Items)
                {
                    GUILayout.Label($"{item.name}\t\t:\t{item.version}");
                }
            GUILayout.EndScrollView();
        }
    }
}
#endif
