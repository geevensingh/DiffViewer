using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DiffViewer.Models;
using DiffViewer.Services;
using FluentAssertions;
using Xunit;

namespace DiffViewer.Tests.Services;

public sealed class BrowserNotifyUpdateServiceTests
{
    [Fact]
    public void CanAutoApply_IsFalse()
    {
        // The whole point of the service is that "apply" launches a
        // browser; that can never happen silently in Automatic mode.
        // The VM uses this flag to demote Automatic -> NotifyOnly for
        // the browser-notify case.
        var sut = NewService(currentVersion: new Version(1, 0, 0), responses: Array.Empty<FakeResponse>());

        sut.CanAutoApply.Should().BeFalse();
    }

    [Fact]
    public async Task CheckAsync_WhenNewerStableReleaseAvailable_ReturnsAvailable()
    {
        var sut = NewService(
            currentVersion: new Version(1, 4, 0),
            responses: new[]
            {
                ReleasesJson(new GhRelease("v1.5.0", "https://github.com/owner/repo/releases/tag/v1.5.0", Draft: false, Prerelease: false)),
            });

        var result = await sut.CheckAsync(CancellationToken.None);

        result.IsAvailable.Should().BeTrue();
        result.Version.Should().Be("1.5.0");
        result.OpaqueHandle.Should().Be("https://github.com/owner/repo/releases/tag/v1.5.0");
    }

    [Fact]
    public async Task CheckAsync_WhenLatestEqualsCurrent_ReturnsNoUpdate()
    {
        var sut = NewService(
            currentVersion: new Version(1, 5, 0),
            responses: new[]
            {
                ReleasesJson(new GhRelease("v1.5.0", "https://example/r", Draft: false, Prerelease: false)),
            });

        var result = await sut.CheckAsync(CancellationToken.None);

        result.Should().BeSameAs(UpdateCheckResult.NoUpdateAvailable);
    }

    [Fact]
    public async Task CheckAsync_DraftReleases_Ignored()
    {
        // A draft release should never surface to users even if its
        // version is newer.
        var sut = NewService(
            currentVersion: new Version(1, 4, 0),
            responses: new[]
            {
                ReleasesJson(new GhRelease("v1.5.0", "https://example/r1", Draft: true, Prerelease: false),
                    new GhRelease("v1.4.0", "https://example/r2", Draft: false, Prerelease: false)),
            });

        var result = await sut.CheckAsync(CancellationToken.None);

        result.Should().BeSameAs(UpdateCheckResult.NoUpdateAvailable);
    }

    [Fact]
    public async Task CheckAsync_PrereleaseIgnoredWhenIncludePreReleasesFalse()
    {
        var sut = NewService(
            currentVersion: new Version(1, 4, 0),
            includePreReleases: false,
            responses: new[]
            {
                ReleasesJson(new GhRelease("v1.5.0-rc1", "https://example/rc", Draft: false, Prerelease: true),
                    new GhRelease("v1.4.0", "https://example/r2", Draft: false, Prerelease: false)),
            });

        var result = await sut.CheckAsync(CancellationToken.None);

        result.Should().BeSameAs(UpdateCheckResult.NoUpdateAvailable);
    }

    [Fact]
    public async Task CheckAsync_PrereleaseConsideredWhenIncludePreReleasesTrue()
    {
        var sut = NewService(
            currentVersion: new Version(1, 4, 0),
            includePreReleases: true,
            responses: new[]
            {
                ReleasesJson(new GhRelease("v1.5.0-rc1", "https://example/rc", Draft: false, Prerelease: true)),
            });

        var result = await sut.CheckAsync(CancellationToken.None);

        result.IsAvailable.Should().BeTrue();
        result.Version.Should().Be("1.5.0");
    }

    [Fact]
    public async Task CheckAsync_PicksHighestVersion_NotMostRecent()
    {
        // GitHub returns releases in created-at order; the API
        // /releases endpoint is reverse-chronological. The highest
        // VERSION should win, not the most recent. (Releases can be
        // backported / cut out-of-order.)
        var sut = NewService(
            currentVersion: new Version(1, 4, 0),
            responses: new[]
            {
                ReleasesJson(new GhRelease("v1.5.0", "https://example/v150", Draft: false, Prerelease: false),
                    new GhRelease("v1.6.0", "https://example/v160", Draft: false, Prerelease: false),
                    new GhRelease("v1.5.1", "https://example/v151", Draft: false, Prerelease: false)),
            });

        var result = await sut.CheckAsync(CancellationToken.None);

        result.IsAvailable.Should().BeTrue();
        result.Version.Should().Be("1.6.0");
        result.OpaqueHandle.Should().Be("https://example/v160");
    }

    [Fact]
    public async Task CheckAsync_HttpErrorResponse_ReturnsNoUpdate()
    {
        var sut = NewService(
            currentVersion: new Version(1, 0, 0),
            responses: new[]
            {
                new FakeResponse(HttpStatusCode.ServiceUnavailable, "{}"),
            });

        var result = await sut.CheckAsync(CancellationToken.None);

        result.Should().BeSameAs(UpdateCheckResult.NoUpdateAvailable);
    }

    [Fact]
    public async Task CheckAsync_NetworkException_ReturnsNoUpdate()
    {
        var sut = NewService(
            currentVersion: new Version(1, 0, 0),
            responses: Array.Empty<FakeResponse>()); // handler throws

        var result = await sut.CheckAsync(CancellationToken.None);

        result.Should().BeSameAs(UpdateCheckResult.NoUpdateAvailable);
    }

    [Fact]
    public async Task CheckAsync_SendsRequiredUserAgent()
    {
        var capturedHeaders = new List<HttpRequestHeaders?>();
        using var http = new HttpClient(new CapturingHandler(capturedHeaders, ReleasesJson()));
        var sut = new BrowserNotifyUpdateService(http, new Version(1, 4, 0), includePreReleases: false);

        await sut.CheckAsync(CancellationToken.None);

        capturedHeaders.Should().ContainSingle();
        var ua = capturedHeaders[0]!.UserAgent.ToString();
        ua.Should().Contain("DiffViewer").And.Contain("1.4.0");
    }

    [Fact]
    public async Task ApplyOnNextLaunchAsync_LaunchesUrlFromOpaqueHandle()
    {
        var launched = new List<string>();
        using var http = new HttpClient(new FakeHttpHandler(Array.Empty<FakeResponse>()));
        var sut = new BrowserNotifyUpdateService(
            http, new Version(1, 0, 0), includePreReleases: false,
            openUrl: url => launched.Add(url));

        await sut.ApplyOnNextLaunchAsync(
            new UpdateCheckResult { IsAvailable = true, Version = "1.5.0", OpaqueHandle = "https://example/v150" },
            CancellationToken.None);

        launched.Should().ContainSingle().Which.Should().Be("https://example/v150");
    }

    [Fact]
    public async Task ApplyOnNextLaunchAsync_WithNoUpdate_NoOps()
    {
        var launched = new List<string>();
        using var http = new HttpClient(new FakeHttpHandler(Array.Empty<FakeResponse>()));
        var sut = new BrowserNotifyUpdateService(
            http, new Version(1, 0, 0), includePreReleases: false,
            openUrl: url => launched.Add(url));

        await sut.ApplyOnNextLaunchAsync(
            UpdateCheckResult.NoUpdateAvailable,
            CancellationToken.None);

        launched.Should().BeEmpty();
    }

    [Fact]
    public async Task DownloadAsync_IsAlwaysANoOp()
    {
        var sut = NewService(new Version(1, 0, 0), responses: Array.Empty<FakeResponse>());

        var act = async () => await sut.DownloadAsync(
            new UpdateCheckResult { IsAvailable = true, Version = "1.5.0", OpaqueHandle = "x" },
            CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Theory]
    [InlineData("v1.5.0", "1.5.0")]
    [InlineData("V1.5.0", "1.5.0")]
    [InlineData("1.5.0", "1.5.0")]
    [InlineData("v1.5.0-rc1", "1.5.0")]
    [InlineData("v1.5.0+build.42", "1.5.0")]
    [InlineData("v1.5.0-rc1+build.42", "1.5.0")]
    public void TryParseVersion_StripsTagPrefixAndSemVerSuffixes(string input, string expected)
    {
        BrowserNotifyUpdateService.TryParseVersion(input)!.ToString().Should().Be(expected);
    }

    [Theory]
    [InlineData("notaversion")]
    [InlineData("")]
    [InlineData(null)]
    public void TryParseVersion_Garbage_ReturnsNull(string? input)
    {
        BrowserNotifyUpdateService.TryParseVersion(input).Should().BeNull();
    }

    // ----- helpers -----

    private static BrowserNotifyUpdateService NewService(
        Version currentVersion,
        IEnumerable<FakeResponse> responses,
        bool includePreReleases = false)
    {
        var handler = new FakeHttpHandler(responses);
        // HttpClient owns the handler — we don't dispose it here so
        // the test can re-use the handler for assertions if needed;
        // GC handles cleanup at test exit.
        var http = new HttpClient(handler);
        return new BrowserNotifyUpdateService(http, currentVersion, includePreReleases, openUrl: _ => { });
    }

    private static FakeResponse ReleasesJson(params GhRelease[] releases)
    {
        // Build the JSON shape GitHub returns (snake_case fields)
        // manually rather than via record serialization — avoids
        // a wrestling match with JsonSerializer over property naming.
        var arr = new System.Text.Json.Nodes.JsonArray();
        foreach (var r in releases)
        {
            arr.Add(new System.Text.Json.Nodes.JsonObject
            {
                ["tag_name"] = r.TagName,
                ["html_url"] = r.HtmlUrl,
                ["draft"] = r.Draft,
                ["prerelease"] = r.Prerelease,
            });
        }
        return new FakeResponse(HttpStatusCode.OK, arr.ToJsonString());
    }

    private sealed record GhRelease(
        string TagName,
        string HtmlUrl,
        bool Draft,
        bool Prerelease);

    private sealed record FakeResponse(HttpStatusCode Status, string BodyJson);

    private sealed class FakeHttpHandler : HttpMessageHandler
    {
        private readonly Queue<FakeResponse> _responses;

        public FakeHttpHandler(IEnumerable<FakeResponse> responses)
        {
            _responses = new Queue<FakeResponse>(responses);
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (_responses.Count == 0)
            {
                // Simulate a network outage for tests that want it.
                throw new HttpRequestException("simulated network failure");
            }
            var canned = _responses.Dequeue();
            var resp = new HttpResponseMessage(canned.Status)
            {
                Content = new StringContent(canned.BodyJson, Encoding.UTF8, "application/json"),
            };
            return Task.FromResult(resp);
        }
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly List<HttpRequestHeaders?> _headers;
        private readonly FakeResponse _response;

        public CapturingHandler(List<HttpRequestHeaders?> headers, FakeResponse response)
        {
            _headers = headers;
            _response = response;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            _headers.Add(request.Headers);
            var resp = new HttpResponseMessage(_response.Status)
            {
                Content = new StringContent(_response.BodyJson, Encoding.UTF8, "application/json"),
            };
            return Task.FromResult(resp);
        }
    }
}



