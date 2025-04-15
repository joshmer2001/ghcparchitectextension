using Microsoft.AspNetCore.Mvc;
using Octokit;
using System.Diagnostics;
using System.Net.Http.Headers;
using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions;
using Microsoft.Extensions.Logging;
using Azure.AI.OpenAI;
using Azure.Identity;
using System.ClientModel;
using OpenAI.Chat;
using Microsoft.AspNetCore.SignalR.Protocol;



var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();
builder.Services.AddOpenApi();
builder.Services.AddHttpClient(); 
builder.Services.AddControllers(); 

var app = builder.Build();
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}
app.UseHttpsRedirection();
app.MapControllers();

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

            AsyncCollectionResult<StreamingChatCompletionUpdate> completion;
            try
            {
                completion = chatClient.CompleteChatStreamingAsync(
                payload.Messages
                    .Select<Message, ChatMessage>(message =>
                        message.Role switch
                        {
                            "user" => new UserChatMessage(message.Content),
                            "system" => new SystemChatMessage(message.Content),
                            "assistant" => new AssistantChatMessage(message.Content),
                            _ => throw new InvalidOperationException($"Unknown role: {message.Role}")
                        }
                    )
                    .ToList()

                );
                Console.WriteLine("Chat completion retrieved.");
            
                try{
                    var responseContent = new List<string>();
                    await foreach (StreamingChatCompletionUpdate completionUpdate in completion)
                    {
                        foreach (ChatMessageContentPart contentPart in completionUpdate.ContentUpdate)
                        {
                            responseContent.Add(contentPart.Text);
                        }
                    }
                    return Results.Json(responseContent);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error processing response: {ex.Message}");
                    return Results.Problem($"Error processing response: {ex.Message}");
                }
            }
            
            catch (Exception ex)
            {
                Console.WriteLine($"Error completing chat: {ex.Message}");
                return Results.Problem($"Error completing chat: {ex.Message}");
            }

            Console.WriteLine("No valid response generated.");
            return Results.Problem("No valid response generated.");
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