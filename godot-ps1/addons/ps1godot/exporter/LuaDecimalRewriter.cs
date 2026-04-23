using System.Globalization;
using System.Text;

namespace PS1Godot.Exporter;

// Rewrites decimal-number literals in Lua source to FixedPoint.newFromRaw()
// calls, so psxlua's NOPARSER (integer-only) tokenizer accepts the output.
//
// Motivation: every first-time Lua author hits `malformed number near '0.06'`
// because the runtime's Lua build can't parse fractions. The fix used to be
// "write 246 and know it's 0.06 * 4096." This rewriter eliminates that tax —
// authors write `Camera.Shake(0.06, 6)` and the exporter emits
// `Camera.Shake(FixedPoint.newFromRaw(246), 6)` behind the scenes.
//
// Only literals of the form <digits>.<digits> (both sides >= 1 digit) are
// rewritten. Integers, hex (0xABC), and scientific notation (1e5) are
// passed through unchanged — the first two are already valid, and scientific
// notation is rare enough that if an author really wants it they can write
// the rounded integer themselves.
//
// String literals and comments are preserved verbatim — the rewriter walks
// a small Lua-lexer state machine so `"price: 0.06"` and `-- 0.06` don't
// get touched.
public static class LuaDecimalRewriter
{
    private const int FixedShift = 12;
    private const int FixedScale = 1 << FixedShift; // 4096 = FixedPoint<12>

    public static string Rewrite(string source)
    {
        if (string.IsNullOrEmpty(source)) return source;
        var sb = new StringBuilder(source.Length + 64);
        int i = 0;
        int n = source.Length;
        while (i < n)
        {
            char c = source[i];

            if (c == '"' || c == '\'')
            {
                int end = SkipShortString(source, i);
                sb.Append(source, i, end - i);
                i = end;
                continue;
            }

            if (c == '[')
            {
                int eqCount = MatchLongBracketOpen(source, i);
                if (eqCount >= 0)
                {
                    int end = SkipLongBracket(source, i, eqCount);
                    sb.Append(source, i, end - i);
                    i = end;
                    continue;
                }
            }

            if (c == '-' && i + 1 < n && source[i + 1] == '-')
            {
                int start = i;
                int after = i + 2;
                if (after < n && source[after] == '[')
                {
                    int eqCount = MatchLongBracketOpen(source, after);
                    if (eqCount >= 0)
                    {
                        int end = SkipLongBracket(source, after, eqCount);
                        sb.Append(source, start, end - start);
                        i = end;
                        continue;
                    }
                }
                int eol = source.IndexOf('\n', after);
                if (eol < 0) eol = n;
                sb.Append(source, start, eol - start);
                i = eol;
                continue;
            }

            if (IsDecimalLiteralStart(source, i))
            {
                int tokStart = i;
                while (i < n && IsAsciiDigit(source[i])) i++;
                int dotPos = i;
                i++; // consume '.'
                while (i < n && IsAsciiDigit(source[i])) i++;
                // If scientific suffix follows, we can't safely rewrite — bail
                // out and emit the whole numeric token (including exponent) as-is.
                if (i < n && (source[i] == 'e' || source[i] == 'E'))
                {
                    i++;
                    if (i < n && (source[i] == '+' || source[i] == '-')) i++;
                    while (i < n && IsAsciiDigit(source[i])) i++;
                    sb.Append(source, tokStart, i - tokStart);
                    continue;
                }
                string intStr = source.Substring(tokStart, dotPos - tokStart);
                string fracStr = source.Substring(dotPos + 1, i - dotPos - 1);
                long raw = ComputeFp12Raw(intStr, fracStr);
                sb.Append("FixedPoint.newFromRaw(");
                sb.Append(raw.ToString(CultureInfo.InvariantCulture));
                sb.Append(')');
                continue;
            }

            // Non-decimal numeric token (hex / integer / pure scientific). We
            // must eat the whole token so later chars don't get mis-parsed as
            // identifiers.
            if (IsAsciiDigit(c) && (i == 0 || !IsIdentChar(source[i - 1])))
            {
                int end = ScanNumberTail(source, i);
                sb.Append(source, i, end - i);
                i = end;
                continue;
            }

            sb.Append(c);
            i++;
        }
        return sb.ToString();
    }

    private static bool IsAsciiDigit(char c) => c >= '0' && c <= '9';

    private static bool IsIdentChar(char c) =>
        c == '_' || (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || IsAsciiDigit(c);

    private static bool IsDecimalLiteralStart(string s, int i)
    {
        int n = s.Length;
        if (i >= n || !IsAsciiDigit(s[i])) return false;
        if (i > 0 && IsIdentChar(s[i - 1])) return false;
        // Reject hex prefix 0x / 0X — not a decimal literal in Lua's grammar.
        if (s[i] == '0' && i + 1 < n && (s[i + 1] == 'x' || s[i + 1] == 'X')) return false;
        int j = i;
        while (j < n && IsAsciiDigit(s[j])) j++;
        if (j >= n || s[j] != '.') return false;
        // `5.` alone (trailing dot with no fractional digits) isn't rewritten —
        // rare in practice and easy to write as `5.0`. Also rules out `a..b`
        // string concat operator appearing after a numeric literal.
        if (j + 1 >= n || !IsAsciiDigit(s[j + 1])) return false;
        return true;
    }

    private static long ComputeFp12Raw(string intStr, string fracStr)
    {
        long intVal = 0;
        foreach (char c in intStr) intVal = intVal * 10 + (c - '0');
        long fracNum = 0;
        foreach (char c in fracStr) fracNum = fracNum * 10 + (c - '0');
        long fracDen = 1;
        for (int k = 0; k < fracStr.Length; k++) fracDen *= 10;
        long intRaw = intVal << FixedShift;
        long fracRawNumer = fracNum << FixedShift;
        // Round-to-nearest, ties toward +inf (fracDen / 2). Values are always
        // non-negative here — unary minus is preserved as a separate token in
        // the Lua source, so `-0.06` → `-FixedPoint.newFromRaw(246)` works
        // via FixedPoint.__unm at runtime.
        long fracRaw = (fracRawNumer + fracDen / 2) / fracDen;
        return intRaw + fracRaw;
    }

    private static int ScanNumberTail(string s, int i)
    {
        int n = s.Length;
        if (i + 1 < n && s[i] == '0' && (s[i + 1] == 'x' || s[i + 1] == 'X'))
        {
            int j = i + 2;
            while (j < n && IsHexDigit(s[j])) j++;
            return j;
        }
        int k = i;
        while (k < n && IsAsciiDigit(s[k])) k++;
        if (k < n && s[k] == '.' && k + 1 < n && IsAsciiDigit(s[k + 1]))
        {
            k++;
            while (k < n && IsAsciiDigit(s[k])) k++;
        }
        if (k < n && (s[k] == 'e' || s[k] == 'E'))
        {
            k++;
            if (k < n && (s[k] == '+' || s[k] == '-')) k++;
            while (k < n && IsAsciiDigit(s[k])) k++;
        }
        return k;
    }

    private static bool IsHexDigit(char c) =>
        (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');

    private static int SkipShortString(string s, int i)
    {
        int n = s.Length;
        char quote = s[i];
        int j = i + 1;
        while (j < n)
        {
            char c = s[j];
            if (c == '\\')
            {
                // Skip the escape sequence — one char past the backslash is
                // enough for the common cases (\n, \\, \", \'); more elaborate
                // forms (\xHH, \ddd, \z) are handled correctly because we'll
                // re-enter the loop at the char after the backslash+1 and
                // hit either a non-special char or the next escape.
                j += 2;
                continue;
            }
            if (c == quote) return j + 1;
            if (c == '\n') return j;
            j++;
        }
        return j;
    }

    // Returns eqCount (≥ 0) if this is a long-bracket opener `[`, `[=[`,
    // `[==[`, …, or -1 if the `[` isn't the start of a long bracket.
    private static int MatchLongBracketOpen(string s, int i)
    {
        int n = s.Length;
        if (i >= n || s[i] != '[') return -1;
        int j = i + 1;
        int eqCount = 0;
        while (j < n && s[j] == '=') { eqCount++; j++; }
        if (j >= n || s[j] != '[') return -1;
        return eqCount;
    }

    // Returns position just past the closing `]=…=]` of a long bracket.
    private static int SkipLongBracket(string s, int i, int eqCount)
    {
        int n = s.Length;
        int j = i + 2 + eqCount;
        while (j < n)
        {
            if (s[j] == ']')
            {
                int k = j + 1;
                int seen = 0;
                while (k < n && s[k] == '=' && seen < eqCount) { seen++; k++; }
                if (seen == eqCount && k < n && s[k] == ']') return k + 1;
            }
            j++;
        }
        return n;
    }
}
