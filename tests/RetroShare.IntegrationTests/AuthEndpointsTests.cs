using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace RetroShare.IntegrationTests;

public abstract class ApiTestBase : IClassFixture<RetroShareFactory>
{
    protected readonly RetroShareFactory Factory;
    protected readonly HttpClient Client;
    protected static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    protected ApiTestBase(RetroShareFactory factory)
    {
        Factory = factory;
        Client = factory.CreateClient();
    }

    protected static StringContent JsonBody(object value) =>
        new(JsonSerializer.Serialize(value, Json), null, "application/json");

    protected record AuthTokens(string AccessToken, string RefreshToken);

    protected async Task<AuthTokens> RegisterAsync(string username)
    {
        var response = await Client.PostAsync("/api/auth/register", JsonBody(new
        {
            username,
            email = $"{username}@example.com",
            password = "Str0ngPass!42",
        }));
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        return new AuthTokens(
            payload.GetProperty("accessToken").GetString()!,
            payload.GetProperty("refreshToken").GetString()!);
    }

    protected async Task<AuthTokens> LoginAsync(string login, string password = "ChangeMe!123")
    {
        var response = await Client.PostAsync("/api/auth/login", JsonBody(new { login, password }));
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        return new AuthTokens(
            payload.GetProperty("accessToken").GetString()!,
            payload.GetProperty("refreshToken").GetString()!);
    }

    protected static HttpRequestMessage Authorized(HttpMethod method, string url, string token,
        HttpContent? content = null) => new(method, url)
    {
        Headers = { Authorization = new("Bearer", token) },
        Content = content,
    };
}

public class AuthEndpointsTests(RetroShareFactory factory) : ApiTestBase(factory)
{
    [Fact]
    public async Task Health_ReturnsHealthy()
    {
        var response = await Client.GetAsync("/api/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Healthy", body);
    }

    [Fact]
    public async Task Register_Login_Me_FullFlow()
    {
        var tokens = await RegisterAsync("flowuser");
        Assert.False(string.IsNullOrEmpty(tokens.AccessToken));

        var login = await LoginAsync("flowuser", "Str0ngPass!42");
        Assert.False(string.IsNullOrEmpty(login.RefreshToken));

        var me = await Client.SendAsync(Authorized(HttpMethod.Get, "/api/auth/me", login.AccessToken));
        me.EnsureSuccessStatusCode();
        var payload = await me.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("flowuser", payload.GetProperty("username").GetString());
        Assert.Contains("files.upload", payload.GetProperty("permissions").EnumerateArray()
            .Select(p => p.GetString()));
    }

    [Fact]
    public async Task Register_DuplicateUsername_Conflicts()
    {
        await RegisterAsync("dupuser");
        var response = await Client.PostAsync("/api/auth/register", JsonBody(new
        {
            username = "dupuser",
            email = "other@example.com",
            password = "Str0ngPass!42",
        }));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Register_WeakPassword_Rejected()
    {
        var response = await Client.PostAsync("/api/auth/register", JsonBody(new
        {
            username = "weakuser",
            email = "weak@example.com",
            password = "12345678",
        }));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Login_UnknownUser_Unauthorized()
    {
        var response = await Client.PostAsync("/api/auth/login", JsonBody(new
        {
            login = "ghost",
            password = "whatever!123",
        }));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_Rotates_Invalidates_OldToken()
    {
        var tokens = await RegisterAsync("refreshuser");

        var first = await Client.PostAsync("/api/auth/refresh", JsonBody(new { refreshToken = tokens.RefreshToken }));
        first.EnsureSuccessStatusCode();
        var rotated = await first.Content.ReadFromJsonAsync<JsonElement>();
        Assert.NotEqual(tokens.RefreshToken, rotated.GetProperty("refreshToken").GetString());

        // The original token must now be rejected.
        var replay = await Client.PostAsync("/api/auth/refresh", JsonBody(new { refreshToken = tokens.RefreshToken }));
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
    }

    [Fact]
    public async Task Logout_Revokes_RefreshToken()
    {
        var tokens = await RegisterAsync("logoutuser");

        var logout = await Client.SendAsync(Authorized(HttpMethod.Post, "/api/auth/logout", tokens.AccessToken,
            JsonBody(new { refreshToken = tokens.RefreshToken })));
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        var replay = await Client.PostAsync("/api/auth/refresh", JsonBody(new { refreshToken = tokens.RefreshToken }));
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
    }

    [Fact]
    public async Task Me_WithoutToken_Unauthorized()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client.GetAsync("/api/auth/me")).StatusCode);
    }
}

public class AuthorizationEndpointsTests(RetroShareFactory factory) : ApiTestBase(factory)
{
    [Fact]
    public async Task NormalUser_Cannot_ListUsers()
    {
        var tokens = await RegisterAsync("plainuser");
        var response = await Client.SendAsync(Authorized(HttpMethod.Get, "/api/users", tokens.AccessToken));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Admin_Can_ListUsers()
    {
        var admin = await LoginAsync("admin");
        var response = await Client.SendAsync(Authorized(HttpMethod.Get, "/api/users", admin.AccessToken));
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(payload.GetProperty("total").GetInt64() >= 1);
    }

    [Fact]
    public async Task Admin_Can_Grant_Role_And_Permissions_Apply_Immediately()
    {
        var admin = await LoginAsync("admin");
        var user = await RegisterAsync("promoteduser");

        // The fresh account cannot read users…
        Assert.Equal(HttpStatusCode.Forbidden,
            (await Client.SendAsync(Authorized(HttpMethod.Get, "/api/users", user.AccessToken))).StatusCode);

        // Find the Moderator role and assign it.
        var roles = await Client.SendAsync(Authorized(HttpMethod.Get, "/api/roles", admin.AccessToken));
        roles.EnsureSuccessStatusCode();
        var roleList = await roles.Content.ReadFromJsonAsync<JsonElement>();
        var moderator = roleList.EnumerateArray().First(r => r.GetProperty("name").GetString() == "Moderator");
        var moderatorId = moderator.GetProperty("id").GetInt32();

        var userId = (await (await Client.SendAsync(Authorized(HttpMethod.Get, "/api/auth/me", user.AccessToken)))
            .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();

        var assign = await Client.SendAsync(Authorized(HttpMethod.Put, $"/api/users/{userId}/roles",
            admin.AccessToken, JsonBody(new { roleIds = new[] { moderatorId } })));
        assign.EnsureSuccessStatusCode();

        // …and immediately after the role change it can — no re-login, no new token.
        var after = await Client.SendAsync(Authorized(HttpMethod.Get, "/api/users", user.AccessToken));
        Assert.Equal(HttpStatusCode.OK, after.StatusCode);
    }

    [Fact]
    public async Task Admin_Cannot_Remove_SystemManage_From_AdminRole()
    {
        var admin = await LoginAsync("admin");
        var roles = await Client.SendAsync(Authorized(HttpMethod.Get, "/api/roles", admin.AccessToken));
        var roleList = await roles.Content.ReadFromJsonAsync<JsonElement>();
        var adminRole = roleList.EnumerateArray().First(r => r.GetProperty("name").GetString() == "Admin");
        var adminRoleId = adminRole.GetProperty("id").GetInt32();

        var permissions = await Client.SendAsync(Authorized(HttpMethod.Get, "/api/permissions", admin.AccessToken));
        var permissionList = await permissions.Content.ReadFromJsonAsync<JsonElement>();
        var filesViewId = permissionList.EnumerateArray()
            .First(p => p.GetProperty("name").GetString() == "files.view").GetProperty("id").GetInt32();

        var update = await Client.SendAsync(Authorized(HttpMethod.Put, $"/api/roles/{adminRoleId}",
            admin.AccessToken, JsonBody(new { permissionIds = new[] { filesViewId } })));
        Assert.Equal(HttpStatusCode.BadRequest, update.StatusCode);
    }

    [Fact]
    public async Task SystemRole_Cannot_Be_Deleted()
    {
        var admin = await LoginAsync("admin");
        var roles = await Client.SendAsync(Authorized(HttpMethod.Get, "/api/roles", admin.AccessToken));
        var roleList = await roles.Content.ReadFromJsonAsync<JsonElement>();
        var adminRoleId = roleList.EnumerateArray().First(r => r.GetProperty("name").GetString() == "Admin")
            .GetProperty("id").GetInt32();

        var response = await Client.SendAsync(Authorized(HttpMethod.Delete, $"/api/roles/{adminRoleId}", admin.AccessToken));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CustomRole_WithPermissions_RoundTrips()
    {
        var admin = await LoginAsync("admin");
        var permissions = await Client.SendAsync(Authorized(HttpMethod.Get, "/api/permissions", admin.AccessToken));
        var permissionList = await permissions.Content.ReadFromJsonAsync<JsonElement>();
        var filesIds = permissionList.EnumerateArray()
            .Where(p => p.GetProperty("name").GetString()!.StartsWith("files."))
            .Select(p => p.GetProperty("id").GetInt32()).ToArray();

        var create = await Client.SendAsync(Authorized(HttpMethod.Post, "/api/roles", admin.AccessToken,
            JsonBody(new { name = "FileViewer", description = "View files only", permissionIds = filesIds })));
        create.EnsureSuccessStatusCode();
        var role = await create.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("FileViewer", role.GetProperty("name").GetString());
        Assert.Equal(filesIds.Length, role.GetProperty("permissions").GetArrayLength());

        var delete = await Client.SendAsync(Authorized(HttpMethod.Delete,
            $"/api/roles/{role.GetProperty("id").GetInt32()}", admin.AccessToken));
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
    }
}
