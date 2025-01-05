using Azure;
using Azure.AI.OpenAI;
using Azure.Communication;
using Azure.Communication.CallAutomation;
using Azure.Messaging;
using Azure.Messaging.EventGrid;
using Azure.Messaging.EventGrid.SystemEvents;
using CopilotStudio;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;


public class Program
{
    string answerPromptSystemTemplate = """ 
    You are an assisant designed to answer the customer query and analyze the sentiment score from the customer tone. 
    You also need to determine the intent of the customer query and classify it into categories such as sales, marketing, shopping, etc.
    Use a scale of 1-10 (10 being highest) to rate the sentiment score. 
    Use the below format, replacing the text in brackets with the result. Do not include the brackets in the output: 
    Content:[Answer the customer query briefly and clearly in two lines and ask if there is anything else you can help with] 
    Score:[Sentiment score of the customer tone] 
    Intent:[Determine the intent of the customer query] 
    Category:[Classify the intent into one of the categories]
    """;

    string helloPrompt = "Hello, thank you for calling! How can I help you today?";
    //string helloPrompt = "Hello, köszönöm a hívásod, miben segíthetek?";
    string timeoutSilencePrompt = "I’m sorry, I didn’t hear anything. If you need assistance please let me know how I can help you.";
    string goodbyePrompt = "Thank you for calling! I hope I was able to assist you. Have a great day!";
    string connectAgentPrompt = "I'm sorry, I was not able to assist you with your request. Let me transfer you to an agent who can help you further. Please hold the line and I'll connect you shortly.";
    string callTransferFailurePrompt = "It looks like all I can’t connect you to an agent right now, but we will get the next available agent to call you back as soon as possible.";
    string agentPhoneNumberEmptyPrompt = "I’m sorry, we're currently experiencing high call volumes and all of our agents are currently busy. Our next available agent will call you back as soon as possible.";
    string EndCallPhraseToConnectAgent = "Sure, please stay on the line. I’m going to transfer you to an agent.";

    string transferFailedContext = "TransferFailed";
    string connectAgentContext = "ConnectAgent";
    string goodbyeContext = "Goodbye";

    private OpenAIClient m_AI_client = null;
    private WebApplicationBuilder m_Builder = null;

    //Copilot settings
    private bool m_IsCopilotStudioAnswer = true;
    //US endpoint
    //string m_TokenEndpoint = "https://bdd71775d624ea2ab799b9b78f6394.03.environment.api.powerplatform.com/powervirtualagents/botsbyschema/crcd4_copilot/directline/token?api-version=2022-03-01-preview";
    //EU endpoint
    string m_TokenEndpoint = "https://0b90d036e017e8409287b1de3d95e2.52.environment.api.powerplatform.com/powervirtualagents/botsbyschema/cr3d7_copilotDirectLineEu/directline/token?api-version=2022-03-01-preview";
    private ConversationData m_ConversationData = new ConversationData();


    public static void Main(string[] args)
    {
        var program = new Program();
        program.Run(args);
    }

    public void Run(string[] args)
    {
        m_Builder = WebApplication.CreateBuilder(args);

        //Get ACS Connection String from appsettings.json
        var acsConnectionString = m_Builder.Configuration.GetValue<string>("AcsConnectionString");
        ArgumentNullException.ThrowIfNullOrEmpty(acsConnectionString);

        //Call Automation Client
        var client = new CallAutomationClient(connectionString: acsConnectionString);

        //Grab the Cognitive Services endpoint from appsettings.json
        var cognitiveServicesEndpoint = m_Builder.Configuration.GetValue<string>("CognitiveServiceEndpoint");
        ArgumentNullException.ThrowIfNullOrEmpty(cognitiveServicesEndpoint);

        
        string agentPhonenumber = m_Builder.Configuration.GetValue<string>("AgentPhoneNumber");
        string chatResponseExtractPattern = @"\s*Content:(.*)\s*Score:(.*\d+)\s*Intent:(.*)\s*Category:(.*)";

        var key = m_Builder.Configuration.GetValue<string>("AzureOpenAIServiceKey");
        ArgumentNullException.ThrowIfNullOrEmpty(key);

        var endpoint = m_Builder.Configuration.GetValue<string>("AzureOpenAIServiceEndpoint");
        ArgumentNullException.ThrowIfNullOrEmpty(endpoint);

        m_AI_client = new OpenAIClient(new Uri(endpoint), new AzureKeyCredential(key));

        //Register and make CallAutomationClient accessible via dependency injection
        m_Builder.Services.AddSingleton(client);
        m_Builder.Services.AddSingleton(m_AI_client);
        var app = m_Builder.Build();

        var devTunnelUri = m_Builder.Configuration.GetValue<string>("DevTunnelUri");
        ArgumentNullException.ThrowIfNullOrEmpty(devTunnelUri);
        var maxTimeout = 2;


        app.MapGet("/", () => "Hello ACS CallAutomation!");

        app.MapPost("/api/incomingCall", async (
            [FromBody] EventGridEvent[] eventGridEvents,
            ILogger<Program> logger) =>
        {
            foreach (var eventGridEvent in eventGridEvents)
            {
                logger.LogInformation($"Incoming Call event received.");

                // Handle system events
                if (eventGridEvent.TryGetSystemEventData(out object eventData))
                {
                    // Handle the subscription validation event.
                    if (eventData is SubscriptionValidationEventData subscriptionValidationEventData)
                    {
                        var responseData = new SubscriptionValidationResponse
                        {
                            ValidationResponse = subscriptionValidationEventData.ValidationCode
                        };
                        return Results.Ok(responseData);
                    }
                }

                var jsonObject = Helper.GetJsonObject(eventGridEvent.Data);
                var callerId = Helper.GetCallerId(jsonObject);
                var incomingCallContext = Helper.GetIncomingCallContext(jsonObject);
                var callbackUri = new Uri(new Uri(devTunnelUri), $"/api/callbacks/{Guid.NewGuid()}?callerId={callerId}");
                Console.WriteLine($"Callback Url: {callbackUri}");
                var options = new AnswerCallOptions(incomingCallContext, callbackUri)
                {
                    CallIntelligenceOptions = new CallIntelligenceOptions() { CognitiveServicesEndpoint = new Uri(cognitiveServicesEndpoint) }
                };

                //sending back the result (callback URI of the call) to ACS 
                AnswerCallResult answerCallResult = await client.AnswerCallAsync(options);
                Console.WriteLine($"Answered call for connection id: {answerCallResult.CallConnection.CallConnectionId}");

                //Use EventProcessor to process CallConnected event
                var answer_result = await answerCallResult.WaitForEventProcessorAsync();
                if (answer_result.IsSuccess)
                {
                    Console.WriteLine($"Call connected event received for connection id: {answer_result.SuccessResult.CallConnectionId}");
                    var callConnectionMedia = answerCallResult.CallConnection.GetCallMedia();
                    await HandleRecognizeAsync(callConnectionMedia, callerId, helloPrompt);
                }

                //subscribe ACS PlayCompleted event
                client.GetEventProcessor().AttachOngoingEventProcessor<PlayCompleted>(answerCallResult.CallConnection.CallConnectionId, async (playCompletedEvent) =>
                {
                    logger.LogInformation($"Play completed event received for connection id: {playCompletedEvent.CallConnectionId}.");
                    if (!string.IsNullOrWhiteSpace(playCompletedEvent.OperationContext) && (playCompletedEvent.OperationContext.Equals(transferFailedContext, StringComparison.OrdinalIgnoreCase)
                    || playCompletedEvent.OperationContext.Equals(goodbyeContext, StringComparison.OrdinalIgnoreCase)))
                    {
                        logger.LogInformation($"Disconnecting the call...");
                        await answerCallResult.CallConnection.HangUpAsync(true);
                    }
                    else if (!string.IsNullOrWhiteSpace(playCompletedEvent.OperationContext) && playCompletedEvent.OperationContext.Equals(connectAgentContext, StringComparison.OrdinalIgnoreCase))
                    {
                        if (string.IsNullOrWhiteSpace(agentPhonenumber))
                        {
                            logger.LogInformation($"Agent phone number is empty");
                            await HandlePlayAsync(agentPhoneNumberEmptyPrompt,
                              transferFailedContext, answerCallResult.CallConnection.GetCallMedia());
                        }
                        else
                        {
                            logger.LogInformation($"Initializing the Call transfer...");
                            CommunicationIdentifier transferDestination = new PhoneNumberIdentifier(agentPhonenumber);
                            TransferCallToParticipantResult result = await answerCallResult.CallConnection.TransferCallToParticipantAsync(transferDestination);
                            logger.LogInformation($"Transfer call initiated: {result.OperationContext}");
                        }
                    }
                });

                //subscribe ACS PlayFailed event
                client.GetEventProcessor().AttachOngoingEventProcessor<PlayFailed>(answerCallResult.CallConnection.CallConnectionId, async (playFailedEvent) =>
                {
                    logger.LogInformation($"Play failed event received for connection id: {playFailedEvent.CallConnectionId}. Hanging up call...");
                    await answerCallResult.CallConnection.HangUpAsync(true);
                });

                //subscribe ACS Call Transfer event
                client.GetEventProcessor().AttachOngoingEventProcessor<CallTransferAccepted>(answerCallResult.CallConnection.CallConnectionId, async (callTransferAcceptedEvent) =>
                {
                    logger.LogInformation($"Call transfer accepted event received for connection id: {callTransferAcceptedEvent.CallConnectionId}.");
                });

                client.GetEventProcessor().AttachOngoingEventProcessor<CallTransferFailed>(answerCallResult.CallConnection.CallConnectionId, async (callTransferFailedEvent) =>
                {
                    logger.LogInformation($"Call transfer failed event received for connection id: {callTransferFailedEvent.CallConnectionId}.");
                    var resultInformation = callTransferFailedEvent.ResultInformation;
                    logger.LogError("Encountered error during call transfer, message={msg}, code={code}, subCode={subCode}", resultInformation?.Message, resultInformation?.Code, resultInformation?.SubCode);

                    await HandlePlayAsync(callTransferFailurePrompt,
                               transferFailedContext, answerCallResult.CallConnection.GetCallMedia());

                });

                //subscribe ACS RecognizeCompleted event
                client.GetEventProcessor().AttachOngoingEventProcessor<RecognizeCompleted>(answerCallResult.CallConnection.CallConnectionId, async (recognizeCompletedEvent) =>
                {
                    Console.WriteLine($"Recognize completed event received for connection id: {recognizeCompletedEvent.CallConnectionId}");
                    var speech_result = recognizeCompletedEvent.RecognizeResult as SpeechResult;
                    if (!string.IsNullOrWhiteSpace(speech_result?.Speech))
                    {
                        Console.WriteLine($"Recognized speech: {speech_result.Speech}");

                        if (await DetectEscalateToAgentIntent(speech_result.Speech, logger))
                        {
                            await HandlePlayAsync(EndCallPhraseToConnectAgent,
                                       connectAgentContext, answerCallResult.CallConnection.GetCallMedia());
                        }
                        else
                        {
                            // answer the question with OpenAI
                            if (this.m_IsCopilotStudioAnswer == false)
                            {
                                var chatGPTResponse = await GetChatGPTResponse(speech_result.Speech);
                                logger.LogInformation($"Chat GPT response: {chatGPTResponse}");
                                Regex regex = new Regex(chatResponseExtractPattern);
                                Match match = regex.Match(chatGPTResponse);
                                if (match.Success)
                                {
                                    string answer = match.Groups[1].Value;
                                    string sentimentScore = match.Groups[2].Value.Trim();
                                    string intent = match.Groups[3].Value;
                                    string category = match.Groups[4].Value;

                                    logger.LogInformation("Chat GPT Answer={ans}, Sentiment Rating={rating}, Intent={Int}, Category={cat}",
                                        answer, sentimentScore, intent, category);
                                    var score = getSentimentScore(sentimentScore);
                                    if (score > -1 && score < 5)
                                    {
                                        await HandlePlayAsync(connectAgentPrompt,
                                            connectAgentContext, answerCallResult.CallConnection.GetCallMedia());
                                    }
                                    else
                                    {
                                        await HandleChatResponse(answer, answerCallResult.CallConnection.GetCallMedia(), callerId, logger);
                                    }
                                }
                                else
                                {
                                    logger.LogInformation("No match found");
                                    await HandleChatResponse(chatGPTResponse, answerCallResult.CallConnection.GetCallMedia(), callerId, logger);
                                }
                            }
                            else
                            {
                                //answer the question with Copilot Studio via Direct Line
                                DirectLineClient directLineClient = null;
                                if (String.IsNullOrEmpty(this.m_ConversationData.ConversationId))
                                {
                                    var regionalEndpoint = await RegionalEndpointHelper.GetEndpointAsync(new Uri(m_TokenEndpoint));
                                    var options = new DirectLineOptions(m_TokenEndpoint, regionalEndpoint);

                                    directLineClient = new DirectLineClient(options);
                                    //this creates a new conversation
                                    await directLineClient.StartConversationAsync();
                                    m_ConversationData.ConversationId = directLineClient.GetConversationId();
                                    m_ConversationData.Token = directLineClient.Token;
                                    m_ConversationData.ChannelId = directLineClient.ChannelAccount.Id;
                                }
                                else
                                {
                                    //we have a conversation id, so we can continue the conversation (we are in context)
                                    var regionalEndpoint = await RegionalEndpointHelper.GetEndpointAsync(new Uri(m_TokenEndpoint));
                                    var options = new DirectLineOptions(m_TokenEndpoint, regionalEndpoint);

                                    directLineClient = new DirectLineClient(options);
                                    //set the watermark to continue the conversation
                                    directLineClient.Watermark = m_ConversationData.Watermark;
                                    await directLineClient.ReconnectToConversationAsync(m_ConversationData.ConversationId, 
                                        m_ConversationData.ChannelId, m_ConversationData.Token);

                                }
                                var sendActivity = new Microsoft.Bot.Connector.DirectLine.Activity
                                {
                                    Type = "message",
                                    Text = speech_result.Speech
                                };

                                await directLineClient.SendActivityAsync(sendActivity).ConfigureAwait(false);

                                var activities = await directLineClient.ReceiveActivitiesAsync().ConfigureAwait(false);
                                // Get the first activity from the bot response
                                var receivedActivity = activities.FirstOrDefault();
                                m_ConversationData.Watermark = directLineClient.Watermark;
                                
                                if (receivedActivity != null && receivedActivity.ChannelData != null)
                                {
                                    // Parse the JSON data into a JsonDocument
                                    JsonDocument jsonDocument = JsonDocument.Parse(receivedActivity.ChannelData.ToString());

                                    // Access specific properties in the JSON data
                                    JsonElement root = jsonDocument.RootElement;
                                    JsonElement summary = root.GetProperty("pva:gpt-feedback").GetProperty("summarizationOpenAIResponse").GetProperty("result").GetProperty("summary");

                                    // Get the values of the properties
                                    string summaryValue = summary.GetString();

                                    //send back to client
                                    var answer = summaryValue;
                                    logger.LogInformation("Copilot Studio Answer={ans}", answer);

                                    //send the reply back to the caller
                                    await HandleChatResponse(answer, answerCallResult.CallConnection.GetCallMedia(), callerId, logger);

                                }
                            }
                        }
                    }
                });

                //subscribe ACS RecognizedFailed event
                client.GetEventProcessor().AttachOngoingEventProcessor<RecognizeFailed>(answerCallResult.CallConnection.CallConnectionId, async (recognizeFailedEvent) =>
                {
                    var callConnectionMedia = answerCallResult.CallConnection.GetCallMedia();

                    if (MediaEventReasonCode.RecognizeInitialSilenceTimedOut.Equals(recognizeFailedEvent.ResultInformation.SubCode.Value.ToString()) && maxTimeout > 0)
                    {
                        Console.WriteLine($"Recognize failed event received for connection id: {recognizeFailedEvent.CallConnectionId}. Retrying recognize...");
                        maxTimeout--;
                        await HandleRecognizeAsync(callConnectionMedia, callerId, timeoutSilencePrompt);
                    }
                    else
                    {
                        Console.WriteLine($"Recognize failed event received for connection id: {recognizeFailedEvent.CallConnectionId}. Playing goodbye message...");
                        await HandlePlayAsync(goodbyePrompt, goodbyeContext, callConnectionMedia);
                    }
                });
            }
            return Results.Ok();
        });

        // api to handle call back events
        app.MapPost("/api/callbacks/{contextId}", async (
            [FromBody] CloudEvent[] cloudEvents,
            [FromRoute] string contextId,
            [Required] string callerId,
            CallAutomationClient callAutomationClient,
            ILogger<Program> logger) =>
        {
            var eventProcessor = client.GetEventProcessor();
            eventProcessor.ProcessEvents(cloudEvents);
            return Results.Ok();
        });

        app.Run();
    }

    async Task HandleChatResponse(string chatResponse, CallMedia callConnectionMedia, string callerId, ILogger logger, string context = "OpenAISample")
    {
        var chatGPTResponseSource = new TextSource(chatResponse)
        {
            VoiceName = "en-US-NancyNeural",
            //VoiceName = "hu-HU-TamasNeural"
        };

        var recognizeOptions =
            new CallMediaRecognizeSpeechOptions(
                targetParticipant: CommunicationIdentifier.FromRawId(callerId))
            {
                InterruptPrompt = false,
                InitialSilenceTimeout = TimeSpan.FromSeconds(15),
                Prompt = chatGPTResponseSource,
                OperationContext = context,
                EndSilenceTimeout = TimeSpan.FromMilliseconds(500)
            };

        var recognize_result = await callConnectionMedia.StartRecognizingAsync(recognizeOptions);
    }

    int getSentimentScore(string sentimentScore)
    {
        string pattern = @"(\d)+";
        Regex regex = new Regex(pattern);
        Match match = regex.Match(sentimentScore);
        return match.Success ? int.Parse(match.Value) : -1;
    }

    async Task<bool> DetectEscalateToAgentIntent(string speechText, ILogger logger) =>
               await HasIntentAsync(userQuery: speechText, intentDescription: "talk to agent", logger);

    async Task<bool> HasIntentAsync(string userQuery, string intentDescription, ILogger logger)
    {
        var systemPrompt = "You are a helpful assistant";
        var baseUserPrompt = "In 1 word: does {0} have similar meaning as {1}?";
        var combinedPrompt = string.Format(baseUserPrompt, userQuery, intentDescription);

        var response = await GetChatCompletionsAsync(systemPrompt, combinedPrompt);

        var isMatch = response.ToLowerInvariant().Contains("yes");
        logger.LogInformation($"OpenAI results: isMatch={isMatch}, customerQuery='{userQuery}', intentDescription='{intentDescription}'");
        return isMatch;
    }

    async Task<string> GetChatGPTResponse(string speech_input)
    {
        return await GetChatCompletionsAsync(answerPromptSystemTemplate, speech_input);
    }

    async Task<string> GetChatCompletionsAsync(string systemPrompt, string userPrompt)
    {
        var chatCompletionsOptions = new ChatCompletionsOptions()
        {
            Messages = {
                    new ChatMessage(ChatRole.System, systemPrompt),
                    new ChatMessage(ChatRole.User, userPrompt),
                    },
            MaxTokens = 1000
        };

        var response = await m_AI_client.GetChatCompletionsAsync(
            deploymentOrModelName: m_Builder.Configuration.GetValue<string>("AzureOpenAIDeploymentModelName"),
            chatCompletionsOptions);

        var response_content = response.Value.Choices[0].Message.Content;
        return response_content;
    }

    async Task HandleRecognizeAsync(CallMedia callConnectionMedia, string callerId, string message)
    {
        // Play greeting message
        var greetingPlaySource = new TextSource(message)
        {
            VoiceName = "en-US-NancyNeural"
            //VoiceName = "hu-HU-TamasNeural"
        };

        var recognizeOptions =
            new CallMediaRecognizeSpeechOptions(
                targetParticipant: CommunicationIdentifier.FromRawId(callerId))
            {
                InterruptPrompt = false,
                InitialSilenceTimeout = TimeSpan.FromSeconds(15),
                Prompt = greetingPlaySource,
                OperationContext = "GetFreeFormText",
                EndSilenceTimeout = TimeSpan.FromMilliseconds(500)
            };

        var recognize_result = await callConnectionMedia.StartRecognizingAsync(recognizeOptions);
    }

    async Task HandlePlayAsync(string textToPlay, string context, CallMedia callConnectionMedia)
    {
        // Play message
        var playSource = new TextSource(textToPlay)
        {
            VoiceName = "en-US-NancyNeural"
            //VoiceName = "hu-HU-TamasNeural"
        };

        var playOptions = new PlayToAllOptions(playSource) { OperationContext = context };
        await callConnectionMedia.PlayToAllAsync(playOptions);
    }

}




