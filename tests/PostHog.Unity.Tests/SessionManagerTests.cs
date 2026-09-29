using PostHogUnity;

namespace PostHogUnity.Tests
{
    public class SessionManagerTests
    {
        [Theory]
        [InlineData(30, false)]
        [InlineData(24 * 60, true)]
        public void SessionId_RotatesOnlyAfterTimeoutBoundary(
            int boundaryMinutes,
            bool keepSessionActiveUntilBoundary
        )
        {
            var start = new DateTime(2026, 4, 2, 14, 57, 39, DateTimeKind.Utc);
            var now = start;
            var sessionManager = new SessionManager(() => now);

            var originalSessionId = sessionManager.SessionId;

            if (keepSessionActiveUntilBoundary)
            {
                // Keep the session active until just before the max session length.
                for (int i = 0; i < 49; i++)
                {
                    now = now.AddMinutes(29);
                    sessionManager.Touch();
                }
            }

            var boundary = start.AddMinutes(boundaryMinutes);
            now = boundary;
            Assert.Equal(originalSessionId, sessionManager.SessionId);

            now = boundary.AddTicks(1);
            var rotatedSessionId = sessionManager.SessionId;

            Assert.NotNull(originalSessionId);
            Assert.NotNull(rotatedSessionId);
            Assert.NotEqual(originalSessionId, rotatedSessionId);
        }

        [Fact]
        public void SessionId_IsClearedOnlyAfterBackgroundInactivityTimeoutBoundary()
        {
            var start = new DateTime(2026, 4, 2, 14, 57, 39, DateTimeKind.Utc);
            var now = start;
            var sessionManager = new SessionManager(() => now);

            var originalSessionId = sessionManager.SessionId;
            sessionManager.OnBackground();

            now = start.AddMinutes(30);
            Assert.Equal(originalSessionId, sessionManager.SessionId);

            now = start.AddMinutes(30).AddTicks(1);

            Assert.NotNull(originalSessionId);
            Assert.Null(sessionManager.SessionId);
        }

        [Fact]
        public void OnForeground_StartsNewSessionAfterBackgroundTimeout()
        {
            var now = new DateTime(2026, 4, 2, 14, 57, 39, DateTimeKind.Utc);
            var sessionManager = new SessionManager(() => now);

            var originalSessionId = sessionManager.SessionId;
            sessionManager.OnBackground();
            now = now.AddMinutes(31);
            sessionManager.OnForeground();

            var newSessionId = sessionManager.SessionId;

            Assert.NotNull(originalSessionId);
            Assert.NotNull(newSessionId);
            Assert.NotEqual(originalSessionId, newSessionId);
        }

        [Fact]
        public void Touch_ExtendsInactivityDeadline()
        {
            var now = new DateTime(2026, 4, 2, 0, 0, 0, DateTimeKind.Utc);
            var manager = new SessionManager(() => now);
            var original = manager.SessionId;
            now = now.AddMinutes(29);
            manager.Touch();
            now = now.AddMinutes(30);
            Assert.Equal(original, manager.SessionId);
            now = now.AddTicks(1);
            Assert.NotEqual(original, manager.SessionId);
        }

        [Fact]
        public void EndSession_DoesNotRestartInBackgroundUntilForegrounded()
        {
            var now = new DateTime(2026, 4, 2, 0, 0, 0, DateTimeKind.Utc);
            var manager = new SessionManager(() => now);
            var original = manager.SessionId;
            manager.OnBackground();
            manager.EndSession();
            manager.EndSession();
            manager.Touch();
            Assert.Null(manager.SessionId);
            manager.OnForeground();
            Assert.NotNull(manager.SessionId);
            Assert.NotEqual(original, manager.SessionId);
        }

        [Fact]
        public void OnForeground_BeforeTimeout_PreservesSession()
        {
            var now = new DateTime(2026, 4, 2, 0, 0, 0, DateTimeKind.Utc);
            var manager = new SessionManager(() => now);
            var original = manager.SessionId;
            manager.OnBackground();
            now = now.AddMinutes(20);
            manager.OnForeground();
            now = now.AddMinutes(20);
            Assert.Equal(original, manager.SessionId);
        }

        [Fact]
        public void StartNewSession_RotatesActiveSession()
        {
            var manager = new SessionManager();
            var original = manager.SessionId;
            manager.StartNewSession();
            Assert.NotNull(manager.SessionId);
            Assert.NotEqual(original, manager.SessionId);
        }

        [Fact]
        public void NewSessionManagerInstanceStartsNewSessionWithinInactivityTimeout()
        {
            var now = new DateTime(2026, 4, 2, 14, 57, 39, DateTimeKind.Utc);
            var firstSessionManager = new SessionManager(() => now);
            var originalSessionId = firstSessionManager.SessionId;

            now = now.AddMinutes(1);
            var restartedSessionManager = new SessionManager(() => now);
            var restartedSessionId = restartedSessionManager.SessionId;

            Assert.NotNull(originalSessionId);
            Assert.NotNull(restartedSessionId);
            Assert.NotEqual(originalSessionId, restartedSessionId);
        }
    }
}
