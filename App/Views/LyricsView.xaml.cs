using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using LyricSyncForPlex.Controls;
using LyricSyncForPlex.Core.Clients;
using LyricSyncForPlex.Core.Lyrics;
using LyricSyncForPlex.Core.Utils;
using LyricSyncForPlex.Core.Models;

namespace LyricSyncForPlex.Views;

public sealed partial class LyricsView
{
    private const double SyncedLyricFontSize = 22;

    public MainWindow? MainWindow { get; set; }

    internal LyricsFile? LyricsFile;
    internal List<SyncedLyricLine>? Lrc;
    private List<SyncedLyricLine>? _transliteratedLrc;
    private LyricsFile? _pendingRomanizedLyrics;
    private string _trackKey = "";
    internal int CurrentLyricIndex = -1;
    private int _scrollOffset;
    private int _visibleLines = 3;
    private string? _localPath;
    private readonly List<TextBlock> _syncedBlocks = [];
    private readonly TranslateTransform _syncedPanelTransform = new();
    private Storyboard? _syncedLineTransition;
    private bool _syncedLineTransitionActive;
    private bool _showJapaneseTransliteration;
    private readonly JapaneseTransliterator _japaneseTransliterator = new();

    public LyricsView()
    {
        InitializeComponent();

        SyncedPanel.RenderTransform = _syncedPanelTransform;
        LySource.Tapped += OnLySourceTapped;
        InteractiveCursorGrid.SetIsClickable(LySource, true);
        LyUpdate.Click += OnLyUpdateClicked;
        SyncedPanel.PointerWheelChanged += OnSyncedScroll;

        SetSyncedLineCount(_visibleLines);
    }

    internal void SetShowJapaneseTransliteration(bool show)
    {
        _showJapaneseTransliteration = show;
    }

    private void RebuildSyncedBlocks()
    {
        SyncedPanel.Children.Clear();
        _syncedBlocks.Clear();

        for (int i = -_visibleLines; i <= _visibleLines; i++)
        {
            TextBlock tb = new()
            {
                Foreground = new SolidColorBrush(Colors.White),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextWrapping = TextWrapping.WrapWholeWords,
                TextAlignment = TextAlignment.Center
            };
            InteractiveCursorGrid.SetIsClickable(tb, true);
            int rel = i;
            tb.Tapped += async (_, _) => await SeekToRelativeAsync(rel);
            _syncedBlocks.Add(tb);
            SyncedPanel.Children.Add(tb);
        }
    }

    /// <summary>
    /// Gets local and remote lyrics. Prioritizes displaying local lyrics.
    /// If there is a discrepancy, local lyrics are overwritten and the user is prompted to refresh UI lyrics.
    /// </summary>
    /// <param name="artist">track artist</param>
    /// <param name="album">track album</param>
    /// <param name="title">track title</param>
    /// <param name="duration">track duration</param>
    /// <param name="trackKey">track trackKey</param>
    /// <param name="ct">cancellation token</param>
    public async Task FetchLyricsAsync(string artist, string album, string title, int duration, string trackKey, CancellationToken ct)
    {
        _trackKey = trackKey;
        LyUpdate.Visibility = Visibility.Collapsed;
        LyricsFile? local = null;

        try
        {
            local = await LyricsClient.GetLocalLyricsAsync(artist, album, title, ct);  // get local lyrics
            if (local is not null) SetLyrics(trackKey, local, true);                   // local lyrics found
            else SetNoLyrics("Searching for lyrics...");                               // local lyrics not found
        }
        catch
        {
            if (Lrc is null) SetNoLyrics("An error occurred while attempting to grab lyrics locally.");
        }

        try
        {
            LyricsFile? remote = await LyricsClient.GetRemoteLyricsAsync(artist, album, title, duration, ct, local);  // get remote lyrics
            if (local is null)
            {
                if (remote is not null) SetLyrics(trackKey, remote, false);                            // remote lyrics found
                else SetNoLyrics("No lyrics found.");                                                  // no local or remote lyrics found
            }

            // if remote lyrics are different from local lyrics display an option to refresh the displayed lyrics
            else if (remote is not null && remote != local)
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (trackKey != _trackKey) return;
                    LyUpdate.Visibility = Visibility.Visible;
                });
            }
        }
        catch
        {
            if (Lrc is null) SetNoLyrics("An error occurred while searching for lyrics.");
        }
    }

    public Task FetchTransliteratedLyricsAsync(string artist, string album, string title, string trackKey, CancellationToken ct)
    {
        if (LyricsFile is null || !_showJapaneseTransliteration) return Task.CompletedTask;

        try
        {
            LyricsFile sourceLyrics = LyricsFile;
            _pendingRomanizedLyrics = null;
            _ = Task.Run(async () =>
            {
                try
                {
                    LyricsFile? existing = await JapaneseTransliterator.GetLocalRomanizedLyricsAsync(artist, album, title, ct);
                    if (existing is not null)
                    {
                        ApplyRomanizedLyrics(trackKey, existing);
                    }

                    LyricsFile? transliterated = await _japaneseTransliterator.TransliterateAsync(sourceLyrics, ct);
                    if (transliterated is null)
                        return;

                    if (JapaneseTransliterator.HasSameContent(existing, transliterated))
                        return;

                    LyricsFile? written = JapaneseTransliterator.WriteRomanizedLyrics(transliterated, artist, album, title);
                    if (written is null)
                        return;

                    if (existing is null)
                    {
                        ApplyRomanizedLyrics(trackKey, written);
                        return;
                    }

                    DispatcherQueue.TryEnqueue(() =>
                    {
                        if (trackKey != _trackKey) return;
                        _pendingRomanizedLyrics = written;
                        LyUpdate.Visibility = Visibility.Visible;
                    });
                }
                catch
                {
                    // ignored
                }
            }, ct);
        }
        catch
        {
            // ignored
        }

        return Task.CompletedTask;
    }

    private void ApplyRomanizedLyrics(string trackKey, LyricsFile romanizedLyrics)
    {
        if (!string.IsNullOrWhiteSpace(romanizedLyrics.Synced))
        {
            var parsedTransliterated = LrcParser.Parse(romanizedLyrics.Synced);
            DispatcherQueue.TryEnqueue(() =>
            {
                if (trackKey != _trackKey) return;
                _transliteratedLrc = parsedTransliterated;
                if (!string.IsNullOrWhiteSpace(LyricsFile?.Synced) && Lrc is not null)
                {
                    UpdateSyncedLyricStack(CurrentLyricIndex + _scrollOffset);
                }
            });
        }
        else if (!string.IsNullOrWhiteSpace(romanizedLyrics.Plain))
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (trackKey != _trackKey) return;
                if (!string.IsNullOrWhiteSpace(LyricsFile?.Plain))
                {
                    PlainText.Text = CombinePlainWithTransliteration(LyricsFile.Plain, romanizedLyrics.Plain, _showJapaneseTransliteration);
                }
            });
        }
    }

    /// <summary>
    /// Updates UI lyrics.
    /// </summary>
    /// <param name="trackKey">track key</param>
    /// <param name="lyricsFile">track lyrics</param>
    /// <param name="local">true if lyrics are local</param>
    private void SetLyrics(string trackKey, LyricsFile lyricsFile, bool local)
    {
        LyricsFile = lyricsFile;
        _pendingRomanizedLyrics = null;
        // updates lyrics source
        DispatcherQueue.TryEnqueue(() =>
        {
            if (trackKey != _trackKey) return;

            if (!local)
            {
                LySource.Text = "Remote";
                return;
            }

            _localPath = lyricsFile.Path;
            int idx = _localPath!.IndexOf("lyrics", StringComparison.OrdinalIgnoreCase);
            string? disp = idx >= 0 ? _localPath[idx..].Replace('\\', '/') : _localPath;
            LySource.Text = "Local";
            ToolTipService.SetToolTip(LySource, disp);
        });

        // update synced lyrics
        if (!string.IsNullOrWhiteSpace(lyricsFile.Synced))
        {
            var parsed = LrcParser.Parse(lyricsFile.Synced);
            DispatcherQueue.TryEnqueue(() =>
            {
                if (trackKey != _trackKey) return;
                StopSyncedLineTransition();
                Lrc = parsed;
                _transliteratedLrc = null;
                _scrollOffset = 0;
                SyncedPanel.Visibility = Visibility.Visible;
                PlainScroll.Visibility = Visibility.Collapsed;
                UpdateSyncedLyricStack(0);
            });
        }

        // update plain lyrics
        else if (!string.IsNullOrWhiteSpace(lyricsFile.Plain))
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (trackKey != _trackKey) return;
                StopSyncedLineTransition();
                Lrc = null;
                _transliteratedLrc = null;
                SyncedPanel.Visibility = Visibility.Collapsed;
                PlainScroll.Visibility = Visibility.Visible;
                PlainText.Text = lyricsFile.Plain;
                PlainScroll.ChangeView(null, 0, null);
            });
        }
    }

    /// <summary>
    /// Updates UI lyrics when there are no lyrics.
    /// Can optionally set a custom message to appear
    /// </summary>
    /// <param name="msg">custom message</param>
    internal void SetNoLyrics(string? msg)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            StopSyncedLineTransition();
            Lrc = null;
            _transliteratedLrc = null;
            _pendingRomanizedLyrics = null;
            SyncedPanel.Visibility = Visibility.Visible;
            PlainScroll.Visibility = Visibility.Collapsed;
            if (_syncedBlocks.Count == 0) SetSyncedLineCount(_visibleLines);
            _syncedBlocks[_visibleLines].Text = msg ?? "No lyrics found.";
            LySource.Text = string.Empty;
            ToolTipService.SetToolTip(LySource, null);
            _localPath = null;
            LyUpdate.Visibility = Visibility.Collapsed;
        });
    }

    /// <summary>
    /// Display lyrics and update current index.
    /// </summary>
    /// <param name="predictedViewOffsetMs">current view offset position</param>
    public void UpdateProgress(int predictedViewOffsetMs)
    {
        if (LyricsFile is null) return;
        if (!string.IsNullOrWhiteSpace(LyricsFile.Synced) && Lrc is not null && Lrc.Count > 0)
        {
            int idx = LrcParser.IndexAt(Lrc, TimeSpan.FromMilliseconds(predictedViewOffsetMs));
            if (idx != CurrentLyricIndex)
            {
                int previousIndex = CurrentLyricIndex;
                bool wasFollowingPlayback = _scrollOffset == 0;
                CurrentLyricIndex = idx;
                _scrollOffset = 0;

                if (wasFollowingPlayback && previousIndex >= 0 && idx == previousIndex + 1 && StartSyncedLineTransition())
                    return;
            }

            if (!_syncedLineTransitionActive)
                UpdateSyncedLyricStack(CurrentLyricIndex + _scrollOffset);
        }
        else
        {
            if (string.IsNullOrWhiteSpace(LyricsFile.Plain)) return;

            CurrentLyricIndex = -1;
            string? cur = _syncedBlocks.Count > _visibleLines ? _syncedBlocks[_visibleLines].Text : "";
            UpdateNonSyncedLyricStack(!string.IsNullOrWhiteSpace(LyricsFile.Synced) ? "" : cur);
        }
    }

    /// <summary>
    /// Updates the lyric stack when synced lyrics exist. It displays the current lyric line along with surrounding lines.
    /// </summary>
    /// <param name="idx">the current lyric index</param>
    internal void UpdateSyncedLyricStack(int idx)
    {
        StopSyncedLineTransition();

        int highlightBlockIndex = -1;
        int curPos = CurrentLyricIndex - idx;
        if (curPos >= -_visibleLines && curPos <= _visibleLines)
        {
            highlightBlockIndex = curPos + _visibleLines;
        }

        for (int i = -_visibleLines; i <= _visibleLines; i++)
        {
            int idxBlock = i + _visibleLines;
            TextBlock tb = _syncedBlocks[idxBlock];
            string original = L(idx + i);
            string transliterated = _showJapaneseTransliteration ? LT(idx + i) : string.Empty;

            tb.Inlines.Clear();
            bool hasOriginal = !string.IsNullOrWhiteSpace(original);
            bool hasTransliterated = !string.IsNullOrWhiteSpace(transliterated) && !string.Equals(original, transliterated, StringComparison.Ordinal);
            bool isHighlight = idxBlock == highlightBlockIndex;

            tb.FontSize = SyncedLyricFontSize;
            tb.Opacity = isHighlight ? 1.0 : 0.85;
            tb.FontWeight = FontWeights.Normal;

            if (hasOriginal || !hasTransliterated)
            {
                Run origRun = new()
                {
                    Text = original,
                    FontWeight = isHighlight ? FontWeights.SemiBold : FontWeights.Light,
                    FontSize = SyncedLyricFontSize
                };
                tb.Inlines.Add(origRun);
            }

            if (hasTransliterated)
            {
                tb.Inlines.Add(new LineBreak());
                Run transRun = new()
                {
                    Text = transliterated,
                    FontWeight = isHighlight ? FontWeights.Normal : FontWeights.Light,
                    FontSize = SyncedLyricFontSize * 0.85,
                    Foreground = new SolidColorBrush(Colors.LightGray)
                };
                tb.Inlines.Add(transRun);
            }
        }

        return;

        string L(int i) => Lrc is not null && i >= 0 && i < Lrc.Count ? Lrc[i].Text : string.Empty;
        string LT(int i) => _transliteratedLrc is not null && i >= 0 && i < _transliteratedLrc.Count ? _transliteratedLrc[i].Text : string.Empty;
    }

    /// <summary>
    /// Updates the lyric stack when no synced lyrics exist. It displays the lyrics on the center line.
    /// </summary>
    /// <param name="lyrics">lyrics to use</param>
    private void UpdateNonSyncedLyricStack(string lyrics)
    {
        for (int i = 0; i < _syncedBlocks.Count; i++)
        {
            TextBlock tb = _syncedBlocks[i];
            tb.Text = string.Empty;
            tb.FontSize = SyncedLyricFontSize;
            tb.Opacity = 1.0;
            tb.FontWeight = FontWeights.Normal;
        }

        if (_syncedBlocks.Count <= _visibleLines) return;

        TextBlock center = _syncedBlocks[_visibleLines];
        center.Text = lyrics;
        center.FontSize = SyncedLyricFontSize;
        center.Opacity = 1.0;
        center.FontWeight = FontWeights.SemiBold;
    }

    private async Task SeekToRelativeAsync(int delta)
    {
        try
        {
            if (Lrc is null || Lrc.Count == 0) return;
            int baseIdx = CurrentLyricIndex + _scrollOffset;
            int targetIdx = baseIdx + delta;
            if (targetIdx < 0 || targetIdx >= Lrc.Count) return;

            int targetMs = (int)Lrc[targetIdx].T.TotalMilliseconds;
            CurrentLyricIndex = targetIdx;
            _scrollOffset = 0;
            UpdateSyncedLyricStack(CurrentLyricIndex);

            if (MainWindow is not null) await MainWindow.SeekToMsAsync(targetMs);
        }
        catch
        {
            // ignored
        }
    }

    private void OnSyncedScroll(object sender, PointerRoutedEventArgs e)
    {
        if (Lrc is null || Lrc.Count == 0) return;
        StopSyncedLineTransition();
        int delta = e.GetCurrentPoint(SyncedPanel).Properties.MouseWheelDelta;
        int dir = delta > 0 ? -1 : 1;
        int maxUp = -CurrentLyricIndex;
        int maxDown = Lrc.Count - 1 - CurrentLyricIndex;
        _scrollOffset = Math.Clamp(_scrollOffset + dir, maxUp, maxDown);
        UpdateSyncedLyricStack(CurrentLyricIndex + _scrollOffset);
        e.Handled = true;
    }

    internal void SetSyncedLineCount(int lines)
    {
        StopSyncedLineTransition();
        _visibleLines = Math.Max(0, lines);
        RebuildSyncedBlocks();

        if (LyricsFile is null) return;
        if (!string.IsNullOrWhiteSpace(LyricsFile.Synced) && Lrc is not null)
            UpdateSyncedLyricStack(CurrentLyricIndex + _scrollOffset);
        else
            UpdateNonSyncedLyricStack(_syncedBlocks.Count > _visibleLines ? _syncedBlocks[_visibleLines].Text : "");
    }

    /// <summary>
    /// Slides the current lyric stack up before showing the next line.
    /// </summary>
    /// <returns>True when the transition started.</returns>
    private bool StartSyncedLineTransition()
    {
        if (_syncedLineTransitionActive || _visibleLines == 0 || _syncedBlocks.Count <= _visibleLines)
            return false;

        double lineHeight = _syncedBlocks[_visibleLines].ActualHeight + SyncedPanel.Spacing;
        if (lineHeight <= 0)
            return false;

        _syncedLineTransitionActive = true;
        Storyboard storyboard = new();
        DoubleAnimation slide = new()
        {
            To = -lineHeight,
            Duration = TimeSpan.FromMilliseconds(360),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
            EnableDependentAnimation = true
        };
        Storyboard.SetTarget(slide, _syncedPanelTransform);
        Storyboard.SetTargetProperty(slide, "Y");
        storyboard.Children.Add(slide);

        storyboard.Completed += (_, _) =>
        {
            if (!ReferenceEquals(_syncedLineTransition, storyboard))
                return;

            storyboard.Stop();
            _syncedLineTransition = null;
            _syncedLineTransitionActive = false;
            _syncedPanelTransform.Y = 0;
            UpdateSyncedLyricStack(CurrentLyricIndex + _scrollOffset);
        };

        _syncedLineTransition = storyboard;
        storyboard.Begin();
        return true;
    }

    /// <summary>
    /// Stops an in-progress lyric slide and restores the stack to its normal position.
    /// </summary>
    private void StopSyncedLineTransition()
    {
        Storyboard? storyboard = _syncedLineTransition;
        _syncedLineTransition = null;
        _syncedLineTransitionActive = false;
        storyboard?.Stop();
        _syncedPanelTransform.Y = 0;
    }

    /// <summary>
    /// Opens explorer when LySource is clicked and LySource.Text = "Local".
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void OnLySourceTapped(object sender, TappedRoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_localPath)) return;
        try
        {
            ProcessStartInfo psi = new()
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{_localPath}\"",
                UseShellExecute = true
            };
            Process.Start(psi);
        }
        catch
        {
            // ignored
        }
    }

    /// <summary>
    /// Refreshes UI lyrics with local lyrics.
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private async void OnLyUpdateClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_pendingRomanizedLyrics is not null)
            {
                ApplyRomanizedLyrics(_trackKey, _pendingRomanizedLyrics);
                _pendingRomanizedLyrics = null;
                LyUpdate.Visibility = Visibility.Collapsed;
                return;
            }

            if (string.IsNullOrWhiteSpace(_localPath)) return;

            string lyrics = await File.ReadAllTextAsync(_localPath);
            LyricsFile local = _localPath.EndsWith(".lrc", StringComparison.OrdinalIgnoreCase)
                ? new LyricsFile(lyrics, null, _localPath)
                : new LyricsFile(null, lyrics, _localPath);
            SetLyrics(_trackKey, local, true);
            _transliteratedLrc = null;

            LyUpdate.Visibility = Visibility.Collapsed;
        }
        catch
        {
            // ignored
        }
    }

    private static string CombinePlainWithTransliteration(string original, string transliterated, bool showTransliteration)
    {
        if (!showTransliteration || string.IsNullOrWhiteSpace(transliterated))
            return original;

        string[] originalLines = original.Replace("\r\n", "\n").Split('\n');
        string[] transliteratedLines = transliterated.Replace("\r\n", "\n").Split('\n');

        StringBuilder sb = new();
        int max = Math.Max(originalLines.Length, transliteratedLines.Length);
        for (int i = 0; i < max; i++)
        {
            string o = i < originalLines.Length ? originalLines[i] : string.Empty;
            string t = i < transliteratedLines.Length ? transliteratedLines[i] : string.Empty;
            if (!string.IsNullOrWhiteSpace(o))
            {
                sb.AppendLine(o);
            }
            if (!string.IsNullOrWhiteSpace(t) && !string.Equals(o, t, StringComparison.Ordinal))
            {
                sb.AppendLine(t);
            }
            if (i < max - 1)
            {
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }
}
