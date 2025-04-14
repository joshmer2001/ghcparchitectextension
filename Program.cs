
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
var app = builder.Build();

string appName = "Architect Copilot GHCP";

app.MapGet("/info", () => "Hello Copilot");
app.MapGet("/callback", () => "You can close this now");
app.MapGet("/", async ([FromHeader(Name = "X-GitHub-Token")] string githubToken, 
    [FromBody] Payload payload) =>
    {
        Console.WriteLine(payload);

        var octokitClient = 
            new GitHubClient(new Octokit.ProductHeaderValue(appName))
        {
            Credentials = new Credentials(githubToken)
        };
        var user = await octokitClient.User.Current();
        Console.WriteLine($"User: {user.Login}");


        var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", githubToken);
        payload.Stream = true;
        
        string key = Environment.GetEnvironmentVariable("AZURE_OPENAI_KEY");
        string azureOpenAIEndpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT");
        string azureOpenAIModel = Environment.GetEnvironmentVariable("AZURE_OPENAI_MODEL");
        AzureOpenAIClient azureClient = new(
            new Uri(azureOpenAIEndpoint),
            new ApiKeyCredential(key));
        ChatClient chatClient = azureClient.GetChatClient(azureOpenAIModel);
        
        payload.Messages.Insert(0, new Message
        {
            Role = "system",
            Content = $"You are talking to {user.Login}."
        });

        ChatCompletion completion = chatClient.CompleteChat(
            payload.Messages
                .Where(message => message.Role == "user")
                .Select(message => new UserChatMessage(message.Content))
                .ToList()
        );

        var responseStream = completion.Content[0];

        Console.WriteLine(completion.Content[0].Text);

        Console.WriteLine($"{completion.Role}: {completion.Content[0].Text}");
        return Results.Ok(responseStream);
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