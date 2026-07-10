public class RetryHelperTests
{
    [Fact]
    public void Execute_ReturnsImmediately_WhenActionSucceedsFirstTry()
    {
        var attempts = 0;
        var result = RetryHelper.Execute(retries: 3, delayMilliseconds: 0, CancellationToken.None, () =>
        {
            attempts++;
            return "ok";
        });

        Assert.Equal("ok", result);
        Assert.Equal(1, attempts);
    }

    [Fact]
    public void Execute_RetriesUntilSuccess_WithinBudget()
    {
        var attempts = 0;
        var result = RetryHelper.Execute(retries: 3, delayMilliseconds: 0, CancellationToken.None, () =>
        {
            attempts++;
            if (attempts < 3)
            {
                throw new InvalidOperationException("transient");
            }

            return "ok";
        });

        Assert.Equal("ok", result);
        Assert.Equal(3, attempts);
    }

    [Fact]
    public void Execute_ThrowsFinalException_WhenAllAttemptsFail()
    {
        var attempts = 0;

        var ex = Assert.Throws<InvalidOperationException>(() =>
            RetryHelper.Execute<string>(retries: 2, delayMilliseconds: 0, CancellationToken.None, () =>
            {
                attempts++;
                throw new InvalidOperationException($"attempt {attempts}");
            }));

        Assert.Equal(3, attempts); // initial try + 2 retries
        Assert.Equal("attempt 3", ex.Message);
    }

    [Fact]
    public void Execute_MakesExactlyOneAttempt_WhenRetriesIsZero()
    {
        var attempts = 0;

        Assert.Throws<InvalidOperationException>(() =>
            RetryHelper.Execute<string>(retries: 0, delayMilliseconds: 0, CancellationToken.None, () =>
            {
                attempts++;
                throw new InvalidOperationException("boom");
            }));

        Assert.Equal(1, attempts);
    }

    [Fact]
    public void Execute_ThrowsOperationCanceled_WhenCancelledBeforeFirstAttempt()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var attempts = 0;

        Assert.Throws<OperationCanceledException>(() =>
            RetryHelper.Execute<string>(retries: 3, delayMilliseconds: 0, cts.Token, () =>
            {
                attempts++;
                return "unreachable";
            }));

        Assert.Equal(0, attempts);
    }

    [Fact]
    public void Execute_StopsRetrying_WhenCancelledDuringBackoffDelay()
    {
        using var cts = new CancellationTokenSource();
        var attempts = 0;

        Assert.Throws<OperationCanceledException>(() =>
            RetryHelper.Execute<string>(retries: 5, delayMilliseconds: 50, cts.Token, () =>
            {
                attempts++;
                cts.Cancel();
                throw new InvalidOperationException("transient");
            }));

        Assert.Equal(1, attempts);
    }
}
