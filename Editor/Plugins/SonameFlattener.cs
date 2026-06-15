using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Conda
{

    public static class SonameFlattener
    {
        // Matches:
        //   libgdal.so.39
        //   libgdal.so.39.4
        //   libgdal.so.39.4.3
        private static readonly Regex SonameRegex =
            new Regex(
                @"^(?<base>.+\.so)\.(?<version>\d+(?:\.\d+)*)$",
                RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static void CreateUnversionedSoFiles(string rootDirectory)
        {
            var bestMatches = new Dictionary<string, Candidate>();

            foreach (var file in Directory.EnumerateFiles(
                         rootDirectory,
                         "*.so*",
                         SearchOption.AllDirectories))
            {
                string fileName = Path.GetFileName(file);

                Match match = SonameRegex.Match(fileName);

                if (!match.Success)
                    continue;

                string baseName = match.Groups["base"].Value;
                string version = match.Groups["version"].Value;

                int componentCount = version.Split('.').Length;

                string outputPath = Path.Combine(
                    Path.GetDirectoryName(file)!,
                    baseName);

                var candidate = new Candidate
                {
                    SourceFile = file,
                    OutputFile = outputPath,
                    ComponentCount = componentCount
                };

                if (!bestMatches.TryGetValue(outputPath, out var existing) ||
                    candidate.ComponentCount > existing.ComponentCount)
                {
                    bestMatches[outputPath] = candidate;
                }
            }

            foreach (var candidate in bestMatches.Values)
            {
                Console.WriteLine(
                    $"Copying {candidate.SourceFile} -> {candidate.OutputFile}");

                if (File.Exists(candidate.OutputFile))
                {
                    File.Delete(candidate.OutputFile);
                }

                File.Copy(candidate.SourceFile, candidate.OutputFile);
            }
        }

        private sealed class Candidate
        {
            public string SourceFile;
            public string OutputFile;
            public int ComponentCount;
        }
    }
}