using System;
using LyricSyncForPlex.Core.Utils;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LyricSyncForPlex.Tests.Lyrics;

[TestClass]
public class LrcParserTests
{
    [TestMethod]
    public void Parse_SortsTimestampsAndPreservesFractionPrecision()
    {
        var lines = LrcParser.Parse("[ar:Artist]\r\n[00:02.05] last  \r\n[00:01.5]middle\ninvalid\n[00:01]first");

        Assert.HasCount(3, lines);
        Assert.AreEqual(("first", 1000d), (lines[0].Text, lines[0].T.TotalMilliseconds));
        Assert.AreEqual(("middle", 1500d), (lines[1].Text, lines[1].T.TotalMilliseconds));
        Assert.AreEqual(("last", 2050d), (lines[2].Text, lines[2].T.TotalMilliseconds));
    }

    [TestMethod]
    [DataRow(999, -1)]
    [DataRow(1000, 1)]
    [DataRow(1999, 1)]
    [DataRow(2000, 2)]
    public void IndexAt_FindsLastLineAtOrBeforePlaybackPosition(int position, int expectedIndex)
    {
        var lines = LrcParser.Parse("[00:01.00]first\n[00:01.00]second\n[00:02.00]third");

        Assert.AreEqual(expectedIndex, LrcParser.IndexAt(lines, TimeSpan.FromMilliseconds(position)));
    }

    [TestMethod]
    public void CopyLrcTimeSpans_KeepsOriginalTextForBlankOrMissingReplacement()
    {
        var source = LrcParser.Parse("[00:01.05]first\n[00:02.00]second\n[00:03.00]third");

        string result = LrcParser.CopyLrcTimeSpans(source, "replacement\n ");

        Assert.AreEqual(
            "[00:01.05]replacement\n[00:02.00]second\n[00:03.00]third\n",
            result.ReplaceLineEndings("\n"));
    }
}
