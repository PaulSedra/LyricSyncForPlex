using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using LyricSyncForPlex.Core.Lyrics;
using LyricSyncForPlex.Core.Models;
using LyricSyncForPlex.Core.Providers;
using LyricSyncForPlex.Core.Utils;

namespace LyricSyncForPlex.Tests.Lyrics;

[TestClass]
[DoNotParallelize]
public class JapaneseTransliteratorTests
{
    private string _testRoot;

    [TestCleanup]
    public void TearDown()
    {
        if (_testRoot is null)
            return;

        LrcParser.SetPathProvider(new DefaultPathProvider());
        if (Directory.Exists(_testRoot))
            Directory.Delete(_testRoot, recursive: true);
    }

    [TestMethod]
    public async Task TransliterateAsync_plainJapaneseLyrics_returnsRomaji()
    {
        JapaneseTransliterator transliterator = new();
        LyricsFile lyrics = new(Synced: null, Plain: "こんにちは\r\n\r\nEnglish refrain", Path: null);

        LyricsFile result = await transliterator.TransliterateAsync(lyrics, CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.AreEqual("konnichiha\n\nEnglish refrain", result.Plain!.ReplaceLineEndings("\n"));
    }

    [TestMethod]
    public async Task TransliterateAsync_syncedJapaneseLyrics_keepsTimestamps()
    {
        JapaneseTransliterator transliterator = new();
        LyricsFile lyrics = new(Synced: "[00:01.00]こんにちは", Plain: null, Path: null);

        LyricsFile result = await transliterator.TransliterateAsync(lyrics, CancellationToken.None);

        Assert.IsNotNull(result);
        string synced = result.Synced!.Replace("\r\n", "\n").Trim();
        Assert.AreEqual("[00:01.00]konnichiha", synced);
    }

    [TestMethod]
    public async Task TransliterateAsync_kanjiHeavyLyrics_returnsSpacedRomaji()
    {
        JapaneseTransliterator transliterator = new();
        LyricsFile lyrics = new(Synced: null, Plain: "僕達は操り人形じゃない", Path: null);

        LyricsFile result = await transliterator.TransliterateAsync(lyrics, CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.AreEqual("bokutachi ha ayatsuri ningyou ja nai", result.Plain);
    }

    [TestMethod]
    public async Task TransliterateAsync_nonJapaneseLyrics_returnsNull()
    {
        JapaneseTransliterator transliterator = new();
        LyricsFile lyrics = new(Synced: null, Plain: "hello", Path: null);

        LyricsFile result = await transliterator.TransliterateAsync(lyrics, CancellationToken.None);

        Assert.IsNull(result);
    }

    [TestMethod]
    public async Task TransliterateAsync_JapaneseTextWithoutValidTimestampsReturnsNull()
    {
        JapaneseTransliterator transliterator = new();
        LyricsFile lyrics = new(Synced: "[ar:こんにちは]", Plain: null, Path: null);

        Assert.IsNull(await transliterator.TransliterateAsync(lyrics, CancellationToken.None));
    }

    [TestMethod]
    public async Task TransliterateAsync_CancelledSyncedRequestStopsConversion()
    {
        JapaneseTransliterator transliterator = new();
        LyricsFile lyrics = new(Synced: "[00:01.00]こんにちは", Plain: null, Path: null);

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            transliterator.TransliterateAsync(lyrics, new CancellationToken(canceled: true)));
    }

    [TestMethod]
    [DataRow("[00:01.00]konnichiha", null, ".lrc")]
    [DataRow(null, "konnichiha", ".txt")]
    public async Task RomanizedCache_RoundTripsLyricsWithoutReplacingTheOriginal(
        string synced, string plain, string extension)
    {
        _testRoot = Path.Combine(Path.GetTempPath(), "LyricSyncForPlexTests", Guid.NewGuid().ToString("N"));
        LrcParser.SetPathProvider(new TestPathProvider(_testRoot));
        LyricsFile original = LrcParser.WriteLyrics(new LyricsFile(null, "こんにちは", null), "Artist", "Album", "Title");

        LyricsFile written = JapaneseTransliterator.WriteRomanizedLyrics(
            new LyricsFile(synced, plain, null), "Artist", "Album", "Title");
        LyricsFile cached = await JapaneseTransliterator.GetLocalRomanizedLyricsAsync(
            "Artist", "Album", "Title", CancellationToken.None);
        LyricsFile source = await LrcParser.ReadLyricsAsync("Artist", "Album", "Title", CancellationToken.None);

        Assert.IsNotNull(written);
        Assert.AreEqual("Title_romanized" + extension, Path.GetFileName(written.Path));
        Assert.AreEqual(new LyricsFile(synced, plain, written.Path), cached);
        Assert.AreEqual(original, source);
    }

    [TestMethod]
    [DataRow("[00:01]line", "[00:01]line", null, null, true)]
    [DataRow("[00:01]line", "[00:02]line", null, null, false)]
    [DataRow(null, null, "Verse", "verse", false)]
    public void HasSameContent_IgnoresPathsButComparesTextAndTiming(
        string leftSynced, string rightSynced, string leftPlain, string rightPlain, bool expected)
    {
        LyricsFile source = new(leftSynced, leftPlain, "source");
        LyricsFile cached = new(rightSynced, rightPlain, "cache");

        Assert.AreEqual(expected, JapaneseTransliterator.HasSameContent(source, cached));
    }
}
