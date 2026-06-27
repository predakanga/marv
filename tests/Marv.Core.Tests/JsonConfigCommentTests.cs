using Microsoft.Extensions.Configuration;
using Xunit;

namespace Marv.Core.Tests;

/// <summary>
/// Verifies that the built-in JSON configuration provider supports comments
/// and trailing commas — the features we rely on after removing Json5.Configuration.
/// </summary>
public class JsonConfigCommentTests : IDisposable
{
    private readonly string _tempFile = Path.GetTempFileName();

    public void Dispose() => File.Delete(_tempFile);

    [Fact]
    public void AddJsonFile_Supports_LineComments_And_TrailingCommas()
    {
        File.WriteAllText(_tempFile, """
            {
                // Line comment
                "Server": "irc.example.com",
                "Channels": [
                    "#general",
                    "#dev",
                ],
            }
            """);

        var config = new ConfigurationBuilder()
            .AddJsonFile(_tempFile, optional: false)
            .Build();

        Assert.Equal("irc.example.com", config["Server"]);
        Assert.Equal("#general", config["Channels:0"]);
        Assert.Equal("#dev", config["Channels:1"]);
    }

    [Fact]
    public void AddJsonFile_Supports_BlockComments()
    {
        File.WriteAllText(_tempFile, """
            {
                /* Block comment */
                "Nick": "Marv",
                "Port": 6697
            }
            """);

        var config = new ConfigurationBuilder()
            .AddJsonFile(_tempFile, optional: false)
            .Build();

        Assert.Equal("Marv", config["Nick"]);
        Assert.Equal("6697", config["Port"]);
    }
}
