namespace LyricSyncForPlex.Core.Models;

public sealed record PlexampSession(
    string RatingKey,
    string Artist,
    string Album,
    int? AlbumYear,
    string Title,
    int ViewOffsetMs,
    int DurationMs,
    string State,
    string ClientUrl,
    string ClientId,
    string AlbumArtUrl
);
