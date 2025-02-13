using System;
using System.Windows;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using emdui.Extensions;
using IntelOrca.Biohazard;
using IntelOrca.Biohazard.Extensions;
using IntelOrca.Biohazard.Model;
using Microsoft.Win32;

namespace emdui
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public static MainWindow Instance { get; private set; }

        private Project _project;

        private ModelFile _activeModelFile;
        private IModelMesh _mesh;
        private TimFile _tim;
        private Emr _baseEmr;
        private Emr _emr;
        private IEdd _edd;

        private ModelScene _scene;
        private DispatcherTimer _timer;

        private AnimationController _animationController;
        private bool _animationPlaying;
        private int _animationIndex;
        private double _time;
        private int _selectedPartIndex;
        private int _isolatedPartIndex;

        public Project Project => _project;

        public MainWindow()
        {
            InitializeComponent();
            // viewport0.SetCameraOrthographic(new Vector3D(-1, 0, 0));
            viewport1.SetCameraOrthographic(new Vector3D(0, 0, 1));
            _timer = new DispatcherTimer(DispatcherPriority.Render);
            _timer.Interval = TimeSpan.FromMilliseconds(40);
            _timer.Tick += _timer_Tick;
            _timer.IsEnabled = true;

            projectTreeView.MainWindow = this;
            Instance = this;

            _animationController = new AnimationController(this);
            animationTimeline.Controller = _animationController;
        }

        private void _timer_Tick(object sender, EventArgs e)
        {
            var edd = _edd;
            if (edd != null)
            {
                var duration = edd.GetAnimationDuration(_animationIndex);
                if (duration > 0)
                {
                    var oldTime = _time;
                    if (_animationPlaying)
                    {
                        _time += 1;
                    }
                    while (_time >= duration)
                    {
                        _time -= duration;
                    }
                    if (oldTime != _time)
                    {
                        _animationController.InvokeTimeChanged();
                    }
                }
            }
            RefreshKeyframe();
        }

        private void RefreshKeyframe()
        {
            if (_mesh == null || _animationIndex == -1)
                return;

            var edd = _edd;
            if (edd == null)
                return;

            var emrKeyframeIndex = 0;
            if (edd.GetAnimationDuration(_animationIndex) > 0)
            {
                emrKeyframeIndex = edd.GetFrameIndex(_animationIndex, (int)_time);
            }
            _scene.SetKeyframe(emrKeyframeIndex);
        }

        private void SetTimFile(TimFile timFile)
        {
            _tim = timFile;
            _project.MainTexture = timFile;
            RefreshTimImage();
        }

        private void LoadProject(string path)
        {
            try
            {
                _project = Project.FromFile(path);
            }
            catch (Exception)
            {
                MessageBox.Show("Invalid or corrupt file.", "Failed to open file.", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            projectTreeView.Project = _project;

            if (_project.MainModel is ModelFile model)
            {
                for (var i = 0; i < model.NumChunks; i++)
                {
                    if (model.GetChunkKind(i) == ModelFile.ChunkKind.Mesh)
                    {
                        var mesh = model.GetChunk<IModelMesh>(i);
                        if (mesh != null)
                        {
                            LoadMesh(mesh);
                        }
                    }
                }
            }
        }

        private void SaveModel(string path)
        {
            _project.Save(path);
        }

        private void RefreshTimImage()
        {
            timImage.Tim = null;
            timImage.Tim = _tim;
        }

        private void RefreshModelView()
        {
#if false
            var emrBuilder = _emr.ToBuilder();
            var morph = _project.MainModel.GetChunk<MorphData>(0);
            for (var i = 0; i < morph.NumParts; i++)
            {
                var partPosition = morph.GetPartPositionData(i);
                for (var j = 0; j < partPosition.Length; j++)
                {
                    emrBuilder.RelativePositions[j] = partPosition[j];
                }
                break;
            }
            _emr = emrBuilder.ToEmr();

            var md1Builder = ((Md1)_mesh).ToBuilder();
            for (var n = 0; n < morph.NumParts; n++)
            {
                var morphData = morph.GetMorphData(n, 1);
                var part = md1Builder.Parts[n == 0 ? 0 : 15];
                for (var i = 0; i < morphData.Length; i++)
                {
                    var v = new Md1.Vector(morphData[i].x, morphData[i].y, morphData[i].z);
                    part.Positions[i] = v;
                }
            }
            _mesh = md1Builder.ToMesh();
#endif

            _scene = new ModelScene();
            _scene.GenerateFrom(_mesh, _emr, _tim);
            viewport0.Scene = _scene;
            viewport0.DarkBackground = DarkBackground;
            viewport1.Scene = _scene;
            viewport1.DarkBackground = DarkBackground;

            RefreshHighlightedPart();
            RefreshStatusBar();

            RefreshKeyframe();
            _animationController.InvokeDataChanged();
        }

        private void RefreshHighlightedPart()
        {
            if (_scene == null)
                return;

            _scene.HighlightPart(_isolatedPartIndex == -1 ? _selectedPartIndex : -1);
            RefreshTimPrimitives();
            RefreshRelativePositionTextBoxes();
            RefreshPartStats();
        }

        private void RefreshTimPrimitives()
        {
            var partIndex = _isolatedPartIndex;
            if (partIndex == -1)
            {
                partIndex = _selectedPartIndex;
            }
            else
            {
                partIndex = 0;
            }

            if (partIndex == -1)
            {
                timImage.Primitives = null;
                return;
            }

            timImage.SetPrimitivesFromMesh(_mesh, partIndex);
        }

        private void RefreshRelativePositionTextBoxes()
        {
            var emr = _emr;
            var partIndex = _selectedPartIndex;
            if (partIndex != -1 && emr != null && partIndex < emr.NumParts)
            {
                var pos = emr.GetRelativePosition(partIndex);
                partPositionControl.Value = pos;
                partGroupBox.Visibility = Visibility.Visible;
            }
            else
            {
                partGroupBox.Visibility = Visibility.Collapsed;
            }
        }

        private void RefreshPartStats()
        {
            var content = "";

            if (_selectedPartIndex != -1)
            {
                var partIndex = _isolatedPartIndex == -1 ? _selectedPartIndex : 0;
                partIndex = Math.Min(partIndex, _mesh.NumParts - 1);
                switch (_mesh)
                {
                    case Tmd tmd:
                    {
                        var obj = tmd.Objects[partIndex];
                        content = $"Vertices: {obj.vtx_count} Normals: {obj.nor_count} Triangles: {obj.pri_count}";
                        break;
                    }
                    case Md1 md1:
                    {
                        var objT = md1.Objects[partIndex * 2 + 0];
                        var objQ = md1.Objects[partIndex * 2 + 1];
                        content = $"Vertices {objT.vtx_count} Normals: {objT.nor_count} Triangles: {objT.pri_count} Quads: {objQ.pri_count}";
                        break;
                    }
                    case Md2 md2:
                    {
                        var obj = md2.Objects[partIndex];
                        content = $"Vertices / Normals: {obj.vtx_count} Triangles: {obj.tri_count} Quads: {obj.quad_count}";
                        break;
                    }
                }
            }

            partStatsLabel.Content = content;
        }

        private void RefreshStatusBar()
        {
            var game = _project.Version == BioVersion.Biohazard2 ? "RE 2" : "RE 3";
            if (_project.Version == BioVersion.Biohazard1)
                game = "RE 1";

            var fileType = _project.MainModel is EmdFile ? ".EMD" : ".PLD";
            var numParts = _project.MainModel.GetMesh(0).NumParts;
            var basePolyCount = _project.MainModel.GetMesh(0).CountPolygons();
            var maxPolyCount = basePolyCount;
            foreach (var file in _project.Files)
            {
                if (file.Content is ModelFile modelFile)
                {
                    if (modelFile != _project.MainModel)
                    {
                        var total = basePolyCount + modelFile.GetMesh(0).CountPolygons();
                        maxPolyCount = Math.Max(maxPolyCount, total);
                    }
                }
            }

            fileTypeLabel.Content = $"{game} {fileType} File";
            numPartsLabel.Content = $"{numParts} parts";
            numPolygonsLabel.Content = $"{maxPolyCount} polygons";

        }

        private void mainWindow_Loaded(object sender, RoutedEventArgs e)
        {
#if false
            var texturePackerWindow = new TexturePackerWindow();
            texturePackerWindow.Meshes = _project.Files
                .Where(x => x.Content is ModelFile)
                .Select(x => ((ModelFile)x.Content).GetMesh(0))
                .ToArray();
            texturePackerWindow.Texture = _project.MainTexture;
            texturePackerWindow.Refresh();

            texturePackerWindow.Owner = this;
            texturePackerWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            if (texturePackerWindow.ShowDialog() == true)
            {
                var updatedMeshes = texturePackerWindow.UpdatedMeshes;
                var index = 0;
                _project.MainTexture = texturePackerWindow.UpdatedTexture;
                foreach (var file in _project.Files)
                {
                    if (file.Content is ModelFile modelFile)
                    {
                        modelFile.SetMesh(0, updatedMeshes[index]);
                        index++;
                    }
                }
                LoadMesh(_project.MainModel.GetMesh(0));
            }
#endif
        }

        private static byte ClampByte(int x) => (byte)Math.Max(0, Math.Min(255, x));
        private static byte ClampPage(int page, int x)
        {
            var pageLeft = page * 128;
            var pageRight = ((page + 1) * 128) - 1;
            return (byte)(Math.Max(pageLeft, Math.Min(pageRight, x)) % 128);
        }

        private void treeParts_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            RefreshHighlightedPart();
            RefreshRelativePositionTextBoxes();
        }

        private void timImage_TimUpdated(object sender, EventArgs e)
        {
            SetTimFile(timImage.Tim);
            RefreshModelView();
        }

        public void SetActiveModelFile(ModelFile modelFile)
        {
            _activeModelFile = modelFile;
        }

        public void LoadMesh(IModelMesh mesh, TimFile texture = null)
        {
            _mesh = mesh;
            _tim = texture ?? _project.MainTexture;

            var baseEmrNumber = 0;
            if (_project.MainModel.Version == BioVersion.Biohazard1 &&
                _project.MainModel.NumChunks == 6)
            {
                baseEmrNumber = 1;
            }
            _baseEmr = _project.MainModel.GetEmr(baseEmrNumber);

            _emr = _baseEmr;
            _edd = null;
            _isolatedPartIndex = -1;
            _animationIndex = -1;
            _selectedPartIndex = -1;
            RefreshModelView();
            RefreshTimImage();
        }

        public void LoadMeshWithoutArmature(IModelMesh mesh, TimFile texture = null)
        {
            _mesh = mesh;
            _tim = texture ?? _project.MainTexture;
            _baseEmr = null;
            _emr = null;
            _edd = null;
            _isolatedPartIndex = -1;
            _animationIndex = -1;
            _selectedPartIndex = -1;
            RefreshModelView();
            RefreshTimImage();
        }

        public void LoadMeshPart(IModelMesh mesh, int partIndex, TimFile texture = null)
        {
            _mesh = mesh;
            _tim = texture ?? _project.MainTexture;
            _edd = null;
            _emr = null;
            _isolatedPartIndex = partIndex;
            RefreshModelView();
            RefreshTimImage();
        }

        public void LoadTexture(TimFile texture)
        {
            _tim = texture;
            RefreshModelView();
            RefreshTimImage();
        }

        public void LoadWeaponModel(PlwFile plw)
        {
            _edd = plw.GetEdd(0);
            _baseEmr = _baseEmr.WithKeyframes(plw.GetEmr(0));
            _emr = _baseEmr;
            _isolatedPartIndex = -1;
            _animationIndex = -1;
            _selectedPartIndex = -1;

            if (_mesh is Md1 md1)
            {
                var targetBuilder = md1.ToBuilder();
                var sourceBuilder = plw.Md1.ToBuilder();
                targetBuilder.Parts[11] = sourceBuilder.Parts[0];
                _mesh = targetBuilder.ToMesh();
            }
            else if (_mesh is Md2 md2)
            {
                // TODO
            }

            _tim = _tim.WithWeaponTexture(plw.Tim);
            RefreshModelView();
            RefreshTimImage();
        }

        public void LoadAnimation(Emr emr, IEdd edd, int index)
        {
            _emr = _baseEmr.WithKeyframes(emr);
            _edd = edd;
            _animationPlaying = true;
            _animationIndex = index;
            _time = 0;
            _selectedPartIndex = -1;
            RefreshModelView();
        }

        public void SelectPart(int partIndex)
        {
            _selectedPartIndex = partIndex;
            RefreshHighlightedPart();
        }

        private void menuExit_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        private void OpenCommandBinding_Executed(object sender, System.Windows.Input.ExecutedRoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog();
            openFileDialog.InitialDirectory = _project == null ? null : System.IO.Path.GetDirectoryName(_project.MainPath);
            openFileDialog.Filter = "EMD/PLD Files (*.emd;*.pld)|*.emd;*.pld";
            if (openFileDialog.ShowDialog() == true)
            {
                LoadProject(openFileDialog.FileName);
            }
        }

        private void OpenRdtCommandBinding_CanExecute(object sender, System.Windows.Input.CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = _project != null;
            e.Handled = true;
        }

        private void OpenRdtCommandBinding_Executed(object sender, System.Windows.Input.ExecutedRoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog();
            openFileDialog.InitialDirectory = _project == null ? null : System.IO.Path.GetDirectoryName(_project.MainPath);
            openFileDialog.Filter = "RDT Files (*.rdt)|*.rdt";
            if (openFileDialog.ShowDialog() == true)
            {
                _project.LoadRdt(openFileDialog.FileName);
                RefreshTreeView();
            }
        }

        private void SaveCommandBinding_Executed(object sender, System.Windows.Input.ExecutedRoutedEventArgs e)
        {
            if (_project == null)
                return;

            SaveModel(_project.MainPath);
        }

        private void SaveAsCommandBinding_Executed(object sender, System.Windows.Input.ExecutedRoutedEventArgs e)
        {
            if (_project == null)
                return;

            CommonFileDialog
                .Save()
                .AddExtension(_project.MainModel is PldFile ? "*.pld" : "*.emd")
                .WithDefaultFileName(_project.MainPath)
                .Show(SaveModel);
        }

        private void ExitCommandBinding_Executed(object sender, System.Windows.Input.ExecutedRoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        private void CommandBinding_CanExecute(object sender, System.Windows.Input.CanExecuteRoutedEventArgs e)
        {

        }

        private void SaveCommandBinding_CanExecute(object sender, System.Windows.Input.CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = _project != null;
            e.Handled = true;
        }

        private void ExportForBioRandCommandBinding_Executed(object sender, System.Windows.Input.ExecutedRoutedEventArgs e)
        {
            var exportToBioRandWindow = new ExportForBioRandWindow();
            exportToBioRandWindow.Owner = this;
            exportToBioRandWindow.Project = _project;
            exportToBioRandWindow.ShowDialog();
        }

        public bool ShowFloor
        {
            get => Settings.Default.ShowFloor;
            set
            {
                Settings.Default.ShowFloor = value;
                Settings.Save();
                RefreshModelView();
            }
        }

        public bool ShowBones
        {
            get => Settings.Default.ShowBones;
            set
            {
                Settings.Default.ShowBones = value;
                Settings.Save();
                RefreshModelView();
            }
        }

        public bool DarkBackground
        {
            get => Settings.Default.DarkBackground;
            set
            {
                Settings.Default.DarkBackground = value;
                Settings.Save();
                RefreshModelView();
            }
        }

        public void RefreshTreeView() => projectTreeView.Refresh();

        private void partPositionControl_ValueChanged(object sender, EventArgs e)
        {
            try
            {
                var partIndex = _selectedPartIndex;
                if (partIndex != -1)
                {
                    var mainModel = _project.MainModel;
                    var oldEmr = mainModel.GetEmr(0);
                    var builder = oldEmr.ToBuilder();
                    builder.RelativePositions[_selectedPartIndex] = partPositionControl.Value;
                    var newEmr = builder.ToEmr();
                    mainModel.SetEmr(0, newEmr);
                    _baseEmr = newEmr;
                    _emr = _baseEmr;
                    RefreshModelView();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ChangeSpeedCommandBinding_Executed(object sender, System.Windows.Input.ExecutedRoutedEventArgs e)
        {
            if (_project.Version != BioVersion.Biohazard2)
            {
                MessageBox.Show("Currently only supported for RE 2", "Not supported", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var value = InputWindow.Show("Change speed", "Enter a speed modifier (faster = 2.0, slower = 0.5):", "1.0",
                s => double.TryParse(s, out var result) && result >= 0 && result <= 100);

            if (!double.TryParse(value, out var speed))
                return;

            if (speed == 1.0)
                return;

            ChangeSpeed(_project.MainModel, speed);
            foreach (var weapon in _project.Weapons)
            {
                ChangeSpeed(weapon, speed);
            }

            _animationController.InvokeDataChanged();
        }

        private void ChangeSpeed(ModelFile model, double speed)
        {
            var speedsPld = new[] { 0, 1, 8, 9 };
            var speedsPlw = new[] { 0, 1, 3, 4 };
            var speeds = model is PldFile ? speedsPld : speedsPlw;

            var edd = model.GetEdd(0);
            var emr = model.GetEmr(0);

            var builder = AnimationBuilder.FromEddEmr(edd, emr);
            foreach (var index in speeds)
            {
                var animation = builder.Animations[index];
                animation.ChangeSpeed(speed);
            }
            var (newEdd, newEmr) = builder.ToEddEmr();

            model.SetEdd(0, newEdd);
            model.SetEmr(0, newEmr);

            _animationController.InvokeDataChanged();
        }

        private class AnimationController : IAnimationController
        {
            private MainWindow _instance;

            public bool Playing
            {
                get => _instance._animationPlaying;
                set
                {
                    if (_instance._animationPlaying != value)
                    {
                        _instance._animationPlaying = value;
                        InvokeTimeChanged();
                    }
                }
            }
            public int Duration => _instance._edd?.GetAnimationDuration(_instance._animationIndex) ?? 0;
            public double Time
            {
                get => _instance._time;
                set
                {
                    _instance._time = Math.Max(0, Math.Min(Duration - 1, value));
                    InvokeTimeChanged();
                }
            }
            public int KeyFrame
            {
                get => (int)Time;
                set
                {
                    if (Duration == 0)
                        Time = 0;
                    else
                        Time = Math.Max(0, Math.Min(Duration - 1, value));
                }
            }
            public int EntityCount
            {
                get
                {
                    var emr = _instance._emr;
                    if (emr == null || emr.KeyFrames.Length == 0)
                        return 0;

                    var firstKeyFrame = emr.KeyFrames[0];
                    return firstKeyFrame.NumAngles * 3;
                }
            }


            public event EventHandler DataChanged;
            public event EventHandler TimeChanged;

            public AnimationController(MainWindow instance)
            {
                _instance = instance;
            }

            public int GetFunction(int time)
            {
                var animationIndex = _instance._animationIndex;
                if (animationIndex == -1)
                    return 0;

                var edd = _instance._edd;
                if (edd == null)
                    return 0;

                return edd.GetFrameFunction(animationIndex, time);
            }

            public void SetFunction(int time, int flags)
            {
                var animationIndex = _instance._animationIndex;
                if (animationIndex == -1)
                    return;

                var edd = _instance._edd;
                if (edd == null)
                    return;

                var builder = ((Edd1)edd).ToBuilder();
                var animation = builder.Animations[animationIndex];
                var frame = animation.Frames[time];
                frame.Flags = flags;
                animation.Frames[time] = frame;

                var newEdd = builder.ToEdd();
                _instance._activeModelFile.SetEdd(0, newEdd);
                _instance._edd = newEdd;
                _instance.RefreshModelView();

                InvokeDataChanged();
            }

            public Emr.Vector GetOffset(int time)
            {
                var animationIndex = _instance._animationIndex;
                if (animationIndex == -1)
                    return default;

                var edd = _instance._edd;
                if (edd == null)
                    return default;

                var frameIndex = edd.GetFrameIndex(animationIndex, time);

                var emr = _instance._emr;
                if (emr == null)
                    return default;

                if (frameIndex < 0 || frameIndex >= emr.KeyFrames.Length)
                    return default;

                var keyFrame = emr.KeyFrames[frameIndex];
                return keyFrame.Offset;
            }

            public void SetOffset(int time, Emr.Vector offset)
            {
                var animationIndex = _instance._animationIndex;
                if (animationIndex == -1)
                    return;

                var edd = _instance._edd;
                if (edd == null)
                    return;

                var frameIndex = edd.GetFrameIndex(animationIndex, time);

                var emr = _instance._emr;
                if (emr == null)
                    return;

                if (frameIndex < 0 || frameIndex >= emr.KeyFrames.Length)
                    return;

                var builder = emr.ToBuilder();
                builder.KeyFrames[frameIndex].Offset = offset;
                _instance._emr = builder.ToEmr();
                _instance._baseEmr = _instance._emr;
                _instance.RefreshModelView();

                InvokeDataChanged();
            }

            public string GetEntityName(int i)
            {
                var iF = i / 3;
                var iC = i % 3;

                var emr = _instance._emr;
                var version = emr.Version;
                var partName = PartName.GetPartName(version, iF);
                string componentName;
                switch (iC)
                {
                    case 0: componentName = "x"; break;
                    case 1: componentName = "y"; break;
                    case 2: componentName = "z"; break;
                    default: throw new Exception();
                }
                return $"{partName}.{componentName}";
            }

            public int? GetEntityRaw(int i, int t)
            {
                var iF = i / 3;
                var iC = i % 3;

                var animationIndex = _instance._animationIndex;
                if (animationIndex == -1)
                    return null;

                var edd = _instance._edd;
                var emr = _instance._emr;
                var duration = edd.GetAnimationDuration(animationIndex);
                if (t < 0 || t >= duration)
                    return null;

                var frameIndex = edd.GetFrameIndex(animationIndex, t);
                if (frameIndex < 0 || frameIndex >= emr.KeyFrames.Length)
                    return null;

                var frame = emr.KeyFrames[frameIndex];
                if (iF < 0 || iF >= frame.NumAngles)
                    return null;

                var result = frame.GetAngle(i / 3);
                if (iC == 0) return result.x;
                if (iC == 1) return result.y;
                if (iC == 2) return result.z;
                return null;
            }

            public double? GetEntity(int i, int t)
            {
                if (GetEntityRaw(i, t) is int value)
                {
                    return Wrap(((value / 4096.0) * 2 - 1) + 1);
                }
                return null;
            }

            public void SetEntityRaw(int i, int t, int value)
            {
                var iF = i / 3;
                var iC = i % 3;

                var animationIndex = _instance._animationIndex;
                if (animationIndex == -1)
                    return;

                var edd = _instance._edd;
                var emr = _instance._emr.ToBuilder();
                var duration = edd.GetAnimationDuration(animationIndex);
                if (t < 0 || t >= duration)
                    return;

                var frameIndex = edd.GetFrameIndex(animationIndex, t);
                if (frameIndex < 0 || frameIndex >= emr.KeyFrames.Count)
                    return;

                var frame = emr.KeyFrames[frameIndex];
                if (iF < 0 || iF >= frame.Angles.Length)
                    return;

                var rawValue = (short)Math.Max(0, Math.Min(4095, value));

                var v = frame.Angles[i / 3];
                if (iC == 0) v.x = rawValue;
                if (iC == 1) v.y = rawValue;
                if (iC == 2) v.z = rawValue;
                frame.Angles[i / 3] = v;

                var newEmr = emr.ToEmr();

                _instance._activeModelFile.SetEmr(0, newEmr);
                _instance._emr = newEmr;
                _instance._baseEmr = newEmr;
                _instance.RefreshModelView();

                InvokeDataChanged();
            }

            public void SetEntity(int i, int t, double value)
            {
                var denormalizedValue = (Wrap(value - 1) + 1) / 2;
                var rawValue = value <= -1
                    ? (short)(2049)
                    : (short)((int)(denormalizedValue * 4096) % 4096);
                SetEntityRaw(i, t, rawValue);
            }

            public void Insert()
            {
                var animationIndex = _instance._animationIndex;
                if (animationIndex == -1)
                    return;

                var keyFrame = KeyFrame;

                var animationBuilder = AnimationBuilder.FromEddEmr(_instance._edd, _instance._emr);
                var animation = animationBuilder.Animations[animationIndex];
                animation.Insert(keyFrame);
                var (newEdd, newEmr) = animationBuilder.ToEddEmr();

                _instance._project.MainModel.SetEdd(0, newEdd);
                _instance._project.MainModel.SetEmr(0, newEmr);
                _instance._edd = newEdd;
                _instance._emr = newEmr;
                _instance._baseEmr = newEmr;
                _instance.RefreshModelView();

                InvokeDataChanged();
            }

            public void Duplicate()
            {
                var animationIndex = _instance._animationIndex;
                if (animationIndex == -1)
                    return;

                var frameIndex = KeyFrame;

                var edd = (Edd1.Builder)_instance._edd.ToBuilder();
                var animation = edd.Animations[animationIndex];
                if (animation.Frames.Count == 0)
                {
                    animation.Frames.Add(new Edd1.Frame());
                }
                else if (animation.Frames.Count > frameIndex)
                {
                    animation.Frames.Insert(frameIndex + 1, animation.Frames[frameIndex]);
                }

                var newEdd = edd.ToEdd();
                _instance._project.MainModel.SetEdd(0, newEdd);
                _instance._edd = newEdd;

                InvokeDataChanged();
            }

            public void Delete()
            {
                if (Duration == 0)
                    return;

                var animationIndex = _instance._animationIndex;
                if (animationIndex == -1)
                    return;

                var frameIndex = KeyFrame;

                var edd = (Edd1.Builder)_instance._edd.ToBuilder();
                var animation = edd.Animations[animationIndex];
                if (animation.Frames.Count > frameIndex)
                {
                    animation.Frames.RemoveAt(frameIndex);
                    var newEdd = edd.ToEdd();
                    _instance._project.MainModel.SetEdd(0, newEdd);
                    _instance._edd = newEdd;
                }

                if (Time >= Duration)
                    Time = Duration - 1;

                InvokeDataChanged();
            }

            public void InvokeDataChanged() => DataChanged?.Invoke(this, EventArgs.Empty);
            public void InvokeTimeChanged() => TimeChanged?.Invoke(this, EventArgs.Empty);

            private static double Wrap(double x)
            {
                while (x < 1) x += 2;
                while (x > 1) x -= 2;
                return x;
            }
        }
    }

    public interface IAnimationController
    {
        event EventHandler DataChanged;
        event EventHandler TimeChanged;

        bool Playing { get; set; }
        int Duration { get; }
        double Time { get; set; }
        int KeyFrame { get; set; }
        int EntityCount { get; }

        int GetFunction(int time);
        void SetFunction(int time, int value);
        Emr.Vector GetOffset(int time);
        void SetOffset(int time, Emr.Vector offset);
        string GetEntityName(int i);
        double? GetEntity(int i, int t);
        int? GetEntityRaw(int i, int t);
        void SetEntity(int entity, int time, double value);
        void SetEntityRaw(int entity, int time, int value);

        void Insert();
        void Duplicate();
        void Delete();
    }
}
