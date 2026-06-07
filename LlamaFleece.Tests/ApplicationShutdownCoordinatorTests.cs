using Xunit;

public class ApplicationShutdownCoordinatorTests
{
    [Fact]
    public void RequestShutdown_CapturesReasonAndInvokesCallbackOnce()
    {
        ApplicationShutdownCoordinator.ResetForTests();

        try
        {
            var callbackCount = 0;
            string? callbackReason = null;

            ApplicationShutdownCoordinator.Configure(reason =>
            {
                callbackCount++;
                callbackReason = reason;
            });

            ApplicationShutdownCoordinator.RequestShutdown("integration test");
            ApplicationShutdownCoordinator.RequestShutdown("second call ignored");

            Assert.True(ApplicationShutdownCoordinator.IsShutdownRequested);
            Assert.Equal("integration test", ApplicationShutdownCoordinator.ShutdownReason);
            Assert.Equal("integration test", callbackReason);
            Assert.Equal(1, callbackCount);
        }
        finally
        {
            ApplicationShutdownCoordinator.ResetForTests();
        }
    }

    [Fact]
    public void Configure_ResetsPreviousShutdownReason()
    {
        ApplicationShutdownCoordinator.ResetForTests();

        try
        {
            ApplicationShutdownCoordinator.RequestShutdown("old reason");
            Assert.Equal("old reason", ApplicationShutdownCoordinator.ShutdownReason);

            ApplicationShutdownCoordinator.Configure(null);

            Assert.False(ApplicationShutdownCoordinator.IsShutdownRequested);
            Assert.Null(ApplicationShutdownCoordinator.ShutdownReason);
        }
        finally
        {
            ApplicationShutdownCoordinator.ResetForTests();
        }
    }
}