using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using LyricSyncForPlex.Core.Clients;
using LyricSyncForPlex.Core.Models;
using LyricSyncForPlex.Tests.TestHelpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LyricSyncForPlex.Tests.Clients;

[TestClass]
public class PlexApiClientTests
{
    private const string Server = "http://server.test";
    private const string Player = "http://player.test";
    private const string ClientId = "player-id";
    private const string Token = "test-token";

    [TestMethod]
    public async Task GetSession_PrefersPlayingPlexampAndReadsMetadata()
    {
        using HttpClient http = new(new FakeHttpMessageHandler(request =>
        {
            AssertRequest(request, HttpMethod.Get, Server + "/status/sessions");
            Assert.AreEqual("no-cache", request.Headers.CacheControl?.ToString());
            return Xml("""
                <MediaContainer>
                  <Track title="Other player"><Player product="Plex" state="playing" /></Track>
                  <Track title="Paused"><Player product="Plexamp" state="paused" /></Track>
                  <Track ratingKey="42" grandparentTitle="Artist" parentTitle="Album" parentYear="2024"
                         title="Playing" viewOffset="2000" duration="180000" thumb="/thumb" art="/art">
                    <Player product="plexamp" state="Playing" address="http://player.test"
                            machineIdentifier="player-id" />
                    <TranscodeSession time="1234" />
                  </Track>
                </MediaContainer>
                """);
        }));
        PlexApiClient client = new(Server + "/", Token, http);

        PlexampSession session = await client.GetPlexampSession(CancellationToken.None);

        Assert.IsNotNull(session);
        Assert.AreEqual("42", session.RatingKey);
        Assert.AreEqual("Artist", session.Artist);
        Assert.AreEqual("Album", session.Album);
        Assert.AreEqual(2024, session.AlbumYear);
        Assert.AreEqual("Playing", session.Title);
        Assert.AreEqual(1234, session.ViewOffsetMs);
        Assert.AreEqual(180000, session.DurationMs);
        Assert.AreEqual("Playing", session.State);
        Assert.AreEqual(Player, session.ClientUrl);
        Assert.AreEqual(ClientId, session.ClientId);
        Assert.AreEqual(Server + "/thumb?X-Plex-Token=" + Token, session.AlbumArtUrl);
    }

    [TestMethod]
    public async Task GetSession_InvalidOptionalMetadataUsesFallbacks()
    {
        using HttpClient http = new(new FakeHttpMessageHandler(_ => Xml("""
            <Track title="Paused" parentYear="0" duration="unknown" viewOffset="3500" art="/art">
              <Player product="Plexamp" state="paused" />
              <TranscodeSession time="unknown" />
            </Track>
            """)));
        PlexApiClient client = new(Server, Token, http);

        PlexampSession session = await client.GetPlexampSession(CancellationToken.None);

        Assert.AreEqual(new PlexampSession(
            "", "", "", null, "Paused", 3500, 0, "paused", "", "",
            Server + "/art?X-Plex-Token=" + Token), session);
    }

    [TestMethod]
    public async Task GetSession_MinimalTrackKeepsMissingTimingAndArtworkEmpty()
    {
        using HttpClient http = new(new FakeHttpMessageHandler(_ => Xml("""
            <Track grandparentTitle="Artist" viewOffset="invalid">
              <Player product="Plexamp" />
            </Track>
            """)));
        PlexApiClient client = new(Server, Token, http);

        PlexampSession session = await client.GetPlexampSession(CancellationToken.None);

        Assert.AreEqual(new PlexampSession("", "Artist", "", null, "", 0, 0, "", "", "", ""), session);
    }

    [TestMethod]
    [DataRow("""<Track title="Other player"><Player product="Plex" /></Track>""")]
    [DataRow("""<Track><Player product="Plexamp" /></Track>""")]
    public async Task GetSession_NoUsablePlexampTrackReturnsNull(string xml)
    {
        using HttpClient http = new(new FakeHttpMessageHandler(_ => Xml(xml)));
        PlexApiClient client = new(Server, Token, http);

        Assert.IsNull(await client.GetPlexampSession(CancellationToken.None));
    }

    [TestMethod]
    [DataRow("pause", HttpStatusCode.NoContent, true)]
    [DataRow("play", HttpStatusCode.ServiceUnavailable, false)]
    [DataRow("skipNext", HttpStatusCode.NoContent, true)]
    [DataRow("skipPrevious", HttpStatusCode.NoContent, true)]
    public async Task PlaybackCommand_RoutesToPlayerAndReportsResponse(
        string command, HttpStatusCode status, bool expected)
    {
        using HttpClient http = new(new FakeHttpMessageHandler(request =>
        {
            AssertPlayerRequest(request, command);
            return new HttpResponseMessage(status);
        }));
        PlexApiClient client = new(Server, Token, http);
        CancellationToken ct = CancellationToken.None;

        bool result = await (command switch
        {
            "pause" => client.PauseAsync(Player, ClientId, ct),
            "play" => client.PlayAsync(Player, ClientId, ct),
            "skipNext" => client.SkipNextAsync(Player, ClientId, ct),
            "skipPrevious" => client.SkipPreviousAsync(Player, ClientId, ct),
            _ => throw new ArgumentOutOfRangeException(nameof(command))
        });

        Assert.AreEqual(expected, result);
    }

    [TestMethod]
    [DataRow(-10, 0)]
    [DataRow(1250, 1250)]
    public async Task Seek_SendsNonnegativeOffset(int offset, int expected)
    {
        using HttpClient http = new(new FakeHttpMessageHandler(request =>
        {
            AssertPlayerRequest(request, $"seekTo?offset={expected}");
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }));
        PlexApiClient client = new(Server, Token, http);

        Assert.IsTrue(await client.SeekToAsync(Player, ClientId, offset, CancellationToken.None));
    }

    [TestMethod]
    [DataRow(1.25, "1.25")]
    [DataRow(3.0, "2")]
    public async Task SetSpeed_ClampsAndFormatsSpeedForPlex(double speed, string expected)
    {
        using HttpClient http = new(new FakeHttpMessageHandler(request =>
        {
            AssertPlayerRequest(request, $"setParameters?speed={expected}&type=music");
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }));
        PlexApiClient client = new(Server, Token, http);
        CultureInfo originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            Assert.IsTrue(await client.SetPlaybackSpeedAsync(Player, ClientId, speed, CancellationToken.None));
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [TestMethod]
    public async Task MissingIdentifiers_DoNotSendRequests()
    {
        using HttpClient http = new(new FakeHttpMessageHandler(_ =>
            throw new AssertFailedException("A request was sent with no identifier.")));
        PlexApiClient client = new(Server, Token, http);
        CancellationToken ct = CancellationToken.None;

        Assert.IsFalse(await client.PauseAsync(Player, "", ct));
        Assert.IsFalse(await client.SeekToAsync(Player, "", 1000, ct));
        Assert.IsFalse(await client.SetPlaybackSpeedAsync(Player, "", 1.25, ct));
        Assert.IsFalse(await client.GetTrackLikedAsync("", ct));
        await client.SetTrackLikedAsync("", true, ct);
    }

    [TestMethod]
    [DataRow(HttpStatusCode.OK, "10.0", true)]
    [DataRow(HttpStatusCode.OK, "8.0", false)]
    [DataRow(HttpStatusCode.NotFound, "10.0", false)]
    public async Task GetTrackLiked_RequiresSuccessfulResponseAndFullRating(
        HttpStatusCode status, string rating, bool expected)
    {
        using HttpClient http = new(new FakeHttpMessageHandler(request =>
        {
            AssertRequest(request, HttpMethod.Get, Server + "/library/metadata/42");
            return new HttpResponseMessage(status) { Content = new StringContent($"<Track userRating=\"{rating}\" />") };
        }));
        PlexApiClient client = new(Server, Token, http);

        Assert.AreEqual(expected, await client.GetTrackLikedAsync("42", CancellationToken.None));
    }

    [TestMethod]
    [DataRow(true, 10)]
    [DataRow(false, -1)]
    public async Task SetTrackLiked_SendsEncodedKeyAndRating(bool liked, int expectedRating)
    {
        int requests = 0;
        using HttpClient http = new(new FakeHttpMessageHandler(request =>
        {
            requests++;
            AssertRequest(request, HttpMethod.Put,
                $"{Server}/:/rate?key=a%26b&identifier=com.plexapp.plugins.library&rating={expectedRating}");
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }));
        PlexApiClient client = new(Server, Token, http);

        await client.SetTrackLikedAsync("a&b", liked, CancellationToken.None);

        Assert.AreEqual(1, requests);
    }

    private static HttpResponseMessage Xml(string xml) =>
        new(HttpStatusCode.OK) { Content = new StringContent(xml) };

    private static void AssertPlayerRequest(HttpRequestMessage request, string command)
    {
        AssertRequest(request, HttpMethod.Get, Player + "/player/playback/" + command);
        Assert.AreEqual(ClientId, request.Headers.GetValues("X-Plex-Client-Identifier").Single());
    }

    private static void AssertRequest(HttpRequestMessage request, HttpMethod method, string uri)
    {
        Assert.AreEqual(method, request.Method);
        Assert.AreEqual(uri, request.RequestUri!.AbsoluteUri);
        Assert.AreEqual(Token, request.Headers.GetValues("X-Plex-Token").Single());
    }
}
