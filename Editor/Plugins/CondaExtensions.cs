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

    enum Platform 
    {
        None,
        Win64,
        Linux64,
        Mac64,
        MacArm64,
    };

    enum OS
    {
        None,
        Win,
        Unix,
        Android
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

    public class CondaApp
    {

        Platform m_Platform = Platform.None;
        OS m_TargetOs = OS.None;
        Architecture m_ProcessArch;
        string m_PixiUrl;
        string m_PixiApp;
        string m_Target;
        string m_CondaPath;
        string m_PluginPath; 
        string m_CondaDefault(string target = "") {
            switch (target)
            {
                case "osx-64":
                case "win-64":
                case "linux-64":
                    return Path.Combine(m_PluginPath, "x64");
                case "osx-arm64":
                    return Path.Combine(m_PluginPath, "arm64");
                case "linux-aarch64":
                    return Path.Combine(m_PluginPath, "Android", "arm64-v8a");
                default:
                    return Path.Combine(m_PluginPath,
                        m_ProcessArch == Architecture.Arm64 ? "arm64" : "x64"
                    );
            }
        }

        public string condaLibrary
        {
            get
            {
                switch (m_TargetOs)
                {
                    case OS.Win:
                        return Path.Combine(m_CondaDefault(m_Target), "Library");
                    case OS.Unix:
                        return Path.Combine(m_CondaDefault(m_Target), "lib");
                    case OS.Android:
                        return Path.Combine(m_CondaDefault(m_Target), "lib");
                    default:
                        throw new Exception("No Target Set when requesting Library Path");
                }
            }
        }

        public string condaShared
        {
            get
            {
                switch (m_TargetOs)
                {
                    case OS.Win:
                        return Path.Combine(condaLibrary, "share");
                    case OS.Unix:
                        return Path.Combine(m_CondaDefault(m_Target), "share");
                    case OS.Android:
                        return Path.Combine(m_CondaDefault(m_Target), "share");
                    default:
                        throw new Exception("No Target Set when requesting Shared Path");
                }
            }
        }
            
        public string condaBin
        {
            get
            {
                switch (m_TargetOs)
                {
                    case OS.Win:
                        return Path.Combine(condaLibrary, "bin"); ;
                    case OS.Unix:
                        return Path.Combine(m_CondaDefault(m_Target), "bin");
                    case OS.Android:
                        return Path.Combine(m_CondaDefault(m_Target), "bin");
                    default:
                        throw new Exception("No Target Set when requesting Binary Path");
                }
            }
        }
        
        public CondaApp()
        {
            m_ProcessArch = RuntimeInformation.ProcessArchitecture;
            m_CondaPath = Path.Combine(Application.dataPath, "Conda");
            m_PluginPath = Path.Combine(m_CondaPath, "Plugins");
#if UNITY_EDITOR_WIN
            m_PixiUrl = "https://github.com/prefix-dev/pixi/releases/latest/download/pixi-x86_64-pc-windows-msvc.exe";
            m_Platform = Platform.Win64;
            m_Target = "win-64";
            m_TargetOs = OS.Win;
            m_PixiApp = Path.Combine(m_CondaPath, "pixi.exe");
#elif UNITY_EDITOR_OSX
            if (m_ProcessArch == Architecture.Arm64)
            {
                m_PixiUrl =  "https://github.com/prefix-dev/pixi/releases/latest/download/pixi-aarch64-apple-darwin";
                m_Platform = Platform.MacArm64;
                m_Target = "osx-arm64";
                m_TargetOs = OS.Unix;
                m_PixiApp = Path.Combine(m_CondaPath, "pixi")
            }
            else
            {
                m_PixiUrl =  "https://github.com/prefix-dev/pixi/releases/latest/download/pixi-x86_64-apple-darwin";
                m_Platform = Platform.Mac64;
                m_Target = "osx-64";
                m_TargetOs = OS.Unix;
                m_PixiApp = Path.Combine(m_CondaPath, "pixi")
            }
#elif UNITY_EDITOR_LINUX
            m_PixiUrl =  "https://github.com/mamba-org/micromamba-releases/releases/latest/download/micromamba-linux-64";
            m_Platform = Platform.Linux64;
            m_Target = "linux-64";
            m_TargetOs = OS.Unix;
            m_PixiApp = Path.Combine(m_CondaPath, "pixi")
#endif
            switch (Environment.GetEnvironmentVariable("CONDA_ARCH_OVERRIDE"))
            {
                case "osx-64":
                    m_Target = "osx-64";
                    m_TargetOs = OS.Unix;
                    break;
                case "osx-arm64":
                    m_Target = "osx-arm64";
                    m_TargetOs = OS.Unix;
                    break;
                case "linux-64":
                    m_Target = "linux-64";
                    m_TargetOs = OS.Unix;
                    break;
                case "linux-aarch64":
                    m_Target = "linux-aarch64";
                    m_TargetOs = OS.Android;
                    break;
                case "win-64":
                    m_Target = "win-64";
                    m_TargetOs = OS.Win;
                    break;
                default:
                    break;
            }
            
            Debug.Log($"Platform : {m_Platform.ToString()}");
            Debug.Log($"Target : {m_Target}");
        }

        public string Install(string install_string)
        {

            Debug.Log($"Starting Install for {install_string}");

            string response;
            
            //Check the Conda environment exists and if it does not not - initialize it
            if (!Directory.Exists(m_CondaPath))
            {
                Directory.CreateDirectory(m_CondaPath);
            };
            if (!Directory.Exists(m_PluginPath))
            {
                Directory.CreateDirectory(m_PluginPath);
            };

            if (!File.Exists(m_PixiApp))
            {
                // need to install micromamba which is totally stand-alone

                using (WebClient client = new WebClient())
                {
                    Debug.Log($"Downloading {m_PixiUrl}");
                    client.DownloadFile(m_PixiUrl, m_PixiApp);
                    switch (m_Platform)
                    {
                        case Platform.Mac64:
                        case Platform.MacArm64:
                            using (Process compiler = new Process())
                            {
                                compiler.StartInfo.FileName = "/bin/bash";
                                compiler.StartInfo.Arguments = $"-c \"chmod 766 {m_PixiApp} && xattr -d \"com.apple.quarantine\" {m_PixiApp} \"";
                                compiler.StartInfo.UseShellExecute = false;
                                compiler.StartInfo.RedirectStandardOutput = true;
                                compiler.StartInfo.RedirectStandardError = true;
                                compiler.StartInfo.CreateNoWindow = true;
                                compiler.StartInfo.WorkingDirectory = m_CondaPath;
                                compiler.Start();
                                compiler.WaitForExit();
                                if ( compiler.ExitCode != 0 )
                                    throw new Exception(compiler.StandardError.ReadToEnd());
                            }
                            ;
                            break;
                        case Platform.Linux64:
                            using (Process compiler = new Process())
                            {
                                compiler.StartInfo.FileName = "/bin/bash";
                                compiler.StartInfo.Arguments = $"-c \"chmod 766 {m_PixiApp}  \"";
                                compiler.StartInfo.UseShellExecute = false;
                                compiler.StartInfo.RedirectStandardOutput = true;
                                compiler.StartInfo.RedirectStandardError= true;
                                compiler.StartInfo.CreateNoWindow = true;
                                compiler.StartInfo.WorkingDirectory = m_CondaPath;
                                compiler.Start();
                                compiler.WaitForExit();
                                if (compiler.ExitCode != 0)
                                    throw new Exception(compiler.StandardError.ReadToEnd());
                            }
                            ;
                            break;
                        default:
                            break;
                    }
                    Debug.Log($"File downloaded to: ${m_PixiApp}");
                }
            };

            //Check if there is a working Conda Env 
            //if (!Envs().envs.Contains(pluginPath, new PathEqualityComparer()))
            if (!File.Exists(Path.Combine(m_CondaPath, "pixi.toml")))
            {
                //run the Mamba create env command
                using (Process compiler = new Process())
                {
                    switch(m_Platform){
                        case Platform.Win64:
                            compiler.StartInfo.FileName = "powershell.exe";
                            compiler.StartInfo.Arguments = $"-ExecutionPolicy Bypass {m_PixiApp} init --platform {m_Target} {m_CondaPath}";
                            break;
                        default:
                            compiler.StartInfo.FileName = "/bin/bash";
                            compiler.StartInfo.Arguments = $"-c '{m_PixiApp} init --platform {m_Target} {m_CondaPath}'";
                            break;
                    }
                    compiler.StartInfo.UseShellExecute = false;
                    compiler.StartInfo.RedirectStandardOutput = true;
                    compiler.StartInfo.RedirectStandardError = true;
                    compiler.StartInfo.CreateNoWindow = true;
                    compiler.StartInfo.WorkingDirectory = m_CondaPath;
                    compiler.Start();
                    response = compiler.StandardOutput.ReadToEnd();
                    compiler.WaitForExit();
                    if (compiler.ExitCode != 0)
                        throw new Exception(compiler.StandardError.ReadToEnd());
                }
                Debug.Log($"Init response {response}");
            }

            //Run the Mamba install process using the package specific install script
            using (Process compiler = new Process())
            {
                switch (m_Platform)
                {
                    case Platform.Win64:
                        compiler.StartInfo.FileName = "powershell.exe";
                        compiler.StartInfo.Arguments = $"-ExecutionPolicy Bypass {m_PixiApp} add --no-install  --platform {m_Target} {install_string}";
                        break;
                    default:
                        compiler.StartInfo.FileName = "/bin/bash";
                        compiler.StartInfo.Arguments = $" -c \"{m_PixiApp} add --no-install  --platform {m_Target} {install_string}\" ";
                        break;
                }
                compiler.StartInfo.UseShellExecute = false;
                compiler.StartInfo.RedirectStandardOutput = true;
                compiler.StartInfo.RedirectStandardError= true;
                compiler.StartInfo.CreateNoWindow = true;
                compiler.StartInfo.WorkingDirectory = m_CondaPath;
                compiler.Start();
                response = compiler.StandardOutput.ReadToEnd();
                compiler.WaitForExit();
                if (compiler.ExitCode != 0)
                    throw new Exception(compiler.StandardError.ReadToEnd());
            }
            Debug.Log($"Add response {response}");
            using (Process compiler = new Process())
            {
                switch (m_Platform)
                {
                    case Platform.Win64:
                        compiler.StartInfo.FileName = "powershell.exe";
                        compiler.StartInfo.Arguments = $"-ExecutionPolicy Bypass {m_PixiApp} exec pixi-install-to-prefix --no-activation-scripts --platform {m_Target} {m_CondaDefault(m_Target)}";
                        break;
                    default:
                        compiler.StartInfo.FileName = "/bin/bash";
                        compiler.StartInfo.Arguments = $" -c \"{m_PixiApp} exec pixi-install-to-prefix --no-activation-scripts --platform {m_Target} {m_CondaDefault(m_Target)}\" ";
                        break;
                }
                compiler.StartInfo.UseShellExecute = false;
                compiler.StartInfo.RedirectStandardOutput = true;
                compiler.StartInfo.RedirectStandardError = true;
                compiler.StartInfo.CreateNoWindow = true;
                compiler.StartInfo.WorkingDirectory = m_CondaPath;
                compiler.Start();
                response = compiler.StandardOutput.ReadToEnd();
                compiler.WaitForExit();
                if (compiler.ExitCode != 0)
                    throw new Exception(compiler.StandardError.ReadToEnd());
            }
            Debug.Log($"Install response {response}");
            return response;
        }


        public CondaList Info()
        {
            string response;
            string error;
            EditorUtility.DisplayProgressBar("Conda Package Manager", "Getting Package List", .5f);
            using (Process compiler = new Process())
            {
                switch (m_Platform)
                {
                    case Platform.Win64:
                        compiler.StartInfo.FileName = "powershell.exe";
                        compiler.StartInfo.Arguments = $" -ExecutionPolicy Bypass {m_PixiApp} list --json ";
                        break;
                    default:
                        compiler.StartInfo.FileName = "/bin/bash";
                        compiler.StartInfo.Arguments = $" -c '{m_PixiApp} list --json ' ";
                        break;
                }
                compiler.StartInfo.UseShellExecute = false;
                compiler.StartInfo.RedirectStandardOutput = true;
                compiler.StartInfo.RedirectStandardError = true;
                compiler.StartInfo.CreateNoWindow = true;
                compiler.StartInfo.WorkingDirectory = m_CondaPath;
                compiler.Start();
                response = compiler.StandardOutput.ReadToEnd();
                error = compiler.StandardError.ReadToEnd();
                compiler.WaitForExit();
                if (compiler.ExitCode != 0)
                    throw new Exception(error);
            }
            EditorUtility.ClearProgressBar();
            return response == "" ? default : JsonUtility.FromJson<CondaList>($"{{\"Items\":{response}}}");
        }

        public bool IsInstalled(string name, string packageVersion)
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

        CondaInfo Envs()
        {
            string response;
            EditorUtility.DisplayProgressBar("Conda Package Manager", "Getting Envs List", .5f);
            using (Process compiler = new Process())
            {
                switch (m_Platform)
                {
                    case Platform.Win64:
                        compiler.StartInfo.FileName = "powershell.exe";
                        compiler.StartInfo.Arguments = $" -ExecutionPolicy Bypass {m_PixiApp}.exe info --json ";
                        break;
                    default:
                        compiler.StartInfo.FileName = "/bin/bash";
                        compiler.StartInfo.Arguments = $" -c '{m_PixiApp}.exe info --json ' ";
                        break;
                }
                compiler.StartInfo.UseShellExecute = false;
                compiler.StartInfo.RedirectStandardOutput = true;
                compiler.StartInfo.RedirectStandardError = true; 
                compiler.StartInfo.CreateNoWindow = true;
                compiler.StartInfo.WorkingDirectory = Path.Combine(Application.dataPath, "Conda");
                compiler.Start();
                response = compiler.StandardOutput.ReadToEnd();
                compiler.WaitForExit();
                if (compiler.ExitCode != 0)
                    throw new Exception(compiler.StandardError.ReadToEnd());
            }
            EditorUtility.ClearProgressBar();
            return response == "" ? default : JsonUtility.FromJson<CondaInfo>(response);
        }

        public void TreeShake()
        {
            switch (m_TargetOs)
            {
                case OS.Win:
                    RecurseAndClean(m_CondaDefault(m_Target), new Regex[] {
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
                        //new Regex("\\.exe$"),
                        //new Regex("\\.json$"),
                        //new Regex("\\.txt$"),
                        new Regex("\\.meta$"),
                        }, new Regex[] {
                            new Regex("^api-"),
                            new Regex("^vcr"),
                            new Regex("^msvcp"),
                        });
                    break;
                case OS.Unix:
                    RecurseAndClean(m_CondaDefault(m_Target), new Regex[] {
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
                    break;
                case OS.Android:
                    RecurseAndClean(m_CondaDefault(m_Target), new Regex[] {
                        new Regex("^\\..*"),
                        new Regex("^conda-meta$"),
                        new Regex("\\.meta$"),
                        new Regex("^bin$"),
                        new Regex("^lib$"),
                    });
                    if (Directory.Exists(condaLibrary))
                    {
                        RecurseAndClean(condaLibrary, new Regex[] {
                        new Regex("\\.so$"),
                        new Regex("\\.meta$"),
                        });
                    }

                    //Set the architecture for all .so to Arm64
                    string[] pluginFiles = Directory.GetFiles(m_PluginPath, "*.so", SearchOption.AllDirectories);
                    foreach (string file in pluginFiles)
                    {
                        if (file.EndsWith(".meta")) continue;

                        string assetPath = file.Replace(Application.dataPath, "Assets");
                        var importer = AssetImporter.GetAtPath(assetPath) as PluginImporter;

                        if (importer == null) continue;

                        importer.SetCompatibleWithPlatform(BuildTarget.Android, true);
                        importer.SetPlatformData(BuildTarget.Android, "CPU", "ARM64");
                        importer.SaveAndReimport();
                    }
                    break;
            }
        }

        public void RecurseAndClean(string path, Regex[] excludes, Regex[] includes = null)
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
                CondaApp conda = new CondaApp();
                window.list = conda.Info();
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
