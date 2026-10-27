using System;
using System.Diagnostics;
using System.IO;

namespace Cad2Gis.Core.Services;

/// <summary>
/// Execution tracer for Step 3 phases, producing a flushed log file (HelperFiles\logs\step3_trace.log).
/// Logs timestamped [ENTRY] and [EXIT] for every phase, elapsed time, and warnings if any phase exceeds 30s.
/// </summary>
public class Step3Tracer
{
    private readonly string _logFilePath;
    private readonly object _lock = new();

    public string LogFilePath => _logFilePath;

    public Step3Tracer(string logFilePath)
    {
        _logFilePath = logFilePath;
        try
        {
            string? dir = Path.GetDirectoryName(_logFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
        }
        catch { }
    }

    public void LogLine(string tag, string message)
    {
        lock (_lock)
        {
            try
            {
                string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{tag}] {message}{Environment.NewLine}";
                File.AppendAllText(_logFilePath, line);
            }
            catch { }
        }
    }

    public void LogInfo(string message) => LogLine("INFO", message);

    public void LogWarning(string message) => LogLine("WARNING", message);

    public void LogError(string message) => LogLine("ERROR", message);

    public IDisposable BeginPhase(string phaseName) => new PhaseScope(this, phaseName);

    private class PhaseScope : IDisposable
    {
        private readonly Step3Tracer _tracer;
        private readonly string _phaseName;
        private readonly Stopwatch _stopwatch;
        private bool _disposed;

        public PhaseScope(Step3Tracer tracer, string phaseName)
        {
            _tracer = tracer;
            _phaseName = phaseName;
            _stopwatch = Stopwatch.StartNew();
            _tracer.LogLine("ENTRY", phaseName);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _stopwatch.Stop();
            long elapsedMs = _stopwatch.ElapsedMilliseconds;
            _tracer.LogLine("EXIT", $"{_phaseName} (Elapsed: {elapsedMs} ms)");

            if (elapsedMs > 30000)
            {
                _tracer.LogWarning($"Phase '{_phaseName}' took longer than 30 seconds ({_stopwatch.Elapsed.TotalSeconds:F1}s)!");
            }
        }
    }
}
