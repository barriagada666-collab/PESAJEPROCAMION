namespace PesajeProCamion.Domain.Validation;

/// <summary>Validadores de RUT chileno (Módulo 11) y patentes vehiculares.</summary>
public static class ChileanValidators
{
    /// <summary>Calcula el dígito verificador chileno (0-9 o K) mediante algoritmo Módulo 11.</summary>
    public static string CalcularDigitoVerificador(string rutSinDv)
    {
        ArgumentNullException.ThrowIfNull(rutSinDv);
        string digits = new(rutSinDv.Where(char.IsAsciiDigit).ToArray());
        if (digits.Length == 0)
        {
            return string.Empty;
        }

        int suma = 0;
        int multiplicador = 2;
        for (int i = digits.Length - 1; i >= 0; i--)
        {
            suma += (digits[i] - '0') * multiplicador;
            multiplicador = multiplicador == 7 ? 2 : multiplicador + 1;
        }

        int resto = 11 - (suma % 11);
        return resto switch
        {
            11 => "0",
            10 => "K",
            _ => resto.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
    }

    /// <summary>Valida que el RUT y su dígito verificador coincidan según Módulo 11.</summary>
    public static bool ValidarRut(string? rutSinDv, string? digito)
    {
        if (string.IsNullOrWhiteSpace(rutSinDv) || string.IsNullOrWhiteSpace(digito))
        {
            return false;
        }

        string calculado = CalcularDigitoVerificador(rutSinDv);
        return calculado.Length > 0 &&
               string.Equals(calculado, digito.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Separa un RUT en cuerpo numérico y dígito verificador normalizados.</summary>
    public static (string Cuerpo, string Digito) SepararRut(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return (string.Empty, string.Empty);
        }

        string cleaned = new(raw.Trim().ToUpperInvariant().Where(c => char.IsAsciiDigit(c) || c == 'K' || c == '-').ToArray());
        if (cleaned.Contains('-'))
        {
            string[] parts = cleaned.Split('-', 2, StringSplitOptions.TrimEntries);
            string cuerpoDigits = new(parts[0].Where(char.IsAsciiDigit).ToArray());
            string dv = parts.Length > 1 && parts[1].Length > 0 ? parts[1][..1] : string.Empty;
            return (cuerpoDigits, dv);
        }

        if (cleaned.Length >= 2)
        {
            return (cleaned[..^1], cleaned[^1..]);
        }

        return (cleaned, string.Empty);
    }

    /// <summary>Normaliza una patente vehicular eliminando espacios sobrantes y convirtiendo a mayúsculas.</summary>
    public static string NormalizarPatente(string? patente)
    {
        if (string.IsNullOrWhiteSpace(patente))
        {
            return string.Empty;
        }

        string normalized = string.Concat(patente.Trim().Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant();
        return normalized.Length > 8 ? normalized[..8] : normalized;
    }

    /// <summary>Verifica si la patente tiene formato válido (4-8 caracteres alfanuméricos o guión).</summary>
    public static bool EsPatenteValida(string? patente)
    {
        string norm = NormalizarPatente(patente);
        return norm.Length is >= 4 and <= 8 &&
               norm.All(c => char.IsAsciiLetterUpper(c) || char.IsAsciiDigit(c) || c == '-');
    }
}
