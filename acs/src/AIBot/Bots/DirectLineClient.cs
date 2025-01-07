// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using Microsoft.Bot.Connector.DirectLine;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace EchoBot.Bots
{
    public class DirectLineClient : DirectLineClientBase
    {
        private Microsoft.Bot.Connector.DirectLine.DirectLineClient _directLineClient;
        private readonly DirectLineOptions _options;
        private Conversation _conversation;
        public string? Watermark { get; set; }
        public ChannelAccount? ChannelAccount { get; set; }
        public string Token { get; set; }

        public DirectLineClient(DirectLineOptions options)
        {
            _options = options;
        }

        public async Task SendActivityAsync(Activity activity, CancellationToken cancellationToken)
        {
            try
            {
                if (_conversation == null)
                {
                    await StartConversationAsync().ConfigureAwait(false);

                    if (activity.Type == Microsoft.Bot.Connector.DirectLine.ActivityTypes.ConversationUpdate)
                    {
                        // StartConversationAsync sends a ConversationUpdate automatically.
                        // Ignore the activity sent if it is the first one we are sending to the bot and it is a ConversationUpdate.
                        // This can happen with recorded scripts where we get a conversation update from the transcript that we don't
                        // want to use.
                        return;
                    }
                }

                var activityPost = new Activity
                {
                    Type = activity.Type,
                    From = ChannelAccount,
                    Text = activity.Text
                };

                await _directLineClient.Conversations.PostActivityAsync(_conversation.ConversationId, activityPost, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                throw new Exception(ex.Message);
            }
        }

        public async Task<List<Activity>> ReceiveActivitiesAsync(CancellationToken cancellationToken)
        {
            ActivitySet? response = null;
            List<Activity>? result = new List<Activity>();
            var retryCount = 0;

            try
            {
                do
                {
                    
                    response = await _directLineClient.Conversations.GetActivitiesAsync(_conversation.ConversationId, Watermark);
                    if (response == null)
                    {
                        // Response can be null if directLineClient token expires
                        throw new Exception("The directline token has expired.");
                    }

                    Watermark = response?.Watermark;
                    result = response?.Activities?.Where(x =>
                        x.Type == ActivityTypes.Message &&
                        x.From.Name != null).ToList();

                    if (result != null && result.Any())
                    {
                        break;
                    }

                    // Wait for one second before polling the bot again, after that wait for 1 additional second each time
                    // BotConnector sample https://github.com/microsoft/PowerVirtualAgentsSamples/blob/master/BotConnectorApp/BotConnectorApp.cs
                    //Thread.Sleep(1000 * (retryCount * 2));
                    Thread.Sleep(1000);
                } while (retryCount++ < Constants.MaxTries);

                if (retryCount > Constants.MaxTries)
                {
                    throw new InvalidOperationException($"Failed to receive activities from the Bot after {Constants.MaxTries} attempts.");
                }

                return result;
            }
            catch (Exception ex)
            {
                throw new Exception(ex.Message);
            }
        }

        public void Dispose()
        {
            _conversation = null;
            Watermark = null;
            ChannelAccount = null;
        }

        public async Task StartConversationAsync()
        {
            // Obtain a token using the BotId and TenantId
            var tokenInfo = await GetDirectLineTokenAsync().ConfigureAwait(false);
            if (tokenInfo == null)
            {
                throw new InvalidOperationException("There was an error getting the directline token. Please check the bot url.");
            }
            else if (tokenInfo.Error != null && tokenInfo.Error.Message != null)
            {
                throw new InvalidOperationException(tokenInfo.Error.Message);
            }
            else if (tokenInfo.ErrorCode == 4103)
            {
                throw new InvalidOperationException("Bot Id is invalid.");
            }

            if (_options.RegionalEndpoint == null)
            {
                throw new InvalidOperationException("There was an error getting the directline URL. Please check the bot url.");
            }
            Token = tokenInfo.Token;
            _directLineClient = new Microsoft.Bot.Connector.DirectLine.DirectLineClient(_options.RegionalEndpoint, new DirectLineClientCredentials(tokenInfo.Token));
            _conversation = await _directLineClient.Conversations.StartConversationAsync().ConfigureAwait(false);
            if (_conversation == null)
            {
                throw new InvalidOperationException("There was an error starting the conversation.");
            }

            ChannelAccount = new ChannelAccount { Id = Guid.NewGuid().ToString() };
        }

        public async Task ReconnectToConversationAsync(string conversationId, string channelId, string token)
        {
            if (_options.RegionalEndpoint == null)
            {
                throw new InvalidOperationException("There was an error getting the directline URL. Please check the bot url.");
            }

            _directLineClient = 
                new Microsoft.Bot.Connector.DirectLine.DirectLineClient(_options.RegionalEndpoint, new DirectLineClientCredentials(token));
            _conversation = await _directLineClient.Conversations.ReconnectToConversationAsync(conversationId);
            if (_conversation == null)
            {
                throw new InvalidOperationException("There was an error starting the conversation.");
            }

            ChannelAccount = new ChannelAccount { Id = channelId };
        }


        /// <summary>
        /// Return the conversation id to continue the conversation
        /// </summary>
        /// <returns></returns>
        public string GetConversationId()
        {
            return _conversation.ConversationId;
        }

        private async Task<TokenInfo> GetDirectLineTokenAsync()
        {
            try
            {
                TokenInfo tokenInfo;

                using HttpClient client = new();
                using HttpRequestMessage httpRequest = new();
                    
                httpRequest.Method = HttpMethod.Get;
                httpRequest.RequestUri = _options.BotUrl;
                using (var response = await client.SendAsync(httpRequest))
                {
                    if (response.IsSuccessStatusCode)
                    {
                        var responseString = await response.Content.ReadAsStringAsync();
                        tokenInfo = JsonConvert.DeserializeObject<TokenInfo>(responseString);
                    }
                    else
                    {
                        return null;
                    }
                }

                return tokenInfo;
            }
            catch (Exception ex)
            {
                throw new Exception(ex.Message);
            }
        }
    }
}
