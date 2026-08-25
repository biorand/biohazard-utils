using System;
using System.Collections.Generic;
using System.IO;
using IntelOrca.Biohazard.Room;

namespace IntelOrca.Biohazard.Tests
{
    internal static class TestInfo
    {
        public static string GetInstallPath(int game)
        {
            var fileName = $"re{game + 1}";
            if (game == 3)
                fileName = "recv";
            for (var letter = 'A'; letter <= 'Z'; letter++)
            {
                var places = new[]
                {
                    $@"{letter}:\games\{fileName}",
                    $"/mnt/{char.ToLowerInvariant(letter)}/games/{fileName}",
                };

                foreach (var place in places)
                {
                    if (Directory.Exists(place))
                    {
                        return place;
                    }
                }
            }
            throw new Exception("Unable to find RE.");
        }

        public static IRdt GetRdt(BioVersion version, string fileName)
        {
            switch (version)
            {
                case BioVersion.Biohazard1:
                {
                    var installPath = TestInfo.GetInstallPath(0);
                    var stage = int.Parse(fileName.Substring(4, 1));
                    var rdtPath = Path.Combine(installPath, "JPN", $"STAGE{stage}", fileName);
                    return new Rdt1(rdtPath);
                }
                case BioVersion.Biohazard2:
                {
                    var installPath = TestInfo.GetInstallPath(1);
                    var player = int.Parse(fileName.Substring(7, 1));
                    var rdtPath = Path.Combine(installPath, "data", $"pl{player}", "rdt", fileName);
                    return new Rdt2(BioVersion.Biohazard2, rdtPath);
                }
                case BioVersion.Biohazard3:
                {
                    var installPath = TestInfo.GetInstallPath(2);
                    var rofsFiles = Directory.GetFiles(installPath, "rofs*.dat");
                    var repo = new FileRepository(installPath);
                    foreach (var file in rofsFiles)
                        repo.AddRE3Archive(file);

                    var rdtPath = Path.Combine(installPath, "data_j", "rdt", fileName);
                    var rdtBytes = repo.GetBytes(rdtPath);
                    return new Rdt2(BioVersion.Biohazard3, rdtBytes);
                }
                default:
                    throw new NotImplementedException();
            }
        }

        public static string[] GetAllRdtFileNames(BioVersion version)
        {
            var results = new List<string>();
            switch (version)
            {
                case BioVersion.Biohazard1:
                {
                    var installPath = TestInfo.GetInstallPath(0);
                    for (var stage = 1; stage <= 7; stage++)
                    {
                        var stageDirectory = Path.Combine(installPath, "JPN", $"STAGE{stage}");
                        var files = Directory.GetFiles(stageDirectory, "*.RDT");
                        foreach (var rdtPath in files)
                        {
                            var length = new FileInfo(rdtPath).Length;
                            if (length <= 5000)
                                continue;

                            var fileName = Path.GetFileName(rdtPath);
                            results.Add(fileName);
                        }
                    }
                    break;
                }
                case BioVersion.Biohazard2:
                {
                    var installPath = TestInfo.GetInstallPath(1);
                    for (var player = 0; player <= 1; player++)
                    {
                        var rdtDirectory = Path.Combine(installPath, "data", $"pl{player}", "rdt");
                        var files = Directory.GetFiles(rdtDirectory, "*.RDT");
                        foreach (var rdtPath in files)
                        {
                            var length = new FileInfo(rdtPath).Length;
                            if (length <= 5000)
                                continue;

                            var fileName = Path.GetFileName(rdtPath);
                            results.Add(fileName);
                        }
                    }
                    break;
                }
                case BioVersion.Biohazard3:
                {
                    var installPath = TestInfo.GetInstallPath(2);
                    var rofsFiles = Directory.GetFiles(installPath, "rofs*.dat");
                    var repo = new FileRepository(installPath);
                    foreach (var file in rofsFiles)
                        repo.AddRE3Archive(file);

                    var rdtDirectory = Path.Combine(installPath, "data_j", "rdt");
                    var files = repo.GetFiles(rdtDirectory);
                    foreach (var rdtPath in files)
                    {
                        // GetRdt reconstructs the full archive path from the file
                        // name; archive listings use '\' separators regardless of
                        // platform so split on both
                        var fileName = rdtPath.Split('/', '\\')[^1];
                        results.Add(fileName);
                    }
                    break;
                }
                default:
                    throw new NotImplementedException();
            }
            return results.ToArray();
        }
    }
}
