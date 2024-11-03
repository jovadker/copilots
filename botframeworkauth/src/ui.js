// Select DOM elements to work with
const signInButton = document.getElementById('signIn');
const signOutButton = document.getElementById('signOut');
const titleDiv = document.getElementById('title-div');
const welcomeDiv = document.getElementById('welcome-div');
const tableDiv = document.getElementById('table-div');
const tableBody = document.getElementById('table-body-div');

const styleOptions = {
  backgroundColor: 'Black',
  bubbleBackground: '#222',
  bubbleBorder: 'solid 1px #444',
  bubbleBorderRadius: 20,
  bubbleFromUserBackground: '#222',
  bubbleFromUserBorder: 'solid 1px #444',
  bubbleFromUserBorderRadius: 20,
  bubbleFromUserTextColor: 'White',
  bubbleTextColor: 'White'
};

var directLine;


function welcomeUser(username) {
  signInButton.classList.add('d-none');
  signOutButton.classList.remove('d-none');
  titleDiv.classList.add('d-none');
  welcomeDiv.classList.remove('d-none');
  welcomeDiv.innerHTML = `Welcome ${username}!`;
};

function updateTable(account) {
  tableDiv.classList.remove('d-none');

  const tokenClaims = createClaimsTable(account.idTokenClaims);

  Object.keys(tokenClaims).forEach((key) => {
    let row = tableBody.insertRow(0);
    let cell1 = row.insertCell(0);
    let cell2 = row.insertCell(1);
    let cell3 = row.insertCell(2);
    cell1.innerHTML = tokenClaims[key][0];
    cell2.innerHTML = tokenClaims[key][1];
    cell3.innerHTML = tokenClaims[key][2];
  });
};


function getOAuthCardResourceUri(activity) {
  if (activity &&
    activity.attachments &&
    activity.attachments[0] &&
    activity.attachments[0].contentType === 'application/vnd.microsoft.card.oauth' &&
    activity.attachments[0].content.tokenExchangeResource) {
    // asking for token exchange with AAD
    return activity.attachments[0].content.tokenExchangeResource.uri;
  }
};

function exchangeTokenAsync(resourceUri) {
  if (!myMSALObj.getAllAccounts().length) {
    return Promise.resolve(null);
  }
  let account = myMSALObj.getAllAccounts()[0];
  
  let user = account.name;
  if (user) {
    let requestObj = {
      scopes: [resourceUri],
      account: account
    };
    return myMSALObj.acquireTokenSilent(requestObj)
      .then(function (tokenResponse) {
        return tokenResponse.accessToken;
      })
      .catch(function (error) {
        console.log(error);
      });
  }
  else {
    return Promise.resolve(null);
  }
}




async function initializeWebChat() {
  try {
    await import('./config.dev.js');
  } catch (error) {
    await import('./config.js');
  }

  var accountIdentifier = myMSALObj.getAllAccounts()[0].localAccountId;
  var accountName = myMSALObj.getAllAccounts()[0].name;

  var userId = accountIdentifier != null ?
    ("You-customized-prefix" + accountIdentifier).substr(0, 64)
    : (Math.random().toString() + Date.now().toString()).substr(0, 64);

  const res = await fetch(config.directLineTokenUrl, { method: 'GET' });
  const { token } = await res.json();

  directLine = window.WebChat.createDirectLine({
    token: token,
    domain: config.directLineDomain
  });
  //Using secret from Copilot Studio (it is not recommended to use secret in production)
  //directLine: window.WebChat.createDirectLine({ secret: config.secret, domain: config.directLineDomain }),

  const store = window.WebChat.createStore({}, ({ dispatch }) => next => action => {
    const { type } = action;
    if (action.type === 'DIRECT_LINE/CONNECT_FULFILLED') {
      dispatch({
        type: 'WEB_CHAT/SEND_EVENT',
        payload: {
          name: 'startConversation',
          type: 'event',
          value: { text: "hello" }
        }
      });
      return next(action);
    }

    if (action.type === 'DIRECT_LINE/INCOMING_ACTIVITY') {
      const activity = action.payload.activity;
      let resourceUri;
      if (activity.from && activity.from.role === 'bot' &&
        (resourceUri = getOAuthCardResourceUri(activity))) {
        exchangeTokenAsync(resourceUri).then(function (token) {
          if (token) {
            directLine.postActivity({
              type: 'invoke',
              name: 'signin/tokenExchange',
              value: {
                id: activity.attachments[0].content.tokenExchangeResource.id,
                connectionName: activity.attachments[0].content.connectionName,
                token
              },
              "from": {
                id: userId,
                name: accountName,
                role: "user"
              }
            }).subscribe(
              id => {
                if (id === 'retry') {
                  // bot was not able to handle the invoke, so display the oauthCard
                  return next(action);
                }
                // else: tokenexchange successful and we do not display the oauthCard
              },
              error => {
                // an error occurred to display the oauthCard
                return next(action);
              }
            );
            return;
          }
          else
            return next(action);
        });
      }
      else
        return next(action);
    }
    else
      return next(action);
  });



  window.WebChat.renderWebChat({
    directLine: directLine,
    store,
    userID: userId,
    styleOptions
  }, document.getElementById('webchat'));
};
