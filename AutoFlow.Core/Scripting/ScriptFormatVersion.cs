namespace AutoFlow.Core.Scripting;

/// <summary>
/// Raised when a script file declares a format version this build does not support.
/// Per FR-11 there is no compatibility path: version 2 is the only accepted format, and a
/// version 1 file must fail loudly rather than be parsed as if it were version 2.
/// </summary>
public sealed class UnsupportedScriptVersionException : Exception
{
    public UnsupportedScriptVersionException(string message) : base(message) { }

    public string? FilePath { get; init; }
    public int ActualVersion { get; init; }
}

/// <summary>
/// The version gate.
///
/// <para><b>Why this must exist.</b> Before this, <c>FormatVersion</c> was written but never
/// read — no code branched on it. A version 1 file loaded into the version 2 model would
/// therefore parse "successfully" while silently dropping the inline condition fields the new
/// model no longer declares, leaving regions at their -1 defaults. The script would then run
/// with whole-frame matching instead of region-scoped matching: no error, wrong behaviour.
/// Failing to load is strictly better.</para>
/// </summary>
public static class ScriptFormatVersion
{
    /// <summary>The only format version this build reads or writes.</summary>
    public const int Current = 2;

    /// <summary>
    /// Throws <see cref="UnsupportedScriptVersionException"/> unless
    /// <paramref name="actualVersion"/> is <see cref="Current"/>. The message carries the file
    /// path, the version found, and what to do about it (AC-06).
    /// </summary>
    public static void Validate(int actualVersion, string filePath)
    {
        if (actualVersion == Current)
            return;

        var reason = actualVersion < Current
            ? $"这是旧格式（内联条件字段），需要用迁移工具转换为 FormatVersion {Current}。"
            : $"这个文件由更新版本的 AutoFlow 写出，当前程序无法读取，请升级程序。";

        throw new UnsupportedScriptVersionException(
            $"脚本格式版本不受支持: FormatVersion = {actualVersion}，本程序只支持 {Current}。\n" +
            $"文件: {filePath}\n{reason}")
        {
            FilePath = filePath,
            ActualVersion = actualVersion
        };
    }
}
