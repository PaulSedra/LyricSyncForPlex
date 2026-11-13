using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlexLyricSync.Core.Models;

namespace PlexLyricSync.Tests.Models;

[TestClass]
public class ConfigTests
{
    [TestMethod]
    [DataRow(0.25, 0.5)]
    [DataRow(0.5, 0.5)]
    [DataRow(1.25, 1.25)]
    [DataRow(2.0, 2.0)]
    [DataRow(2.5, 2.0)]
    public void ClampPlaybackSpeed_boundsPlaybackSpeed(double input, double expected)
    {
        Assert.AreEqual(expected, Config.ClampPlaybackSpeed(input));
    }

    [TestMethod]
    public void ClampPlaybackSpeed_invalidPlaybackSpeed_returnsDefault()
    {
        Assert.AreEqual(1.0, Config.ClampPlaybackSpeed(double.NaN));
        Assert.AreEqual(1.0, Config.ClampPlaybackSpeed(double.PositiveInfinity));
    }
}
