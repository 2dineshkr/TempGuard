using System;
using System.Collections.Generic;
using System.Linq;
using LibreHardwareMonitor.Hardware;

namespace TempGuard
{
    internal sealed class TemperatureReading
    {
        public string HardwareName { get; set; }
        public string SensorName { get; set; }
        public string HardwareType { get; set; }
        public float Celsius { get; set; }

        public string DisplayName
        {
            get { return HardwareName + " - " + SensorName; }
        }
    }

    internal sealed class TemperatureSnapshot
    {
        public DateTime Time { get; set; }
        public IList<TemperatureReading> Readings { get; set; }
        public string Error { get; set; }

        public TemperatureReading Hottest
        {
            get
            {
                if (Readings == null || Readings.Count == 0) return null;
                return Readings.OrderByDescending(r => r.Celsius).First();
            }
        }
    }

    internal sealed class TemperatureMonitor : IDisposable
    {
        private readonly Computer _computer;
        private string _initializationError;

        public TemperatureMonitor()
        {
            try
            {
                _computer = new Computer
                {
                    IsCpuEnabled = true,
                    IsGpuEnabled = true
                };
                _computer.Open();
            }
            catch (Exception ex)
            {
                _initializationError = ex.Message;
            }
        }

        public TemperatureSnapshot Read()
        {
            TemperatureSnapshot snapshot = new TemperatureSnapshot
            {
                Time = DateTime.Now,
                Readings = new List<TemperatureReading>(),
                Error = _initializationError
            };

            if (_computer == null) return snapshot;

            try
            {
                _computer.Accept(new UpdateVisitor());
                foreach (IHardware hardware in _computer.Hardware)
                {
                    Collect(hardware, snapshot.Readings);
                }

                if (snapshot.Readings.Count == 0)
                {
                    snapshot.Error = "No CPU or GPU temperature sensor is available. Try Restart as administrator.";
                }
            }
            catch (Exception ex)
            {
                snapshot.Error = ex.Message;
            }

            return snapshot;
        }

        private static void Collect(IHardware hardware, IList<TemperatureReading> readings)
        {
            foreach (ISensor sensor in hardware.Sensors)
            {
                if (sensor.SensorType != SensorType.Temperature || !sensor.Value.HasValue) continue;
                float value = sensor.Value.Value;
                if (value < -10 || value > 125) continue;

                readings.Add(new TemperatureReading
                {
                    HardwareName = hardware.Name,
                    SensorName = sensor.Name,
                    HardwareType = FriendlyHardwareType(hardware.HardwareType),
                    Celsius = value
                });
            }

            foreach (IHardware child in hardware.SubHardware)
            {
                Collect(child, readings);
            }
        }

        private static string FriendlyHardwareType(HardwareType type)
        {
            string value = type.ToString();
            if (value.StartsWith("Gpu", StringComparison.OrdinalIgnoreCase)) return "GPU";
            if (value.Equals("Cpu", StringComparison.OrdinalIgnoreCase)) return "CPU";
            return value;
        }

        public void Dispose()
        {
            if (_computer != null)
            {
                try { _computer.Close(); }
                catch { }
            }
        }

        private sealed class UpdateVisitor : IVisitor
        {
            public void VisitComputer(IComputer computer) { computer.Traverse(this); }
            public void VisitHardware(IHardware hardware)
            {
                hardware.Update();
                foreach (IHardware child in hardware.SubHardware) child.Accept(this);
            }
            public void VisitSensor(ISensor sensor) { }
            public void VisitParameter(IParameter parameter) { }
        }
    }
}
