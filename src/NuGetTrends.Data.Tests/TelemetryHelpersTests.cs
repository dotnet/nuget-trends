using System.Runtime.CompilerServices;
using FluentAssertions;
using Xunit;

namespace NuGetTrends.Data.Tests;

public class TelemetryHelpersTests
{
    public class GetRelativeFilePathMethod
    {
        [Theory]
        [InlineData("/Users/bruno/git/nuget-trends/src/NuGetTrends.Data/ClickHouse/ClickHouseService.cs",
            "src/NuGetTrends.Data/ClickHouse/ClickHouseService.cs")]
        [InlineData("/home/runner/work/nuget-trends/nuget-trends/src/NuGetTrends.Scheduler/Program.cs",
            "src/NuGetTrends.Scheduler/Program.cs")]
        [InlineData("C:\\Users\\dev\\projects\\nuget-trends\\src\\NuGetTrends.Web\\PackageController.cs",
            "src/NuGetTrends.Web/PackageController.cs")]
        public void AbsolutePath_WithSrcMarker_ReturnsRelativePath(string absolutePath, string expected)
        {
            // Act
            var result = TelemetryHelpers.GetRelativeFilePath(absolutePath);

            // Assert
            result.Should().Be(expected);
        }

        [Theory]
        [InlineData("/Users/bruno/git/nuget-trends/SRC/NuGetTrends.Data/File.cs",
            "SRC/NuGetTrends.Data/File.cs")]
        [InlineData("D:\\Projects\\SrC\\MyProject\\File.cs",
            "SrC/MyProject/File.cs")]
        public void AbsolutePath_WithDifferentCaseSrcMarker_ReturnsRelativePath(string absolutePath, string expected)
        {
            // Act
            var result = TelemetryHelpers.GetRelativeFilePath(absolutePath);

            // Assert
            result.Should().Be(expected);
        }

        [Theory]
        [InlineData("C:\\src\\project\\file.cs", "src/project/file.cs")]
        [InlineData("D:\\work\\src\\deep\\nested\\path\\file.cs", "src/deep/nested/path/file.cs")]
        public void WindowsPath_NormalizesBackslashes(string absolutePath, string expected)
        {
            // Act
            var result = TelemetryHelpers.GetRelativeFilePath(absolutePath);

            // Assert
            result.Should().Be(expected);
        }

        [Theory]
        [InlineData("/home/user/projects/other-project/lib/Utils.cs", "Utils.cs")]
        [InlineData("/var/app/code/Service.cs", "Service.cs")]
        public void UnixAbsolutePath_WithoutSrcMarker_ReturnsFileName(string absolutePath, string expected)
        {
            // Act
            var result = TelemetryHelpers.GetRelativeFilePath(absolutePath);

            // Assert
            result.Should().Be(expected);
        }

        [Fact]
        public void WindowsPath_WithoutSrcMarker_FallbackBehavior()
        {
            // Note: Path.GetFileName uses the original path (before normalization) in the fallback.
            // On non-Windows, backslashes aren't recognized as path separators by Path.GetFileName,
            // so it returns the whole path. This is acceptable because:
            // 1. In practice, CallerFilePath always contains "src/" in this project
            // 2. The code runs on Linux in production where paths use forward slashes
            var path = "C:\\Projects\\NoSrcFolder\\Code\\Helper.cs";

            var result = TelemetryHelpers.GetRelativeFilePath(path);

            // On Windows, this would return "Helper.cs"
            // On Unix, this returns the whole path since backslash isn't a separator
            if (Path.DirectorySeparatorChar == '\\')
            {
                result.Should().Be("Helper.cs");
            }
            else
            {
                result.Should().Be(path);
            }
        }

        [Theory]
        [InlineData("File.cs", "File.cs")]
        [InlineData("MyClass.cs", "MyClass.cs")]
        public void RelativeFileName_ReturnsFileName(string path, string expected)
        {
            // Act
            var result = TelemetryHelpers.GetRelativeFilePath(path);

            // Assert
            result.Should().Be(expected);
        }

        [Theory]
        [InlineData("", "")]
        public void EmptyString_ReturnsEmpty(string path, string expected)
        {
            // Act
            var result = TelemetryHelpers.GetRelativeFilePath(path);

            // Assert
            result.Should().Be(expected);
        }

        [Fact]
        public void NullPath_ThrowsNullReferenceException()
        {
            // Act
            var act = () => TelemetryHelpers.GetRelativeFilePath(null!);

            // Assert
            // Note: The method doesn't guard against null input, so it throws NullReferenceException
            // from string.Replace. In practice, CallerFilePath will never be null.
            act.Should().Throw<NullReferenceException>();
        }

        [Theory]
        [InlineData("/path/to/resource/file.txt", "file.txt")]
        [InlineData("/some/src-like/folder/file.cs", "file.cs")]
        public void Path_WithoutSrcMarker_ReturnsFileName(string path, string expected)
        {
            // Act
            var result = TelemetryHelpers.GetRelativeFilePath(path);

            // Assert
            result.Should().Be(expected);
        }

        [Theory]
        [InlineData("/mysrc/project/file.cs", "src/project/file.cs")]
        public void Path_WithSrcSubstring_MatchesSrcMarker(string path, string expected)
        {
            // Note: The implementation searches for "src/" substring, so "/mysrc/" matches
            // This is expected behavior since real-world paths won't have "mysrc" directories
            var result = TelemetryHelpers.GetRelativeFilePath(path);

            result.Should().Be(expected);
        }

        [Theory]
        [InlineData("/path/src/", "src/")]
        [InlineData("C:\\code\\src\\", "src/")]
        public void Path_EndingWithSrcSlash_ReturnsSrcSlash(string path, string expected)
        {
            // Act
            var result = TelemetryHelpers.GetRelativeFilePath(path);

            // Assert
            result.Should().Be(expected);
        }

        [Theory]
        [InlineData("/first/src/project/second/src/nested/file.cs", "src/project/second/src/nested/file.cs")]
        public void Path_WithMultipleSrcMarkers_ReturnsFromFirstSrc(string path, string expected)
        {
            // Act
            var result = TelemetryHelpers.GetRelativeFilePath(path);

            // Assert
            result.Should().Be(expected);
        }

        [Theory]
        [InlineData("/path/with spaces/src/project/file.cs", "src/project/file.cs")]
        [InlineData("C:\\Users\\John Doe\\Documents\\src\\MyProject\\File.cs", "src/MyProject/File.cs")]
        public void Path_WithSpaces_HandlesCorrectly(string path, string expected)
        {
            // Act
            var result = TelemetryHelpers.GetRelativeFilePath(path);

            // Assert
            result.Should().Be(expected);
        }

        [Theory]
        [InlineData("/path/src/project/file with spaces.cs", "src/project/file with spaces.cs")]
        public void FileName_WithSpaces_PreservesSpaces(string path, string expected)
        {
            // Act
            var result = TelemetryHelpers.GetRelativeFilePath(path);

            // Assert
            result.Should().Be(expected);
        }

        [Theory]
        [InlineData("/path/to/src/special-chars!@#/file.cs", "src/special-chars!@#/file.cs")]
        [InlineData("/path/src/über-project/Schöne.cs", "src/über-project/Schöne.cs")]
        public void Path_WithSpecialCharacters_PreservesCharacters(string path, string expected)
        {
            // Act
            var result = TelemetryHelpers.GetRelativeFilePath(path);

            // Assert
            result.Should().Be(expected);
        }

        [Theory]
        [InlineData("   ", "   ")]
        [InlineData(" \t\n ", " \t\n ")]
        public void WhitespaceOnlyPath_ReturnsWhitespace(string path, string expected)
        {
            // Note: Path.GetFileName returns the input if there are no path separators
            // This is acceptable since CallerFilePath will never be whitespace-only
            var result = TelemetryHelpers.GetRelativeFilePath(path);

            result.Should().Be(expected);
        }

        [Theory]
        [InlineData("/path/src", "src")]
        public void Path_EndingWithSrcNoSlash_ReturnsSrc(string path, string expected)
        {
            // Act
            var result = TelemetryHelpers.GetRelativeFilePath(path);

            // Assert
            result.Should().Be(expected);
        }

        [Theory]
        [InlineData("/src/file.cs", "src/file.cs")]
        [InlineData("src/file.cs", "src/file.cs")]
        public void Path_StartingWithSrc_ReturnsFromSrc(string path, string expected)
        {
            // Act
            var result = TelemetryHelpers.GetRelativeFilePath(path);

            // Assert
            result.Should().Be(expected);
        }

        [Theory]
        [InlineData(".", ".")]
        [InlineData("..", "..")]
        [InlineData("./file.cs", "file.cs")]
        [InlineData("../file.cs", "file.cs")]
        public void RelativePath_WithDots_ReturnsFileName(string path, string expected)
        {
            // Act
            var result = TelemetryHelpers.GetRelativeFilePath(path);

            // Assert
            result.Should().Be(expected);
        }

        [Theory]
        [InlineData("/path/to/src/../src/project/file.cs", "src/../src/project/file.cs")]
        public void Path_WithParentDirectoryTraversal_PreservesTraversal(string path, string expected)
        {
            // Act
            var result = TelemetryHelpers.GetRelativeFilePath(path);

            // Assert
            result.Should().Be(expected);
        }

        [Theory]
        [InlineData("/very/long/path/that/goes/on/and/on/src/deep/nested/structure/with/many/levels/file.cs",
            "src/deep/nested/structure/with/many/levels/file.cs")]
        public void VeryLongPath_HandlesCorrectly(string path, string expected)
        {
            // Act
            var result = TelemetryHelpers.GetRelativeFilePath(path);

            // Assert
            result.Should().Be(expected);
        }

        [Theory]
        [InlineData("/path/src/src/file.cs", "src/src/file.cs")]
        [InlineData("/src/src/src/file.cs", "src/src/src/file.cs")]
        public void Path_WithNestedSrcDirectories_ReturnsFromFirstSrc(string path, string expected)
        {
            // Act
            var result = TelemetryHelpers.GetRelativeFilePath(path);

            // Assert
            result.Should().Be(expected);
        }

        [Theory]
        [InlineData("/path/to/SRC/Project/File.cs", "SRC/Project/File.cs")]
        [InlineData("/path/to/Src/Project/File.cs", "Src/Project/File.cs")]
        public void Path_PreservesSrcCasing(string path, string expected)
        {
            // The implementation is case-insensitive for matching but preserves original casing
            var result = TelemetryHelpers.GetRelativeFilePath(path);

            result.Should().Be(expected);
        }

        [Theory]
        [InlineData("src/NuGetTrends.Data/File.cs", "src/NuGetTrends.Data/File.cs")]
        public void AlreadyRelativePath_ReturnsUnchanged(string path, string expected)
        {
            // If the path is already relative (starts with src/), return as-is
            var result = TelemetryHelpers.GetRelativeFilePath(path);

            result.Should().Be(expected);
        }

        [Fact]
        public void RealWorldCallerFilePath_ReturnsExpectedFormat()
        {
            // This test uses the actual CallerFilePath to ensure it works in practice
            var result = GetPathFromCaller();

            result.Should().StartWith("src/");
            result.Should().EndWith("TelemetryHelpersTests.cs");
            result.Should().Contain("NuGetTrends.Data.Tests");
        }

        private static string GetPathFromCaller([CallerFilePath] string filePath = "")
        {
            return TelemetryHelpers.GetRelativeFilePath(filePath);
        }

        [Theory]
        [InlineData("/home/runner/work/nuget-trends/nuget-trends/src/Dir/File.cs", "src/Dir/File.cs")]
        [InlineData("/github/workspace/src/Dir/File.cs", "src/Dir/File.cs")]
        public void CiEnvironmentPaths_HandleCorrectly(string path, string expected)
        {
            // GitHub Actions and other CI environments have different path structures
            var result = TelemetryHelpers.GetRelativeFilePath(path);

            result.Should().Be(expected);
        }

        [Theory]
        [InlineData("/path/to/src/file", "src/file")]
        [InlineData("/path/to/src/noextension", "src/noextension")]
        public void Path_WithoutFileExtension_HandlesCorrectly(string path, string expected)
        {
            var result = TelemetryHelpers.GetRelativeFilePath(path);

            result.Should().Be(expected);
        }

        [Theory]
        [InlineData("/path/to/src/file.test.cs", "src/file.test.cs")]
        [InlineData("/path/to/src/My.Long.Namespace.cs", "src/My.Long.Namespace.cs")]
        public void Path_WithMultipleDots_HandlesCorrectly(string path, string expected)
        {
            var result = TelemetryHelpers.GetRelativeFilePath(path);

            result.Should().Be(expected);
        }
    }
}
