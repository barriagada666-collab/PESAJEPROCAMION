using System.IO.Ports;
using PesajeProCamion.Domain.Entities;

namespace PesajeProCamion.Hardware.Scale;

public sealed class ScaleReadingEventArgs : EventArgs
{
    public ScaleReadingEventArgs(int weightKg, bool stable, bool isZero, bool isDynamic, bool commError, bool dataError, string rawData)
    {
        WeightKg = weightKg;
        Stable = stable;
        IsZero = isZero;
        IsDynamic = isDynamic;
        CommError = commError;
        DataError = dataError;
        RawData = rawData;
    }

    public int WeightKg { get; }
    public bool Stable { get; }
    public bool IsZero { get; }
    public bool IsDynamic { get; }
    public bool CommError { get; }
    public bool DataError { get; }
    public string RawData { get; }
}

/// <summary>Servicio unificado de lectura de báscula RS-232 con modo simulación/demo seguro cuando no hay puerto físico.</summary>
public sealed class ScaleReaderService : IDisposable
{
    private readonly SerialFrameParser _parser = new();
    private SerialPort? _port;
    private ConfiguracionSistema _config = new();
    private CancellationTokenSource? _demoCts;
    private bool _disposed;
    private int _currentWeightKg;

    public event EventHandler<ScaleReadingEventArgs>? ReadingReceived;

    public bool IsOpen => _port?.IsOpen == true || IsSimulationActive;
    public bool IsSimulationActive { get; private set; }
    public int CurrentWeightKg => _currentWeightKg;
    public string StatusDescription { get; private set; } = "DESCONECTADO";

    public static string[] GetAvailablePorts() => SerialPort.GetPortNames().OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray();

    public void UpdateConfig(ConfiguracionSistema cfg)
    {
        ArgumentNullException.ThrowIfNull(cfg);
        _config = cfg;
    }

    public bool TryOpenSerial(HardwareSetup setup, ConfiguracionSistema cfg, out string? errorMessage)
    {
        ArgumentNullException.ThrowIfNull(setup);
        ArgumentNullException.ThrowIfNull(cfg);
        Close();
        _config = cfg;
        _parser.Reset();

        string portName = $"COM{Math.Max((short)1, setup.ComNumber)}";
        try
        {
            Parity parity = setup.ParityIndex switch
            {
                1 => Parity.Even,
                2 => Parity.Odd,
                3 => Parity.Space,
                4 => Parity.Mark,
                _ => Parity.None
            };
            StopBits stopBits = (setup.StopBitsIndex ?? 0) switch
            {
                1 => StopBits.OnePointFive,
                2 => StopBits.Two,
                _ => StopBits.One
            };

            _port = new SerialPort(portName, setup.BaudRateReal, parity, setup.DataBitsReal, stopBits)
            {
                ReadTimeout = 500,
                WriteTimeout = 500
            };
            _port.DataReceived += OnSerialDataReceived;
            _port.Open();

            IsSimulationActive = false;
            StatusDescription = $"COMUNICACIÓN DIRECTA ({portName} @ {setup.BaudRateReal})";
            errorMessage = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _port?.Dispose();
            _port = null;
            StatusDescription = $"SIN INDICADOR EN {portName}";
            errorMessage = ex.Message;
            ReadingReceived?.Invoke(this, new ScaleReadingEventArgs(0, false, true, false, true, false, ex.Message));
            return false;
        }
    }

    public void EnableSimulation(ConfiguracionSistema cfg, int initialWeightKg = 0)
    {
        ArgumentNullException.ThrowIfNull(cfg);
        Close();
        _config = cfg;
        IsSimulationActive = true;
        StatusDescription = "MODO SIMULACIÓN / PRUEBA";
        EmitSimulatedWeight(initialWeightKg, stable: true);
    }

    public void EmitSimulatedWeight(int weightKg, bool stable = true)
    {
        if (!IsSimulationActive)
        {
            IsSimulationActive = true;
            StatusDescription = "MODO SIMULACIÓN / PRUEBA";
        }
        _currentWeightKg = weightKg;
        bool isZero = Domain.Services.WeighingCalculator.EstaEnCero(weightKg, _config);
        ReadingReceived?.Invoke(this, new ScaleReadingEventArgs(weightKg, stable, isZero, false, false, false, $"SIM:{weightKg:D6}"));
    }

    public async Task PlayDemoCurveAsync(IReadOnlyList<DemoMuestra> samples, int intervalMs = 60, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(samples);
        StopDemo();
        _demoCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        IsSimulationActive = true;
        StatusDescription = "REPRODUCCIÓN DEMO DINÁMICO";
        try
        {
            foreach (DemoMuestra sample in samples)
            {
                _demoCts.Token.ThrowIfCancellationRequested();
                int peso = (int)Math.Round(sample.PesoKg);
                _currentWeightKg = peso;
                bool stable = _parser.UpdateStability(peso, _config);
                bool isZero = Domain.Services.WeighingCalculator.EstaEnCero(peso, _config);
                ReadingReceived?.Invoke(this, new ScaleReadingEventArgs(peso, stable, isZero, true, false, false, sample.Hora));
                await Task.Delay(Math.Max(10, intervalMs), _demoCts.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Detención normal solicitada por el usuario
        }
    }

    public void StopDemo()
    {
        if (_demoCts is not null)
        {
            _demoCts.Cancel();
            _demoCts.Dispose();
            _demoCts = null;
        }
    }

    private void OnSerialDataReceived(object sender, SerialDataReceivedEventArgs e)
    {
        if (_port is null || !_port.IsOpen)
        {
            return;
        }

        try
        {
            int count = _port.BytesToRead;
            if (count <= 0)
            {
                return;
            }
            byte[] buf = new byte[count];
            int read = _port.Read(buf, 0, count);
            ParsedWeightFrame? frame = _parser.TryFeedBytes(buf.AsSpan(0, read), _config);
            if (frame is null)
            {
                return;
            }

            if (!frame.Valid)
            {
                ReadingReceived?.Invoke(this, new ScaleReadingEventArgs(_currentWeightKg, false, false, false, false, true, frame.RawFrame));
                return;
            }

            _currentWeightKg = frame.PesoKg;
            bool stable = _parser.UpdateStability(frame.PesoKg, _config);
            bool isZero = Domain.Services.WeighingCalculator.EstaEnCero(frame.PesoKg, _config);
            ReadingReceived?.Invoke(this, new ScaleReadingEventArgs(frame.PesoKg, stable, isZero, _config.DinamicoActivo, false, false, frame.RawFrame));
        }
        catch (TimeoutException)
        {
            ReadingReceived?.Invoke(this, new ScaleReadingEventArgs(_currentWeightKg, false, false, false, true, false, "Timeout"));
        }
        catch (InvalidOperationException)
        {
            // Puerto cerrado concurrentemente
        }
    }

    public void Close()
    {
        StopDemo();
        if (_port is not null)
        {
            _port.DataReceived -= OnSerialDataReceived;
            if (_port.IsOpen)
            {
                _port.Close();
            }
            _port.Dispose();
            _port = null;
        }
        IsSimulationActive = false;
        StatusDescription = "DESCONECTADO";
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            Close();
            _disposed = true;
        }
    }
}
