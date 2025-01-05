using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EchoBot.Bots
{
    internal static class ConversationController
    {
        public static async Task SendMessagesToChatBot(CancellationToken cancellationToken = default)
        {
            string tokenEndpoint = "https://bdd71775d624ea2ab799b9b78f6394.03.environment.api.powerplatform.com/powervirtualagents/botsbyschema/crcd4_copilot/directline/token?api-version=2022-03-01-preview";
            var regionalEndpoint = await RegionalEndpointHelper.GetEndpointAsync(new Uri(tokenEndpoint));
            var options = new DirectLineOptions(tokenEndpoint, regionalEndpoint);
            //var options = new DirectLineOptions(tokenEndpoint, new Uri("https://europe.directline.botframework.com"));

            using (var directLineClient = new DirectLineClient(options))
            {
                var sendActivity = new Microsoft.Bot.Connector.DirectLine.Activity
                {
                    Type = "message",
                    Text = "Ki volt Napoleon?"
                };

                Console.WriteLine($"User sends: {sendActivity.Text}");

                await directLineClient.SendActivityAsync(sendActivity, cancellationToken).ConfigureAwait(false);

                Stopwatch stopwatch = new Stopwatch();
                stopwatch.Start();
                var activities = await directLineClient.ReceiveActivitiesAsync(cancellationToken).ConfigureAwait(false);
                stopwatch.Stop();
                Console.WriteLine($"Execution time: {stopwatch.ElapsedMilliseconds} milliseconds");

                // Get the first activity from the bot response
                var receivedActivity = activities.FirstOrDefault();
                Console.WriteLine($"Bot responds: {receivedActivity.Text}");

                sendActivity = new Microsoft.Bot.Connector.DirectLine.Activity
                {
                    Type = "message",
                    Text = "Mikor született?"
                };

                Console.WriteLine($"User sends: {sendActivity.Text}");


                await directLineClient.SendActivityAsync(sendActivity, cancellationToken).ConfigureAwait(false);

                stopwatch = new Stopwatch();
                stopwatch.Start();
                activities = await directLineClient.ReceiveActivitiesAsync(cancellationToken).ConfigureAwait(false);
                stopwatch.Stop();
                Console.WriteLine($"Execution time: {stopwatch.ElapsedMilliseconds} milliseconds");

                // Get the first activity from the bot response
                receivedActivity = activities.FirstOrDefault();
                Console.WriteLine($"Bot responds: {receivedActivity.Text}");
            }
        }

    }
}
