#bin/bash
set -e
# To run the script use the following command
# az login (MS FDPO tenant)
# az account set --subscription baa70448-593c-4dc7-8a91-c92cf7eaf66e

current_date_time=$(date +%Y%m%d%H%M%S)

resource_group_name=Telephony.Automated.RG
location=eastus

az group create --name $resource_group_name --location $location

tenant_id=$(az account show --query tenantId --output tsv)

# create user assigned identity
identity_name=automatedbotIdentity
identity=$(az identity create --resource-group $resource_group_name --name $identity_name --location $location)

echo "output:" + $identity

identity_id=$(echo $identity | jq -r '.id')
client_id=$(echo $identity | jq -r '.clientId')
resource_id=$(echo $identity | jq -r '.id')
echo "Identity ID: $identity_id"
echo "Client ID: $client_id"
echo "Resource ID: $resource_id"
echo "Tenant ID: $tenant_id"

botId=copilotstudiobot$current_date_time

az deployment group create --resource-group $resource_group_name \
    --template-file ./iac/BotWebApp.json \
    --parameters appServiceName=automatedbot \
      appServicePlanName=automatedbotAppPlan appServicePlanLocation=$location \
      appType=UserAssignedMSI appId=$identity_id appSecret="" tenantId=$tenant_id \
      UMSIName=$identity_name UMSIResourceGroupName=$resource_group_name \
    --verbose


# app id = client id of managed identity
# tenant id = tenant id of the subscription
# msiResourceId = /subscriptions/baa70448-593c-4dc7-8a91-c92cf7eaf66e/resourceGroups/Telephony.Automated.RG/providers/Microsoft.ManagedIdentity/userAssignedIdentities/automatedbotIdentity

az deployment group create --resource-group $resource_group_name \
    --template-file ./iac/bot.json \
    --parameters appType=UserAssignedMSI botId=$botId \
      msAppId=$client_id tenantId=$tenant_id msiResourceId=$identity_id


# building the app
dotnet build ./src/jovadkerecho/EchoBot.csproj


