using AutoFlow.Core.Scripting.Conditions;
using Xunit;

namespace AutoFlow.Core.Tests;

/// <summary>
/// Guards the two-level fallback that RuleScriptService now delegates to. The order is
/// load-bearing: swapping the levels would silently change which image existing scripts
/// resolve to.
/// </summary>
public class TemplatePathResolverTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "AutoFlowPath_" + Guid.NewGuid().ToString("N"));

    public TemplatePathResolverTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private string Touch(params string[] segments)
    {
        var path = Path.Combine(new[] { _root }.Concat(segments).ToArray());
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[] { 0 });
        return path;
    }

    [Fact]
    public void EmptyPath_ReturnedUnchanged()
    {
        Assert.Null(TemplatePathResolver.Resolve(null, _root));
        Assert.Equal("", TemplatePathResolver.Resolve("", _root));
    }

    [Fact]
    public void AbsolutePath_ReturnedUnchanged()
    {
        var absolute = Touch("images", "abs.png");

        // Already-absolute paths bypass resolution entirely, matching prior behaviour.
        Assert.Equal(absolute, TemplatePathResolver.Resolve(absolute, _root));
    }

    [Fact]
    public void PrefersFileNextToReferencingFile()
    {
        // This is the TestScripts/MultiPlayerGame.rules.json case: a script carrying its
        // own private images resolves them relative to itself.
        var scriptDir = Path.Combine(_root, "TestScripts");
        var expected = Touch("TestScripts", "images", "welcome.png");

        var resolved = TemplatePathResolver.Resolve("images/welcome.png", scriptDir);

        // Compare canonical full paths, not raw strings: Path.Combine preserves the forward
        // slashes from the input, so the result mixes separators — which is exactly what the
        // original code produced for the existing "LastCloudia/images/x.png" style paths.
        Assert.NotNull(resolved);
        Assert.Equal(Path.GetFullPath(expected), Path.GetFullPath(resolved!));
    }

    [Fact]
    public void UnresolvablePath_ReturnsNull()
    {
        Assert.Null(TemplatePathResolver.Resolve("images/nope.png", _root));
    }

    [Fact]
    public void Candidates_ListReferencingDirectoryFirst()
    {
        var candidates = TemplatePathResolver
            .Candidates("images/x.png", @"C:\scripts")
            .ToList();

        Assert.Equal(2, candidates.Count);
        Assert.StartsWith(@"C:\scripts", candidates[0]);
        Assert.StartsWith(TemplatePathResolver.TemplateRoot, candidates[1]);
    }

    [Fact]
    public void Candidates_OmitReferencingDirectoryWhenUnknown()
    {
        var candidates = TemplatePathResolver.Candidates("images/x.png", null).ToList();

        Assert.Single(candidates);
        Assert.StartsWith(TemplatePathResolver.TemplateRoot, candidates[0]);
    }

    [Fact]
    public void DescribeAttempts_MentionsEveryCandidate()
    {
        var description = TemplatePathResolver.DescribeAttempts("images/x.png", @"C:\scripts");

        Assert.Contains(@"C:\scripts", description);
        Assert.Contains("RuleTemplates", description);
    }
}
