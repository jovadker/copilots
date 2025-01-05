// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using Microsoft.Bot.Connector.DirectLine;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace EchoBot.Bots
{
    public interface DirectLineClientBase : IDisposable
    {
        public Task SendActivityAsync(Activity activity, CancellationToken cancellationToken);

        public Task<List<Activity>> ReceiveActivitiesAsync(CancellationToken cancellationToken);

    }
}
