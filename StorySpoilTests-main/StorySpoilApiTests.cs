using System.Net;
using System.Text.Json;
using NUnit.Framework;
using RestSharp;
using RestSharp.Authenticators;
using StorySpoilTests.Models;

namespace StorySpoilTests;

[TestFixture]
public class StorySpoilApiTests
{
    private const string BaseUrl = "https://d3s5nxhwblsjbi.cloudfront.net/api";

    private static RestClient _client = null!;
    private static string _token = string.Empty;
    private static string _storyId = string.Empty;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        var bootstrap = new RestClient(BaseUrl);

        var username = $"qa_{Guid.NewGuid():N}".Substring(0, 15);
        var password = "P@ssw0rd!42";

        var createUser = new CreateUserRequestDto
        {
            UserName = username,
            FirstName = "QA",
            MidName = "",
            LastName = "User",
            Email = $"{username}@mail.test",
            Password = password,
            RePassword = password
        };
        var reqCreateUser = new RestRequest("/User/Create", Method.Post).AddJsonBody(createUser);
        await bootstrap.ExecuteAsync(reqCreateUser);

        var reqAuth = new RestRequest("/User/Authentication", Method.Post)
            .AddJsonBody(new LoginRequestDto { UserName = username, Password = password });

        var respAuth = await bootstrap.ExecuteAsync(reqAuth);
        Assert.That(respAuth.StatusCode, Is.EqualTo(HttpStatusCode.OK), "Login should return 200 OK.");
        var auth = JsonSerializer.Deserialize<AuthResponseDto>(respAuth.Content!, JsonOpts);
        Assert.That(auth?.AccessToken, Is.Not.Null.And.Not.Empty, "AccessToken must be present.");
        _token = auth!.AccessToken!;

        var opts = new RestClientOptions(BaseUrl)
        {
            Authenticator = new JwtAuthenticator(_token)
        };
        _client = new RestClient(opts);
    }

    [OneTimeTearDown]
    public void OneTimeTearDown() => _client?.Dispose();

    [Test, Order(1)]
    public async Task Create_Story_With_Required_Fields_Should_Return_201_And_StoryId()
    {
        var story = new StoryDto
        {
            Title = "Test Story " + Guid.NewGuid().ToString("N")[..8],
            Description = "Auto-created by tests",
            Url = null
        };

        var req = new RestRequest("/Story/Create", Method.Post).AddJsonBody(story);
        var resp = await _client.ExecuteAsync(req);

        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.Created), "Expected 201 Created.");

        var data = SafeDeserialize<ApiResponseDto>(resp.Content);
        Assert.That(data?.StoryId, Is.Not.Null.And.Not.Empty, "StoryId should be returned.");
        Assert.That((data?.Msg ?? string.Empty), Does.Contain("Successfully created").IgnoreCase);

        _storyId = data!.StoryId!;
        TestContext.Progress.WriteLine($"Created StoryId: {_storyId}");
    }

    [Test, Order(2)]
    public async Task Edit_Existing_Story_Should_Return_200_And_Success_Message()
    {
        Assume.That(!string.IsNullOrEmpty(_storyId), "StoryId must be created in previous test.");

        var edited = new StoryDto
        {
            Title = "Edited Title",
            Description = "Edited Description",
            Url = ""
        };

        var req = new RestRequest($"/Story/Edit/{_storyId}", Method.Put).AddJsonBody(edited);
        var resp = await _client.ExecuteAsync(req);

        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK), "Expected 200 OK.");
        var data = SafeDeserialize<ApiResponseDto>(resp.Content);
        Assert.That((data?.Msg ?? string.Empty), Does.Contain("Successfully edited").IgnoreCase);
    }

    [Test, Order(3)]
    public async Task Get_All_Stories_Should_Return_200_And_NonEmpty_Array()
    {
        var req = new RestRequest("/Story/All", Method.Get);
        var resp = await _client.ExecuteAsync(req);

        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK), "Expected 200 OK.");
        using var doc = JsonDocument.Parse(resp.Content!);
        Assert.That(doc.RootElement.ValueKind, Is.EqualTo(JsonValueKind.Array), "Response must be an array.");
        Assert.That(doc.RootElement.GetArrayLength(), Is.GreaterThan(0), "Array should not be empty.");
    }

    [Test, Order(4)]
    public async Task Delete_Existing_Story_Should_Return_200_And_Success_Message()
    {
        Assume.That(!string.IsNullOrEmpty(_storyId), "StoryId must be created in previous test.");

        var req = new RestRequest($"/Story/Delete/{_storyId}", Method.Delete);
        var resp = await _client.ExecuteAsync(req);

        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK), "Expected 200 OK.");
        var data = SafeDeserialize<ApiResponseDto>(resp.Content);
        Assert.That((data?.Msg ?? string.Empty), Does.Contain("Deleted successfully").IgnoreCase);
    }

    [Test, Order(5)]
    public async Task Create_Without_Required_Fields_Should_Return_400()
    {
        var req = new RestRequest("/Story/Create", Method.Post)
            .AddJsonBody(new { });
        var resp = await _client.ExecuteAsync(req);

        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), "Expected 400 BadRequest.");
    }

    [Test, Order(6)]
    public async Task Edit_NonExisting_Story_Should_Return_400_And_No_Spoilers_Message()
    {
        var nonExistingId = Guid.NewGuid().ToString();
        var payload = new StoryDto { Title = "X", Description = "Y" };

        var req = new RestRequest($"/Story/Edit/{nonExistingId}", Method.Put).AddJsonBody(payload);
        var resp = await _client.ExecuteAsync(req);

        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), "Expected 400 BadRequest.");

        var msg = ExtractMsg(resp.Content);
        if (!string.IsNullOrEmpty(msg))
        {
            Assert.That(msg, Does.Contain("No spoilers").IgnoreCase
                              .Or.Contain("Unable").IgnoreCase,
                        $"Unexpected message: '{msg}'");
        }
        else
        {
            TestContext.Progress.WriteLine("Warning: Empty response body for non-existing Edit.");
        }
    }


    [Test, Order(7)]
    public async Task Delete_NonExisting_Story_Should_Return_400_And_Unable_To_Delete_Message()
    {
        var nonExistingId = Guid.NewGuid().ToString();

        var req = new RestRequest($"/Story/Delete/{nonExistingId}", Method.Delete);
        var resp = await _client.ExecuteAsync(req);

        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), "Expected 400 BadRequest.");

        var msg = ExtractMsg(resp.Content);
        if (!string.IsNullOrEmpty(msg))
        {
            Assert.That(msg, Does.Contain("Unable to delete this story spoiler").IgnoreCase
                              .Or.Contain("No spoilers").IgnoreCase,
                        $"Unexpected message: '{msg}'");
        }
        else
        {
            TestContext.Progress.WriteLine("Warning: Empty response body for non-existing Delete.");
        }
    }


    private static T? SafeDeserialize<T>(string? content)
    {
        if (string.IsNullOrWhiteSpace(content)) return default;
        try { return JsonSerializer.Deserialize<T>(content, JsonOpts); }
        catch { return default; }
    }

    private static string ExtractMsg(string? content)
    {
        if (string.IsNullOrWhiteSpace(content)) return string.Empty;
        try
        {
            using var doc = JsonDocument.Parse(content);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("msg", out var m))
            {
                return m.GetString() ?? string.Empty;
            }
        }
        catch { }
        return string.Empty;
    }
}
