using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace TempGuard
{
    internal sealed class ProcessLoadReading
    {
        public int ProcessId { get; set; }
        public int SessionId { get; set; }
        public string Name { get; set; }
        public string WindowTitle { get; set; }
        public double CpuPercent { get; set; }
        public long WorkingSetBytes { get; set; }
    }

    internal sealed class ProcessLoadMonitor
    {
        private sealed class ProcessSample
        {
            public string Name { get; set; }
            public TimeSpan TotalProcessorTime { get; set; }
            public DateTime CapturedAtUtc { get; set; }
        }

        private readonly Dictionary<int, ProcessSample> _samples = new Dictionary<int, ProcessSample>();
        private readonly int _logicalProcessorCount = Math.Max(1, Environment.ProcessorCount);
        private bool _hasBaseline;

        public bool HasBaseline { get { return _hasBaseline; } }

        public IList<ProcessLoadReading> Read()
        {
            DateTime capturedAt = DateTime.UtcNow;
            Dictionary<int, ProcessSample> nextSamples = new Dictionary<int, ProcessSample>();
            List<ProcessLoadReading> readings = new List<ProcessLoadReading>();

            foreach (Process process in Process.GetProcesses())
            {
                try
                {
                    int processId = process.Id;
                    int sessionId = process.SessionId;
                    string name = process.ProcessName;
                    if (processId == 0 || string.Equals(name, "Idle", StringComparison.OrdinalIgnoreCase))
                        continue;

                    TimeSpan processorTime = process.TotalProcessorTime;
                    long workingSet = process.WorkingSet64;
                    string windowTitle = string.Empty;
                    try { windowTitle = process.MainWindowTitle; }
                    catch { }

                    ProcessSample previous;
                    double cpuPercent = 0;
                    if (_samples.TryGetValue(processId, out previous) &&
                        string.Equals(previous.Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        double elapsedMilliseconds = (capturedAt - previous.CapturedAtUtc).TotalMilliseconds;
                        double processorMilliseconds = (processorTime - previous.TotalProcessorTime).TotalMilliseconds;
                        if (elapsedMilliseconds > 0 && processorMilliseconds >= 0)
                        {
                            cpuPercent = processorMilliseconds / elapsedMilliseconds / _logicalProcessorCount * 100.0;
                            cpuPercent = Math.Max(0, Math.Min(100, cpuPercent));
                        }
                    }

                    nextSamples[processId] = new ProcessSample
                    {
                        Name = name,
                        TotalProcessorTime = processorTime,
                        CapturedAtUtc = capturedAt
                    };
                    readings.Add(new ProcessLoadReading
                    {
                        ProcessId = processId,
                        SessionId = sessionId,
                        Name = name,
                        WindowTitle = windowTitle,
                        CpuPercent = cpuPercent,
                        WorkingSetBytes = workingSet
                    });
                }
                catch
                {
                    // Processes can exit or become inaccessible while the snapshot is collected.
                }
                finally
                {
                    process.Dispose();
                }
            }

            _samples.Clear();
            foreach (KeyValuePair<int, ProcessSample> sample in nextSamples)
                _samples[sample.Key] = sample.Value;
            _hasBaseline = true;

            return readings
                .OrderByDescending(reading => reading.CpuPercent)
                .ThenByDescending(reading => reading.WorkingSetBytes)
                .ThenBy(reading => reading.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}
