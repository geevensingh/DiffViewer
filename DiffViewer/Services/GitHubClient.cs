using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using DiffViewer.Models;

namespace DiffViewer.Services;

/// <summary>
/// <see cref="IGitHubClient"/> backed by a plain <see cref="HttpClient"/>.
/// No Octokit dependency — the v1 surface is one endpoint, the schema is
/// stable, and an extra package would inflate the single-file publish for
/// negligible code savings.
/// </summary>
/// <remarks>
/// <para>Enumerates the full error matrix documented in the plan and
/// surfaces every row as a <see cref="GitHubException"/> with a
/// user-actionable message. The 401 path invalidates the cached token
/// and retries once before giving up, so a token rotated in another
/// shell (<c>gh auth refresh</c>) gets picked up without restarting
/// DiffViewer.</para>
///
/// <para>The class is free-threaded and safe to register as an
/// app-singleton: <see cref="HttpClient"/> is documented as thread-safe
/// for sending requests, and the auth provider is also thread-safe.</para>
/// </remarks>
internal sealed class GitHubClient : IGitHubClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly IGitHubAuthProvider _auth;

    public GitHubClient(HttpClient http, IGitHubAuthProvider auth)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _auth = auth ?? throw new ArgumentNullException(nameof(auth));
    }

    public async Task<PullRequestInfo> GetPullRequestAsync(PullRequestRef pr, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(pr);

        var response = await SendAsync(pr, ct).ConfigureAwait(false);
        try
        {
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                // Token may have been rotated in another shell; drop the
                // cache and try once more before giving up.
                response.Dispose();
                _auth.InvalidateCache(pr.Host);
                response = await SendAsync(pr, ct).ConfigureAwait(false);

                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    throw new GitHubException(
                        "GitHub rejected the auth token. Run `gh auth login` (or " +
                        "`gh auth refresh`) and try again.");
                }
            }

            if (response.IsSuccessStatusCode)
            {
                var stream = await response.Content
                    .ReadAsStreamAsync(ct).ConfigureAwait(false);
                var dto = await JsonSerializer
                    .DeserializeAsync<PullRequestDto>(stream, JsonOptions, ct)
                    .ConfigureAwait(false)
                    ?? throw new GitHubException(
                        "GitHub returned an empty response for the PR. Try again, " +
                        "or open the PR in a browser to confirm it exists.");

                return Project(pr.Number, dto);
            }

            throw await BuildErrorAsync(response, ct).ConfigureAwait(false);
        }
        finally
        {
            response.Dispose();
        }
    }

    private async Task<HttpResponseMessage> SendAsync(PullRequestRef pr, CancellationToken ct)
    {
        var url = $"https://api.github.com/repos/{pr.Owner}/{pr.Repo}/pulls/{pr.Number}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);

        // Use the modern Accept header per GitHub's API guidance.
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");

        var token = await _auth.TryGetTokenAsync(pr.Host, ct).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        try
        {
            return await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new GitHubException(
                "No network connection to GitHub. Check your internet connection and try again.",
                ex);
        }
    }

    private static async Task<GitHubException> BuildErrorAsync(
        HttpResponseMessage response, CancellationToken ct)
    {
        var status = (int)response.StatusCode;

        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            if (response.Headers.TryGetValues("X-RateLimit-Remaining", out var remainingValues)
                && remainingValues.FirstOrDefault() == "0")
            {
                var retry = response.Headers.RetryAfter?.Delta?.TotalSeconds.ToString("0")
                    ?? "a few minutes";
                return new GitHubException(
                    $"GitHub API rate limit hit. Try again in {retry} seconds.");
            }

            return new GitHubException(
                "GitHub refused the request (403). The PR may be private and your " +
                "token lacks `repo` scope, or your org requires SSO authorization for " +
                "this token.");
        }

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return new GitHubException(
                "PR not found, or not visible to the current GitHub token. " +
                "Confirm the URL, and that your `gh` login has access to the repo.");
        }

        if (status >= 500 && status < 600)
        {
            return new GitHubException(
                $"GitHub is having a moment ({status}). Try again in a few minutes.");
        }

        // Anything we didn't explicitly recognize gets a generic message
        // with the status code so the user has something to grep on.
        string body;
        try
        {
            body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            body = string.Empty;
        }

        return new GitHubException(
            $"GitHub returned an unexpected response ({status} {response.ReasonPhrase}). " +
            (string.IsNullOrWhiteSpace(body) ? string.Empty : $"Details: {body.Trim()}"));
    }

    private static PullRequestInfo Project(int number, PullRequestDto dto)
    {
        if (dto.Base is null || dto.Head is null)
        {
            throw new GitHubException(
                "GitHub response is missing base/head information for this PR.");
        }

        if (dto.Base.Repo is null)
        {
            throw new GitHubException(
                "GitHub response is missing the base repository for this PR. The " +
                "upstream repository may have been deleted.");
        }

        if (dto.Head.Repo is null)
        {
            throw new GitHubException(
                "GitHub response is missing the head repository for this PR. The " +
                "source fork may have been deleted, so the PR head can no longer " +
                "be fetched.");
        }

        return new PullRequestInfo(
            Number: number,
            Title: dto.Title ?? string.Empty,
            State: dto.State ?? "unknown",
            Merged: dto.Merged,
            BaseRef: dto.Base.Ref ?? string.Empty,
            BaseSha: dto.Base.Sha ?? string.Empty,
            HeadRef: dto.Head.Ref ?? string.Empty,
            HeadSha: dto.Head.Sha ?? string.Empty,
            HeadRepoCloneUrl: dto.Head.Repo.CloneUrl ?? string.Empty,
            BaseRepoCloneUrl: dto.Base.Repo.CloneUrl ?? string.Empty);
    }

    private sealed record PullRequestDto(
        string? Title,
        string? State,
        bool Merged,
        [property: JsonPropertyName("base")] PullRequestSideDto? Base,
        [property: JsonPropertyName("head")] PullRequestSideDto? Head);

    private sealed record PullRequestSideDto(
        [property: JsonPropertyName("ref")] string? Ref,
        string? Sha,
        PullRequestSideRepoDto? Repo);

    private sealed record PullRequestSideRepoDto(
        [property: JsonPropertyName("clone_url")] string? CloneUrl);
}
