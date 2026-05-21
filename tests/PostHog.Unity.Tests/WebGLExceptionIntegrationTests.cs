using System.Reflection;
using PostHogUnity.ErrorTracking;
using UnityEngine;

namespace PostHogUnity.Tests
{
    /// <summary>
    /// Characterization tests for <see cref="WebGLExceptionIntegration"/>.
    ///
    /// Notes on the test seam (limitations of the Unity3D.SDK stub package):
    ///   * <c>Application.logMessageReceived += …</c> and <c>-= …</c> route through
    ///     the stub's <c>add_logMessageReceived</c> / <c>remove_logMessageReceived</c>
    ///     accessors, which call into native code (ECall) and throw
    ///     <c>SecurityException</c> in this test host. That means we cannot
    ///     directly exercise <see cref="WebGLExceptionIntegration.Register"/> or
    ///     <see cref="WebGLExceptionIntegration.Unregister"/> as a black box —
    ///     those tests are <c>Skip</c>ped below with the reason recorded.
    ///   * We can, however, exercise the private <c>HandleLogMessage</c>
    ///     handler — the one piece of logic with branching: <c>LogType</c>
    ///     filtering, "[PostHog]" prefix filtering, and try/catch around the
    ///     callback. We do so via reflection, populating the private callback
    ///     field directly. These tests are <em>structural</em>: if the rewrite
    ///     renames the field/method, the reflection lookups below must be
    ///     updated to point at whatever replaces them. The observable behavior
    ///     they pin (filter rules + non-propagation) is still part of the public
    ///     contract.
    ///   * Internal logging paths reach <c>Debug.LogError</c>, which also calls
    ///     into native code. As in <see cref="UnityExceptionIntegrationTests"/>,
    ///     each test first swaps <c>Debug.unityLogger.logHandler</c> for a
    ///     no-op test double so the native call never happens.
    /// </summary>
    [Collection("UnityGlobals")]
    public class WebGLExceptionIntegrationTests
    {
        sealed class NullLogHandler : ILogHandler
        {
            public void LogException(Exception exception, UnityEngine.Object context) { }

            public void LogFormat(
                LogType logType,
                UnityEngine.Object context,
                string format,
                params object[] args
            ) { }
        }

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

        const string CallbackFieldName = "_callback";
        const string HandleLogMessageMethodName = "HandleLogMessage";

        static readonly MethodInfo HandleLogMessageMethod =
            typeof(WebGLExceptionIntegration).GetMethod(
                HandleLogMessageMethodName,
                BindingFlags.Instance | BindingFlags.NonPublic
            ) ?? throw new InvalidOperationException(
                $"Could not find {HandleLogMessageMethodName} on {typeof(WebGLExceptionIntegration).FullName}. "
                + "If the rewrite renamed this private method, update the reflection lookup above to match."
            );

        static readonly FieldInfo CallbackField =
            typeof(WebGLExceptionIntegration).GetField(
                CallbackFieldName,
                BindingFlags.Instance | BindingFlags.NonPublic
            ) ?? throw new InvalidOperationException(
                $"Could not find {CallbackFieldName} on {typeof(WebGLExceptionIntegration).FullName}. "
                + "If the rewrite renamed this field, update the reflection lookup above to match."
            );

        static void SetCallback(
            WebGLExceptionIntegration integration,
            Action<string, string> callback
        )
        {
            CallbackField.SetValue(integration, callback);
        }

        static void RaiseLogMessage(
            WebGLExceptionIntegration integration,
            string condition,
            string stackTrace,
            LogType type
        )
        {
            try
            {
                HandleLogMessageMethod.Invoke(
                    integration,
                    new object[] { condition, stackTrace, type }
                );
            }
            catch (TargetInvocationException tie) when (tie.InnerException != null)
            {
                // Surface the original exception so test assertions can reason about it.
                throw tie.InnerException;
            }
        }

        [Collection("UnityGlobals")]
        public class TheRegisterMethod
        {
            [Fact(
                Skip =
                    "Register subscribes via Application.logMessageReceived += … which calls "
                    + "into native code (ECall) in the Unity3D.SDK stub and throws SecurityException. "
                    + "Cannot exercise the public Register contract from this test host."
            )]
            public void AfterRegister_ExceptionLogMessageInvokesCallback() { }
        }

        [Collection("UnityGlobals")]
        public class TheUnregisterMethod
        {
            [Fact(
                Skip =
                    "Unregister unsubscribes via Application.logMessageReceived -= … which calls "
                    + "into native code (ECall) in the Unity3D.SDK stub and throws SecurityException. "
                    + "Cannot exercise the public Unregister contract from this test host."
            )]
            public void AfterUnregister_LogMessageDoesNotInvokeCallback() { }
        }

        [Collection("UnityGlobals")]
        public class TheHandleLogMessageMethod
        {
            // Structural tests: these invoke the private HandleLogMessage method
            // via reflection because Application.logMessageReceived cannot be raised
            // through the stub's public event API. They still pin the public-contract
            // behavior of the filter rules and non-propagation guarantee.

            [Fact]
            public void ExceptionType_WithNonEmptyConditionAndStackTrace_InvokesCallback()
            {
                using var scope = new HandlerScope(new NullLogHandler());

                var integration = new WebGLExceptionIntegration();
                string capturedCondition = null;
                string capturedStack = null;
                SetCallback(
                    integration,
                    (c, s) =>
                    {
                        capturedCondition = c;
                        capturedStack = s;
                    }
                );

                RaiseLogMessage(integration, "NullReferenceException", "stack", LogType.Exception);

                Assert.Equal("NullReferenceException", capturedCondition);
                Assert.Equal("stack", capturedStack);
            }

            [Theory]
            [InlineData(LogType.Error)]
            [InlineData(LogType.Warning)]
            [InlineData(LogType.Log)]
            [InlineData(LogType.Assert)]
            public void NonExceptionType_DoesNotInvokeCallback(LogType type)
            {
                using var scope = new HandlerScope(new NullLogHandler());

                var integration = new WebGLExceptionIntegration();
                int invocations = 0;
                SetCallback(integration, (_, __) => invocations++);

                RaiseLogMessage(integration, "anything", "stack", type);

                Assert.Equal(0, invocations);
            }

            [Fact]
            public void ConditionStartingWithPostHogPrefix_IsFilteredOut()
            {
                using var scope = new HandlerScope(new NullLogHandler());

                var integration = new WebGLExceptionIntegration();
                int invocations = 0;
                SetCallback(integration, (_, __) => invocations++);

                RaiseLogMessage(
                    integration,
                    "[PostHog] An internal warning",
                    "stack",
                    LogType.Exception
                );

                Assert.Equal(0, invocations);
            }

            [Fact]
            public void CallbackThatThrows_DoesNotPropagate()
            {
                using var scope = new HandlerScope(new NullLogHandler());

                var integration = new WebGLExceptionIntegration();
                SetCallback(
                    integration,
                    (_, __) => throw new ApplicationException("callback failed")
                );

                var thrown = Record.Exception(
                    () => RaiseLogMessage(integration, "msg", "stack", LogType.Exception)
                );

                Assert.Null(thrown);
            }

            [Fact]
            public void WithNoRegisteredCallback_DoesNothing()
            {
                using var scope = new HandlerScope(new NullLogHandler());

                // No callback set — exercise the null-conditional branch.
                var integration = new WebGLExceptionIntegration();

                var thrown = Record.Exception(
                    () => RaiseLogMessage(integration, "msg", "stack", LogType.Exception)
                );

                Assert.Null(thrown);
            }
        }
    }
}
