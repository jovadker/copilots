﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using Newtonsoft.Json;

namespace EchoBot.Bots
{
    public class TokenError
    {
        [JsonProperty("Message")]
        public string Message { get; set; }

        [JsonProperty("Code")]
        public string Code { get; set; }
    }
}
