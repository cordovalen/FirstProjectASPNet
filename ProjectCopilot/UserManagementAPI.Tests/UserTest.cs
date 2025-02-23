using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;
using Microsoft.AspNetCore.Mvc.Testing;

namespace UserManagementAPI.Tests;

public class UsersTest : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public UsersTest(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetUsers_ReturnsListOfUsers()
    {
        // Act
        var response = await _client.GetAsync("/users");

        // Assert
        response.EnsureSuccessStatusCode();
        var responseString = await response.Content.ReadAsStringAsync();
        Assert.Contains("Alice", responseString);
        Assert.Contains("Bob", responseString);
    }

    [Fact]
    public async Task GetUserById_ReturnsUser()
    {
        // Act
        var response = await _client.GetAsync("/users/1");

        // Assert
        response.EnsureSuccessStatusCode();
        var responseString = await response.Content.ReadAsStringAsync();
        Assert.Contains("Alice", responseString);
    }

    [Fact]
    public async Task PostUser_CreatesNewUser()
    {
        // Arrange
        var newUser = new User { Name = "Charlie", Email = "charlie@example.com" };
        var content = new StringContent(JsonSerializer.Serialize(newUser), Encoding.UTF8, "application/json");

        // Act
        var response = await _client.PostAsync("/users", content);

        // Assert
        response.EnsureSuccessStatusCode();
        var responseString = await response.Content.ReadAsStringAsync();
        Assert.Contains("Charlie", responseString);
    }
}
