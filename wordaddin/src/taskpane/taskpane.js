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
    // Poll for changes in localStorage
    setInterval(() => {
      const action = localStorage.getItem("action");
      if (action) {
        const message = JSON.parse(action);
        if (message.action === "summarize") {
          // Trigger the summarize action in the task pane
          summarizeInTaskPane();
        } else if (message.action === "rephrase") {
          // Trigger the rephrase action in the task pane
          rephraseInTaskPane();
        }
        // Clear the action after handling it
        localStorage.removeItem("action");
      }
    }, 1000); // Poll every second
    //Initialize bot framework webchat
    initializeWebChatUS();
  }
});

async function initializeWebChatUS() {
  try {
    await import("./config.dev.js");
  } catch (error) {
    await import("./config.js");
  }
  const res = await fetch(config.directLineTokenUrl, { method: "GET" });
  const { token } = await res.json();

  directLine = window.WebChat.createDirectLine({
    token: token,
    domain: config.directLineDomain,
  });

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

function summarizeInTaskPane() {
  // Your code to handle the summarize action in the task pane
  console.log("Summarize action triggered in task pane");
  Word.run(async (context) => {
    const selection = context.document.getSelection();
    selection.load("text");
    await context.sync();
    // Send the selected text to the bot
    sendMessageToBotWithInstruction(selection.text, "Summarize the following text in the same language");

    console.log(`Selected text: ${selection.text}`);
  });
}

function rephraseInTaskPane() {
  // Your code to handle the rephrase action in the task pane
  console.log("Rephrase action triggered in task pane");
  Word.run(async (context) => {
    const selection = context.document.getSelection();
    selection.load("text");
    await context.sync();
    // Send the selected text to the bot
    sendMessageToBotWithInstruction(selection.text, "Rephrase the following text in the same language");

    console.log(`Selected text: ${selection.text}`);
  });
}

function sendMessageToBotWithInstruction(message, instruction) {
  directLine
    .postActivity({
      from: { id: "user1", name: "User" },
      type: "message",
      text: instruction + " " + message,
    })
    .subscribe(
      (id) => console.log(`Message sent with ID ${id}`),
      (error) => console.error(`Error sending message: ${error}`)
    );
}
