using JetBrains.Annotations;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using LyricSyncForPlex.Core.Clients;
using LyricSyncForPlex.Core.Models;
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
[TestSubject(typeof(LyricsClient))]
public class LyricsClientTest
{

    [TestMethod]
    public async Task GetRemoteLyricsAsync_localSyncedAndRemotePlain_doesNotWritePlainLyrics()
    {
        string testRoot = Path.Combine(Path.GetTempPath(), "LyricSyncForPlexTests", Guid.NewGuid().ToString("N"));
        LrcParser.SetPathProvider(new TestPathProvider(testRoot));

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

        LyricsFile? result = await LyricsClient.GetRemoteLyricsAsync(
            artist,
            album,
            title,
            duration: 60,
            ct: CancellationToken.None,
            existingLocalLyrics: local,
            http: new HttpClient(handler));

        Assert.IsNull(result);
        Assert.IsFalse(File.Exists(Path.Combine(testRoot, "PlexLyricSync", "lyrics", artist, album, title + ".txt")));
    }
}
