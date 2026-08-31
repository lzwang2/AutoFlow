using AutoFlow.Core.Scripting;
using AutoFlow.Core.Scripting.Instructions;
using Newtonsoft.Json;
using Xunit;

namespace AutoFlow.Core.Tests;

public class ScriptFormatVersionTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "AutoFlowTests_" + Guid.NewGuid().ToString("N"));

    public ScriptFormatVersionTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private string WriteRaw(string fileName, object payload)
    {
        var path = Path.Combine(_dir, fileName);
        File.WriteAllText(path, JsonConvert.SerializeObject(payload));
        return path;
    }

    [Fact]
    public void CurrentIsTwo() => Assert.Equal(2, ScriptFormatVersion.Current);

    [Fact]
    public void ValidateAcceptsCurrent()
        => ScriptFormatVersion.Validate(ScriptFormatVersion.Current, "x.json");

    [Fact]
    public void ValidateRejectsVersionOneWithPathAndVersion()
    {
        var ex = Assert.Throws<UnsupportedScriptVersionException>(
            () => ScriptFormatVersion.Validate(1, @"C:\scripts\old.json"));

        // AC-06：错误信息必须含文件路径与实际版本号。
        Assert.Contains(@"C:\scripts\old.json", ex.Message);
        Assert.Contains("1", ex.Message);
        Assert.Contains("迁移", ex.Message);
        Assert.Equal(1, ex.ActualVersion);
        Assert.Equal(@"C:\scripts\old.json", ex.FilePath);
    }

    [Fact]
    public void ValidateRejectsFutureVersionWithDifferentAdvice()
    {
        var ex = Assert.Throws<UnsupportedScriptVersionException>(
            () => ScriptFormatVersion.Validate(99, "future.json"));

        // 未来版本不该建议「用迁移工具」——迁移工具只会往前迁。
        Assert.Contains("升级程序", ex.Message);
        Assert.DoesNotContain("迁移工具", ex.Message);
    }

    [Fact]
    public void LoadRejectsVersionOneDocument()
    {
        // 这是闸门存在的理由：v1 文件不加闸门会被静默当作 v2 解析。
        var path = WriteRaw("v1.json", new
        {
            Schema = "autoflow/script",
            FormatVersion = 1,
            Instructions = new[] { new { CommandType = "Click" } }
        });

        var ex = Assert.Throws<UnsupportedScriptVersionException>(
            () => new ScriptDocumentService().Load(path));

        Assert.Contains(path, ex.Message);
    }

    [Fact]
    public void LoadAcceptsVersionTwoDocument()
    {
        var path = WriteRaw("v2.json", new
        {
            Schema = "autoflow/script",
            FormatVersion = 2,
            Instructions = new[] { new { CommandType = "Click" } }
        });

        var document = new ScriptDocumentService().Load(path);

        Assert.Equal(2, document.FormatVersion);
        Assert.Single(document.Instructions);
    }

    [Fact]
    public void MissingFormatVersionUsesCurrentDefault()
    {
        // 缺字段时反序列化取默认值；默认值必须是 Current，否则新建脚本过不了自己的闸门。
        var path = WriteRaw("nover.json", new
        {
            Schema = "autoflow/script",
            Instructions = Array.Empty<object>()
        });

        var document = new ScriptDocumentService().Load(path);
        Assert.Equal(ScriptFormatVersion.Current, document.FormatVersion);
    }

    [Fact]
    public void SaveStampsCurrentVersionEvenIfDocumentSaysOtherwise()
    {
        var service = new ScriptDocumentService();
        var path = Path.Combine(_dir, "stamped.json");

        var document = new ScriptDocument { FormatVersion = 1 };
        document.Instructions.Add(new Instruction { CommandType = CommandType.Comment });

        service.Save(document, path);

        // 写出 v1 会产生本程序自己都拒绝加载的文件。
        Assert.Equal(ScriptFormatVersion.Current, document.FormatVersion);
        Assert.Equal(ScriptFormatVersion.Current, service.Load(path).FormatVersion);
    }

    [Fact]
    public void NewDocumentDefaultsToCurrent()
        => Assert.Equal(ScriptFormatVersion.Current, new ScriptDocument().FormatVersion);

    [Fact]
    public void SaveThenLoadRoundTripsWithoutVersionError()
    {
        var service = new ScriptDocumentService();
        var path = Path.Combine(_dir, "round.json");

        service.Save(service.CreateNew(), path);

        service.Load(path); // 不抛异常即通过
    }
}
