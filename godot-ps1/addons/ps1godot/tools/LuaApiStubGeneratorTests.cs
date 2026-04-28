#if TOOLS
using System;
using Godot;
using PS1Godot.Tools;

namespace PS1Godot.Tools;

// Parser regression tests for LuaApiStubGenerator. Feeds canned luaapi.hh
// fragments through the public pipeline (Parse + Emit) and asserts that
// the generated EmmyLua output contains the expected function stubs and
// annotations. Doesn't touch the filesystem.
//
// Run via Project > Tools > PS1Godot: Run Lua API Stub Generator Tests.
public static class LuaApiStubGeneratorTests
{
    private delegate void TestFn();
    private static readonly (string Name, TestFn Run)[] s_tests =
    {
        ("simple no-arg void binding",      TestSimple),
        ("single-param GetPosition binding",TestSingleParam),
        ("table-literal arg becomes Vec3",  TestTableArgVec3),
        ("optional brackets dropped",       TestOptionalBracketsDropped),
        ("docstring lines preserved above stub", TestDocstringPreserved),
        ("blank line resets doc accumulator",    TestBlankLineResets),
        ("return type infers object-or-nil",TestReturnObjectOrNil),
        ("return type infers boolean",      TestReturnBoolean),
        ("multi-namespace groups separately",TestMultipleNamespaces),
    };

    public static bool RunAll()
    {
        int pass = 0, fail = 0;
        GD.Print("[PS1Godot] Running LuaApiStubGenerator parser tests...");
        foreach (var (name, run) in s_tests)
        {
            try { run(); GD.Print($"  pass  {name}"); pass++; }
            catch (Exception e) { GD.PushError($"  FAIL  {name}: {e.Message}"); fail++; }
        }
        GD.Print($"[PS1Godot] Stub generator tests: {pass} passed, {fail} failed.");
        return fail == 0;
    }

    // Test harness: runs Parse + Emit against a canned input, returns output.
    private static string GenerateFrom(params string[] lines)
    {
        var binds = InvokeParse(lines);
        return InvokeEmit(binds);
    }

    // Use reflection to reach the private static Parse / Emit — keeps the
    // public surface of the generator small (just Run()) while letting us
    // test the pure-text pipeline in isolation.
    private static System.Collections.IList InvokeParse(string[] lines)
    {
        var mi = typeof(LuaApiStubGenerator).GetMethod("Parse",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            ?? throw new Exception("LuaApiStubGenerator.Parse not found");
        return (System.Collections.IList)mi.Invoke(null, new object[] { lines })!;
    }

    private static string InvokeEmit(System.Collections.IList binds)
    {
        var mi = typeof(LuaApiStubGenerator).GetMethod("Emit",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            ?? throw new Exception("LuaApiStubGenerator.Emit not found");
        return (string)mi.Invoke(null, new object[] { binds, "test.hh" })!;
    }

    private static void AssertContains(string s, string needle, string ctx)
    {
        if (s.IndexOf(needle, StringComparison.Ordinal) < 0)
            throw new Exception($"{ctx}: expected '{needle}' in output:\n{s}");
    }

    private static void AssertNotContains(string s, string needle, string ctx)
    {
        if (s.IndexOf(needle, StringComparison.Ordinal) >= 0)
            throw new Exception($"{ctx}: did NOT expect '{needle}' in output:\n{s}");
    }

    private static void TestSimple()
    {
        var s = GenerateFrom("    // Audio.Stop()");
        AssertContains(s, "function Audio.Stop() end", "zero-arg stub");
    }

    private static void TestSingleParam()
    {
        var s = GenerateFrom("    // Entity.GetTag(object) -> number");
        AssertContains(s, "function Entity.GetTag(object) end", "single param stub");
        AssertContains(s, "---@param object GameObject", "inferred type");
        AssertContains(s, "---@return number", "return type");
    }

    private static void TestTableArgVec3()
    {
        var s = GenerateFrom("    // Entity.SetPosition(object, {x, y, z})");
        AssertContains(s, "function Entity.SetPosition(object, pos) end", "table arg named 'pos'");
        AssertContains(s, "---@param pos Vec3", "table arg typed as Vec3");
    }

    private static void TestOptionalBracketsDropped()
    {
        var s = GenerateFrom("    // Entity.Spawn(tag, {x,y,z} [, rotY]) -> object or nil");
        AssertContains(s, "function Entity.Spawn(tag, pos, rotY) end", "bracket stripped");
        AssertContains(s, "---@return GameObject|nil", "object-or-nil return");
    }

    private static void TestDocstringPreserved()
    {
        // luaapi.hh writes the description line(s) AFTER the structured
        // signature comment, NOT before. The parser collects everything
        // between the sig and the next non-comment line.
        var s = GenerateFrom(
            "    // Entity.Destroy(object) -> nil",
            "    // Deactivates the object (fires onDisable). Lets the pool re-use it.",
            "    static int Entity_Destroy(lua_State* L);");
        AssertContains(s, "--- Deactivates the object", "post-sig doc preserved");
        AssertContains(s, "function Entity.Destroy(object) end", "sig still emitted");
    }

    private static void TestBlankLineResets()
    {
        // The static declaration (or any non-comment line) terminates
        // the doc accumulator so unrelated `//` comments later in the
        // file don't leak into the next bind.
        var s = GenerateFrom(
            "    // Audio.Stop()",
            "    static int Audio_Stop(lua_State* L);",
            "",
            "    // Unrelated comment between binds",
            "    // Music.Play(name)",
            "    static int Music_Play(lua_State* L);");
        AssertNotContains(s, "Unrelated comment between binds", "non-comment finalizes doc");
        AssertContains(s, "function Audio.Stop() end", "first sig still emitted");
        AssertContains(s, "function Music.Play(name) end", "second sig still emitted");
    }

    private static void TestReturnObjectOrNil()
    {
        var s = GenerateFrom("    // Entity.Find(name) -> object or nil");
        AssertContains(s, "---@return GameObject|nil", "GameObject|nil return");
    }

    private static void TestReturnBoolean()
    {
        var s = GenerateFrom("    // Entity.IsActive(object) -> boolean");
        AssertContains(s, "---@return boolean", "boolean return");
    }

    private static void TestMultipleNamespaces()
    {
        var s = GenerateFrom(
            "    // Entity.GetCount() -> number",
            "    // Audio.Stop()");
        AssertContains(s, "Entity = {}", "Entity table declared");
        AssertContains(s, "Audio = {}", "Audio table declared");
    }
}
#endif
