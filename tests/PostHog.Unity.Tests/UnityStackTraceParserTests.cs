using PostHogUnity.ErrorTracking;

namespace PostHogUnity.Tests
{
    public class UnityStackTraceParserTests
    {
        public class TheParseMethod
        {
            [Fact]
            public void WithNullInput_ReturnsEmptyList()
            {
                var frames = UnityStackTraceParser.Parse(null);

                Assert.Empty(frames);
            }

            [Fact]
            public void WithEmptyString_ReturnsEmptyList()
            {
                var frames = UnityStackTraceParser.Parse(string.Empty);

                Assert.Empty(frames);
            }

            [Fact]
            public void WithLineThatHasNoFileMarker_PopulatesFunctionAndLeavesFileFieldsNull()
            {
                var line = "SomeNamespace.SomeClass:SomeMethod (System.Int32,System.String)";

                var frames = UnityStackTraceParser.Parse(line);

                Assert.Single(frames);
                var frame = frames[0];
                Assert.Equal(line, frame["function"]);
                Assert.Null(frame["filename"]);
                Assert.Null(frame["abs_path"]);
                Assert.Null(frame["lineno"]);
            }

            [Fact]
            public void WithLineThatHasNoParensAtAll_UsesRawLineAsFunction()
            {
                var line = "JustAFunctionName";

                var frames = UnityStackTraceParser.Parse(line);

                Assert.Single(frames);
                var frame = frames[0];
                Assert.Equal(line, frame["function"]);
                Assert.Null(frame["filename"]);
                Assert.Null(frame["abs_path"]);
                Assert.Null(frame["lineno"]);
            }

            [Fact]
            public void WithFileAndLineNumber_PopulatesFilenameAbsPathAndLineno()
            {
                var line =
                    "SomeNamespace.SomeClass:SomeMethod (System.Int32,System.String) (at Assets/Foo/Bar.cs:42)";

                var frames = UnityStackTraceParser.Parse(line);

                Assert.Single(frames);
                var frame = frames[0];
                Assert.Equal(
                    "SomeNamespace.SomeClass:SomeMethod (System.Int32,System.String)",
                    frame["function"]
                );
                Assert.Equal("Bar.cs", frame["filename"]);
                Assert.Equal("Assets/Foo/Bar.cs", frame["abs_path"]);
                Assert.Equal(42, frame["lineno"]);
            }

            [Fact]
            public void WithFileButNoLineNumber_LeavesLinenoNull()
            {
                var line = "Foo.Bar:Baz () (at Assets/Foo/Bar.cs)";

                var frames = UnityStackTraceParser.Parse(line);

                Assert.Single(frames);
                var frame = frames[0];
                Assert.Equal("Foo.Bar:Baz ()", frame["function"]);
                Assert.Equal("Bar.cs", frame["filename"]);
                Assert.Equal("Assets/Foo/Bar.cs", frame["abs_path"]);
                Assert.Null(frame["lineno"]);
            }

            [Fact]
            public void WithIl2CppPlaceholderAndZeroLineNumber_StripsToEmptyAndPinsLinenoAsZero()
            {
                // The IL2CPP placeholder strips to empty strings (not null) to
                // signal "location intentionally unknown". Lineno follows the
                // raw input verbatim, so :0 becomes 0 rather than null.
                var line = "SomeClass:Foo () (at <00000000>:0)";

                var frames = UnityStackTraceParser.Parse(line);

                Assert.Single(frames);
                var frame = frames[0];
                Assert.Equal("SomeClass:Foo ()", frame["function"]);
                Assert.Equal("", frame["filename"]);
                Assert.Equal("", frame["abs_path"]);
                Assert.Equal(0, frame["lineno"]);
            }

            [Fact]
            public void WithIl2CppPlaceholderAndNoLineNumber_StripsToEmptyAndLeavesLinenoNull()
            {
                var line = "SomeClass:Foo () (at <00000000>)";

                var frames = UnityStackTraceParser.Parse(line);

                Assert.Single(frames);
                var frame = frames[0];
                Assert.Equal("SomeClass:Foo ()", frame["function"]);
                Assert.Equal("", frame["filename"]);
                Assert.Equal("", frame["abs_path"]);
                Assert.Null(frame["lineno"]);
            }

            [Fact]
            public void WithMultipleLines_ReturnsFramesInSourceOrder()
            {
                var trace = string.Join(
                    "\n",
                    "A.B:First () (at Assets/A.cs:1)",
                    "A.B:Second () (at Assets/A.cs:2)",
                    "A.B:Third () (at Assets/A.cs:3)"
                );

                var frames = UnityStackTraceParser.Parse(trace);

                Assert.Equal(3, frames.Count);
                Assert.Equal("A.B:First ()", frames[0]["function"]);
                Assert.Equal(1, frames[0]["lineno"]);
                Assert.Equal("A.B:Second ()", frames[1]["function"]);
                Assert.Equal(2, frames[1]["lineno"]);
                Assert.Equal("A.B:Third ()", frames[2]["function"]);
                Assert.Equal(3, frames[2]["lineno"]);
            }

            [Fact]
            public void WithCrLfLineEndings_ToleratesTrailingCarriageReturn()
            {
                var trace = "A.B:First () (at Assets/A.cs:1)\r\nA.B:Second () (at Assets/B.cs:2)\r\n";

                var frames = UnityStackTraceParser.Parse(trace);

                Assert.Equal(2, frames.Count);
                Assert.Equal("Assets/A.cs", frames[0]["abs_path"]);
                Assert.Equal(1, frames[0]["lineno"]);
                Assert.Equal("Assets/B.cs", frames[1]["abs_path"]);
                Assert.Equal(2, frames[1]["lineno"]);
            }

            [Fact]
            public void WithBlankLinesBetweenFrames_SkipsThem()
            {
                var trace = "\nA.B:First () (at Assets/A.cs:1)\n\n\nA.B:Second () (at Assets/A.cs:2)\n";

                var frames = UnityStackTraceParser.Parse(trace);

                Assert.Equal(2, frames.Count);
                Assert.Equal("A.B:First ()", frames[0]["function"]);
                Assert.Equal("A.B:Second ()", frames[1]["function"]);
            }

            [Fact]
            public void EveryFrameHasCustomPlatformAndCsharpLang()
            {
                var trace = string.Join(
                    "\n",
                    "NoFileMarker",
                    "A.B:Foo ()",
                    "A.B:Bar () (at Assets/X.cs:10)",
                    "A.B:Baz () (at <00000000>:0)"
                );

                var frames = UnityStackTraceParser.Parse(trace);

                Assert.Equal(4, frames.Count);
                foreach (var frame in frames)
                {
                    Assert.Equal("custom", frame["platform"]);
                    Assert.Equal("csharp", frame["lang"]);
                }
            }

            [Fact]
            public void WithMalformedLineMissingClosingParen_DoesNotThrowAndPreservesRawLine()
            {
                var line = "Broken.Method (";

                List<Dictionary<string, object>> frames = null;
                var ex = Record.Exception(() => frames = UnityStackTraceParser.Parse(line));

                Assert.Null(ex);
                Assert.Single(frames);
                Assert.Equal(line, frames[0]["function"]);
                Assert.Null(frames[0]["filename"]);
                Assert.Null(frames[0]["abs_path"]);
                Assert.Null(frames[0]["lineno"]);
            }

            [Fact]
            public void WithHeaderLikeRethrowLine_PreservesRawLineAsFunction()
            {
                // Lines without a ')' (rethrow markers, "End of stack trace" markers,
                // JS-style "at fn (file.js:1:1)" entries from WebGL builds) must
                // remain in the frame list so the captured stack stays diagnostic.
                var line = "Rethrow as InvalidOperationException: boom";

                var frames = UnityStackTraceParser.Parse(line);

                Assert.Single(frames);
                Assert.Equal(line, frames[0]["function"]);
                Assert.Null(frames[0]["abs_path"]);
            }

            [Fact]
            public void WithWindowsStylePath_ExtractsLeafFilenameUsingBackslash()
            {
                var line = @"MyGame.Player:Update () (at C:\Projects\Game\Assets\Player.cs:99)";

                var frames = UnityStackTraceParser.Parse(line);

                var frame = Assert.Single(frames);
                Assert.Equal(@"C:\Projects\Game\Assets\Player.cs", frame["abs_path"]);
                Assert.Equal("Player.cs", frame["filename"]);
                Assert.Equal(99, frame["lineno"]);
            }
        }

        public class TheParseExceptionMethod
        {
            // The eight contract keys that every ParseException frame must carry. If any
            // frame is missing one of these keys, the dictionary lookup below will throw
            // KeyNotFoundException — flagging a behavior change in the wire format.
            static readonly string[] ContractKeys =
            {
                "platform",
                "lang",
                "filename",
                "abs_path",
                "function",
                "module",
                "lineno",
                "colno",
            };

            static readonly Exception ThrownException = Capture();

            static Exception Capture()
            {
                try
                {
                    throw new InvalidOperationException("characterization");
                }
                catch (Exception e)
                {
                    return e;
                }
            }

            [Fact]
            public void WithNullInput_ReturnsEmptyList()
            {
                var frames = UnityStackTraceParser.ParseException(null);

                Assert.Empty(frames);
            }

            [Fact]
            public void WithThrownException_ReturnsAtLeastOneFrame()
            {
                var frames = UnityStackTraceParser.ParseException(ThrownException);

                Assert.NotEmpty(frames);
            }

            [Fact]
            public void EveryFrameContainsAllEightContractKeys()
            {
                var frames = UnityStackTraceParser.ParseException(ThrownException);

                Assert.NotEmpty(frames);
                foreach (var frame in frames)
                {
                    foreach (var key in ContractKeys)
                    {
                        Assert.True(
                            frame.ContainsKey(key),
                            $"frame missing contract key '{key}'. keys present: {string.Join(",", frame.Keys)}"
                        );
                    }
                }
            }

            [Fact]
            public void EveryFrameHasCustomPlatformAndCsharpLang()
            {
                var frames = UnityStackTraceParser.ParseException(ThrownException);

                Assert.NotEmpty(frames);
                foreach (var frame in frames)
                {
                    Assert.Equal("custom", frame["platform"]);
                    Assert.Equal("csharp", frame["lang"]);
                }
            }

            [Fact]
            public void WithFreshlyConstructedException_ReturnsEmptyListWithoutThrowing()
            {
                var ex = new InvalidOperationException("not thrown");

                List<Dictionary<string, object>> frames = null;
                var thrown = Record.Exception(
                    () => frames = UnityStackTraceParser.ParseException(ex)
                );

                Assert.Null(thrown);
                Assert.Empty(frames);
            }

            [Fact]
            public void FunctionFieldIsPopulatedForThrownFrame()
            {
                var frames = UnityStackTraceParser.ParseException(ThrownException);

                Assert.NotEmpty(frames);
                var function = frames[0]["function"] as string;
                Assert.False(string.IsNullOrEmpty(function), "expected non-empty function name");
            }
        }
    }
}
