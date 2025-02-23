using Microsoft.AspNetCore.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

public partial class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Add services to the container.
        builder.Services.AddOpenApi();
        builder.Services.AddLogging();

        var app = builder.Build();

        // Configure the HTTP request pipeline.
        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
        }

        app.UseHttpsRedirection();

        // Add custom middleware
        app.UseMiddleware<ErrorHandlingMiddleware>();
        app.UseMiddleware<TokenAuthenticationMiddleware>();
        app.UseMiddleware<RequestResponseLoggingMiddleware>();

        // Middleware para manejo global de excepciones
        app.UseExceptionHandler("/error");

        List<User> users =
        [
            new User { Id = 1, Name = "Alice", Email = "alice@example.com" },
            new User { Id = 2, Name = "Bob", Email = "bob@example.com" }
        ];

        app.MapGet("/users", (int page = 1, int pageSize = 10) =>
        {
            IEnumerable<User> pagedUsers = users.Skip((page - 1) * pageSize).Take(pageSize);
            return Results.Ok(pagedUsers);
        });

        app.MapGet("/users/{id}", (int id) =>
        {
            try
            {
                User? user = users.FirstOrDefault(u => u.Id == id);
                return user is not null ? Results.Ok(user) : Results.NotFound(new { Message = "User not found" });
            }
            catch (Exception ex)
            {
                // Log the exception
                Console.WriteLine($"Error retrieving user: {ex.Message}");
                return Results.Problem("An error occurred", ex.Message, StatusCodes.Status500InternalServerError);
            }
        });

        app.MapPost("/users", (User user) =>
        {
            try{
                if (string.IsNullOrEmpty(user.Name) || string.IsNullOrEmpty(user.Email))
                {
                    return Results.BadRequest(new { Message = "Name and Email are required" });
                }

                if (!MyRegex().IsMatch(user.Email))
                {
                    return Results.BadRequest(new { Message = "Invalid email format" });
                }

                user.Id = users.Count > 0 ? users.Max(u => u.Id) + 1 : 1;
                users.Add(user);
                return Results.Created($"/users/{user.Id}", user);
            }
            catch (Exception ex)
            {
                // Log the exception
                Console.WriteLine($"Error retrieving user: {ex.Message}");
                return Results.Problem("An error occurred", ex.Message, StatusCodes.Status500InternalServerError);
            }
        });

        app.MapPut("/users/{id}", (int id, User inputUser) =>
        {
            try{
                User? user = users.FirstOrDefault(u => u.Id == id);
                if (user is null) return Results.NotFound(new { Message = "User not found" });

                if (string.IsNullOrEmpty(inputUser.Name) || string.IsNullOrEmpty(inputUser.Email))
                {
                    return Results.BadRequest(new { Message = "Name and Email are required" });
                }

                if (!MyRegex().IsMatch(user.Email))
                {
                    return Results.BadRequest(new { Message = "Invalid email format" });
                }

                user.Name = inputUser.Name;
                user.Email = inputUser.Email;

                return Results.Ok("User updated succesfully");
            }
            catch (Exception ex)
            {
                // Log the exception
                Console.WriteLine($"Error retrieving user: {ex.Message}");
                return Results.Problem("An error occurred", ex.Message, StatusCodes.Status500InternalServerError);
            }
        });

        app.MapDelete("/users/{id}", (int id) =>
        {
            try{
            User? user = users.FirstOrDefault(u => u.Id == id);
            if (user is not null)
            {
                users.Remove(user);
                return Results.Ok(user);
            }

            return Results.NotFound(new { Message = "User not found" });;
            }
            catch (Exception ex)
            {
                // Log the exception
                Console.WriteLine($"Error retrieving user: {ex.Message}");
                return Results.Problem("An error occurred", ex.Message, StatusCodes.Status500InternalServerError);
            }
        });

        app.Map("/error", (HttpContext context) =>
        {
            var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
            return Results.Problem(exception?.Message);
        });

        app.Run();
    }

    [GeneratedRegex(@"^[\w-\.]+@([\w-]+\.)+[\w-]{2,4}$")]
    private static partial Regex MyRegex();
}

public class User
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string Email { get; set; }
}

public class RequestResponseLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestResponseLoggingMiddleware> _logger;

    public RequestResponseLoggingMiddleware(RequestDelegate next, ILogger<RequestResponseLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Log request
        _logger.LogInformation($"Incoming request: {context.Request.Method} {context.Request.Path}");

        // Copy the original response body stream
        var originalBodyStream = context.Response.Body;

        using (var responseBody = new MemoryStream())
        {
            context.Response.Body = responseBody;

            // Call the next middleware in the pipeline
            await _next(context);

            // Log response
            context.Response.Body.Seek(0, SeekOrigin.Begin);
            var responseText = await new StreamReader(context.Response.Body).ReadToEndAsync();
            context.Response.Body.Seek(0, SeekOrigin.Begin);

            _logger.LogInformation($"Outgoing response: {context.Response.StatusCode} {responseText}");

            // Copy the contents of the new memory stream (which contains the response) to the original stream
            await responseBody.CopyToAsync(originalBodyStream);
        }
    }
}

public class ErrorHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ErrorHandlingMiddleware> _logger;

    public ErrorHandlingMiddleware(RequestDelegate next, ILogger<ErrorHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An unhandled exception occurred.");
            await HandleExceptionAsync(context, ex);
        }
    }

    private static Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = "application/json";
        context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;

        var result = JsonSerializer.Serialize(new { error = "Internal server error." });
        return context.Response.WriteAsync(result);
    }
}

public class TokenAuthenticationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<TokenAuthenticationMiddleware> _logger;

    public TokenAuthenticationMiddleware(RequestDelegate next, ILogger<TokenAuthenticationMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Headers.TryGetValue("Authorization", out var token))
        {
            context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
            await context.Response.WriteAsync("Authorization token is missing.");
            return;
        }

        // Validate token (this is a simplified example, implement your own token validation logic)
        if (token != "valid-token")
        {
            context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
            await context.Response.WriteAsync("Invalid authorization token.");
            return;
        }

        await _next(context);
    }
}



//Copilot helped me creating the crud using Minimal Apis, and helped me prevent bugs in a very fast way.

//I was also able to create the HTTP Rapid file and gave me step -by -step instructions on how to create the unit tests with Xunit.

//Logging Middleware: Logs all incoming requests and outgoing responses.
//1. Error Handling Middleware: Ensures consistent error handling across all endpoints.
//2. Token-Based Authentication Middleware: Secures API endpoints using token-based authentication.
//3. Middleware Pipeline Configuration: Configures the middleware pipeline for optimal performance.