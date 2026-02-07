using FluentAssertions;
using Xunit;

namespace NuGetTrends.Web.Tests;

public class ExtractGitHubUrlTests
{
    [Theory]
    [InlineData("https://github.com/getsentry/sentry-dotnet", "https://github.com/getsentry/sentry-dotnet")]
    [InlineData("https://github.com/owner/repo", "https://github.com/owner/repo")]
    public void ValidGitHubUrl_ReturnsRepoUrl(string input, string expected)
    {
        TrendingPackagesCache.ExtractGitHubUrl(input).Should().Be(expected);
    }

    [Fact]
    public void GitSuffix_IsStripped()
    {
        TrendingPackagesCache.ExtractGitHubUrl("https://github.com/owner/repo.git")
            .Should().Be("https://github.com/owner/repo");
    }

    [Theory]
    [InlineData("https://github.com/owner/repo/issues")]
    [InlineData("https://github.com/owner/repo/tree/main/src")]
    [InlineData("https://github.com/owner/repo/blob/main/README.md")]
    public void DeepPaths_StrippedToRepoUrl(string input)
    {
        TrendingPackagesCache.ExtractGitHubUrl(input)
            .Should().Be("https://github.com/owner/repo");
    }

    [Fact]
    public void CaseInsensitiveHost_Works()
    {
        TrendingPackagesCache.ExtractGitHubUrl("https://GitHub.COM/owner/repo")
            .Should().Be("https://github.com/owner/repo");
    }

    [Theory]
    [InlineData("https://gitlab.com/owner/repo")]
    [InlineData("https://bitbucket.org/owner/repo")]
    [InlineData("https://example.com/owner/repo")]
    public void NonGitHubUrls_ReturnNull(string input)
    {
        TrendingPackagesCache.ExtractGitHubUrl(input).Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void NullOrEmpty_ReturnsNull(string? input)
    {
        TrendingPackagesCache.ExtractGitHubUrl(input).Should().BeNull();
    }

    [Fact]
    public void InvalidUrl_ReturnsNull()
    {
        TrendingPackagesCache.ExtractGitHubUrl("not-a-url").Should().BeNull();
    }

    [Fact]
    public void RelativeUrl_ReturnsNull()
    {
        TrendingPackagesCache.ExtractGitHubUrl("/owner/repo").Should().BeNull();
    }

    [Fact]
    public void GitHubUrlWithOnlyOwner_ReturnsNull()
    {
        TrendingPackagesCache.ExtractGitHubUrl("https://github.com/owner")
            .Should().BeNull();
    }

    [Fact]
    public void GitHubRootUrl_ReturnsNull()
    {
        TrendingPackagesCache.ExtractGitHubUrl("https://github.com")
            .Should().BeNull();
    }

    [Fact]
    public void GitSuffix_CaseInsensitive()
    {
        TrendingPackagesCache.ExtractGitHubUrl("https://github.com/owner/repo.GIT")
            .Should().Be("https://github.com/owner/repo");
    }
}
