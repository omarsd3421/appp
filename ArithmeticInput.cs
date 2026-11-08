using System;
using System.Globalization;

namespace LibraryPrintApp;

/// <summary>
/// Parses simple addition expressions typed into amount fields (for example: 60+50+35).
/// The parser tolerates an unfinished trailing plus while the user is typing so totals
/// can update live without rewriting the text/caret in the active input control.
/// </summary>
internal static class ArithmeticInput
{
    public static bool TryEvaluateSum(string? expression, out decimal value)
    {
        value = 0m;
        if (string.IsNullOrWhiteSpace(expression)) return false;

        string text = NormalizeDigits(expression.Trim())
            .Replace('٫', '.')
            .Replace('٬', ',');
        var terms = text.Split('+');
        bool hasTerm = false;
        decimal sum = 0m;

        for (int i = 0; i < terms.Length; i++)
        {
            string term = terms[i].Trim();
            if (term.Length == 0)
            {
                // A trailing '+' is a legitimate in-progress expression: 60+50+
                if (i == terms.Length - 1 && hasTerm) continue;
                return false;
            }

            if (!decimal.TryParse(term, NumberStyles.Number, CultureInfo.CurrentCulture, out decimal part) &&
                !decimal.TryParse(term, NumberStyles.Number, CultureInfo.InvariantCulture, out part))
                return false;

            if (part < 0m) return false;
            try { sum = checked(sum + part); }
            catch (OverflowException) { return false; }
            hasTerm = true;
        }

        if (!hasTerm) return false;
        value = sum;
        return true;
    }

    public static bool TryEvaluateCompleteSum(string? expression, out decimal value)
    {
        value = 0m;
        if (string.IsNullOrWhiteSpace(expression) || expression.TrimEnd().EndsWith('+'))
            return false;
        return TryEvaluateSum(expression, out value);
    }

    public static bool IsAllowedKey(char keyChar)
    {
        return char.IsControl(keyChar) || char.IsDigit(keyChar) ||
               keyChar == '+' || keyChar == '.' || keyChar == ',' ||
               keyChar == '٫' || keyChar == '٬';
    }

    private static string NormalizeDigits(string text) => text
        .Replace('٠', '0').Replace('١', '1').Replace('٢', '2')
        .Replace('٣', '3').Replace('٤', '4').Replace('٥', '5')
        .Replace('٦', '6').Replace('٧', '7').Replace('٨', '8')
        .Replace('٩', '9');
}
