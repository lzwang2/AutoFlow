using System.IO;
using System.Text;

namespace AutoFlow.App.Services;

/// <summary>
/// Appends the session's log lines to a file on disk.
///
/// <para>Exists because the in-window log is capped — it keeps only the newest lines so the
/// scroll stays responsive — and the lines that get dropped first are the ones written at
/// startup: reference resolution, condition-library warnings, reference-space mismatches. Those
/// are exactly what a mis-matching rule needs to be diagnosed, and they were unrecoverable once
/// scrolled off. The file is the full record; the window is a live tail of it.</para>
///
/// <para>One file per process start, named by timestamp, so a run's log is never interleaved with
/// an earlier run's. Writes are appended and flushed immediately: a log that only survives a
/// clean shutdown is useless for diagnosing a crash or a hang, which is when it matters most.</para>
/// </summary>
public sealed class SessionLogWriter : IDisposable
{
    private readonly object _gate = new();
    private StreamWriter? _writer;
    private bool _failed;

    public SessionLogWriter(string? directory = null)
    {
        Directory = directory ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
        FilePath = Path.Combine(Directory, $"autoflow_{DateTime.Now:yyyyMMdd_HHmmss}.log");
    }

    /// <summary>Where log files are written. Created on first successful write.</summary>
    public string Directory { get; }

    /// <summary>This session's log file.</summary>
    public string FilePath { get; }

    /// <summary>
    /// Appends one line. Never throws: logging is diagnostics, so a disk problem must not take
    /// down the operation being logged. The first failure is reported through
    /// <paramref name="onError"/> and then suppressed, otherwise a full disk would produce one
    /// error per line for the rest of the session.
    /// </summary>
    public void Append(string line, Action<string>? onError = null)
    {
        lock (_gate)
        {
            if (_failed) return;

            try
            {
                if (_writer is null)
                {
                    System.IO.Directory.CreateDirectory(Directory);
                    _writer = new StreamWriter(FilePath, append: true, Encoding.UTF8)
                    {
                        AutoFlush = true
                    };
                }

                _writer.WriteLine(line);
            }
            catch (Exception ex)
            {
                _failed = true;
                onError?.Invoke($"写入日志文件失败，本次会话不再落盘: {ex.Message}");
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }
}
