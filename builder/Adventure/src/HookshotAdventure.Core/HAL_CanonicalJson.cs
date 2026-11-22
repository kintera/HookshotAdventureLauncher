using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HookshotAdventure.Core;

/// <summary>Canonical JSON used by Python's sorted, ASCII-escaped seed hashes.</summary>
public static class HAL_CanonicalJson
{
    private sealed class PythonStringComparer : IComparer<string>
    {
        public int Compare(string? x, string? y)
        {
            if (x is null || y is null) return x is null ? y is null ? 0 : -1 : 1;
            var left = x.EnumerateRunes().GetEnumerator(); var right = y.EnumerateRunes().GetEnumerator();
            while (true)
            {
                bool a = left.MoveNext(), b = right.MoveNext();
                if (!a || !b) return a == b ? 0 : a ? 1 : -1;
                int comparison = left.Current.Value.CompareTo(right.Current.Value);
                if (comparison != 0) return comparison;
            }
        }
    }
    private static readonly IComparer<string> KeyComparer = new PythonStringComparer();

    public static string Serialize(JsonElement value)
    {
        var result = new StringBuilder(); Append(result, value); return result.ToString();
    }
    public static string Digest(JsonElement value) => Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(Serialize(value)))).ToLowerInvariant();

    public static string DigestWithoutProperty(JsonElement value, string omitted)
    {
        if (value.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Expected a seed JSON object.");
        var result = new StringBuilder(); AppendObject(result, value, omitted);
        return Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(result.ToString()))).ToLowerInvariant();
    }

    private static void String(StringBuilder result, string value)
    {
        result.Append('"');
        foreach (char c in value)
        {
            switch (c)
            {
                case '"': result.Append("\\\""); break;
                case '\\': result.Append("\\\\"); break;
                case '\b': result.Append("\\b"); break;
                case '\f': result.Append("\\f"); break;
                case '\n': result.Append("\\n"); break;
                case '\r': result.Append("\\r"); break;
                case '\t': result.Append("\\t"); break;
                default:
                    if (c < 0x20 || c >= 0x7f) result.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else result.Append(c);
                    break;
            }
        }
        result.Append('"');
    }

    internal static string Float(double value)
    {
        if (!double.IsFinite(value)) throw new InvalidDataException("Non-finite seed JSON numbers are unsupported.");
        if (value == 0) return double.IsNegative(value) ? "-0.0" : "0.0";
        bool negative = value < 0;
        string raw = Math.Abs(value).ToString("R", CultureInfo.InvariantCulture);
        int e = raw.IndexOfAny('E', 'e');
        int exponent = e < 0 ? 0 : int.Parse(raw.AsSpan(e + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        string mantissa = e < 0 ? raw : raw[..e];
        int dot = mantissa.IndexOf('.');
        int decimalPosition = (dot < 0 ? mantissa.Length : dot) + exponent;
        string digits = mantissa.Replace(".", "", StringComparison.Ordinal);
        int first = 0; while (first < digits.Length - 1 && digits[first] == '0') first++;
        digits = digits[first..]; decimalPosition -= first;
        while (digits.Length > 1 && digits[^1] == '0') digits = digits[..^1];
        int scientificExponent = decimalPosition - 1;
        string body;
        if (scientificExponent < -4 || scientificExponent >= 16)
        {
            body = digits[..1] + (digits.Length > 1 ? "." + digits[1..] : "") + "e" +
                (scientificExponent < 0 ? "-" : "+") + Math.Abs(scientificExponent).ToString("D2", CultureInfo.InvariantCulture);
        }
        else if (decimalPosition <= 0) body = "0." + new string('0', -decimalPosition) + digits;
        else if (decimalPosition >= digits.Length) body = digits + new string('0', decimalPosition - digits.Length) + ".0";
        else body = digits[..decimalPosition] + "." + digits[decimalPosition..];
        return negative ? "-" + body : body;
    }
    private static void AppendObject(StringBuilder result, JsonElement value, string? omitted)
    {
        var properties = value.EnumerateObject().ToArray();
        if (properties.Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() != properties.Length)
            throw new InvalidDataException("Duplicate seed JSON property.");
        result.Append('{'); bool first = true;
        foreach (var property in properties.Where(p => p.Name != omitted).OrderBy(p => p.Name, KeyComparer))
        {
            if (!first) result.Append(','); first = false;
            String(result, property.Name); result.Append(':'); Append(result, property.Value);
        }
        result.Append('}');
    }
    private static void Append(StringBuilder result, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                AppendObject(result, value, null); break;
            case JsonValueKind.Array:
                result.Append('['); bool initial = true;
                foreach (var item in value.EnumerateArray()) { if (!initial) result.Append(','); initial = false; Append(result, item); }
                result.Append(']'); break;
            case JsonValueKind.String: String(result, value.GetString()!); break;
            case JsonValueKind.Number:
                string raw = value.GetRawText();
                if (raw.Contains('.') || raw.Contains('e') || raw.Contains('E')) result.Append(Float(value.GetDouble()));
                else result.Append(BigInteger.Parse(raw, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture));
                break;
            case JsonValueKind.True: result.Append("true"); break;
            case JsonValueKind.False: result.Append("false"); break;
            case JsonValueKind.Null: result.Append("null"); break;
            default: throw new InvalidDataException("Unsupported seed JSON value.");
        }
    }
}
