using System.Globalization;
using System.Text;
using PesajeProCamion.Domain.Entities;

namespace PesajeProCamion.Hardware.Scale;

public sealed record ParsedWeightFrame(bool Valid, int PesoKg, string RawFrame, string? Error);

/// <summary>Analiza tramas seriales del indicador según STX, posición y largo configurados en DBCONFIG.</summary>
public sealed class SerialFrameParser
{
    private readonly List<byte> _buffer = [];
    private readonly Queue<int> _recentWeights = new();

    public ParsedWeightFrame? TryFeedBytes(ReadOnlySpan<byte> bytes, ConfiguracionSistema cfg)
    {
        ArgumentNullException.ThrowIfNull(cfg);
        foreach (byte b in bytes)
        {
            _buffer.Add(b);
        }

        if (_buffer.Count > 4096)
        {
            _buffer.RemoveRange(0, _buffer.Count - 512);
        }

        byte stx = (byte)Math.Clamp(cfg.VisorStx, 0, 255);
        int stxIdx = _buffer.IndexOf(stx);
        if (stxIdx < 0)
        {
            return null;
        }

        if (stxIdx > 0)
        {
            _buffer.RemoveRange(0, stxIdx);
        }

        int posOffset = Math.Max(1, cfg.VisorPosData); // 1-based tras STX o desde inicio de trama
        int dataLen = Math.Max(1, cfg.VisorLenData);
        int frameMinLen = posOffset + dataLen;

        // Buscar fin de línea CR/LF si existe después del STX o consumir ventana fija
        int crIdx = _buffer.FindIndex(1, b => b is (byte)'\r' or (byte)'\n');
        int consumeLen;
        if (crIdx > 0 && crIdx >= frameMinLen - 1)
        {
            consumeLen = crIdx + 1;
        }
        else if (_buffer.Count >= frameMinLen + 1)
        {
            int nextStx = _buffer.FindIndex(1, b => b == stx);
            consumeLen = nextStx > 0 ? nextStx : frameMinLen + 1;
        }
        else
        {
            return null;
        }

        byte[] frameBytes = [.. _buffer.Take(consumeLen)];
        _buffer.RemoveRange(0, consumeLen);

        string ascii = Encoding.ASCII.GetString(frameBytes);
        int startIdx = Math.Clamp(posOffset - 1, 0, Math.Max(0, ascii.Length - 1));
        int takeLen = Math.Min(dataLen, ascii.Length - startIdx);
        if (takeLen <= 0)
        {
            return new ParsedWeightFrame(false, 0, ascii, "Trama incompleta");
        }

        string slice = ascii.Substring(startIdx, takeLen).Trim();
        string numericChars = new(slice.Where(c => char.IsAsciiDigit(c) || c == '-' || c == '.').ToArray());
        if (double.TryParse(numericChars, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
        {
            return new ParsedWeightFrame(true, (int)Math.Round(parsed), ascii, null);
        }

        return new ParsedWeightFrame(false, 0, ascii, $"Dato inválido: '{slice}'");
    }

    public bool UpdateStability(int weightKg, ConfiguracionSistema cfg)
    {
        ArgumentNullException.ThrowIfNull(cfg);
        int window = Math.Max(1, cfg.VLimit);
        _recentWeights.Enqueue(weightKg);
        while (_recentWeights.Count > window)
        {
            _recentWeights.Dequeue();
        }

        if (_recentWeights.Count < window)
        {
            return false;
        }

        int min = _recentWeights.Min();
        int max = _recentWeights.Max();
        return (max - min) <= Math.Max(0, cfg.VDelta);
    }

    public void Reset()
    {
        _buffer.Clear();
        _recentWeights.Clear();
    }
}

/// <summary>Procesador de señal dinámica para pesaje por ejes en movimiento con recorte porcentual y calibración.</summary>
public sealed class DynamicAxleProcessor
{
    private readonly List<double> _currentAxleSamples = [];
    private bool _capturing;

    public int AxleIndex { get; private set; }

    public void Reset()
    {
        _currentAxleSamples.Clear();
        _capturing = false;
        AxleIndex = 0;
    }

    /// <summary>Alimenta una lectura de peso en kg; devuelve el peso final del eje cuando el eje abandona la plataforma.</summary>
    public int? FeedSample(double rawWeightKg, ConfiguracionSistema cfg)
    {
        ArgumentNullException.ThrowIfNull(cfg);
        if (!_capturing)
        {
            if (rawWeightKg >= cfg.VmopUnder)
            {
                _capturing = true;
                _currentAxleSamples.Clear();
                _currentAxleSamples.Add(rawWeightKg);
            }
            return null;
        }

        if (rawWeightKg > cfg.VmopOver)
        {
            _currentAxleSamples.Add(rawWeightKg);
            return null;
        }

        _capturing = false;
        if (_currentAxleSamples.Count == 0)
        {
            return null;
        }

        int result = ComputeFilteredAxleWeight(_currentAxleSamples, cfg, AxleIndex);
        AxleIndex++;
        _currentAxleSamples.Clear();
        return result;
    }

    public static int ComputeFilteredAxleWeight(IReadOnlyList<double> samples, ConfiguracionSistema cfg, int axleZeroBasedIndex)
    {
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentNullException.ThrowIfNull(cfg);
        if (samples.Count == 0)
        {
            return 0;
        }

        double[] sorted = [.. samples.OrderBy(x => x)];
        int cutLeft = (int)Math.Floor(sorted.Length * (Math.Clamp(cfg.VmopCutLeft, 0, 45) / 100.0));
        int cutRight = (int)Math.Floor(sorted.Length * (Math.Clamp(cfg.VmopCutRight, 0, 45) / 100.0));
        int start = Math.Min(cutLeft, sorted.Length - 1);
        int end = Math.Max(start + 1, sorted.Length - cutRight);

        double avg = sorted[start..end].Average();
        int idx = Math.Clamp(axleZeroBasedIndex, 0, 6);
        double adjusted = avg + cfg.Calibraciones[idx] + cfg.Offsets[idx];
        return Math.Max(0, (int)Math.Round(adjusted));
    }
}
