// Generated with EchoBot .NET Template version v4.22.0

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Schema;

namespace EchoBot.Bots
{
    /// <summary>
    /// Vadkerti's EchoBot
    /// </summary>
    public class EchoBot : ActivityHandler
    {

        string tokenEndpoint = "https://bdd71775d624ea2ab799b9b78f6394.03.environment.api.powerplatform.com/powervirtualagents/botsbyschema/crcd4_copilot/directline/token?api-version=2022-03-01-preview";

        private BotState _conversationState;
        private BotState _userState;


        public EchoBot(ConversationState conversationState, UserState userState)
        {
            _conversationState = conversationState;
            _userState = userState;
        }

        /// <summary>
        /// Save the state of the conversation and user data in every turn
        /// </summary>
        /// <param name="turnContext"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public override async Task OnTurnAsync(ITurnContext turnContext, CancellationToken cancellationToken = default(CancellationToken))
        {
            await base.OnTurnAsync(turnContext, cancellationToken);

            // Save any state changes that might have occurred during the turn.
            await _conversationState.SaveChangesAsync(turnContext, false, cancellationToken);
            await _userState.SaveChangesAsync(turnContext, false, cancellationToken);
        }


        /// <summary>
        /// Call Copilot Studio chatbot via DirectLine API to get the response
        /// </summary>
        /// <param name="turnContext"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        protected override async Task OnMessageActivityAsync(ITurnContext<IMessageActivity> turnContext, CancellationToken cancellationToken)
        {
            //get the state from memory storage
            var accessor = _conversationState.CreateProperty<ConversationData>("Data");
            var conversationData = await accessor.GetAsync(turnContext, () => new ConversationData());
            DirectLineClient directLineClient = null;
            if (String.IsNullOrEmpty(conversationData.ConversationId))
            {
                var regionalEndpoint = await RegionalEndpointHelper.GetEndpointAsync(new Uri(tokenEndpoint));
                var options = new DirectLineOptions(tokenEndpoint, regionalEndpoint);

                directLineClient = new DirectLineClient(options);
                //this creates a new conversation
                await directLineClient.StartConversationAsync();
                conversationData.ConversationId = directLineClient.GetConversationId();
                conversationData.Token = directLineClient.Token;
                conversationData.ChannelId = directLineClient.ChannelAccount.Id;
            }
            else
            {
                //we have a conversation id, so we can continue the conversation (we are in context)
                var regionalEndpoint = await RegionalEndpointHelper.GetEndpointAsync(new Uri(tokenEndpoint));
                var options = new DirectLineOptions(tokenEndpoint, regionalEndpoint);

                directLineClient = new DirectLineClient(options);
                //set the watermark to continue the conversation
                directLineClient.Watermark = conversationData.Watermark;
                await directLineClient.ReconnectToConversationAsync(conversationData.ConversationId, conversationData.ChannelId, conversationData.Token);

            }
            var sendActivity = new Microsoft.Bot.Connector.DirectLine.Activity
            {
                Type = "message",
                Text = turnContext.Activity.Text
            };

            await directLineClient.SendActivityAsync(sendActivity, cancellationToken).ConfigureAwait(false);
            
            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();

            var activities = await directLineClient.ReceiveActivitiesAsync(cancellationToken).ConfigureAwait(false);
            // Get the first activity from the bot response
            var receivedActivity = activities.FirstOrDefault();
            conversationData.Watermark = directLineClient.Watermark;
            stopwatch.Stop();

            //send back to client
            var replyText = receivedActivity.Text;
            await turnContext.SendActivityAsync(MessageFactory.Text(replyText, replyText), cancellationToken);
            await turnContext.SendActivityAsync(MessageFactory.Text($"Time: {stopwatch.ElapsedMilliseconds} ms"), cancellationToken);

        }

        /// <summary>
        /// New member joins to the conversation
        /// </summary>
        /// <param name="membersAdded"></param>
        /// <param name="turnContext"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        protected override async Task OnMembersAddedAsync(IList<ChannelAccount> membersAdded, ITurnContext<IConversationUpdateActivity> turnContext, CancellationToken cancellationToken)
        {
            var welcomeText = "Hello and welcome!";
            foreach (var member in membersAdded)
            {
                if (member.Id != turnContext.Activity.Recipient.Id)
                {
                    await turnContext.SendActivityAsync(MessageFactory.Text(welcomeText, welcomeText), cancellationToken);
                }
            }
        }

        /// <summary>
        /// Conversation ends
        /// </summary>
        /// <param name="turnContext"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        protected override Task OnEndOfConversationActivityAsync(ITurnContext<IEndOfConversationActivity> turnContext, CancellationToken cancellationToken)
        {
            return base.OnEndOfConversationActivityAsync(turnContext, cancellationToken);
        }
    }
}
