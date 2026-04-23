#if TOOLS
using System;
using Godot;
using PS1Godot.Exporter;

namespace PS1Godot.Tools;

// Regression tests for LuaDecimalRewriter. Pure text → text conversion,
// so each test is a small input string + expected-substring assertions.
// Follows the same harness shape as MidiSerializerTests (prints pass/fail
// to Godot output, returns true iff all passed).
public static class LuaDecimalRewriterTests
{
    private delegate void TestFn();
    private static readonly (string Name, TestFn Run)[] s_tests =
    {
        ("basic 0.06 → newFromRaw(246)",        TestBasic),
        ("integer passes through unchanged",     TestIntegerUntouched),
        ("hex literal passes through",           TestHexUntouched),
        ("scientific notation passes through",   TestScientificUntouched),
        ("string contents preserved",            TestStringUntouched),
        ("line comment preserved",               TestLineCommentUntouched),
        ("long comment preserved",               TestLongCommentUntouched),
        ("long string preserved",                TestLongStringUntouched),
        ("multiple decimals rewrite independently", TestMultipleDecimals),
        ("leading zero handled (0.5)",           TestLeadingZero),
        ("negative stays as unary minus",        TestNegativeUnary),
        ("identifier.field not rewritten",       TestFieldAccessUntouched),
        ("trailing dot not rewritten (5. alone)",TestTrailingDotUntouched),
        ("concat operator .. survives adjacent ints", TestConcatSurvives),
        ("3.14 rounds to 12861",                 TestPiRounding),
        ("1.0 → newFromRaw(4096)",               TestUnity),
    };

    public static bool RunAll()
    {
        int pass = 0, fail = 0;
        GD.Print("[PS1Godot] Running LuaDecimalRewriter regression tests...");
        foreach (var (name, run) in s_tests)
        {
            try
            {
                run();
                GD.Print($"  pass  {name}");
                pass++;
            }
            catch (Exception e)
            {
                GD.PushError($"  FAIL  {name}: {e.Message}");
                fail++;
            }
        }
        GD.Print($"[PS1Godot] Lua rewriter tests: {pass} passed, {fail} failed.");
        return fail == 0;
    }

    private static void AssertContains(string haystack, string needle, string ctx)
    {
        if (haystack.IndexOf(needle, StringComparison.Ordinal) < 0)
            throw new Exception($"{ctx}: expected to contain '{needle}', got: {haystack}");
    }

    private static void AssertNotContains(string haystack, string needle, string ctx)
    {
        if (haystack.IndexOf(needle, StringComparison.Ordinal) >= 0)
            throw new Exception($"{ctx}: expected NOT to contain '{needle}', got: {haystack}");
    }

    private static void AssertEq(string expected, string actual, string ctx)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
            throw new Exception($"{ctx}: expected '{expected}', got '{actual}'");
    }

    private static void TestBasic()
    {
        var r = LuaDecimalRewriter.Rewrite("Camera.Shake(0.06, 6)");
        AssertContains(r, "FixedPoint.newFromRaw(246)", "rewrite");
        AssertNotContains(r, "0.06", "original literal gone");
    }

    private static void TestIntegerUntouched()
    {
        AssertEq("local n = 42", LuaDecimalRewriter.Rewrite("local n = 42"),
                 "integer literal passthrough");
    }

    private static void TestHexUntouched()
    {
        AssertEq("0xDEADBEEF + 0x10", LuaDecimalRewriter.Rewrite("0xDEADBEEF + 0x10"),
                 "hex literal passthrough");
    }

    private static void TestScientificUntouched()
    {
        // 1e5 has no `.` so never matches IsDecimalLiteralStart; 1.5e2 DOES
        // start as decimal but the exponent-suffix branch bails out and
        // emits the whole token unchanged.
        var r1 = LuaDecimalRewriter.Rewrite("local e = 1e5");
        AssertEq("local e = 1e5", r1, "pure sci");
        var r2 = LuaDecimalRewriter.Rewrite("local e = 1.5e2");
        AssertContains(r2, "1.5e2", "sci with decimal stays verbatim");
        AssertNotContains(r2, "newFromRaw", "sci form not rewritten");
    }

    private static void TestStringUntouched()
    {
        var r = LuaDecimalRewriter.Rewrite("print(\"price: 0.06 USD\")");
        AssertContains(r, "\"price: 0.06 USD\"", "string body preserved");
        AssertNotContains(r, "newFromRaw", "no rewrite inside string");
    }

    private static void TestLineCommentUntouched()
    {
        var r = LuaDecimalRewriter.Rewrite("-- shake is 0.06\nreal = 1");
        AssertContains(r, "-- shake is 0.06", "line comment preserved");
        AssertNotContains(r, "newFromRaw", "no rewrite inside comment");
    }

    private static void TestLongCommentUntouched()
    {
        var r = LuaDecimalRewriter.Rewrite("--[[ 0.06 goes here ]] x = 1");
        AssertContains(r, "0.06 goes here", "long comment preserved");
        AssertNotContains(r, "newFromRaw", "no rewrite inside long comment");
    }

    private static void TestLongStringUntouched()
    {
        var r = LuaDecimalRewriter.Rewrite("s = [[hello 0.5]]");
        AssertContains(r, "hello 0.5", "long string preserved");
        AssertNotContains(r, "newFromRaw", "no rewrite inside long string");
    }

    private static void TestMultipleDecimals()
    {
        var r = LuaDecimalRewriter.Rewrite("Vec3.new(0.5, 1.0, 2.25)");
        AssertContains(r, "FixedPoint.newFromRaw(2048)", "0.5");
        AssertContains(r, "FixedPoint.newFromRaw(4096)", "1.0");
        AssertContains(r, "FixedPoint.newFromRaw(9216)", "2.25");
    }

    private static void TestLeadingZero()
    {
        // 0.5 * 4096 = 2048
        var r = LuaDecimalRewriter.Rewrite("return 0.5");
        AssertContains(r, "FixedPoint.newFromRaw(2048)", "0.5 mapping");
    }

    private static void TestNegativeUnary()
    {
        // Rewriter doesn't consume leading `-`; unary minus flows through
        // and FixedPoint.__unm handles it at runtime.
        var r = LuaDecimalRewriter.Rewrite("x = -0.5");
        AssertContains(r, "-FixedPoint.newFromRaw(2048)", "unary minus retained");
    }

    private static void TestFieldAccessUntouched()
    {
        var r = LuaDecimalRewriter.Rewrite("local y = foo.bar");
        AssertEq("local y = foo.bar", r, "field access passthrough");
    }

    private static void TestTrailingDotUntouched()
    {
        // `5.` is a rare Lua form; we don't rewrite it. The ScanNumberTail
        // branch still eats it so subsequent chars aren't mis-parsed.
        var r = LuaDecimalRewriter.Rewrite("x = 5.0 + 6");
        AssertContains(r, "FixedPoint.newFromRaw(20480)", "5.0 rewrites");
        // Force the edge case: `5.` alone followed by space. Not rewritten,
        // but also not broken.
        var r2 = LuaDecimalRewriter.Rewrite("x = 5. + 6");
        AssertNotContains(r2, "newFromRaw", "5. alone not rewritten");
    }

    private static void TestConcatSurvives()
    {
        // `"a" .. 1 .. "b"` — the `..` is concat, not a decimal. Numbers are
        // integers here so nothing should change.
        var r = LuaDecimalRewriter.Rewrite("s = \"a\" .. 1 .. \"b\"");
        AssertEq("s = \"a\" .. 1 .. \"b\"", r, "concat survives");
    }

    private static void TestPiRounding()
    {
        // 3.14 * 4096 = 12861.44, round-to-nearest → 12861
        var r = LuaDecimalRewriter.Rewrite("pi = 3.14");
        AssertContains(r, "FixedPoint.newFromRaw(12861)", "3.14 rounding");
    }

    private static void TestUnity()
    {
        var r = LuaDecimalRewriter.Rewrite("one = 1.0");
        AssertContains(r, "FixedPoint.newFromRaw(4096)", "1.0 = 4096");
    }
}
#endif
