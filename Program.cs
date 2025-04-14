using Microsoft.AspNetCore.Mvc;
using Octokit;
using System.Diagnostics;
using System.Net.Http.Headers;
using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions;
using Azure.AI.OpenAI;
using Azure.Identity;
using System.ClientModel;
using OpenAI.Chat;
using Microsoft.AspNetCore.SignalR.Protocol;



var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();
var app = builder.Build();

string appName = "ArchitectCopilotGHCP";

app.MapGet("/info", () => "Hello Copilot");
app.MapGet("/callback", () => "You can close this now");
app.MapPost("/", async ([FromHeader(Name = "X-GitHub-Token")] string githubToken, 
    [FromBody] Payload payload) =>
    {
        try
        {
            Console.WriteLine("Received payload:");
            Console.WriteLine(payload);

            GitHubClient octokitClient;
            try
            {
                octokitClient = new GitHubClient(new Octokit.ProductHeaderValue(appName))
                {
                    Credentials = new Credentials(githubToken)
                };
                Console.WriteLine("GitHub client initialized.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error initializing GitHub client: {ex.Message}");
                return Results.Problem($"Error initializing GitHub client: {ex.Message}");
            }

            Octokit.User user;
            try
            {
                user = await octokitClient.User.Current();
                Console.WriteLine($"User retrieved: {user.Login}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error retrieving GitHub user: {ex.Message}");
                return Results.Problem($"Error retrieving GitHub user: {ex.Message}");
            }

            HttpClient httpClient;
            try
            {
                httpClient = new HttpClient();
                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", githubToken);
                Console.WriteLine("HTTP client initialized.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error initializing HTTP client: {ex.Message}");
                return Results.Problem($"Error initializing HTTP client: {ex.Message}");
            }

            payload.Stream = true;

            string key, azureOpenAIEndpoint, azureOpenAIModel;
            try
            {
                key = Environment.GetEnvironmentVariable("AZURE_OPENAI_KEY");
                azureOpenAIEndpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT");
                azureOpenAIModel = Environment.GetEnvironmentVariable("AZURE_OPENAI_MODEL");

                if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(azureOpenAIEndpoint) || string.IsNullOrEmpty(azureOpenAIModel))
                {
                    throw new Exception("One or more required environment variables are missing.");
                }

                Console.WriteLine("Environment variables retrieved.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error retrieving environment variables: {ex.Message}");
                return Results.Problem($"Error retrieving environment variables: {ex.Message}");
            }

            AzureOpenAIClient azureClient;
            ChatClient chatClient;
            try
            {
                azureClient = new AzureOpenAIClient(new Uri(azureOpenAIEndpoint), new ApiKeyCredential(key));
                chatClient = azureClient.GetChatClient(azureOpenAIModel);
                Console.WriteLine("Azure OpenAI client initialized.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error initializing Azure OpenAI client: {ex.Message}");
                return Results.Problem($"Error initializing Azure OpenAI client: {ex.Message}");
            }

            try
            {
                payload.Messages.Insert(0, new Message
                {
                    Role = "system",
                    Content = $"You are talking to {user.Login}."
                });
                Console.WriteLine("System message added to payload.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error modifying payload: {ex.Message}");
                return Results.Problem($"Error modifying payload: {ex.Message}");
            }

            ChatCompletion completion;
            try
            {
                completion = chatClient.CompleteChat(
                    payload.Messages
                        .Where(message => message.Role == "user")
                        .Select(message => new UserChatMessage(message.Content))
                        .ToList()
                );
                Console.WriteLine("Chat completion retrieved.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error completing chat: {ex.Message}");
                return Results.Problem($"Error completing chat: {ex.Message}");
            }

            try
            {
                var responseStream = completion.Content[0];
                Console.WriteLine($"Response: {completion.Content[0].Text}");
                return Results.Ok(responseStream);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing response: {ex.Message}");
                return Results.Problem($"Error processing response: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Unexpected error: {ex.Message}");
            return Results.Problem($"Unexpected error: {ex.Message}");
        }
    }
);


app.Run();

public class Message{
    public required string Role {get;set;}
    public required string Content {get;set;}
}

public class Payload{
    public bool Stream {get;set;}
    public List<Message> Messages {get;set;} = [];
}