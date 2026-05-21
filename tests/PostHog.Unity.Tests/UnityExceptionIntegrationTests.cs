using PostHogUnity.ErrorTracking;
using UnityEngine;

namespace PostHogUnity.Tests
{
    /// <summary>
    /// Serializes tests that mutate global Unity state (e.g. <c>Debug.unityLogger.logHandler</c>,
    /// the <c>Application.logMessageReceived</c> backing field). Every nested test class in
    /// <see cref="UnityExceptionIntegrationTests"/> and <see cref="WebGLExceptionIntegrationTests"/>
    /// must opt in with <c>[Collection("UnityGlobals")]</c> — xUnit does not propagate the
    /// outer-class attribute to nested test classes.
    /// </summary>
    [CollectionDefinition("UnityGlobals")]
    public class UnityGlobalsCollection { }

    /// <summary>
    /// Characterization tests for <see cref="UnityExceptionIntegration"/>.
    ///
    /// Notes on the test seam:
    ///   * The Unity3D.SDK stub package's <c>Debug.Log*</c> methods call into ECall
    ///     (native code) and throw <c>SecurityException</c> when invoked from a
    ///     standard .NET test host. To make any test that exercises code reaching
    ///     <c>PostHogLogger</c> survive, every test below first swaps
    ///     <c>Debug.unityLogger.logHandler</c> for a recording test double via
    ///     <see cref="HandlerScope"/>. With the swap in place, <c>Debug.LogWarning</c>
    ///     / <c>Debug.LogError</c> route through managed code into the test double
    ///     rather than into the native stub.
    ///   * Tests in this collection share that global state and therefore run
    ///     serially (see the <c>Collection</c> attribute).
    /// </summary>
    [Collection("UnityGlobals")]
    public class UnityExceptionIntegrationTests
    {
        /// <summary>
        /// Recording <see cref="ILogHandler"/> used as both the "original" log
        /// handler the integration captures at <c>Register</c> time and the
        /// stand-in to route any internal PostHog logging through.
        /// </summary>
        sealed class RecordingLogHandler : ILogHandler
        {
            public int LogExceptionCount;
            public int LogFormatCount;
            public Exception LastException;
            public LogType LastLogType;
            public string LastFormat;

            public void LogException(Exception exception, UnityEngine.Object context)
            {
                LogExceptionCount++;
                LastException = exception;
            }

            public void LogFormat(
                LogType logType,
                UnityEngine.Object context,
                string format,
                params object[] args
            )
            {
                LogFormatCount++;
                LastLogType = logType;
                LastFormat = format;
            }
        }

        /// <summary>
        /// Snapshots <c>Debug.unityLogger.logHandler</c> on construction and
        /// restores it on disposal. Uses the supplied test double in between so
        /// any internal <c>Debug.Log*</c> calls route through managed code.
        /// </summary>
        sealed class HandlerScope : IDisposable
        {
            readonly ILogHandler _original;

            public HandlerScope(ILogHandler handler)
            {
                _original = Debug.unityLogger.logHandler;
                Debug.unityLogger.logHandler = handler;
            }

            public void Dispose()
            {
                Debug.unityLogger.logHandler = _original;
            }
        }

        [Collection("UnityGlobals")]
        public class TheRegisterMethod
        {
            [Fact]
            public void AfterRegister_LogExceptionInvokesCallbackWithException()
            {
                var handler = new RecordingLogHandler();
                using var scope = new HandlerScope(handler);

                var integration = new UnityExceptionIntegration();
                Exception captured = null;
                integration.Register(ex => captured = ex);

                var thrown = new InvalidOperationException("boom");
                ((ILogHandler)integration).LogException(thrown, null);

                Assert.Same(thrown, captured);
            }

            [Fact]
            public void CallingRegisterTwice_DoesNotReplaceCallbackAndDoesNotThrow()
            {
                var handler = new RecordingLogHandler();
                using var scope = new HandlerScope(handler);

                var integration = new UnityExceptionIntegration();
                Exception capturedByFirst = null;
                Exception capturedBySecond = null;
                integration.Register(ex => capturedByFirst = ex);

                var thrown = Record.Exception(() =>
                    integration.Register(ex => capturedBySecond = ex)
                );

                Assert.Null(thrown);

                var raised = new InvalidOperationException("test");
                ((ILogHandler)integration).LogException(raised, null);

                Assert.Same(raised, capturedByFirst);
                Assert.Null(capturedBySecond);
            }
        }

        [Collection("UnityGlobals")]
        public class TheUnregisterMethod
        {
            [Fact]
            public void AfterUnregister_LogExceptionDoesNotInvokeCallback()
            {
                var handler = new RecordingLogHandler();
                using var scope = new HandlerScope(handler);

                var integration = new UnityExceptionIntegration();
                int callbackInvocations = 0;
                integration.Register(_ => callbackInvocations++);
                integration.Unregister();

                ((ILogHandler)integration).LogException(new Exception("after"), null);

                Assert.Equal(0, callbackInvocations);
            }

            [Fact]
            public void WhenNotRegistered_DoesNotThrow()
            {
                var handler = new RecordingLogHandler();
                using var scope = new HandlerScope(handler);

                var integration = new UnityExceptionIntegration();

                var thrown = Record.Exception(() => integration.Unregister());

                Assert.Null(thrown);
            }
        }

        [Collection("UnityGlobals")]
        public class TheLogExceptionMethod
        {
            [Fact]
            public void ForwardsToTheOriginalLogHandler()
            {
                var handler = new RecordingLogHandler();
                using var scope = new HandlerScope(handler);

                var integration = new UnityExceptionIntegration();
                integration.Register(_ => { });

                var raised = new InvalidOperationException("forward");
                ((ILogHandler)integration).LogException(raised, null);

                Assert.Equal(1, handler.LogExceptionCount);
                Assert.Same(raised, handler.LastException);
            }

            [Fact]
            public void WhenCallbackThrows_DoesNotPropagate()
            {
                var handler = new RecordingLogHandler();
                using var scope = new HandlerScope(handler);

                var integration = new UnityExceptionIntegration();
                integration.Register(_ => throw new ApplicationException("callback failed"));

                var raised = new InvalidOperationException("raise");
                var thrown = Record.Exception(() =>
                    ((ILogHandler)integration).LogException(raised, null)
                );

                Assert.Null(thrown);
                // The original handler still gets the exception.
                Assert.Equal(1, handler.LogExceptionCount);
                Assert.Same(raised, handler.LastException);
            }

            [Fact]
            public void WithNullException_DoesNotInvokeCallback()
            {
                var handler = new RecordingLogHandler();
                using var scope = new HandlerScope(handler);

                var integration = new UnityExceptionIntegration();
                int callbackInvocations = 0;
                integration.Register(_ => callbackInvocations++);

                var thrown = Record.Exception(() =>
                    ((ILogHandler)integration).LogException(null, null)
                );

                Assert.Null(thrown);
                Assert.Equal(0, callbackInvocations);
            }
        }

        [Collection("UnityGlobals")]
        public class TheLogFormatMethod
        {
            [Fact]
            public void DoesNotInvokeCallback()
            {
                var handler = new RecordingLogHandler();
                using var scope = new HandlerScope(handler);

                var integration = new UnityExceptionIntegration();
                int callbackInvocations = 0;
                integration.Register(_ => callbackInvocations++);

                ((ILogHandler)integration).LogFormat(LogType.Error, null, "{0}", "msg");
                ((ILogHandler)integration).LogFormat(LogType.Warning, null, "{0}", "msg");
                ((ILogHandler)integration).LogFormat(LogType.Log, null, "{0}", "msg");
                ((ILogHandler)integration).LogFormat(LogType.Assert, null, "{0}", "msg");

                Assert.Equal(0, callbackInvocations);
            }

            [Fact]
            public void ForwardsToTheOriginalLogHandler()
            {
                var handler = new RecordingLogHandler();
                using var scope = new HandlerScope(handler);

                var integration = new UnityExceptionIntegration();
                integration.Register(_ => { });

                ((ILogHandler)integration).LogFormat(LogType.Warning, null, "fmt", "arg");

                Assert.Equal(1, handler.LogFormatCount);
                Assert.Equal(LogType.Warning, handler.LastLogType);
                Assert.Equal("fmt", handler.LastFormat);
            }
        }
    }
}
