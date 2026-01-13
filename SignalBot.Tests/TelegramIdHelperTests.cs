using SignalBot.Utils;
using Xunit;

namespace SignalBot.Tests;

public class TelegramIdHelperTests
{
    [Theory]
    [InlineData(-1003045070745, 3045070745)]
    [InlineData(-1002243348201, 2243348201)]
    [InlineData(-1001234567890, 1234567890)]
    [InlineData(1234567890, 1234567890)] // Already in API format
    [InlineData(0, 0)]
    public void ConvertToApiFormat_ConvertsCorrectly(long fullId, long expectedApiId)
    {
        // Act
        var result = TelegramIdHelper.ConvertToApiFormat(fullId);

        // Assert
        Assert.Equal(expectedApiId, result);
    }

    [Fact]
    public void ConvertToApiFormat_WithNegativeNonChannelId_ReturnsUnchanged()
    {
        // Arrange
        long nonChannelId = -123; // Too short to be a channel ID

        // Act
        var result = TelegramIdHelper.ConvertToApiFormat(nonChannelId);

        // Assert
        Assert.Equal(nonChannelId, result);
    }

    [Theory]
    [InlineData(3045070745, new long[] { -1003045070745, -1002243348201 }, true)]
    [InlineData(2243348201, new long[] { -1003045070745, -1002243348201 }, true)]
    [InlineData(9999999999, new long[] { -1003045070745, -1002243348201 }, false)]
    [InlineData(3045070745, new long[] { 3045070745 }, true)] // Direct match
    [InlineData(0, new long[] { 0 }, true)]
    public void IsMonitoredChannel_ChecksCorrectly(long peerId, long[] configuredIds, bool expected)
    {
        // Act
        var result = TelegramIdHelper.IsMonitoredChannel(peerId, configuredIds);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void IsMonitoredChannel_WithEmptyList_ReturnsFalse()
    {
        // Arrange
        long peerId = 3045070745;
        long[] configuredIds = [];

        // Act
        var result = TelegramIdHelper.IsMonitoredChannel(peerId, configuredIds);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsMonitoredChannel_WithMixedFormats_FindsMatch()
    {
        // Arrange - mix of full format and API format
        long peerId = 3045070745;
        long[] configuredIds = [-1002243348201, 3045070745, -1001234567890];

        // Act
        var result = TelegramIdHelper.IsMonitoredChannel(peerId, configuredIds);

        // Assert
        Assert.True(result);
    }
}
