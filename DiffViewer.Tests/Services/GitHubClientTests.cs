using System.Net;
using System.Net.Http;
using System.Text;
using DiffViewer.Models;
using DiffViewer.Services;
using FluentAssertions;
using Xunit;

namespace DiffViewer.Tests.Services;

public sealed class GitHubClientTests
{
    private static readonly PullRequestRef SamplePr =
        new("github.com", "octocat", "hello-world", 1);

    private const string SampleJson = """
        {
            "title": "Sample PR",
            "state": "open",
            "merged": false,
            "base": {
                "ref": "main",
                "sha": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                "repo": { "clone_url": "https://github.com/octocat/hello-world.git" }
            },
            "head": {
                "ref": "feature",
                "sha": "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
                "repo": { "clone_url": "https://github.com/fork/hello-world.git" }
            }
        }
        """;

    [Fact]
    public async Task GetPullRequestAsync_Success_ParsesAllFields()
    {
        var handler = new FakeHandler();
        handler.Enqueue(req =>
        {
            req.RequestUri.Should().Be("https://api.github.com/repos/octocat/hello-world/pulls/1");
            req.Headers.Accept.ToString().Should().Contain("application/vnd.github+json");
            req.Headers.UserAgent.ToString().Should().StartWith("DiffViewer/",
                because: "GitHub rejects requests without a User-Agent");
            req.Headers.Authorization!.Scheme.Should().Be("Bearer");
            req.Headers.Authorization.Parameter.Should().Be("ghp_test");
            return JsonOk(SampleJson);
        });
        var auth = new FakeAuth("ghp_test");
        var client = new GitHubClient(new HttpClient(handler), auth);

        var info = await client.GetPullRequestAsync(SamplePr, default);

        info.Title.Should().Be("Sample PR");
        info.State.Should().Be("open");
        info.Merged.Should().BeFalse();
        info.BaseRef.Should().Be("main");
        info.BaseSha.Should().Be("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
        info.HeadRef.Should().Be("feature");
        info.HeadSha.Should().Be("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb");
        info.BaseRepoCloneUrl.Should().Be("https://github.com/octocat/hello-world.git");
        info.HeadRepoCloneUrl.Should().Be("https://github.com/fork/hello-world.git");
        auth.InvalidatedHosts.Should().BeEmpty();
    }

    [Fact]
    public async Task GetPullRequestAsync_NoToken_OmitsAuthorizationHeader()
    {
        var handler = new FakeHandler();
        handler.Enqueue(req =>
        {
            req.Headers.Authorization.Should().BeNull();
            return JsonOk(SampleJson);
        });
        var client = new GitHubClient(new HttpClient(handler), new FakeAuth(token: null));

        var info = await client.GetPullRequestAsync(SamplePr, default);
        info.Title.Should().Be("Sample PR");
    }

    [Fact]
    public async Task GetPullRequestAsync_FirstResponse401_InvalidatesCacheAndRetriesOnce()
    {
        var handler = new FakeHandler();
        handler.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        handler.Enqueue(_ => JsonOk(SampleJson));
        var auth = new FakeAuth("ghp_test");
        var client = new GitHubClient(new HttpClient(handler), auth);

        var info = await client.GetPullRequestAsync(SamplePr, default);

        info.Title.Should().Be("Sample PR");
        auth.InvalidatedHosts.Should().Equal("github.com");
        handler.RequestCount.Should().Be(2);
    }

    [Fact]
    public async Task GetPullRequestAsync_Second401_ThrowsAuthError()
    {
        var handler = new FakeHandler();
        handler.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        handler.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var auth = new FakeAuth("ghp_test");
        var client = new GitHubClient(new HttpClient(handler), auth);

        var act = () => client.GetPullRequestAsync(SamplePr, default);
        var ex = await act.Should().ThrowAsync<GitHubException>();
        ex.Which.Message.Should().Contain("gh auth");
        auth.InvalidatedHosts.Should().Equal("github.com");
    }

    [Fact]
    public async Task GetPullRequestAsync_403WithRateLimitZero_ReportsRetryAfter()
    {
        var handler = new FakeHandler();
        handler.Enqueue(_ =>
        {
            var resp = new HttpResponseMessage(HttpStatusCode.Forbidden);
            resp.Headers.Add("X-RateLimit-Remaining", "0");
            resp.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(60));
            return resp;
        });
        var client = new GitHubClient(new HttpClient(handler), new FakeAuth("ghp_test"));

        var act = () => client.GetPullRequestAsync(SamplePr, default);
        var ex = await act.Should().ThrowAsync<GitHubException>();
        ex.Which.Message.Should().Contain("rate limit");
        ex.Which.Message.Should().Contain("60");
    }

    [Fact]
    public async Task GetPullRequestAsync_403WithoutRateLimit_ReportsPermissions()
    {
        var handler = new FakeHandler();
        handler.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        var client = new GitHubClient(new HttpClient(handler), new FakeAuth("ghp_test"));

        var act = () => client.GetPullRequestAsync(SamplePr, default);
        var ex = await act.Should().ThrowAsync<GitHubException>();
        ex.Which.Message.Should().Contain("repo", because: "we hint at the `repo` scope");
        ex.Which.Message.Should().NotContain("rate limit");
    }

    [Fact]
    public async Task GetPullRequestAsync_403WithJsonMessage_SurfacesGitHubMessage()
    {
        var handler = new FakeHandler();
        handler.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent(
                """{"message":"Resource protected by org SAML enforcement.","documentation_url":"https://example"}""",
                Encoding.UTF8,
                "application/json"),
        });
        var client = new GitHubClient(new HttpClient(handler), new FakeAuth("ghp_test"));

        var act = () => client.GetPullRequestAsync(SamplePr, default);
        var ex = await act.Should().ThrowAsync<GitHubException>();
        ex.Which.Message.Should().Contain("GitHub said:");
        ex.Which.Message.Should().Contain("Resource protected by org SAML enforcement.");
        ex.Which.Message.Should().NotContain("documentation_url",
            because: "we extract the message field, not the raw JSON");
    }

    [Fact]
    public async Task GetPullRequestAsync_403WithPlaintextBody_SurfacesPlaintext()
    {
        // GitHub's real-world response when the User-Agent header is missing
        // is plaintext, not the JSON error envelope.
        const string PlainTextBody =
            "Request forbidden by administrative rules. Please make sure your "
            + "request has a User-Agent header (https://docs.github.com/...).";
        var handler = new FakeHandler();
        handler.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent(PlainTextBody, Encoding.UTF8, "text/plain"),
        });
        var client = new GitHubClient(new HttpClient(handler), new FakeAuth("ghp_test"));

        var act = () => client.GetPullRequestAsync(SamplePr, default);
        var ex = await act.Should().ThrowAsync<GitHubException>();
        ex.Which.Message.Should().Contain("GitHub said:");
        ex.Which.Message.Should().Contain("User-Agent header");
    }

    [Fact]
    public async Task GetPullRequestAsync_404_ReportsNotFound()
    {
        var handler = new FakeHandler();
        handler.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var client = new GitHubClient(new HttpClient(handler), new FakeAuth("ghp_test"));

        var act = () => client.GetPullRequestAsync(SamplePr, default);
        var ex = await act.Should().ThrowAsync<GitHubException>();
        ex.Which.Message.Should().Contain("not found");
    }

    [Fact]
    public async Task GetPullRequestAsync_5xx_ReportsTransient()
    {
        var handler = new FakeHandler();
        handler.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.BadGateway));
        var client = new GitHubClient(new HttpClient(handler), new FakeAuth("ghp_test"));

        var act = () => client.GetPullRequestAsync(SamplePr, default);
        var ex = await act.Should().ThrowAsync<GitHubException>();
        ex.Which.Message.Should().Contain("502");
    }

    [Fact]
    public async Task GetPullRequestAsync_NetworkException_ReportsNoNetwork()
    {
        var handler = new FakeHandler();
        handler.Enqueue(_ => throw new HttpRequestException("DNS failure"));
        var client = new GitHubClient(new HttpClient(handler), new FakeAuth("ghp_test"));

        var act = () => client.GetPullRequestAsync(SamplePr, default);
        var ex = await act.Should().ThrowAsync<GitHubException>();
        ex.Which.Message.Should().Contain("network");
        ex.Which.InnerException.Should().BeOfType<HttpRequestException>();
    }

    [Fact]
    public async Task GetPullRequestAsync_BaseRepoNull_Throws()
    {
        const string json = """
            {
                "title": "Sample",
                "state": "open",
                "merged": false,
                "base": { "ref": "main", "sha": "aaa", "repo": null },
                "head": { "ref": "feature", "sha": "bbb", "repo": { "clone_url": "https://x/y" } }
            }
            """;
        var handler = new FakeHandler();
        handler.Enqueue(_ => JsonOk(json));
        var client = new GitHubClient(new HttpClient(handler), new FakeAuth("ghp_test"));

        var act = () => client.GetPullRequestAsync(SamplePr, default);
        var ex = await act.Should().ThrowAsync<GitHubException>();
        ex.Which.Message.Should().Contain("base");
    }

    [Fact]
    public async Task GetPullRequestAsync_HeadRepoNull_Throws()
    {
        const string json = """
            {
                "title": "Sample",
                "state": "open",
                "merged": false,
                "base": { "ref": "main", "sha": "aaa", "repo": { "clone_url": "https://x/y" } },
                "head": { "ref": "feature", "sha": "bbb", "repo": null }
            }
            """;
        var handler = new FakeHandler();
        handler.Enqueue(_ => JsonOk(json));
        var client = new GitHubClient(new HttpClient(handler), new FakeAuth("ghp_test"));

        var act = () => client.GetPullRequestAsync(SamplePr, default);
        var ex = await act.Should().ThrowAsync<GitHubException>();
        ex.Which.Message.Should().Contain("head");
    }

    [Fact(Skip = "Live test against api.github.com — opt-in only.")]
    [Trait("Category", "Live")]
    public async Task GetPullRequestAsync_Live_OctocatHelloWorldPr1()
    {
        // Schema-drift canary. Run manually with:
        //   dotnet test --filter "FullyQualifiedName~Live_OctocatHelloWorld" -- xunit.methodDisplay=method
        // (or remove the Skip attribute locally).
        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("DiffViewer/test");
        var auth = new FakeAuth(token: null);
        var client = new GitHubClient(http, auth);

        var info = await client.GetPullRequestAsync(
            new PullRequestRef("github.com", "octocat", "Hello-World", 1),
            CancellationToken.None);

        info.Title.Should().NotBeNullOrWhiteSpace();
        info.BaseRepoCloneUrl.Should().Contain("Hello-World");
    }

    private static HttpResponseMessage JsonOk(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
    }

    // ---- GetPullRequestPolledAsync ----

    [Fact]
    public async Task GetPullRequestPolledAsync_Success_ReturnsInfoAndETag()
    {
        var handler = new FakeHandler();
        handler.Enqueue(req =>
        {
            req.Headers.Contains("If-None-Match").Should().BeFalse(
                "no ETag passed → no conditional-get header");
            var resp = JsonOk(SampleJson);
            resp.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"abc123\"");
            resp.Headers.TryAddWithoutValidation("X-RateLimit-Remaining", "4823");
            return resp;
        });
        var client = new GitHubClient(new HttpClient(handler), new FakeAuth("ghp_test"));

        var result = await client.GetPullRequestPolledAsync(SamplePr, ifNoneMatch: null, default);

        result.Info.Should().NotBeNull();
        result.Info!.Title.Should().Be("Sample PR");
        result.ETag.Should().Be("\"abc123\"");
        result.RateLimitRemaining.Should().Be(4823);
    }

    [Fact]
    public async Task GetPullRequestPolledAsync_NotModified_ReturnsNullInfoAndPreservesETag()
    {
        var handler = new FakeHandler();
        handler.Enqueue(req =>
        {
            req.Headers.GetValues("If-None-Match").Should().ContainSingle()
                .Which.Should().Be("\"prev-etag\"");
            var resp = new HttpResponseMessage(HttpStatusCode.NotModified);
            // Server omits ETag on 304 (common); caller keeps the prior one.
            resp.Headers.TryAddWithoutValidation("X-RateLimit-Remaining", "100");
            return resp;
        });
        var client = new GitHubClient(new HttpClient(handler), new FakeAuth("ghp_test"));

        var result = await client.GetPullRequestPolledAsync(SamplePr, ifNoneMatch: "\"prev-etag\"", default);

        result.Info.Should().BeNull();
        result.ETag.Should().Be("\"prev-etag\"", "304 with no server ETag preserves the caller's value");
        result.RateLimitRemaining.Should().Be(100);
    }

    [Fact]
    public async Task GetPullRequestPolledAsync_MissingRateLimitHeader_ReturnsNullRemaining()
    {
        var handler = new FakeHandler();
        handler.Enqueue(_ => JsonOk(SampleJson));
        var client = new GitHubClient(new HttpClient(handler), new FakeAuth("ghp_test"));

        var result = await client.GetPullRequestPolledAsync(SamplePr, null, default);

        result.RateLimitRemaining.Should().BeNull();
    }

    [Fact]
    public async Task GetPullRequestPolledAsync_Unauthorized_RetriesOnceAfterInvalidatingToken()
    {
        var handler = new FakeHandler();
        handler.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        handler.Enqueue(_ => JsonOk(SampleJson));
        var auth = new FakeAuth("ghp_test");
        var client = new GitHubClient(new HttpClient(handler), auth);

        var result = await client.GetPullRequestPolledAsync(SamplePr, null, default);

        result.Info.Should().NotBeNull();
        handler.RequestCount.Should().Be(2);
        auth.InvalidatedHosts.Should().ContainSingle().Which.Should().Be("github.com");
    }

    [Fact]
    public async Task GetPullRequestPolledAsync_RepeatedUnauthorized_ThrowsTokenRejectedError()
    {
        var handler = new FakeHandler();
        handler.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        handler.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var client = new GitHubClient(new HttpClient(handler), new FakeAuth("ghp_test"));

        var act = () => client.GetPullRequestPolledAsync(SamplePr, null, default);

        await act.Should().ThrowAsync<GitHubException>()
            .Where(ex => ex.Message.Contains("rejected the auth token"));
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _queue = new();
        public int RequestCount { get; private set; }

        public void Enqueue(Func<HttpRequestMessage, HttpResponseMessage> producer)
        {
            _queue.Enqueue(producer);
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            if (_queue.Count == 0)
            {
                throw new InvalidOperationException("FakeHandler ran out of queued responses.");
            }
            return Task.FromResult(_queue.Dequeue()(request));
        }
    }

    private sealed class FakeAuth : IGitHubAuthProvider
    {
        private string? _token;
        public List<string> InvalidatedHosts { get; } = new();

        public FakeAuth(string? token)
        {
            _token = token;
        }

        public Task<string?> TryGetTokenAsync(string host, CancellationToken ct)
            => Task.FromResult(_token);

        public void InvalidateCache(string host)
        {
            InvalidatedHosts.Add(host);
            // Don't drop the token: tests want the retry call to also be
            // authorized; the assertion is that we tried to invalidate.
        }
    }
}
