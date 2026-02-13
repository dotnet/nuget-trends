using FluentAssertions;
using Xunit;

namespace NuGetTrends.Data.Tests;

public class TfmNormalizerTests
{
    // .NET Framework long forms
    [Theory]
    [InlineData(".NETFramework,Version=v4.5", "net45", ".NET Framework")]
    [InlineData(".NETFramework,Version=v4.5.1", "net451", ".NET Framework")]
    [InlineData(".NETFramework,Version=v4.7.2", "net472", ".NET Framework")]
    [InlineData(".NETFramework,Version=v4.0", "net40", ".NET Framework")]
    [InlineData(".NETFramework4.5", "net45", ".NET Framework")]
    [InlineData(".NETFramework4.7.2", "net472", ".NET Framework")]
    public void Normalize_NetFrameworkLongForm_ReturnsCorrectTfm(string input, string expectedShort, string expectedFamily)
    {
        var result = TfmNormalizer.Normalize(input);
        result.Should().NotBeNull();
        result!.ShortName.Should().Be(expectedShort);
        result.Family.Should().Be(expectedFamily);
    }

    // .NET Framework short forms
    [Theory]
    [InlineData("net45", "net45", ".NET Framework")]
    [InlineData("net451", "net451", ".NET Framework")]
    [InlineData("net472", "net472", ".NET Framework")]
    [InlineData("net40", "net40", ".NET Framework")]
    [InlineData("net20", "net20", ".NET Framework")]
    public void Normalize_NetFrameworkShortForm_ReturnsCorrectTfm(string input, string expectedShort, string expectedFamily)
    {
        var result = TfmNormalizer.Normalize(input);
        result.Should().NotBeNull();
        result!.ShortName.Should().Be(expectedShort);
        result.Family.Should().Be(expectedFamily);
    }

    // .NET Standard long forms
    [Theory]
    [InlineData(".NETStandard,Version=v1.0", "netstandard1.0", ".NET Standard")]
    [InlineData(".NETStandard,Version=v2.0", "netstandard2.0", ".NET Standard")]
    [InlineData(".NETStandard,Version=v2.1", "netstandard2.1", ".NET Standard")]
    [InlineData(".NETStandard2.0", "netstandard2.0", ".NET Standard")]
    public void Normalize_NetStandardLongForm_ReturnsCorrectTfm(string input, string expectedShort, string expectedFamily)
    {
        var result = TfmNormalizer.Normalize(input);
        result.Should().NotBeNull();
        result!.ShortName.Should().Be(expectedShort);
        result.Family.Should().Be(expectedFamily);
    }

    // .NET Standard short forms
    [Theory]
    [InlineData("netstandard1.0", "netstandard1.0", ".NET Standard")]
    [InlineData("netstandard2.0", "netstandard2.0", ".NET Standard")]
    [InlineData("netstandard2.1", "netstandard2.1", ".NET Standard")]
    public void Normalize_NetStandardShortForm_ReturnsCorrectTfm(string input, string expectedShort, string expectedFamily)
    {
        var result = TfmNormalizer.Normalize(input);
        result.Should().NotBeNull();
        result!.ShortName.Should().Be(expectedShort);
        result.Family.Should().Be(expectedFamily);
    }

    // .NET Core long forms
    [Theory]
    [InlineData(".NETCoreApp,Version=v1.0", "netcoreapp1.0", ".NET Core")]
    [InlineData(".NETCoreApp,Version=v2.1", "netcoreapp2.1", ".NET Core")]
    [InlineData(".NETCoreApp,Version=v3.1", "netcoreapp3.1", ".NET Core")]
    [InlineData(".NETCoreApp3.1", "netcoreapp3.1", ".NET Core")]
    public void Normalize_NetCoreLongForm_ReturnsCorrectTfm(string input, string expectedShort, string expectedFamily)
    {
        var result = TfmNormalizer.Normalize(input);
        result.Should().NotBeNull();
        result!.ShortName.Should().Be(expectedShort);
        result.Family.Should().Be(expectedFamily);
    }

    // .NET Core short forms
    [Theory]
    [InlineData("netcoreapp1.0", "netcoreapp1.0", ".NET Core")]
    [InlineData("netcoreapp2.1", "netcoreapp2.1", ".NET Core")]
    [InlineData("netcoreapp3.1", "netcoreapp3.1", ".NET Core")]
    public void Normalize_NetCoreShortForm_ReturnsCorrectTfm(string input, string expectedShort, string expectedFamily)
    {
        var result = TfmNormalizer.Normalize(input);
        result.Should().NotBeNull();
        result!.ShortName.Should().Be(expectedShort);
        result.Family.Should().Be(expectedFamily);
    }

    // .NET 5+ (NuGet uses NETCoreApp internally)
    [Theory]
    [InlineData(".NETCoreApp,Version=v5.0", "net5.0", ".NET")]
    [InlineData(".NETCoreApp,Version=v6.0", "net6.0", ".NET")]
    [InlineData(".NETCoreApp,Version=v7.0", "net7.0", ".NET")]
    [InlineData(".NETCoreApp,Version=v8.0", "net8.0", ".NET")]
    [InlineData(".NETCoreApp,Version=v9.0", "net9.0", ".NET")]
    [InlineData(".NETCoreApp,Version=v10.0", "net10.0", ".NET")]
    [InlineData(".NETCoreApp,Version=v11.0", "net11.0", ".NET")]
    public void Normalize_ModernDotNetFromNuGetLongForm_ReturnsCorrectTfm(string input, string expectedShort, string expectedFamily)
    {
        var result = TfmNormalizer.Normalize(input);
        result.Should().NotBeNull();
        result!.ShortName.Should().Be(expectedShort);
        result.Family.Should().Be(expectedFamily);
    }

    // Modern .NET short forms
    [Theory]
    [InlineData("net5.0", "net5.0", ".NET")]
    [InlineData("net6.0", "net6.0", ".NET")]
    [InlineData("net7.0", "net7.0", ".NET")]
    [InlineData("net8.0", "net8.0", ".NET")]
    [InlineData("net9.0", "net9.0", ".NET")]
    [InlineData("net10.0", "net10.0", ".NET")]
    [InlineData("net11.0", "net11.0", ".NET")]
    public void Normalize_ModernDotNetShortForm_ReturnsCorrectTfm(string input, string expectedShort, string expectedFamily)
    {
        var result = TfmNormalizer.Normalize(input);
        result.Should().NotBeNull();
        result!.ShortName.Should().Be(expectedShort);
        result.Family.Should().Be(expectedFamily);
    }

    // Preview suffix stripping
    [Theory]
    [InlineData("net8.0-preview1", "net8.0", ".NET")]
    [InlineData("net9.0-rc.1", "net9.0", ".NET")]
    [InlineData("net11.0-preview.1.25113.2", "net11.0", ".NET")]
    public void Normalize_PreviewSuffix_StrippedFromShortName(string input, string expectedShort, string expectedFamily)
    {
        var result = TfmNormalizer.Normalize(input);
        result.Should().NotBeNull();
        result!.ShortName.Should().Be(expectedShort);
        result.Family.Should().Be(expectedFamily);
    }

    // Null / empty / whitespace
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Normalize_NullOrEmpty_ReturnsNull(string? input)
    {
        var result = TfmNormalizer.Normalize(input);
        result.Should().BeNull();
    }

    // Unrecognizable frameworks
    [Theory]
    [InlineData("Xamarin.iOS,Version=v1.0")]
    [InlineData("MonoAndroid,Version=v10.0")]
    [InlineData("portable-net45+win8")]
    [InlineData(".NETPortable,Version=v4.5")]
    [InlineData("UAP,Version=v10.0")]
    [InlineData("Silverlight,Version=v5.0")]
    [InlineData("random-garbage")]
    public void Normalize_UnrecognizableFramework_ReturnsNull(string input)
    {
        var result = TfmNormalizer.Normalize(input);
        result.Should().BeNull();
    }

    // Case insensitivity
    [Theory]
    [InlineData("NETSTANDARD2.0", "netstandard2.0", ".NET Standard")]
    [InlineData("NetStandard2.0", "netstandard2.0", ".NET Standard")]
    [InlineData("NETCOREAPP3.1", "netcoreapp3.1", ".NET Core")]
    [InlineData("NET8.0", "net8.0", ".NET")]
    public void Normalize_CaseInsensitive_ReturnsCorrectTfm(string input, string expectedShort, string expectedFamily)
    {
        var result = TfmNormalizer.Normalize(input);
        result.Should().NotBeNull();
        result!.ShortName.Should().Be(expectedShort);
        result.Family.Should().Be(expectedFamily);
    }

    // Whitespace handling
    [Fact]
    public void Normalize_WithLeadingTrailingWhitespace_TrimsAndNormalizes()
    {
        var result = TfmNormalizer.Normalize("  net8.0  ");
        result.Should().NotBeNull();
        result!.ShortName.Should().Be("net8.0");
        result.Family.Should().Be(".NET");
    }
}
