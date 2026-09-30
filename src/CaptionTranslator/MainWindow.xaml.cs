using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using CaptionTranslator.Captions;
using CaptionTranslator.Speech;
using CaptionTranslator.Speech.Piper;
using CaptionTranslator.Translation;
using Microsoft.ML.OnnxRuntime;

namespace CaptionTranslator
{
    public partial class MainWindow : Window
    {
        private const int maxLines = 500;
        private const double drawerWidth = 340;

        private static readonly TimeSpan continuationWindow = TimeSpan.FromSeconds(45);

        private readonly ObservableCollection<CaptionLine> lines = new ObservableCollection<CaptionLine>();
        private readonly AppSettings settings;
        private readonly CaptionPipeline pipeline;
        private ICaptionSource? currentSource;
        private SourceState sourceState = SourceState.NotFound;
        private string sourceStatus = "Starting…";
        private string modelStatus = "Loading translation model…";
        private SpeechReader? speechReader;
        private readonly VoiceFiles voiceFiles = new VoiceFiles();
        private readonly UiWatchdog uiWatchdog = new UiWatchdog(System.Windows.Threading.Dispatcher.CurrentDispatcher);
        private IReadOnlyList<VoiceOption> voiceOptions = Array.Empty<VoiceOption>();
        private VoiceOption? activeVoice;
        private CancellationTokenSource? voiceSelection;
        private bool updatingVoiceBox;
        private volatile bool readAloudActive;
        private bool refreshingDevices;
        private bool followLatest = true;
        private bool initializing = true;
        private volatile string? myName;
        private volatile bool skipMyLines;

        public MainWindow()
        {
            this.settings = AppSettings.Load();
            this.pipeline = new CaptionPipeline(TimeSpan.FromMilliseconds(this.settings.QuietPeriodMilliseconds));

            InitializeComponent();

            this.LinesList.ItemsSource = this.lines;
            this.lines.CollectionChanged += (sender, e) => UpdateEmptyState();

            this.ShowGermanSwitch.IsChecked = this.settings.ShowGerman;
            this.FontSizeSlider.Value = this.settings.FontSize;
            ApplyFontSize(this.settings.FontSize);
            ApplyShowGerman();
            this.PinButton.IsChecked = this.settings.Topmost;
            this.Topmost = this.settings.Topmost;
            InitializeSpeech();

            this.pipeline.LineAdded += line => Dispatcher.BeginInvoke(() => AddLine(line));
            // Read aloud only settled sentences, each once; cards themselves keep updating while Teams revises them.
            this.pipeline.SentenceReady += (line, german, english) =>
            {
                if (!this.readAloudActive || english.Length == 0)
                    return;

                // Runs on the translation thread; uses the name snapshot, not line.IsMe, which is set on the UI thread.
                if (this.skipMyLines && SpeakerMatcher.IsSameSpeaker(line.Speaker, this.myName))
                    return;

                this.speechReader?.Enqueue(line.Speaker, english);
            };
            this.pipeline.PendingChanged += segment => Dispatcher.BeginInvoke(() => ShowPending(segment));
            this.pipeline.StatusChanged += (status, state) => Dispatcher.BeginInvoke(() =>
            {
                this.sourceStatus = status;
                this.sourceState = state;
                UpdateStatus();
            });

            this.initializing = false;
        }

        private void OnSourceInitialized(object? sender, EventArgs e) => WindowEffects.Apply(this, 0x3A3D52);

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            ((Storyboard)FindResource("PulseStoryboard")).Begin(this, true);
            ((Storyboard)FindResource("TypingStoryboard")).Begin(this, true);

            if (this.settings.Source == AppSettings.LiveCaptionsSource)
                this.LiveSourceOption.IsChecked = true;
            else
                this.TeamsSourceOption.IsChecked = true;

            UpdateStatus();
            _ = LoadTranslatorAsync();
            this.uiWatchdog.Start();
        }

        private void OnClosing(object? sender, CancelEventArgs e)
        {
            this.uiWatchdog.Dispose();
            this.settings.Save();
            this.pipeline.Dispose();
            this.speechReader?.Dispose();
        }

        // ---------- Window chrome ----------

        private void OnStateChanged(object? sender, EventArgs e)
        {
            // WindowEffects limits the maximized window to the work area, so no padding is needed; only hide the border.
            this.RootBorder.BorderThickness = this.WindowState == WindowState.Maximized ? new Thickness(0) : new Thickness(1);
            this.MaximizeButton.Content = this.WindowState == WindowState.Maximized ? "" : "";
            this.MaximizeButton.ToolTip = this.WindowState == WindowState.Maximized ? "Restore" : "Maximize";
        }

        private void OnMinimizeClick(object sender, RoutedEventArgs e) => this.WindowState = WindowState.Minimized;

        private void OnMaximizeClick(object sender, RoutedEventArgs e)
            => this.WindowState = this.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

        private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

        private void OnPinClick(object sender, RoutedEventArgs e)
        {
            this.settings.Topmost = this.PinButton.IsChecked == true;
            this.Topmost = this.settings.Topmost;
            ShowToast(this.Topmost ? "Stays on top of other windows" : "No longer on top");
        }

        // ---------- Translation model ----------

        private async Task LoadTranslatorAsync()
        {
            this.Spinner.Visibility = Visibility.Visible;
            ((Storyboard)FindResource("SpinStoryboard")).Begin(this, true);

            try
            {
                if (ModelFiles.MissingFiles().Count > 0)
                {
                    Progress<string> progress = new Progress<string>(message =>
                    {
                        this.modelStatus = message;
                        UpdateStatus();
                    });
                    await ModelFiles.DownloadMissingAsync(progress, CancellationToken.None);
                }

                this.modelStatus = "Loading translation model…";
                UpdateStatus();

                ITranslator translator = await Task.Run(() =>
                {
                    OpusMtTranslator loaded = new OpusMtTranslator(ModelFiles.Directory);

                    // The first inference is several times slower (lazy allocations); do it now, not on the first caption.
                    loaded.Translate("Guten Morgen, wir fangen jetzt an.");
                    return loaded;
                });
                this.pipeline.SetTranslator(translator);
                this.modelStatus = string.Empty;
                Log.Info("Translation model loaded.");
            }
            catch (Exception exception)
            {
                // Without a model the app still shows the German captions.
                Log.Error("Could not load translation model.", exception);
                this.modelStatus = $"Translation model not available ({exception.Message})";
                this.pipeline.SetTranslator(null);
            }

            ((Storyboard)FindResource("SpinStoryboard")).Stop(this);
            this.Spinner.Visibility = Visibility.Collapsed;
            UpdateStatus();
        }

        // ---------- Caption source ----------

        private void OnSourceOptionChecked(object sender, RoutedEventArgs e)
        {
            if (sender is not RadioButton { Tag: string source })
                return;

            this.settings.Source = source;
            this.currentSource = source == AppSettings.LiveCaptionsSource
                ? new LiveCaptionsSource()
                : new TeamsCaptionSource(this.settings.TeamsCaptionContainerPattern);

            Log.Info("Source: " + this.currentSource.DisplayName);
            this.sourceState = SourceState.NotFound;
            this.sourceStatus = "Looking for " + this.currentSource.DisplayName + "…";
            this.pipeline.Start(this.currentSource);
            UpdateReadAloud();
            UpdateStatus();
        }

        // ---------- Captions list ----------

        private void AddLine(CaptionLine line)
        {
            bool wasAtBottom = IsScrolledToBottom();
            line.IsMe = SpeakerMatcher.IsSameSpeaker(line.Speaker, this.myName);
            CaptionLine? previous = this.lines.Count > 0 ? this.lines[^1] : null;
            line.IsContinuation = previous != null && previous.Speaker == line.Speaker && line.Time - previous.Time < continuationWindow;

            this.lines.Add(line);
            while (this.lines.Count > maxLines)
                this.lines.RemoveAt(0);

            if (!wasAtBottom && !this.followLatest)
                this.NewCaptionsButton.Visibility = Visibility.Visible;
        }

        private bool IsScrolledToBottom() => this.Scroller.VerticalOffset >= this.Scroller.ScrollableHeight - 24;

        /// <summary>
        /// Keeps the newest caption in view while cards are added or grow (Teams text is updated in place),
        /// unless the user scrolled up to read something older.
        /// </summary>
        private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (e.ExtentHeightChange == 0)
                this.followLatest = IsScrolledToBottom();
            else if (this.followLatest)
                this.Scroller.ScrollToEnd();

            if (IsScrolledToBottom())
                this.NewCaptionsButton.Visibility = Visibility.Collapsed;
        }

        private void OnNewCaptionsClick(object sender, RoutedEventArgs e)
        {
            this.followLatest = true;
            this.Scroller.ScrollToEnd();
            this.NewCaptionsButton.Visibility = Visibility.Collapsed;
        }

        private void ShowPending(CaptionSegment? segment)
        {
            if (segment == null)
            {
                this.PendingBar.Visibility = Visibility.Collapsed;
                return;
            }

            this.PendingText.Text = (segment.Speaker.Length > 0 ? segment.Speaker + ": " : string.Empty) + segment.Text;
            this.PendingBar.Visibility = Visibility.Visible;
        }

        private void UpdateEmptyState()
        {
            this.EmptyState.Visibility = this.lines.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void OnCopyLineClick(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not CaptionLine line)
                return;

            Clipboard.SetText(line.SpeakerLabel + (line.EnglishText ?? string.Empty) + Environment.NewLine + "(" + line.GermanText + ")");
            ShowToast("Line copied");
        }

        private void OnCopyClick(object sender, RoutedEventArgs e)
        {
            if (this.lines.Count == 0)
            {
                ShowToast("Nothing to copy yet");
                return;
            }

            StringBuilder builder = new StringBuilder();
            foreach (CaptionLine line in this.lines)
            {
                builder.Append('[').Append(line.TimeLabel).Append("] ").Append(line.SpeakerLabel).AppendLine(line.EnglishText ?? string.Empty);
                builder.Append("    (").Append(line.GermanText).AppendLine(")");
            }

            Clipboard.SetText(builder.ToString());
            ShowToast($"Copied {this.lines.Count} lines");
        }

        private void OnClearClick(object sender, RoutedEventArgs e)
        {
            this.lines.Clear();
            this.NewCaptionsButton.Visibility = Visibility.Collapsed;
        }

        // ---------- Status ----------

        private void UpdateStatus()
        {
            (Brush dotBrush, string title, string hint) = DescribeState();

            this.StatusDot.Fill = dotBrush;
            this.StatusHalo.Fill = dotBrush;
            this.StatusHalo.Visibility = this.sourceState == SourceState.Live ? Visibility.Visible : Visibility.Hidden;
            this.StatusText.Text = this.sourceStatus;
            this.ModelStatusText.Text = this.modelStatus.Length > 0 ? this.modelStatus : "Offline translation ready";
            this.EmptyTitle.Text = title;
            this.EmptyHint.Text = hint;
            UpdateEmptyState();
        }

        private (Brush DotBrush, string Title, string Hint) DescribeState()
        {
            bool liveCaptions = this.settings.Source == AppSettings.LiveCaptionsSource;
            switch (this.sourceState)
            {
                case SourceState.Live:
                    return ((Brush)FindResource("SuccessBrush"), "Listening…", "Translations appear here as each sentence is finished.");
                case SourceState.Waiting:
                    return ((Brush)FindResource("WarningBrush"), "Captions found", "Translations appear here as soon as someone speaks.");
                case SourceState.Error:
                    return ((Brush)FindResource("DangerBrush"), "Something went wrong", this.sourceStatus + "\nOpen Settings → Save diagnostics file if this keeps happening.");
                default:
                    return liveCaptions
                        ? ((Brush)FindResource("FaintBrush"), "Start Windows Live Captions", "Press Win + Ctrl + L and set the caption language to German.\nAny audio on this PC will be translated.")
                        : ((Brush)FindResource("FaintBrush"), "Turn on Teams live captions", "In your Teams meeting: More (…) → Language and speech → Turn on live captions.\nSet the spoken language to German.");
            }
        }

        private void ShowToast(string message)
        {
            this.ToastText.Text = message;
            DoubleAnimationUsingKeyFrames fade = new DoubleAnimationUsingKeyFrames();
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(150))));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1600))));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1900))));
            this.Toast.BeginAnimation(OpacityProperty, fade);
        }

        // ---------- Settings drawer ----------

        private void OnSettingsToggleChanged(object sender, RoutedEventArgs e) => SetDrawerOpen(this.SettingsToggle.IsChecked == true);

        private void OnCloseDrawerClick(object sender, RoutedEventArgs e) => SetDrawerOpen(false);

        private void OnBackdropMouseDown(object sender, MouseButtonEventArgs e) => SetDrawerOpen(false);

        private void SetDrawerOpen(bool open)
        {
            this.SettingsToggle.IsChecked = open;
            Duration duration = new Duration(TimeSpan.FromMilliseconds(220));
            IEasingFunction easing = new CubicEase { EasingMode = EasingMode.EaseOut };

            if (open)
            {
                RefreshDevices();
                this.Drawer.Visibility = Visibility.Visible;
                this.DrawerBackdrop.Visibility = Visibility.Visible;
            }

            DoubleAnimation slide = new DoubleAnimation(open ? 0 : drawerWidth, duration) { EasingFunction = easing };
            DoubleAnimation fade = new DoubleAnimation(open ? 1 : 0, duration);
            if (!open)
            {
                slide.Completed += (animationSender, args) =>
                {
                    if (this.SettingsToggle.IsChecked != true)
                    {
                        this.Drawer.Visibility = Visibility.Collapsed;
                        this.DrawerBackdrop.Visibility = Visibility.Collapsed;
                    }
                };
            }

            this.DrawerShift.BeginAnimation(TranslateTransform.XProperty, slide);
            this.DrawerBackdrop.BeginAnimation(OpacityProperty, fade);
        }

        private void OnShowGermanClick(object sender, RoutedEventArgs e)
        {
            this.settings.ShowGerman = this.ShowGermanSwitch.IsChecked == true;
            ApplyShowGerman();
        }

        private void ApplyShowGerman()
            => this.LinesList.Tag = this.settings.ShowGerman ? Visibility.Visible : Visibility.Collapsed;

        private void OnFontSizeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (this.initializing)
                return;

            ApplyFontSize(e.NewValue);
        }

        private void OnCaptionsMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Keyboard.Modifiers != ModifierKeys.Control)
                return;

            this.FontSizeSlider.Value = Math.Clamp(this.FontSizeSlider.Value + (e.Delta > 0 ? 1 : -1), this.FontSizeSlider.Minimum, this.FontSizeSlider.Maximum);
            e.Handled = true;
        }

        private void ApplyFontSize(double size)
        {
            this.settings.FontSize = size;
            this.LinesList.FontSize = size;
            this.FontSizeLabel.Text = $"{size:0} pt";
        }

        private async void OnDiagnoseClick(object sender, RoutedEventArgs e)
        {
            ICaptionSource? source = this.currentSource;
            if (source == null)
                return;

            ShowToast("Saving diagnostics…");
            try
            {
                string dump = await Task.Run(source.DumpTree);
                string path = Log.WriteDump("uitree", dump);
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
                ShowToast("Diagnostics saved");
            }
            catch (Exception exception)
            {
                Log.Error("Diagnose failed.", exception);
                ShowToast("Diagnostics failed: " + exception.Message);
            }
        }

        private void OnOpenLogsClick(object sender, RoutedEventArgs e)
        {
            Directory.CreateDirectory(AppPaths.LogDirectory);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppPaths.LogDirectory}\"") { UseShellExecute = true });
        }

        // ---------- Read aloud ----------

        private void InitializeSpeech()
        {
            IReadOnlyList<string> windowsVoices = Array.Empty<string>();
            try
            {
                this.speechReader = new SpeechReader();
                windowsVoices = WindowsVoice.GetInstalledNames();
            }
            catch (Exception exception)
            {
                // No speech engine on this PC: the app works without reading aloud (or with natural voices only).
                Log.Error("Windows voices not available.", exception);
            }

            bool naturalAvailable = EspeakPhonemizer.Shared != null;
            this.voiceOptions = VoiceOptions.Build(windowsVoices, PiperVoiceCatalog.All, naturalAvailable);

            if (this.speechReader == null || this.voiceOptions.Count == 0)
            {
                this.ReadAloudToggle.IsEnabled = false;
                this.VoiceBox.IsEnabled = false;
                this.SpeedBox.IsEnabled = false;
                this.DeviceBox.IsEnabled = false;
                this.TestVoiceButton.IsEnabled = false;
                this.VolumeSlider.IsEnabled = false;
                this.SkipMyLinesSwitch.IsEnabled = false;
                this.MyNameBox.IsEnabled = false;
                this.ReadAloudToggle.ToolTip = "No voices available on this PC.";
                return;
            }

            FillVoiceBox();
            if (!naturalAvailable)
                SetVoiceStatus("Natural voices unavailable – see log", false);

            // Start with a Windows voice so reading works at once; a saved natural voice replaces it when loaded (a few seconds).
            VoiceOption? saved = VoiceOptions.Resolve(this.voiceOptions, this.settings.Voice);
            VoiceOption? start = saved != null && !saved.Id.IsNatural ? saved : this.voiceOptions.FirstOrDefault(option => !option.Id.IsNatural);
            if (start != null)
                ActivateWindowsVoice(start, save: false);

            if (saved?.Natural != null)
            {
                if (this.voiceFiles.IsDownloaded(saved.Natural))
                {
                    SelectVoiceBoxItem(saved);
                    _ = UseNaturalVoiceAsync(saved);
                }
                else
                {
                    Log.Info($"Saved natural voice {saved.Id} is not downloaded; using {start?.Id}.");
                    SetVoiceStatus($"{saved.DisplayName} is not downloaded – select it to download", false);
                }
            }

            int speedIndex = this.SpeedBox.Items.Cast<ComboBoxItem>().ToList().FindIndex(item => (string)item.Tag == this.settings.SpeechRate.ToString());
            this.SpeedBox.SelectedIndex = speedIndex >= 0 ? speedIndex : 1;
            this.ReadAloudToggle.IsChecked = this.settings.ReadAloud;

            this.VolumeSlider.Value = Math.Clamp(this.settings.ReadAloudVolume, 0, 100);
            ApplyVolume(this.VolumeSlider.Value);
            this.SkipMyLinesSwitch.IsChecked = this.settings.SkipMyLines;
            this.skipMyLines = this.settings.SkipMyLines;
            if (string.IsNullOrWhiteSpace(this.settings.MyName))
                this.settings.MyName = SpeakerMatcher.GetWindowsDisplayName();
            this.myName = this.settings.MyName;
            this.MyNameBox.Text = this.settings.MyName ?? string.Empty;
            RefreshDevices();
        }

        /// <summary>
        /// Reading is off for Windows Live Captions: that source transcribes all PC audio, so it would caption its own speech.
        /// </summary>
        private void UpdateReadAloud()
        {
            bool isLiveCaptions = this.settings.Source == AppSettings.LiveCaptionsSource;
            if (this.speechReader != null && this.voiceOptions.Count > 0)
            {
                this.ReadAloudToggle.IsEnabled = !isLiveCaptions;
                this.ReadAloudToggle.ToolTip = isLiveCaptions
                    ? "Not available with Windows Live Captions: it would caption its own speech."
                    : "Speak each English line. Use headphones so the meeting does not hear it.";
            }

            bool active = this.settings.ReadAloud && !isLiveCaptions && this.speechReader != null;
            if (!active)
                this.speechReader?.StopAll();

            this.readAloudActive = active;
            this.ReadAloudIcon.Text = active ? "" : "";
        }

        private void OnReadAloudClick(object sender, RoutedEventArgs e)
        {
            this.settings.ReadAloud = this.ReadAloudToggle.IsChecked == true;
            UpdateReadAloud();

            if (this.readAloudActive)
            {
                string device = (this.DeviceBox.SelectedItem as AudioOutputDevice)?.Name ?? AudioOutputDevice.DefaultName;
                ShowToast("Reading aloud on " + device);
            }
        }

        private void OnVolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (this.speechReader != null)
                ApplyVolume(e.NewValue);
        }

        private void ApplyVolume(double percent)
        {
            this.settings.ReadAloudVolume = (int)percent;
            this.VolumeLabel.Text = $"{percent:0} %";
            if (this.speechReader != null)
                this.speechReader.Volume = (float)(percent / 100);
        }

        private void OnSkipMyLinesClick(object sender, RoutedEventArgs e)
        {
            this.settings.SkipMyLines = this.SkipMyLinesSwitch.IsChecked == true;
            this.skipMyLines = this.settings.SkipMyLines;
        }

        private void OnMyNameChanged(object sender, TextChangedEventArgs e) => SetMyName(this.MyNameBox.Text);

        private void OnMarkMeClick(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not CaptionLine line || !line.HasSpeaker)
                return;

            this.MyNameBox.Text = line.Speaker;
            this.SkipMyLinesSwitch.IsChecked = true;
            OnSkipMyLinesClick(sender, e);
            ShowToast($"Got it – \"{line.Speaker}\" is you");
        }

        private void SetMyName(string name)
        {
            this.settings.MyName = name.Trim();
            this.myName = this.settings.MyName;
            foreach (CaptionLine line in this.lines)
                line.IsMe = SpeakerMatcher.IsSameSpeaker(line.Speaker, this.myName);
        }

        private async void OnVoiceChanged(object sender, SelectionChangedEventArgs e)
        {
            if (this.updatingVoiceBox || (this.VoiceBox.SelectedItem as ComboBoxItem)?.Tag is not VoiceOption option || option == this.activeVoice)
                return;

            Log.Info($"Voice selected: {option.Id} (in use: {this.activeVoice?.Id}).");
            this.voiceSelection?.Cancel();
            if (option.Natural == null)
            {
                SetVoiceStatus(null, false);
                ActivateWindowsVoice(option, save: true);
                return;
            }

            await UseNaturalVoiceAsync(option);
        }

        private void OnCancelVoiceDownloadClick(object sender, RoutedEventArgs e) => this.voiceSelection?.Cancel();

        private void ActivateWindowsVoice(VoiceOption option, bool save)
        {
            try
            {
                Stopwatch stopwatch = Stopwatch.StartNew();
                this.speechReader?.SetVoice(new WindowsVoice(option.Id.Name));
                this.activeVoice = option;
                if (save)
                    this.settings.Voice = option.Id.ToString();
                SelectVoiceBoxItem(option);
                Log.Info($"Windows voice {option.Id.Name} in use ({stopwatch.ElapsedMilliseconds} ms).");
            }
            catch (ArgumentException exception)
            {
                Log.Error($"Windows voice '{option.Id.Name}' could not be selected.", exception);
                SetVoiceStatus("This Windows voice is not available", false);
            }
        }

        /// <summary>Downloads the voice if needed, loads it and switches to it. The previous voice keeps reading meanwhile.</summary>
        private async Task UseNaturalVoiceAsync(VoiceOption option)
        {
            PiperVoiceInfo info = option.Natural!;
            EspeakPhonemizer? phonemizer = EspeakPhonemizer.Shared;
            if (phonemizer == null || this.speechReader == null)
                return;

            CancellationTokenSource selection = new CancellationTokenSource();
            this.voiceSelection = selection;
            try
            {
                if (!this.voiceFiles.IsDownloaded(info))
                {
                    SetVoiceStatus($"Downloading {option.DisplayName}… 0 %", true);
                    Progress<int> progress = new Progress<int>(percent =>
                    {
                        if (!selection.IsCancellationRequested)
                            SetVoiceStatus($"Downloading {option.DisplayName}… {percent} %", true);
                    });
                    await Task.Run(() => this.voiceFiles.DownloadAsync(info, progress, selection.Token));
                    FillVoiceBox();
                    SelectVoiceBoxItem(option);
                }

                SetVoiceStatus($"Loading {option.DisplayName}…", false);
                Log.Info($"Loading natural voice {info.Id}…");
                Stopwatch stopwatch = Stopwatch.StartNew();
                PiperVoice voice = await Task.Run(() => PiperVoice.Load(this.voiceFiles.ModelPath(info), this.voiceFiles.ConfigPath(info), phonemizer));
                Log.Info($"Natural voice {info.Id} loaded in {stopwatch.ElapsedMilliseconds} ms{(selection.IsCancellationRequested ? " (not used: another voice was chosen)" : string.Empty)}.");
                if (selection.IsCancellationRequested)
                {
                    // Another voice was chosen while this one was loading.
                    voice.Dispose();
                    return;
                }

                this.speechReader.SetVoice(voice);
                this.activeVoice = option;
                this.settings.Voice = option.Id.ToString();
                SetVoiceStatus(null, false);
                Log.Info($"Natural voice {info.Id} in use.");
            }
            catch (OperationCanceledException) when (selection.IsCancellationRequested)
            {
                if (this.voiceSelection == selection)
                {
                    SetVoiceStatus(null, false);
                    SelectVoiceBoxItem(this.activeVoice);
                }
            }
            catch (Exception exception) when (exception is HttpRequestException || exception is TaskCanceledException || exception is IOException
                                              || exception is OnnxRuntimeException || exception is System.Text.Json.JsonException
                                              || exception is KeyNotFoundException || exception is NotSupportedException || exception is InvalidOperationException)
            {
                Log.Error($"Natural voice {info.Id} could not be used.", exception);
                bool network = exception is HttpRequestException || exception is TaskCanceledException;
                SetVoiceStatus(network ? "Download failed – check internet connection" : "Voice could not be loaded – see log", false);
                SelectVoiceBoxItem(this.activeVoice);
            }
        }

        private void FillVoiceBox()
        {
            this.updatingVoiceBox = true;
            this.VoiceBox.Items.Clear();
            AddVoiceGroup("NATURAL VOICES", this.voiceOptions.Where(option => option.Natural != null));
            AddVoiceGroup("WINDOWS VOICES", this.voiceOptions.Where(option => option.Natural == null));
            this.updatingVoiceBox = false;
        }

        private void AddVoiceGroup(string header, IEnumerable<VoiceOption> options)
        {
            List<VoiceOption> group = options.ToList();
            if (group.Count == 0)
                return;

            this.VoiceBox.Items.Add(new ComboBoxItem
            {
                Content = header,
                IsEnabled = false,
                FontSize = 11,
                Foreground = (Brush)FindResource("FaintBrush"),
            });
            foreach (VoiceOption option in group)
                this.VoiceBox.Items.Add(new ComboBoxItem { Content = VoiceLabel(option), Tag = option });
        }

        private string VoiceLabel(VoiceOption option)
        {
            if (option.Natural == null)
                return option.DisplayName;

            return this.voiceFiles.IsDownloaded(option.Natural)
                ? $"{option.DisplayName}  ✓"
                : $"{option.DisplayName} · {option.Natural.SizeMegabytes} MB";
        }

        private void SelectVoiceBoxItem(VoiceOption? option)
        {
            this.updatingVoiceBox = true;
            this.VoiceBox.SelectedItem = this.VoiceBox.Items.OfType<ComboBoxItem>().FirstOrDefault(item => Equals(item.Tag, option));
            this.updatingVoiceBox = false;
        }

        /// <param name="message">Null hides the status line.</param>
        private void SetVoiceStatus(string? message, bool canCancel)
        {
            this.VoiceStatusPanel.Visibility = message == null ? Visibility.Collapsed : Visibility.Visible;
            this.VoiceStatusText.Text = message ?? string.Empty;
            this.VoiceCancelLink.Visibility = canCancel ? Visibility.Visible : Visibility.Collapsed;
        }

        private void OnSpeedChanged(object sender, SelectionChangedEventArgs e)
        {
            if ((this.SpeedBox.SelectedItem as ComboBoxItem)?.Tag is string tag && int.TryParse(tag, out int rate))
            {
                this.settings.SpeechRate = rate;
                this.speechReader?.SetRate(rate);
            }
        }

        /// <summary>Reloads connected output devices (headsets come and go) and keeps the saved choice if still connected.</summary>
        private void RefreshDevices()
        {
            if (this.speechReader == null)
                return;

            IReadOnlyList<AudioOutputDevice> devices;
            try
            {
                devices = AudioOutputDevice.GetConnected();
            }
            catch (Exception exception)
            {
                // Audio subsystem unavailable: fall back to the default device only.
                Log.Error("Could not list audio devices.", exception);
                devices = new[] { AudioOutputDevice.Default };
            }

            this.refreshingDevices = true;
            this.DeviceBox.ItemsSource = devices;
            this.DeviceBox.SelectedItem = devices.FirstOrDefault(device => device.Id == this.settings.AudioDeviceId) ?? devices[0];
            this.refreshingDevices = false;
            this.speechReader.SetDevice((this.DeviceBox.SelectedItem as AudioOutputDevice)?.Id);
        }

        private void OnDeviceDropDownOpened(object? sender, EventArgs e) => RefreshDevices();

        private void OnDeviceChanged(object sender, SelectionChangedEventArgs e)
        {
            if (this.refreshingDevices || this.DeviceBox.SelectedItem is not AudioOutputDevice device)
                return;

            this.settings.AudioDeviceId = device.Id;
            this.speechReader?.SetDevice(device.Id);
        }

        private async void OnTestVoiceClick(object sender, RoutedEventArgs e)
        {
            if (this.speechReader == null)
                return;

            this.TestVoiceButton.IsEnabled = false;
            string device = (this.DeviceBox.SelectedItem as AudioOutputDevice)?.Name ?? AudioOutputDevice.DefaultName;
            try
            {
                ShowToast("Playing on " + device);
                await Task.Run(() => this.speechReader.SpeakNowAsync("This is a test of the caption translator voice.", 1f, CancellationToken.None));
            }
            catch (Exception exception)
            {
                Log.Error("Test voice failed.", exception);
                ShowToast("Test voice failed: " + exception.Message);
            }
            finally
            {
                this.TestVoiceButton.IsEnabled = true;
            }
        }
    }
}
