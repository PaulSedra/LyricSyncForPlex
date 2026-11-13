using System;
using System.Threading;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Navigation;
using PlexLyricSync.Utils;
using PlexLyricSync.Core.Models;

namespace PlexLyricSync.Views;

public sealed partial class SettingsPage
{
    private MainWindow? _mainWindow;
    private Config _config = new();
    private bool _loading;
    private readonly DispatcherTimer _debounceTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };

    public SettingsPage()
    {
        InitializeComponent();
        _debounceTimer.Tick += DebounceTimer_Tick;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        DetachMainWindow();
        _mainWindow = e.Parameter as MainWindow;
        _config = ConfigLoader.LoadExisting();

        _loading = true;
        NavTabs.SelectedIndex = 0;

        PlexUrlBox.Text = _config.PlexBaseUrl;
        PlexTokenBox.Text = _config.PlexToken;
        SetPlaybackSpeedUi(_mainWindow?.PlaybackSpeed ?? _config.PlaybackSpeed);
        SyncedLinesBox.Value = _config.SyncedLyricLines;
        ShowJapaneseTransliterationSwitch.IsOn = _config.ShowJapaneseTransliteration;
        _loading = false;

    }

    private void SettingChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading || _mainWindow is null) return;

        _config.PlexBaseUrl = PlexUrlBox.Text;
        _config.PlexToken = PlexTokenBox.Text;
        SaveConfigAndRestart();
    }

    private void ShowJapaneseTransliterationToggled(object sender, RoutedEventArgs e)
    {
        if (_loading || _mainWindow is null) return;

        _config.ShowJapaneseTransliteration = ShowJapaneseTransliterationSwitch.IsOn;
        SaveConfigAndRestart();
    }

    private void SaveConfigAndRestart()
    {
        ConfigLoader.SaveConfig(_config);


        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    private void SyncedLinesChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_loading || _mainWindow is null) return;

        _config.SyncedLyricLines = (int)sender.Value;
        SaveConfigAndRestart();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        DetachMainWindow();

        base.OnNavigatedFrom(e);
    }

    private void PlaybackSpeedChanged(object sender, RangeBaseValueChangedEventArgs args)
    {
        if (_loading || _mainWindow is null) return;

        double playbackSpeed = Config.ClampPlaybackSpeed(args.NewValue);
        SetPlaybackSpeedUi(playbackSpeed);

        _config.PlaybackSpeed = playbackSpeed;
        ConfigLoader.SaveConfig(_config);
        _mainWindow.PlaybackSpeed = playbackSpeed;
        _ = _mainWindow.ApplyPlaybackSpeedAsync(CancellationToken.None);
    }

    private void SetPlaybackSpeedUi(double playbackSpeed)
    {
        playbackSpeed = Config.ClampPlaybackSpeed(playbackSpeed);
        PlaybackSpeedSlider.Value = playbackSpeed;
        PlaybackSpeedLabel.Text = playbackSpeed == 1.0 ? "1x" : $"{playbackSpeed:0.##}x";
    }

    private void DebounceTimer_Tick(object? sender, object e)
    {
        _debounceTimer.Stop();
        if (string.IsNullOrWhiteSpace(_config.PlexBaseUrl) || string.IsNullOrWhiteSpace(_config.PlexToken)) return;
        UpdateConfigAsync();
    }

    private void UpdateConfigAsync()
    {
        if (_mainWindow is null) return;

        _mainWindow.PollCts?.Cancel();
        _mainWindow.UiTimer.Stop();
        _ = _mainWindow.InitAsync();
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        MainWindow? mainWindow = _mainWindow;
        DetachMainWindow();
        mainWindow?.CloseSettings();
    }

    private void DetachMainWindow()
    {
        _mainWindow = null;
    }

    private void NavTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        PlexPanel.Visibility = Visibility.Collapsed;
        PlaybackPanel.Visibility = Visibility.Collapsed;
        LyricsPanel.Visibility = Visibility.Collapsed;

        switch (NavTabs.SelectedIndex)
        {
            case 0:
                PlexPanel.Visibility = Visibility.Visible;
                break;
            case 1:
                PlaybackPanel.Visibility = Visibility.Visible;
                break;
            case 2:
                LyricsPanel.Visibility = Visibility.Visible;
                break;
        }
    }

}
