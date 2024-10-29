/* eslint-disable no-undef */
/*
 * Copyright (c) Microsoft Corporation. All rights reserved. Licensed under the MIT license.
 * See LICENSE in the project root for license information.
 */

/* global document, Office, Word */

const styleOptions = {
  backgroundColor: "Black",
  bubbleBackground: "#222",
  bubbleBorder: "solid 1px #444",
  bubbleBorderRadius: 20,
  bubbleFromUserBackground: "#222",
  bubbleFromUserBorder: "solid 1px #444",
  bubbleFromUserBorderRadius: 20,
  bubbleFromUserTextColor: "White",
  bubbleTextColor: "White",
};

var directLine;

Office.onReady((info) => {
  if (info.host === Office.HostType.Word) {
    document.getElementById("sideload-msg").style.display = "none";
    document.getElementById("app-body").style.display = "flex";
    document.getElementById("run").onclick = run;
    document.getElementById("sendMessage").onclick = sendMessage;
    initializeWebChatUS();
  }
});

async function initializeWebChatUS() {
  // DirectLineEU in prod environment of jovadkere5 tenant
  //USA: https://bdd71775d624ea2ab799b9b78f6394.03.environment.api.powerplatform.com/powervirtualagents/botsbyschema/crcd4_copilot/directline/token?api-version=2022-03-01-preview
  //EU: https://0b90d036e017e8409287b1de3d95e2.52.environment.api.powerplatform.com/powervirtualagents/botsbyschema/cr3d7_copilotDirectLineEu/directline/token?api-version=2022-03-01-preview
  const res = await fetch(
    "https://bdd71775d624ea2ab799b9b78f6394.03.environment.api.powerplatform.com/powervirtualagents/botsbyschema/crcd4_copilot/directline/token?api-version=2022-03-01-preview",
    { method: "GET" }
  );
  const { token } = await res.json();

  directLine = window.WebChat.createDirectLine({ token });
  window.WebChat.renderWebChat(
    {
      directLine: directLine,
      styleOptions,
    },
    document.getElementById("webchat")
  );

  // Subscribe to receive messages from the bot
  directLine.activity$.subscribe((activity) => {
    if (activity.type === "message" && activity.from.role === "bot") {
      console.log(`Bot said: ${activity.text}`);
      Word.run(async (context) => {
        // insert a paragraph with the bot message at the end of the document.
        const paragraph = context.document.body.insertParagraph(activity.text, Word.InsertLocation.end);
        // change the paragraph color to blue.
        paragraph.font.color = "blue";
        await context.sync();
      });
    }
  });
}

export async function run() {
  return Word.run(async (context) => {
    /**
     * Insert your Word code here
     */

    // insert a paragraph at the end of the document.
    const paragraph = context.document.body.insertParagraph("Hello World", Word.InsertLocation.end);

    // change the paragraph color to blue.
    paragraph.font.color = "blue";

    await context.sync();
  });
}

export function sendMessage() {
  //send a message to the bot
  directLine
    .postActivity({
      from: { id: "user1", name: "User" },
      type: "message",
      text: "Create a sales template for customer John Doe",
    })
    .subscribe(
      (id) => console.log(`Message sent with ID ${id}`),
      (error) => console.error(`Error sending message: ${error}`)
    );
}