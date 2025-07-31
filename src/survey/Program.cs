using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;
using survey;

namespace IntelOrca.Biohazard.Survey
{
    public static class Program
    {
        [DllImport("kernel32.dll")]
        private extern static bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, IntPtr lpBuffer, IntPtr nSize, out IntPtr lpNumberOfBytesRead);

        private static string _jsonPath = @"M:\git\rer\IntelOrca.Biohazard.BioRand\data\recv\enemy.json";

        private static bool _exit;
        private static byte[] _buffer = new byte[64];
        private static List<EnemyPosition> _enemyPositions = new List<EnemyPosition>();
        private static List<string> _log = new List<string>();

        private static void LoadJSON()
        {
            var json = File.ReadAllText(_jsonPath);
            _enemyPositions = JsonSerializer.Deserialize<List<EnemyPosition>>(json, new JsonSerializerOptions()
            {
                ReadCommentHandling = JsonCommentHandling.Skip,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
        }

        private static void SaveJSON()
        {
            var sb = new StringBuilder();
            sb.Append("[\n");
            var positions = _enemyPositions
                .OrderBy(x => x.Room)
                .ThenByDescending(x => x.Y)
                .ThenBy(x => x.X)
                .ThenBy(x => x.Z)
                .ToArray();
            if (positions.Length > 0)
            {
                foreach (var pos in positions)
                {
                    sb.Append($"    {{ \"room\": \"{pos.Room}\", \"x\": {pos.X}, \"y\": {pos.Y}, \"z\": {pos.Z}, \"d\": {pos.D}, \"f\": {pos.F} }},\n");
                }
                sb.Remove(sb.Length - 2, 2);
                sb.Append('\n');
            }
            sb.Append("]\n");
            File.WriteAllText(_jsonPath, sb.ToString());
        }

        public static void Main(string[] args)
        {
            var manifests = GetManifests();
            GameState2? gameState = null;

            var lastGameCheck = DateTime.MinValue;
            var gameCheckInterval = TimeSpan.FromSeconds(2.5);

            Console.CancelKeyPress += Console_CancelKeyPress;
            Console.WriteLine("Looking for game...");
            do
            {
                var now = DateTime.UtcNow;
                if (now >= lastGameCheck + gameCheckInterval)
                {
                    lastGameCheck = now;
                    gameState = GetGameState(manifests);
                }
                else
                {
                    Thread.Sleep(100);
                }
            }

            while (gameState == null || _exit);
            if (_exit)
                return;

            Console.WriteLine("Game found");
            while (!_exit && !gameState.Process.HasExited)
            {
                if (gameState.Refresh())
                {
                    WriteXml(gameState);
                }
                Thread.Sleep(500);
                ReadXml(gameState);
            }
        }

        private static ImmutableArray<GameStateManifest> GetManifests()
        {
            var result = new List<GameStateManifest>();
            var assembly = Assembly.GetExecutingAssembly();
            var resourceNames = assembly.GetManifestResourceNames();
            foreach (var resourceName in resourceNames)
            {
                if (resourceName.EndsWith(".json"))
                {
                    using var stream = assembly.GetManifestResourceStream(resourceName);
                    if (stream != null)
                    {
                        var reader = new StreamReader(stream);
                        var json = reader.ReadToEnd();
                        result.Add(GameStateManifest.FromJson(json));
                    }
                }
            }
            return [.. result];
        }

        private static GameState2? GetGameState(ImmutableArray<GameStateManifest> manifests)
        {
            var pAll = Process.GetProcesses();
            foreach (var process in pAll)
            {
                var processName = process.ProcessName;
                foreach (var manifest in manifests)
                {
                    var pattern = new Regex(manifest.Process.Name);
                    if (pattern.IsMatch(processName))
                    {
                        return new GameState2(manifest, process);
                    }
                }
            }
            return null;
        }

        private static void WriteXml(GameState2 state)
        {
            var dir = new DirectoryInfo("state");
            if (!dir.Exists)
                dir.Create();

            var targetPath = Path.Combine(dir.FullName, "state.xml");
            using (var writer = XmlWriter.Create(targetPath, new XmlWriterSettings { Indent = true }))
            {
                writer.WriteStartDocument();
                writer.WriteStartElement("state");

                writer.WriteStartElement("variables");
                foreach (var variable in state.Manifest.Variables)
                {
                    writer.WriteStartElement("variable");
                    writer.WriteAttributeString("name", variable.Name);

                    var value = state.GetValue(variable.Name);
                    if (value != null)
                    {
                        writer.WriteValue(value.ToString() ?? string.Empty);
                    }

                    writer.WriteEndElement();
                }
                writer.WriteEndElement();

                writer.WriteStartElement("flags");
                foreach (var group in state.Manifest.FlagGroups)
                {
                    writer.WriteStartElement("group");
                    writer.WriteAttributeString("id", group.Id.ToString());
                    writer.WriteAttributeString("name", group.Name);
                    for (int i = 0; i < group.Count; i++)
                    {
                        var flagValue = state.GetValue($"flags.{group.Name}[{i}]");
                        var flagKey = $"{group.Id}:{i}";
                        state.Manifest.FlagNames.TryGetValue(flagKey, out var flagName);

                        writer.WriteStartElement("flag");
                        writer.WriteAttributeString("id", i.ToString());
                        if (flagName != null)
                            writer.WriteAttributeString("name", flagName);
                        writer.WriteValue(flagValue?.ToString() ?? string.Empty);
                        writer.WriteEndElement();
                    }
                    writer.WriteEndElement();
                }
                writer.WriteEndElement();

                writer.WriteEndElement();
                writer.WriteEndDocument();
            }

            _lastXmlModified = File.GetLastWriteTimeUtc(targetPath);
        }

        private static DateTime _lastXmlModified = DateTime.MinValue;
        private static void ReadXml(GameState2 state)
        {
            var path = "state/state.xml";
            var lastModified = File.GetLastWriteTimeUtc(path);
            if (lastModified <= _lastXmlModified)
                return;

            _lastXmlModified = lastModified;

            var xml = GetFileContentsRetry(path);
            var dict = new Dictionary<string, object>();
            var doc = new XmlDocument();
            doc.LoadXml(xml);
            foreach (XmlNode variableNode in doc.SelectNodes("state/variables/variable")!)
            {
                var name = variableNode.Attributes?["name"]?.Value ?? null;
                if (name == null)
                    continue;

                if (!int.TryParse(variableNode.InnerText, out var value))
                    continue;

                dict[name] = value;
            }
            foreach (XmlNode flagGroupNode in doc.SelectNodes("state/flags/group")!)
            {
                var groupName = flagGroupNode.Attributes?["name"]?.Value ?? null;
                if (groupName == null)
                    continue;

                foreach (XmlNode flagNode in flagGroupNode.SelectNodes("flag")!)
                {
                    var flagId = flagNode.Attributes?["id"]?.Value ?? null;
                    if (flagId == null || !int.TryParse(flagId, out var flagIdInt))
                        continue;

                    if (!int.TryParse(flagNode.InnerText, out var value))
                        continue;

                    dict[$"flags.{groupName}[{flagIdInt}]"] = value;
                }
            }

            state.Update(dict);
        }

        private static string GetFileContentsRetry(string path)
        {
            for (var i = 0; ; i++)
            {
                try
                {
                    return File.ReadAllText(path);
                }
                catch (Exception)
                {
                    if (i >= 5)
                    {
                        throw;
                    }
                    Thread.Sleep(100);
                }
            }
        }

        public static void MainOld(string[] args)
        {
            LoadJSON();

            Console.CancelKeyPress += Console_CancelKeyPress;
            while (!_exit)
            {
                var pAll = Process.GetProcesses();
                var p = pAll.FirstOrDefault(x => x.ProcessName.StartsWith("Bio"));
                if (p != null)
                {
                    Spy(p, 0x0500, 500);
                }
                p = pAll.FirstOrDefault(x => x.ProcessName.StartsWith("bio2"));
                if (p != null)
                {
                    Spy(p, 0x2300, 500);
                }
                p = pAll.FirstOrDefault(x => x.ProcessName.StartsWith("BIOHAZARD(R) 3"));
                if (p != null)
                {
                    Spy(p, 0x2300, 500);
                }
                p = pAll.FirstOrDefault(x => x.ProcessName.StartsWith("pcsx2"));
                if (p != null)
                {
                    Spy(p, 0x0080, 5);
                }

                Console.WriteLine("Waiting for RE 1, RE 2, RE 3 or RE CV to start...");
                Thread.Sleep(4000);
            }
        }

        private static void PrintSimilarEntries()
        {
            for (int i = 0; i < _enemyPositions.Count; i++)
            {
                var a = _enemyPositions[i];
                var closestOtherEnemyDistance = int.MaxValue;
                for (int j = 0; j < _enemyPositions.Count; j++)
                {
                    if (i == j)
                        continue;

                    var b = _enemyPositions[j];
                    var dist = a.DistanceTo(b);
                    if (dist == int.MaxValue)
                        continue;

                    if (dist < closestOtherEnemyDistance)
                        closestOtherEnemyDistance = dist;
                }
                if (closestOtherEnemyDistance == int.MaxValue)
                    continue;
                Console.WriteLine("{1,12} {0}", a, closestOtherEnemyDistance);
            }
        }

        private static void Console_CancelKeyPress(object? sender, ConsoleCancelEventArgs e)
        {
            _exit = true;
        }

        private static void Spy(Process p, int expectedKey, int distance)
        {
            var lastGameState = new GameState();
            var gameState = new GameState();
            while (!p.HasExited)
            {
                GetGameState(p, gameState);
                if (!gameState.Equals(lastGameState))
                {
                    Console.CursorTop = 0;
                    Console.WriteLine($"Key: {gameState.Key,6:X4}");
                    Console.WriteLine($"Room:   {gameState.Stage + 1:X}{gameState.Room:X2}{gameState.Variant}");
                    Console.WriteLine($"Cut: {gameState.Cut,6}");
                    Console.WriteLine($"X: {gameState.X,8}");
                    Console.WriteLine($"Y: {gameState.Y,8}");
                    Console.WriteLine($"Z: {gameState.Z,8}");
                    Console.WriteLine($"D: {gameState.D,8}");
                    Console.WriteLine($"F: {gameState.Floor,8}");

                    if (gameState.Key == expectedKey)
                    {
                        AddEnemyPosition(gameState, distance);
                        // RemoveEnemyPositions(gameState, 2000);
                    }

                    Console.WriteLine("--------------------");
                    for (int i = 0; i < 10; i++)
                    {
                        var index = _log.Count - i - 1;
                        if (index < 0)
                            break;

                        Console.WriteLine("{0,64}", _log[index]);
                    }
                }
                Thread.Sleep(10);

                (gameState, lastGameState) = (lastGameState, gameState);
            }
        }

        private static void AddEnemyPosition(GameState state, int distance)
        {
            var pos = new EnemyPosition()
            {
                Room = state.RtdId,
                X = state.X,
                Y = state.Y,
                Z = state.Z,
                D = state.D,
                F = state.Floor,
            };

            var closeBy = _enemyPositions
                .Where(x => x.IsVeryClose(pos, distance))
                .ToArray();
            if (closeBy.Length != 0)
                return;

            if (!_enemyPositions.Contains(pos))
            {
                _enemyPositions.Add(pos);
                _log.Add(string.Format("[ADD] {0}, {1}, {2}, {3}, {4}                         ", pos.Room, pos.X, pos.Y, pos.Z, pos.D));
                SaveJSON();
            }
        }

        private static EnemyPosition[] RemoveEnemyPositions(GameState state, int radius)
        {
            var result = new List<EnemyPosition>();
            for (int i = 0; i < _enemyPositions.Count; i++)
            {
                var enemy = _enemyPositions[i];
                if (enemy.DistanceTo(state) < radius)
                {
                    var pos = enemy;
                    _log.Add(string.Format("[DEL] {0}, {1}, {2}, {3}, {4}                         ", pos.Room, pos.X, pos.Y, pos.Z, pos.D));
                    result.Add(enemy);
                    _enemyPositions.RemoveAt(i);
                    i--;
                }
            }
            SaveJSON();
            return result.ToArray();
        }

        private static void GetGameState(Process p, GameState gameState)
        {
            var buffer = _buffer;

            if (p.ProcessName.StartsWith("Bio"))
            {
                ReadMemory(p, 0x00C38710, buffer, 0, 2);
                gameState.Key = BitConverter.ToUInt16(buffer, 0);

                ReadMemory(p, 0x00C351E8, buffer, 0, 12);
                gameState.X = BitConverter.ToInt16(buffer, 0);
                gameState.Y = BitConverter.ToInt16(buffer, 4);
                gameState.Z = BitConverter.ToInt16(buffer, 8);

                ReadMemory(p, 0x00C35228, buffer, 0, 2);
                gameState.D = BitConverter.ToInt16(buffer, 0);

                gameState.Floor = 0;

                ReadMemory(p, 0x00C386F0, buffer, 0, 3);
                gameState.Stage = buffer[0];
                gameState.Room = buffer[1];
                gameState.Cut = buffer[2];
            }
            else if (p.ProcessName.StartsWith("bio2"))
            {
                ReadMemory(p, 0x00988604, buffer, 0, 2);
                gameState.Key = BitConverter.ToUInt16(buffer, 0);

                ReadMemory(p, 0x0098890C, buffer, 0, 10);
                gameState.X = BitConverter.ToInt16(buffer, 0);
                gameState.Y = BitConverter.ToInt16(buffer, 2);
                gameState.Z = BitConverter.ToInt16(buffer, 4);
                gameState.D = BitConverter.ToInt16(buffer, 8);

                ReadMemory(p, 0x00989FF6, buffer, 0, 1);
                gameState.Floor = buffer[0];

                ReadMemory(p, 0x0098EB14, buffer, 0, 10);
                gameState.Stage = buffer[0];
                gameState.Room = buffer[2];
                gameState.Cut = buffer[4];
                gameState.LastCut = buffer[6];
            }
            else if (p.ProcessName.StartsWith("BIOHAZARD(R) 3"))
            {
                ReadMemory(p, 0x00A61C84, buffer, 0, 2);
                gameState.Key = BitConverter.ToUInt16(buffer, 0);

                ReadMemory(p, 0x00A62494, buffer, 0, 10);
                gameState.X = BitConverter.ToInt16(buffer, 0);
                gameState.Y = BitConverter.ToInt16(buffer, 2);
                gameState.Z = BitConverter.ToInt16(buffer, 4);
                gameState.D = BitConverter.ToInt16(buffer, 8);

                ReadMemory(p, 0x00A6201D, buffer, 0, 1);
                gameState.Floor = buffer[0];

                ReadMemory(p, 0x00A673C6, buffer, 0, 10);
                gameState.Stage = buffer[0];
                gameState.Room = buffer[2];
                gameState.Cut = buffer[4];
                gameState.LastCut = buffer[6];
            }
            else
            {
                ReadMemory(p, 0x2044E1B0, buffer, 0, 4);
                gameState.Key = (ushort)BitConverter.ToUInt32(buffer, 0);

                ReadMemory(p, 0x204F6EE8, buffer, 0, 12);
                gameState.X = (short)BitConverter.ToSingle(buffer, 0);
                gameState.Y = (short)BitConverter.ToSingle(buffer, 4);
                gameState.Z = (short)BitConverter.ToSingle(buffer, 8);

                ReadMemory(p, 0x204322FC, buffer, 0, 12);
                gameState.D = (short)(BitConverter.ToInt32(buffer, 4) & 0xFFFF);

                gameState.Floor = 0;

                ReadMemory(p, 0x204339B4, buffer, 0, 3);
                gameState.Stage = buffer[0];
                gameState.Room = buffer[1];
                gameState.Variant = buffer[3];
            }
        }

        private unsafe static bool ReadMemory(Process process, int address, byte[] buffer, int offset, int length)
        {
            IntPtr outLength;
            fixed (byte* bufferP = buffer)
            {
                var dst = bufferP + offset;
                return ReadProcessMemory(process.Handle, (IntPtr)address, (IntPtr)dst, (IntPtr)length, out outLength);
            }
        }

        public class GameState : IEquatable<GameState>
        {
            public ushort Key { get; set; }
            public byte Stage { get; set; }
            public byte Room { get; set; }
            public byte? Variant { get; set; }
            public byte Cut { get; set; }
            public byte LastCut { get; set; }
            public short X { get; set; }
            public short Y { get; set; }
            public short Z { get; set; }
            public short D { get; set; }
            public byte Floor { get; set; }

            public string RtdId => $"{Stage + 1:X}{Room:X2}{Variant}";

            public override bool Equals(object obj)
            {
                return Equals(obj as GameState);
            }

            public bool Equals(GameState other)
            {
                return other is GameState state &&
                       Key == state.Key &&
                       Stage == state.Stage &&
                       Room == state.Room &&
                       Cut == state.Cut &&
                       LastCut == state.LastCut &&
                       X == state.X &&
                       Y == state.Y &&
                       Z == state.Z &&
                       D == state.D &&
                       Floor == state.Floor;
            }

            public override int GetHashCode()
            {
                int hashCode = 1576073333;
                hashCode = hashCode * -1521134295 + Key.GetHashCode();
                hashCode = hashCode * -1521134295 + Stage.GetHashCode();
                hashCode = hashCode * -1521134295 + Room.GetHashCode();
                hashCode = hashCode * -1521134295 + Cut.GetHashCode();
                hashCode = hashCode * -1521134295 + LastCut.GetHashCode();
                hashCode = hashCode * -1521134295 + X.GetHashCode();
                hashCode = hashCode * -1521134295 + Y.GetHashCode();
                hashCode = hashCode * -1521134295 + Z.GetHashCode();
                hashCode = hashCode * -1521134295 + D.GetHashCode();
                hashCode = hashCode * -1521134295 + Floor.GetHashCode();
                hashCode = hashCode * -1521134295 + RtdId.GetHashCode();
                return hashCode;
            }

            public static bool operator ==(GameState left, GameState right)
            {
                return EqualityComparer<GameState>.Default.Equals(left, right);
            }

            public static bool operator !=(GameState left, GameState right)
            {
                return !(left == right);
            }
        }

        public struct EnemyPosition : IEquatable<EnemyPosition>
        {
            public string Room { get; set; }
            public int X { get; set; }
            public int Y { get; set; }
            public int Z { get; set; }
            public int D { get; set; }
            public int F { get; set; }

            public override bool Equals(object obj)
            {
                return obj is EnemyPosition pos ? Equals(pos) : false;
            }

            public bool Equals(EnemyPosition other)
            {
                return other is EnemyPosition position &&
                       Room == position.Room &&
                       X == position.X &&
                       Y == position.Y &&
                       Z == position.Z &&
                       D == position.D &&
                       F == position.F;
            }

            public override int GetHashCode()
            {
                int hashCode = 1967392198;
                hashCode = hashCode * -1521134295 + EqualityComparer<string>.Default.GetHashCode(Room);
                hashCode = hashCode * -1521134295 + X.GetHashCode();
                hashCode = hashCode * -1521134295 + Y.GetHashCode();
                hashCode = hashCode * -1521134295 + Z.GetHashCode();
                hashCode = hashCode * -1521134295 + D.GetHashCode();
                hashCode = hashCode * -1521134295 + F.GetHashCode();
                return hashCode;
            }

            public int DistanceTo(string room, int x, int y, int z)
            {
                if (Room != room)
                    return int.MaxValue;
                if (Y != y)
                    return int.MaxValue;

                var deltaX = (long)Math.Abs(X - x);
                var deltaZ = (long)Math.Abs(Z - z);
                var dist = Math.Sqrt(deltaX * deltaX + deltaZ * deltaZ);
                if (double.IsNaN(dist))
                    throw new Exception();
                return (int)dist;
            }

            public int DistanceTo(GameState other) => DistanceTo(other.RtdId, other.X, other.Y, other.Z);
            public int DistanceTo(EnemyPosition other) => DistanceTo(other.Room, other.X, other.Y, other.Z);
            public bool IsVeryClose(EnemyPosition other, int distance) => DistanceTo(other) <= distance;

            public override string ToString()
            {
                return $"{Room}: {X}, {Y}, {Z}, {D}, {F}";
            }

            public static bool operator ==(EnemyPosition left, EnemyPosition right)
            {
                return left.Equals(right);
            }

            public static bool operator !=(EnemyPosition left, EnemyPosition right)
            {
                return !(left == right);
            }
        }
    }
}
