using JetBrains.Annotations;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using LyricSyncForPlex.Core.Clients;
using LyricSyncForPlex.Core.Models;
using LyricSyncForPlex.Core.Providers;
using LyricSyncForPlex.Core.Utils;
using LyricSyncForPlex.Tests.TestHelpers;
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LyricSyncForPlex.Tests.Clients;

[TestClass]
[DoNotParallelize]
[TestSubject(typeof(LyricsClient))]
public class LyricsClientTest
{
    private string _testRoot;

    [TestInitialize]
    public void SetUp()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), "LyricSyncForPlexTests", Guid.NewGuid().ToString("N"));
        LrcParser.SetPathProvider(new TestPathProvider(_testRoot));
    }

    [TestCleanup]
    public void TearDown()
    {
        LrcParser.SetPathProvider(new DefaultPathProvider());
        if (Directory.Exists(_testRoot))
            Directory.Delete(_testRoot, recursive: true);
    }

    [TestMethod]
    public async Task GetRemoteLyricsAsync_localSyncedAndRemotePlain_doesNotWritePlainLyrics()
    {
        const string artist = "Artist";
        const string album = "Album";
        const string title = "Title";
        LyricsFile local = LrcParser.WriteLyrics(new LyricsFile("[00:01.00]こんにちは", null, null), artist, album, title)!;

        FakeHttpMessageHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{ "syncedLyrics": null, "plainLyrics": "plain lyrics" }""",
                Encoding.UTF8,
                "application/json")
        });

        using HttpClient http = new(handler);
        LyricsFile result = await LyricsClient.GetRemoteLyricsAsync(
            artist,
            album,
            title,
            duration: 60,
            ct: CancellationToken.None,
            existingLocalLyrics: local,
            http: http);

        Assert.IsNull(result);
        Assert.IsFalse(File.Exists(Path.Combine(_testRoot, "PlexLyricSync", "lyrics", artist, album, title + ".txt")));
    }

    [TestMethod]
    public async Task GetRemoteLyricsAsync_NullAlbumMatchRetriesWithoutAlbumAndCachesPlainLyrics()
    {
        const string artist = "A&B";
        const string album = "Live #1";
        const string title = "First / Second";
        const string trackQuery = "track_name=First%20%2F%20Second&duration=123";
        int requests = 0;
        using HttpClient http = new(new FakeHttpMessageHandler(request =>
        {
            Assert.AreEqual(HttpMethod.Get, request.Method);
            Assert.AreEqual("https://lrclib.net/api/get", request.RequestUri!.GetLeftPart(UriPartial.Path));
            requests++;
            if (requests == 1)
            {
                Assert.AreEqual("?artist_name=A%26B&album_name=Live%20%231&" + trackQuery,
                    request.RequestUri.Query);
                return Json("null");
            }

            Assert.AreEqual("?artist_name=A%26B&" + trackQuery, request.RequestUri.Query);
            return Json("""{"plainLyrics":"first line"}""");
        }));

        LyricsFile remote = await LyricsClient.GetRemoteLyricsAsync(
            artist, album, title, 123, CancellationToken.None, http: http);
        LyricsFile local = await LyricsClient.GetLocalLyricsAsync(artist, album, title, CancellationToken.None);

        Assert.AreEqual(2, requests);
        Assert.IsNotNull(remote);
        Assert.AreEqual(Path.Combine(_testRoot, "PlexLyricSync", "lyrics", artist, album, "First _ Second.txt"),
            remote.Path);
        Assert.AreEqual(new LyricsFile(null, "first line", remote.Path), local);
    }

    [TestMethod]
    public async Task GetRemoteLyricsAsync_SyncedMatchTakesPrecedenceOverCachedPlainLyrics()
    {
        const string synced = "[00:01.00]Timed lyrics";
        LyricsFile plain = LrcParser.WriteLyrics(new LyricsFile(null, "Untimed lyrics", null), "Artist", "Album", "Track");
        using HttpClient http = new(new FakeHttpMessageHandler(_ =>
            Json("""{"syncedLyrics":"[00:01.00]Timed lyrics","plainLyrics":"Untimed lyrics"}""")));

        LyricsFile remote = await LyricsClient.GetRemoteLyricsAsync(
            "Artist", "Album", "Track", 123, CancellationToken.None, plain, http);
        LyricsFile local = await LyricsClient.GetLocalLyricsAsync("Artist", "Album", "Track", CancellationToken.None);

        Assert.IsNotNull(remote);
        Assert.AreEqual(new LyricsFile(synced, null, Path.ChangeExtension(plain.Path, ".lrc")), remote);
        Assert.AreEqual(remote, local);
    }

    [TestMethod]
    [DataRow(false, HttpStatusCode.NotFound, null, 1)]
    [DataRow(true, HttpStatusCode.ServiceUnavailable, null, 2)]
    [DataRow(true, HttpStatusCode.OK, "null", 2)]
    [DataRow(false, HttpStatusCode.OK, """{"syncedLyrics":" ","plainLyrics":""}""", 1)]
    public async Task GetRemoteLyricsAsync_NoUsableMatchDoesNotCache(
        bool tryWithoutAlbum, HttpStatusCode status, string json, int expectedRequests)
    {
        int requests = 0;
        using HttpClient http = new(new FakeHttpMessageHandler(_ =>
            ++requests == 1 && tryWithoutAlbum ? Json("null") : Json(json, status)));

        Assert.IsNull(await LyricsClient.GetRemoteLyricsAsync(
            "Artist", "Album", "Track", 123, CancellationToken.None, http: http));
        Assert.AreEqual(expectedRequests, requests);
        Assert.IsNull(await LyricsClient.GetLocalLyricsAsync("Artist", "Album", "Track", CancellationToken.None));
        Assert.IsEmpty(Directory.GetFiles(_testRoot, "*", SearchOption.AllDirectories));
    }

    [TestMethod]
    public async Task GetLocalLyricsAsync_WithoutArtistOrTitleLeavesCacheUntouched()
    {
        Assert.IsNull(await LyricsClient.GetLocalLyricsAsync(" ", "Album", "", CancellationToken.None));
        Assert.IsFalse(Directory.Exists(_testRoot));
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json ?? "", Encoding.UTF8, "application/json") };
}
