using System;
using System.Text;

public static class EntitlementKeys
{
    public const string DreamMobile80th =
        "PDT_VEHICLE_DREAM_MOBILE_80TH";

    public static bool TryNormalize(
        string value,
        out string normalizedValue
    )
    {
        normalizedValue = value?.TrimEnd('\0');

        if (!IsCanonical(normalizedValue))
        {
            normalizedValue = null;
            return false;
        }

        return true;
    }

    public static bool IsCanonical(string value)
    {
        if (
            string.IsNullOrWhiteSpace(value) ||
            value.Length <= 4 ||
            !value.StartsWith("PDT_", StringComparison.Ordinal) ||
            Encoding.UTF8.GetByteCount(value) > 32
        )
        {
            return false;
        }

        foreach (char character in value)
        {
            bool isUppercaseLetter =
                character >= 'A' && character <= 'Z';
            bool isDigit = character >= '0' && character <= '9';

            if (!isUppercaseLetter && !isDigit && character != '_')
            {
                return false;
            }
        }

        return true;
    }
}
