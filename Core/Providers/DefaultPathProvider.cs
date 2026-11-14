using System;

namespace LyricSyncForPlex.Core.Providers;

public sealed class DefaultPathProvider : IPathProvider
{
    public string GetLyricsBasePath() => Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
}