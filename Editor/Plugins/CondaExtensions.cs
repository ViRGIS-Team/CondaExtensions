using Newtonsoft.Json;
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
    public class ConfigFile
    {
        [Serializable]
        public class Clean
        {
            public string[] path;
            public string[] includes;
            public string[] excludes;
        }

        [Serializable]
        public class Package : IEquatable<Package>
        {
            public string Name;
            public Clean[] Cleans;
            public string[] Shared_Datas;

            public bool Equals(Package other)
            {
                return Name == other.Name;
            }
        }

        public List<Package> Packages;

        public ConfigFile()
        {
            Packages = new ();
        }
    }

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
    public class PixiEnv
    {
        public string name;
        public string[] features;
        public string solve_group;
        public string environment_size;
        public string[] dependencies;
        public string[] platforms;
    }

    [Serializable]
    public class PixiProject
    {
        public string name;
        public string manifest_path;
        public string last_updated;
    }

    [Serializable]
    public class PixiInfo
    {
        public string platform;
        public string version;
        public PixiProject project_info;
        public PixiEnv[] environments_info;
    }

    public enum Platform 
    {
        None,
        Windows_x64,
        Linux_x64,
        Mac_x64,
        Mac_Arm64,
        Linux_Arm64,
        Android_Arm64
    };

    static class PlatformExtensions
    {
        public static string ToCondaString(this Platform platform) => platform switch
        {
            Platform.Windows_x64 => "win-64",
            Platform.Mac_x64 => "osx-64",
            Platform.Linux_x64 => "linux-64",
            Platform.Mac_Arm64 => "osx-arm64",
            Platform.Linux_Arm64 => "linux-aarch64",
            _ => platform.ToString()
        };
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
        public const string TARGETS = "--platform win-64 --platform osx-64 --platform osx-arm64 --platform linux-64";
        public Platform[] TARGETLIST = new Platform[] { 
            Platform.Windows_x64,
            Platform.Mac_x64,
            Platform.Mac_Arm64,
            Platform.Linux_x64};
        public string CondaPath;
        public string PluginPath;

        Platform m_Platform = Platform.None;
        Architecture m_ProcessArch;
        string m_PixiUrl;
        string m_PixiApp;
        Platform m_Target;

        ConfigFile m_Config;

        string CondaDefault(Platform target) {
            switch (target)
            {
                case Platform.Mac_x64:
                    return Path.Combine(PluginPath, "OSX", "x64");
                case Platform.Windows_x64:
                    return Path.Combine(PluginPath, "Windows", "x64");
                case Platform.Linux_x64:
                    return Path.Combine(PluginPath, "Linux", "x64");
                case Platform.Mac_Arm64:
                    return Path.Combine(PluginPath, "OSX", "arm64");
                //case Platform.:
                //    return Path.Combine(PluginPath, "Android", "arm64-v8a");
                default:
                    switch (m_Platform)
                    {
                        case Platform.Windows_x64:
                            return Path.Combine(PluginPath, "Windows", "x64");
                        case Platform.Mac_x64:
                            return Path.Combine(PluginPath, "OSX", "x64");
                        case Platform.Mac_Arm64:
                            return Path.Combine(PluginPath, "OSX", "arm64");
                        case Platform.Linux_x64:
                            return Path.Combine(PluginPath, "Linux", "x64");
                        default:
                            throw new Exception("Conda Platform is invalid");
                    }
            }
        }

        public string condaLibrary(Platform target)
        {
            switch (target)
            {
                case Platform.Windows_x64:
                    return Path.Combine(CondaDefault(target), "Library");
                case Platform.Mac_x64:
                case Platform.Mac_Arm64:
                case Platform.Linux_x64:
                case Platform.Linux_Arm64:
                    return Path.Combine(CondaDefault(target), "lib");
                default:
                    throw new Exception("No Target Set when requesting Library Path");
            }
        }

        public string condaShared(Platform target)
        {
            switch (target)
            {
                case Platform.Windows_x64:
                    return Path.Combine(condaLibrary(target), "share");
                case Platform.Mac_x64:
                case Platform.Mac_Arm64:
                case Platform.Linux_x64:
                case Platform.Linux_Arm64:
                    return Path.Combine(CondaDefault(m_Target), "share");
                default:
                    throw new Exception("No Target Set when requesting Shared Path");
            }
        }
            
        public string condaBin(Platform target)
        {
            switch (target)
            {
                case Platform.Windows_x64:
                    return Path.Combine(condaLibrary(target), "bin"); ;
                case Platform.Mac_x64:
                case Platform.Mac_Arm64:
                case Platform.Linux_x64:
                case Platform.Linux_Arm64:
                    return Path.Combine(CondaDefault(target), "bin");
                default:
                    throw new Exception("No Target Set when requesting Binary Path");
            }
        }

        public CondaApp()
        {
            m_ProcessArch = RuntimeInformation.ProcessArchitecture;
            CondaPath = Path.Combine(Application.dataPath, "Conda");
            PluginPath = Path.Combine(CondaPath, "Plugins");
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                m_PixiUrl = "https://github.com/prefix-dev/pixi/releases/latest/download/pixi-x86_64-pc-windows-msvc.exe";
                m_Platform = Platform.Windows_x64;
                m_PixiApp = Path.Combine(CondaPath, "pixi.exe");
            } else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                if (m_ProcessArch == Architecture.Arm64)
                {
                    m_PixiUrl = "https://github.com/prefix-dev/pixi/releases/latest/download/pixi-aarch64-apple-darwin";
                    m_Platform = Platform.Mac_Arm64;
                    m_PixiApp = Path.Combine(CondaPath, "pixi");
                }
                else
                {
                    m_PixiUrl = "https://github.com/prefix-dev/pixi/releases/latest/download/pixi-x86_64-apple-darwin";
                    m_Platform = Platform.Mac_x64;
                    m_PixiApp = Path.Combine(CondaPath, "pixi");
                }
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) {
                m_PixiUrl = "https://github.com/mamba-org/micromamba-releases/releases/latest/download/micromamba-linux-64";
                m_Platform = Platform.Linux_x64;
                m_PixiApp = Path.Combine(CondaPath, "pixi");
            }

            switch (Environment.GetEnvironmentVariable("CONDA_ARCH_OVERRIDE"))
            {
                case "osx-64":
                    m_Target = Platform.Mac_x64;
                    break;
                case "osx-arm64":
                    m_Target = Platform.Mac_Arm64;
                    break;
                case "linux-64":
                    m_Target = Platform.Linux_x64;
                    break;
                case "linux-aarch64":
                    m_Target = Platform.Linux_Arm64;
                    break;
                case "win-64":
                    m_Target = Platform.Windows_x64;
                    break;
                default:
                    m_Target = m_Platform;
                    break;
            }

            if (!Directory.Exists(CondaPath))
            {
                Directory.CreateDirectory(CondaPath);
            }
            ;
            if (!Directory.Exists(PluginPath))
            {
                Directory.CreateDirectory(PluginPath);
            }
            ;

            if (!File.Exists(m_PixiApp))
            {
                // need to install micromamba which is totally stand-alone
                Debug.Log($"Platform : {m_Platform.ToCondaString()}");
                if (m_Target != m_Platform)
                {
                    Debug.Log($"Target : {m_Target.ToCondaString()}");
                }
                using (WebClient client = new WebClient())
                {
                    Debug.Log($"Downloading {m_PixiUrl}");
                    client.DownloadFile(m_PixiUrl, m_PixiApp);
                    switch (m_Platform)
                    {
                        case Platform.Mac_x64:
                        case Platform.Mac_Arm64:
                            using (Process compiler = new Process())
                            {
                                compiler.StartInfo.FileName = "/bin/bash";
                                compiler.StartInfo.Arguments = $"-c \"chmod 766 {m_PixiApp} && xattr -d \"com.apple.quarantine\" {m_PixiApp} \"";
                                compiler.StartInfo.UseShellExecute = false;
                                compiler.StartInfo.RedirectStandardOutput = true;
                                compiler.StartInfo.RedirectStandardError = true;
                                compiler.StartInfo.CreateNoWindow = true;
                                compiler.StartInfo.WorkingDirectory = CondaPath;
                                compiler.Start();
                                compiler.WaitForExit();
                                if (compiler.ExitCode != 0)
                                    throw new Exception(compiler.StandardError.ReadToEnd());
                            }
                            ;
                            break;
                        case Platform.Linux_x64:
                            using (Process compiler = new Process())
                            {
                                compiler.StartInfo.FileName = "/bin/bash";
                                compiler.StartInfo.Arguments = $"-c \"chmod 766 {m_PixiApp}  \"";
                                compiler.StartInfo.UseShellExecute = false;
                                compiler.StartInfo.RedirectStandardOutput = true;
                                compiler.StartInfo.RedirectStandardError = true;
                                compiler.StartInfo.CreateNoWindow = true;
                                compiler.StartInfo.WorkingDirectory = CondaPath;
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
            }

            //Check if there is a working Pixi Env
            // If not (i.e. there is no pixi.lock file - init 
            if (!File.Exists(Path.Combine(CondaPath, "pixi.toml")))
            {
                //run the Mamba create env command
                using (Process compiler = new Process())
                {
                    switch (m_Platform)
                    {
                        case Platform.Windows_x64:
                            compiler.StartInfo.FileName = "powershell.exe";
                            compiler.StartInfo.Arguments = $"-ExecutionPolicy Bypass {m_PixiApp} init {TARGETS} {CondaPath}";
                            break;
                        default:
                            compiler.StartInfo.FileName = "/bin/bash";
                            compiler.StartInfo.Arguments = $"-c '{m_PixiApp} init {TARGETS} {CondaPath}'";
                            break;
                    }
                    compiler.StartInfo.UseShellExecute = false;
                    compiler.StartInfo.RedirectStandardOutput = true;
                    compiler.StartInfo.RedirectStandardError = true;
                    compiler.StartInfo.CreateNoWindow = true;
                    compiler.StartInfo.WorkingDirectory = CondaPath;
                    compiler.Start();
                    //response = compiler.StandardOutput.ReadToEnd();
                    compiler.WaitForExit();
                    if (compiler.ExitCode != 0)
                        throw new Exception(compiler.StandardError.ReadToEnd());

                    GitIgnoreUpdater( new string[] {
                        "Plugins/*",
                        "Plugins.meta",
                        "pixi",
                        "pixi.meta",
                        "pixi.exe",
                        "pixi.exe.meta"
                    });
                }
            }
            if (File.Exists(Path.Combine(CondaPath, ".config.json")))
            {
                string json = File.ReadAllText(Path.Combine(CondaPath, ".config.json"));
                m_Config = JsonConvert.DeserializeObject<ConfigFile>(json);
            }
            else
            {
                m_Config = new();
            }
        }

        private void SaveConfig()
        {
            try
            {
                string json = JsonConvert.SerializeObject(m_Config, Formatting.Indented);
                File.WriteAllText(Path.Combine(CondaPath, ".config.json"), json);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        public void GitIgnoreUpdater(string[] newEntries)
        {
            string gitignorePath = Path.Combine(CondaPath, ".gitignore");

            // Ensure .gitignore exists
            if (!File.Exists(gitignorePath))
            {
                Debug.Log(".gitignore not found. Creating a new one...");
                File.WriteAllText(gitignorePath, string.Empty);
            }

            // Read existing lines (ignoring blank lines and whitespace)
            var lines = File.ReadAllLines(gitignorePath)
                            .Select(l => l.Trim())
                            .Where(l => !string.IsNullOrWhiteSpace(l))
                            .ToList();

            bool modified = false;

            foreach (string entry in newEntries)
            {
                if (!lines.Contains(entry))
                {
                    lines.Add(entry);
                    modified = true;
                    Console.WriteLine($"Added: {entry}");
                }
                else
                {
                    Console.WriteLine($"Already present: {entry}");
                }
            }

            if (modified)
            {
                File.WriteAllLines(gitignorePath, lines);
                Console.WriteLine("✅ .gitignore updated successfully.");
            }
            else
            {
                Console.WriteLine("No changes made. All entries already exist.");
            }
        }

        public void Add(string install_string, ConfigFile.Package package_details = null)
        {

            Debug.Log($"Starting Package Add for {install_string}");
 
            //Run the Pixi Add process using the package specific install script
            using (Process compiler = new Process())
            {
                switch (m_Platform)
                {
                    case Platform.Windows_x64:
                        compiler.StartInfo.FileName = "powershell.exe";
                        compiler.StartInfo.Arguments = $"-ExecutionPolicy Bypass {m_PixiApp} add --no-install  {TARGETS} {install_string}";
                        break;
                    default:
                        compiler.StartInfo.FileName = "/bin/bash";
                        compiler.StartInfo.Arguments = $" -c \"{m_PixiApp} add --no-install  {TARGETS} {install_string}\" ";
                        break;
                }
                compiler.StartInfo.UseShellExecute = false;
                compiler.StartInfo.RedirectStandardOutput = true;
                compiler.StartInfo.RedirectStandardError = true;
                compiler.StartInfo.CreateNoWindow = true;
                compiler.StartInfo.WorkingDirectory = CondaPath;
                compiler.Start();
                //response = compiler.StandardOutput.ReadToEnd();
                compiler.WaitForExit();
                if (compiler.ExitCode != 0)
                    throw new Exception(compiler.StandardError.ReadToEnd());
            }

            // update the config with the package details

            if (package_details != null)
            {
                if (m_Config.Packages.Contains(package_details))
                {
                    m_Config.Packages[m_Config.Packages.IndexOf(package_details)] = package_details;
                }
                else
                {
                    m_Config.Packages.Add(package_details);
                }
                SaveConfig();
            }
            Install(m_Target);
        }

        public void Install(Platform target)
        { 
            using (Process compiler = new Process())
            {
                switch (m_Platform)
                {
                    case Platform.Windows_x64:
                        compiler.StartInfo.FileName = "powershell.exe";
                        compiler.StartInfo.Arguments = $"-ExecutionPolicy Bypass {m_PixiApp} exec pixi-install-to-prefix --no-activation-scripts --platform {target.ToCondaString()} {CondaDefault(m_Target)}";
                        break;
                    default:
                        compiler.StartInfo.FileName = "/bin/bash";
                        compiler.StartInfo.Arguments = $" -c \"{m_PixiApp} exec pixi-install-to-prefix --no-activation-scripts --platform {target.ToCondaString()} {CondaDefault(m_Target)}\" ";
                        break;
                }
                compiler.StartInfo.UseShellExecute = false;
                compiler.StartInfo.RedirectStandardOutput = true;
                compiler.StartInfo.RedirectStandardError = true;
                compiler.StartInfo.CreateNoWindow = true;
                compiler.StartInfo.WorkingDirectory = CondaPath;
                compiler.Start();
                //compiler.StandardOutput.ReadToEnd();
                compiler.WaitForExit();
                if (compiler.ExitCode != 0)
                    throw new Exception(compiler.StandardError.ReadToEnd());
            }
            TreeShake(target);
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
                    case Platform.Windows_x64:
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
                compiler.StartInfo.WorkingDirectory = CondaPath;
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
            if (!Directory.Exists(PluginPath) || Directory.GetDirectories(PluginPath).Length == 0) return false;
            return Array.Exists( items, item => item.name == name && item.version == packageVersion );
        }

        public void TreeShake( Platform target)
        {
            string sharedAssets = Application.streamingAssetsPath;
            if (!Directory.Exists(sharedAssets)) Directory.CreateDirectory(sharedAssets);
            foreach (ConfigFile.Package package in m_Config.Packages)
            {
                if (package.Cleans != null && package.Cleans.Length > 0)
                {
                    foreach (ConfigFile.Clean item in package.Cleans)
                    {
                        List<Regex> includes = new();
                        List<Regex> excludes = new();
                        foreach (string include in item.includes)
                        {
                            includes.Add(new Regex(include));
                        }
                        foreach (string exclude in item.excludes)
                        {
                            excludes.Add(new Regex(exclude));
                        }
                        string path = "";
                        foreach (string component in item.path)
                        {
                            switch (component)
                            {
                                case "conda_library":
                                    path = condaLibrary(target);
                                    break;
                                case "conda_bin":
                                    path = condaBin(target);
                                    break;
                                case "conda_shared":
                                    path = condaShared(target);
                                    break;
                                default:
                                    path = Path.Combine(path, component);
                                    break;
                            }
                        }
                        if (Directory.Exists(path)){
                            RecurseAndClean(path, excludes.ToArray(), includes.ToArray());
                        } else {
                            Debug.Log($"Attempted to Clean invalid directory {path}");
                        }
                    }
                }
                if (package.Shared_Datas != null && package.Shared_Datas.Length > 0)
                {
                    foreach (string item in package.Shared_Datas)
                    {
                        string dest = Path.Combine(sharedAssets, item);
                        if (!Directory.Exists(dest)) Directory.CreateDirectory(dest);
                        if (Directory.Exists(condaShared(target)) && Directory.Exists(Path.Combine(condaShared(target), item)))
                            foreach (var file in Directory.GetFiles(Path.Combine(condaShared(target), item)))
                            {
                                File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), true);
                            }
                    }
                }
            }
            switch (target)
            {
                case Platform.Windows_x64:
                    RecurseAndClean(CondaDefault(target), new Regex[] {
                        new Regex("^\\..*"),
                        new Regex("^conda-meta$"),
                        new Regex("\\.meta$"),
                        new Regex("^Library$"),
                        new Regex("\\.txt$"),
                    });
                    if (Directory.Exists(condaLibrary(target)))
                        RecurseAndClean(condaLibrary(target), new Regex[]{
                        new Regex("^bin$")
                    });
                    if (Directory.Exists(condaBin(target)))
                        RecurseAndClean(condaBin(target), new Regex[] {
                        new Regex("\\.dll$"),
                        new Regex("\\.meta$"),
                        }, new Regex[] {
                            new Regex("^api-"),
                            new Regex("^vcr"),
                            new Regex("^msvcp"),
                        });
                    break;
                case Platform.Mac_x64:
                case Platform.Mac_Arm64:
                case Platform.Linux_x64:
                case Platform.Linux_Arm64:
                    RecurseAndClean(CondaDefault(target), new Regex[] {
                        new Regex("^\\..*"),
                        new Regex("^conda-meta$"),
                        new Regex("\\.meta$"),
                        new Regex("^bin$"),
                        new Regex("^lib$"),
                    });
                    if (Directory.Exists(condaLibrary(target)))
                    {
                        RecurseAndClean(condaLibrary(target), new Regex[] {
                        new Regex("\\.lib$"),
                        new Regex("\\.dylib$"),
                        new Regex("\\.so$"),
                        new Regex("\\.meta$"),
                        });
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

        [MenuItem("Conda/Settings")]
        static void Settings() 
        {
            SettingsWindow window = (SettingsWindow)EditorWindow.GetWindow(typeof(SettingsWindow));
            window.Show();
        }
    }

    public class ListWindow : EditorWindow
    {
        public CondaList list;
        public Vector2 svposition = Vector2.zero;

        private void OnGUI()
        {
            svposition = EditorGUILayout.BeginScrollView(svposition);
            if (list != null)
            {
                EditorGUILayout.LabelField("Primary Packages :", EditorStyles.largeLabel);
                foreach (CondaItem item in list.Items)
                {
                    if (item.is_explicit || item.name == "libgdal")
                        DrawItemCard(item);
                }
                EditorGUILayout.LabelField("Dependencies :", EditorStyles.largeLabel);
                foreach (CondaItem item in list.Items)
                {
                    if (! item.is_explicit && 
                        ! item.name.Contains("dotnet") &&
                        ! item.name.StartsWith("vc") &&
                        ! item.name.StartsWith("vs"))
                        DrawItemCard(item);
                }
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawItemCard(CondaItem item)
        {
            EditorGUILayout.BeginHorizontal("box", GUILayout.Width(200), GUILayout.Height(20));
            EditorGUILayout.LabelField(item.name, EditorStyles.boldLabel);
            EditorGUILayout.LabelField(item.version);

            EditorGUILayout.EndHorizontal();
        }
    }

    public class SettingsWindow : EditorWindow
    {
        enum PlatformStatus
        {
            NotInstalled,
            Installed
        }
        
        [System.Serializable]
        class PlatformData
        {
            public Platform platform;
            public PlatformStatus status;
        }

        private List<PlatformData> m_Platforms = new List<PlatformData>();
        private Vector2 m_ScrollPos;
        private CondaApp m_Conda;

        private void OnEnable()
        {
            m_Conda = new();
            RenewPlatforms();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Environment Manager", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            m_ScrollPos = EditorGUILayout.BeginScrollView(m_ScrollPos);

            foreach (var platform in m_Platforms)
            {
                DrawEnvironmentCard(platform);

            }

            EditorGUILayout.EndScrollView();
        }

        private void RenewPlatforms() 
        {
            if (!Directory.Exists(m_Conda.PluginPath))
            {
                m_Platforms = new();
                foreach(Platform platform in m_Conda.TARGETLIST)
                {
                    m_Platforms.Add(new PlatformData()
                    {
                        platform = platform,
                        status = PlatformStatus.NotInstalled,
                    });
                }
                return;
            }
            m_Platforms = new();
            string[] dlist =  Directory.GetDirectories(m_Conda.PluginPath);
            foreach (Platform platform in m_Conda.TARGETLIST)
            {
                PlatformStatus status = PlatformStatus.NotInstalled;
                switch (platform)
                {
                    case Platform.Windows_x64:
                        if (Array.Exists(dlist, item => 
                                Path.GetFileName(item) == "Windows" &&
                                Directory.Exists(Path.Combine(item, "x64"))
                            ))
                        {
                            status = PlatformStatus.Installed;
                        }
                        break;
                    case Platform.Mac_x64:
                        if (Array.Exists(dlist, item =>
                                Path.GetFileName(item) == "OSX" &&
                                Directory.Exists(Path.Combine(item, "x64"))
                            ))
                        {
                            status = PlatformStatus.Installed;
                        }
                        break;
                    case Platform.Linux_x64:
                        if (Array.Exists(dlist, item =>
                                Path.GetFileName(item) == "Linux" &&
                                Directory.Exists(Path.Combine(item, "x64"))
                            ))
                        {
                            status = PlatformStatus.Installed;
                        }
                        break;
                    case Platform.Mac_Arm64:
                        if (Array.Exists(dlist, item =>
                                Path.GetFileName(item) == "OSX" &&
                                Directory.Exists(Path.Combine(item, "arm64"))
                            ))
                        {
                            status = PlatformStatus.Installed;
                        }
                        break;
                }
                m_Platforms.Add(new PlatformData()
                {
                    platform = platform,
                    status = status
                });
            }
            return;
        }

        private void DrawEnvironmentCard(PlatformData platform)
        {
            EditorGUILayout.BeginHorizontal("box", GUILayout.Width(200), GUILayout.Height(30));

            EditorGUILayout.LabelField(platform.platform.ToString(), EditorStyles.boldLabel);
            EditorGUILayout.LabelField(platform.status == PlatformStatus.Installed ? "Installed" : "Not Installed");



            if (GUILayout.Button("Install"))
            {
                m_Conda.Install(platform.platform);
            }
            if (GUILayout.Button("Update"))
            {
                m_Conda.Install(platform.platform);
            }
            if (GUILayout.Button("Uninstall"))
            {
                switch (platform.platform) {
                    case Platform.Windows_x64:
                        if (Directory.Exists(Path.Combine(m_Conda.PluginPath, "Windows"))){
                            Directory.Delete(Path.Combine(m_Conda.PluginPath, "Windows"), true);
                        }
                        break;
                    case Platform.Mac_x64:
                        if (Directory.Exists(Path.Combine(m_Conda.PluginPath, "OSX"))){
                            Directory.Delete(Path.Combine(m_Conda.PluginPath, "Windows"), true);
                        }
                        break;
                    case Platform.Linux_x64:
                        if (Directory.Exists(Path.Combine(m_Conda.PluginPath, "Linux"))){
                            Directory.Delete(Path.Combine(m_Conda.PluginPath, "Linux"), true);
                        }
                        break;
                    case Platform.Mac_Arm64:
                        if (Directory.Exists(Path.Combine(m_Conda.PluginPath, "OSX")))
                        {
                            Directory.Delete(Path.Combine(m_Conda.PluginPath, "Windows"), true);
                        }
                        break;
                }
            }
            EditorGUILayout.EndHorizontal();
        }
    }
}
#endif
