using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Xunit;

/// Wrapper for values that should be passed as-is into the JSON args array (no re-serialization).
public record RawJson(string Json);

public class SpacetimeTestClient : IDisposable
{
    private static readonly string BaseUrl =
        Environment.GetEnvironmentVariable("STDB_TEST_URL") ?? "http://localhost:3001";
    private static readonly string DefaultDb =
        Environment.GetEnvironmentVariable("STDB_TEST_DB") ?? "tvs";

    private readonly HttpClient _http;
    private readonly string _dbName;

    public string Token { get; }
    public string IdentityHex { get; }

    private SpacetimeTestClient(string dbName, string token, string identityHex)
    {
        _dbName = dbName;
        Token = token;
        IdentityHex = identityHex;
        _http = new HttpClient { BaseAddress = new Uri(BaseUrl) };
        _http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
    }

    /// Creates a test client with a fresh identity. Each client is a unique "player."
    public static SpacetimeTestClient Create(string? dbName = null)
    {
        dbName ??= DefaultDb;
        using var http = new HttpClient { BaseAddress = new Uri(BaseUrl) };

        var response = http.PostAsync("/v1/identity", null).Result;
        response.EnsureSuccessStatusCode();

        var json = JsonDocument.Parse(response.Content.ReadAsStringAsync().Result);
        var token = json.RootElement.GetProperty("token").GetString()!;
        var identity = json.RootElement.GetProperty("identity").GetString()!;

        return new SpacetimeTestClient(dbName, token, identity);
    }

    /// Call a reducer by name. Arguments are a JSON array string, e.g. "[\"Alice\"]" or "[1]".
    /// Returns the raw HttpResponseMessage so tests can assert on status codes.
    public HttpResponseMessage CallReducer(string reducerName, string argsJson = "[]")
    {
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes(argsJson));
        content.Headers.ContentType =
            System.Net.Http.Headers.MediaTypeHeaderValue.Parse("application/json");
        var response = _http.PostAsync($"/v1/database/{_dbName}/call/{reducerName}", content).Result;
        return response;
    }

    /// Call a reducer and assert that it succeeded (2xx status).
    /// Pass C# values directly — strings, numbers, booleans are serialized to JSON for you.
    public void CallReducerExpectSuccess(string reducerName, params object[] args)
    {
        var response = CallReducer(reducerName, ArgsToJson(args));
        if (!response.IsSuccessStatusCode)
        {
            var body = response.Content.ReadAsStringAsync().Result;
            throw new Exception(
                $"Reducer '{reducerName}' failed with {(int)response.StatusCode}: {body}");
        }
    }

    /// Call a reducer and assert that it failed (non-2xx status).
    /// Returns the response body for further assertions.
    public string CallReducerExpectFailure(string reducerName, params object[] args)
    {
        var response = CallReducer(reducerName, ArgsToJson(args));
        var body = response.Content.ReadAsStringAsync().Result;
        Assert.False(response.IsSuccessStatusCode,
            $"Expected reducer '{reducerName}' to fail but got {(int)response.StatusCode}: {body}");
        return body;
    }

    private static string ArgsToJson(object[] args)
    {
        var elements = args.Select(a => a is RawJson raw ? raw.Json : JsonSerializer.Serialize(a));
        return $"[{string.Join(",", elements)}]";
    }

    /// Run a SQL query against the test database. Returns the raw JSON response body.
    public string Sql(string query)
    {
        var content = new StringContent(query, Encoding.UTF8, "text/plain");
        var response = _http.PostAsync($"/v1/database/{_dbName}/sql", content).Result;
        response.EnsureSuccessStatusCode();
        return response.Content.ReadAsStringAsync().Result;
    }

    /// Run a SQL query and parse the result rows from the first statement.
    /// Returns the rows as a JsonElement array.
    public JsonElement[] SqlRows(string query)
    {
        var json = JsonDocument.Parse(Sql(query));
        var results = json.RootElement;

        if (results.GetArrayLength() == 0)
            return Array.Empty<JsonElement>();

        var firstStatement = results[0];
        if (!firstStatement.TryGetProperty("rows", out var rows))
            return Array.Empty<JsonElement>();

        var list = new JsonElement[rows.GetArrayLength()];
        for (int i = 0; i < list.Length; i++)
            list[i] = rows[i];
        return list;
    }

    /// Assert that a SQL query returns exactly the expected number of rows.
    public void AssertRowCount(string table, int expected)
    {
        var rows = SqlRows($"SELECT * FROM {table}");
        Assert.Equal(expected, rows.Length);
    }

    /// Assert that a SQL query with a WHERE clause returns exactly the expected number of rows.
    public void AssertRowCount(string table, string where, int expected)
    {
        var rows = SqlRows($"SELECT * FROM {table} WHERE {where}");
        Assert.Equal(expected, rows.Length);
    }

    /// Creates a game and returns its ID by looking up the session owned by this client's identity.
    public ulong CreateGame(uint maxPlayers)
    {
        CallReducerExpectSuccess("create_game", maxPlayers);
        var rows = SqlRows($"SELECT Id FROM game_session WHERE OwnerIdentity = x'{IdentityHex}'");
        return rows[^1][0].GetUInt64();
    }

    public void ClearData()
    {
        CallReducerExpectSuccess("clear_data");
    }

    /// Returns this client's identity as a reducer argument.
    /// SpacetimeDB expects Identity as a 1-element tuple containing a hex-formatted 256-bit integer.
    public RawJson IdentityArg()
    {
        return new RawJson($"[\"0x{IdentityHex}\"]");
    }

    public void Dispose()
    {
        _http.Dispose();
    }
}
