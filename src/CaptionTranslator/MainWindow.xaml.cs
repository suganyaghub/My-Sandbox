using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using CaptionTranslator.Captions;
using CaptionTranslator.Translation;

namespace CaptionTranslator
{
    public partial class MainWindow : Window
    {
        private const int maxLines = 500;

        private readonly ObservableCollection<CaptionLine> lines = new ObservableCollection<CaptionLine>();
        private readonly AppSettings settings;
        private readonly CaptionPipeline pipeline;
        private ICaptionSource? currentSource;
        private string sourceStatus = string.Empty;
        private string modelStatus = "Loading translation model…";

        public MainWindow()
        {
            this.settings = AppSettings.Load();
            this.pipeline = new CaptionPipeline(TimeSpan.FromMilliseconds(this.settings.QuietPeriodMilliseconds));

            InitializeComponent();

            this.LinesList.ItemsSource = this.lines;
            this.LinesList.FontSize = this.settings.FontSize;
            this.ShowGermanBox.IsChecked = this.settings.ShowGerman;
            this.TopmostBox.IsChecked = this.settings.Topmost;
            this.Topmost = this.settings.Topmost;
            ApplyShowGerman();

            this.pipeline.LineAdded += line => Dispatcher.BeginInvoke(() => AddLine(line));
            this.pipeline.PendingChanged += segment => Dispatcher.BeginInvoke(() => ShowPending(segment));
            this.pipeline.StatusChanged += status => Dispatcher.BeginInvoke(() =>
            {
                this.sourceStatus = status;
                UpdateStatus();
            });
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            this.SourceBox.SelectedIndex = this.settings.Source == AppSettings.LiveCaptionsSource ? 1 : 0;
            _ = LoadTranslatorAsync();
        }

        private void OnClosing(object? sender, CancelEventArgs e)
        {
            this.settings.Save();
            this.pipeline.Dispose();
        }

        private async Task LoadTranslatorAsync()
        {
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

                ITranslator translator = await Task.Run(() => new OpusMtTranslator(ModelFiles.Directory));
                this.pipeline.SetTranslator(translator);
                this.modelStatus = string.Empty;
                Log.Info("Translation model loaded.");
            }
            catch (Exception exception)
            {
                // Without a model the app still shows the German captions.
                Log.Error("Could not load translation model.", exception);
                this.modelStatus = $"Translation model not available ({exception.Message}). Folder: {ModelFiles.Directory}";
                this.pipeline.SetTranslator(null);
            }

            UpdateStatus();
        }

        private void OnSourceChanged(object sender, SelectionChangedEventArgs e)
        {
            string source = (this.SourceBox.SelectedItem as ComboBoxItem)?.Tag as string ?? AppSettings.TeamsSource;
            this.settings.Source = source;

            this.currentSource = source == AppSettings.LiveCaptionsSource
                ? new LiveCaptionsSource()
                : new TeamsCaptionSource(this.settings.TeamsCaptionContainerPattern);

            Log.Info("Source: " + this.currentSource.DisplayName);
            this.pipeline.Start(this.currentSource);
        }

        private void AddLine(CaptionLine line)
        {
            this.lines.Add(line);
            while (this.lines.Count > maxLines)
                this.lines.RemoveAt(0);

            this.Scroller.ScrollToEnd();
        }

        private void ShowPending(CaptionSegment? segment)
        {
            this.PendingText.Text = segment == null
                ? string.Empty
                : (segment.Speaker.Length > 0 ? segment.Speaker + ": " : string.Empty) + segment.Text + " …";
        }

        private void UpdateStatus()
        {
            this.StatusText.Text = this.modelStatus.Length > 0 ? this.modelStatus + "   |   " + this.sourceStatus : this.sourceStatus;
        }

        private void OnShowGermanClick(object sender, RoutedEventArgs e)
        {
            this.settings.ShowGerman = this.ShowGermanBox.IsChecked == true;
            ApplyShowGerman();
        }

        private void ApplyShowGerman()
            => this.LinesList.Tag = this.settings.ShowGerman ? Visibility.Visible : Visibility.Collapsed;

        private void OnTopmostClick(object sender, RoutedEventArgs e)
        {
            this.settings.Topmost = this.TopmostBox.IsChecked == true;
            this.Topmost = this.settings.Topmost;
        }

        private void OnSmallerClick(object sender, RoutedEventArgs e) => ChangeFontSize(-2);

        private void OnLargerClick(object sender, RoutedEventArgs e) => ChangeFontSize(2);

        private void ChangeFontSize(double delta)
        {
            this.settings.FontSize = Math.Clamp(this.settings.FontSize + delta, 10, 48);
            this.LinesList.FontSize = this.settings.FontSize;
        }

        private void OnCopyClick(object sender, RoutedEventArgs e)
        {
            StringBuilder builder = new StringBuilder();
            foreach (CaptionLine line in this.lines)
            {
                builder.Append(line.SpeakerLabel).AppendLine(line.EnglishText ?? string.Empty);
                builder.Append("    (").Append(line.GermanText).AppendLine(")");
            }

            Clipboard.SetText(builder.ToString());
            this.StatusText.Text = $"Copied {this.lines.Count} lines to the clipboard.";
        }

        private void OnClearClick(object sender, RoutedEventArgs e) => this.lines.Clear();

        private async void OnDiagnoseClick(object sender, RoutedEventArgs e)
        {
            ICaptionSource? source = this.currentSource;
            if (source == null)
                return;

            this.StatusText.Text = "Saving UI tree…";
            try
            {
                string dump = await Task.Run(source.DumpTree);
                string path = Log.WriteDump("uitree", dump);
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
                this.StatusText.Text = "UI tree saved: " + path;
            }
            catch (Exception exception)
            {
                Log.Error("Diagnose failed.", exception);
                this.StatusText.Text = "Diagnose failed: " + exception.Message;
            }
        }
    }
}
