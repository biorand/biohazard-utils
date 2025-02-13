using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace emdui
{
    internal class BlenderSupport : IDisposable
    {
        public string TempPath { get; private set; }
        public string BlendPath { get; private set; }
        public string ImportedObjectPath { get; private set; }
        public string ExportedObjectPath { get; private set; }

        public BlenderSupport()
        {
            Setup();
        }

        public void Dispose()
        {
        }

        private void Setup()
        {
            TempPath = Path.Combine(Path.GetTempPath(), "emdui", RandomHash(5));
            Directory.CreateDirectory(TempPath);
            ImportedObjectPath = Path.Combine(TempPath, "import.obj");
            BlendPath = Path.Combine(TempPath, "model.blend");
            ExportedObjectPath = Path.Combine(TempPath, "export.obj");
            var importScriptPath = Path.Combine(TempPath, "import.py");
            var exportScriptPath = Path.Combine(TempPath, "export.py");

            var importScript = $@"
import bpy

bpy.context.preferences.view.show_splash = False

context = bpy.context
scene = context.scene
for c in scene.collection.children:
    scene.collection.children.unlink(c)

bpy.ops.wm.obj_import(up_axis='NEGATIVE_Y', filepath=""{EscapePath(ImportedObjectPath)}"")
bpy.ops.wm.save_as_mainfile(filepath=""{EscapePath(BlendPath)}"")
";
            File.WriteAllText(importScriptPath, importScript);

            var exportScript = $@"
import bpy

bpy.context.preferences.view.show_splash = False
bpy.ops.wm.obj_export(up_axis='NEGATIVE_Y', filepath=""{EscapePath(ExportedObjectPath)}"")
bpy.ops.wm.quit_blender()
";
            File.WriteAllText(exportScriptPath, exportScript);
        }

        public bool EditInBlender()
        {
            if (!StartBlender(TempPath, null, "import.py"))
                return false;

            return StartBlender(TempPath, BlendPath, "export.py");
        }

        private bool StartBlender(string cwd, string blendFile, string scriptPath)
        {
            var blenderPath = FindBlenderExe();
            if (blenderPath == null)
                return false;

            var psi = new ProcessStartInfo(blenderPath, $"{blendFile} -P {scriptPath}")
            {
                WorkingDirectory = cwd
            };
            var p = Process.Start(psi);
            var cts = new CancellationTokenSource();
            Task.Run(() =>
            {
                p.WaitForExit();
                cts.Cancel();
            });
            return WaitWindow.Wait("Waiting for Blender", "Waiting for Blender to close...", cts.Token);
        }

        private string FindBlenderExe()
        {
            var blenderExe = Settings.Default.BlenderPath;
            if (string.IsNullOrEmpty(blenderExe))
            {
                blenderExe = AutoFindBlender();
                if (blenderExe == null)
                {
                    var openFileDialog = new OpenFileDialog();
                    openFileDialog.Filter = "Executable Files (*.exe)|*.exe";
                    if (openFileDialog.ShowDialog() != true)
                        return null;

                    blenderExe = openFileDialog.FileName;
                }

                Settings.Default.BlenderPath = blenderExe;
                Settings.Save();
            }
            return blenderExe;
        }

        private string AutoFindBlender()
        {
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var blenderRoot = Path.Combine(programFiles, "Blender Foundation");
            if (!Directory.Exists(blenderRoot))
                return null;

            var blenderPath = Directory
                .GetDirectories(blenderRoot)
                .OrderByDescending(x => x)
                .FirstOrDefault();
            if (blenderPath == null)
                return null;

            var blenderExe = Path.Combine(blenderPath, "blender.exe");
            if (!File.Exists(blenderExe))
                return null;

            return blenderExe;
        }

        private static string RandomHash(int length)
        {
            var random = new Random();
            var result = new char[length];
            for (int i = 0; i < length; i++)
            {
                var value = random.Next(0, 16);
                if (value >= 10)
                    result[i] = (char)('A' + value);
                else
                    result[i] = (char)('0' + value);
            }
            return new string(result);
        }

        private static string EscapePath(string path) => path.Replace("\\", "\\\\");
    }
}
