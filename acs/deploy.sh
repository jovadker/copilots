#bin/bash

# To run the script use the following command
# az login (MS FDPO tenant)
# az account set --subscription baa70448-593c-4dc7-8a91-c92cf7eaf66e

current_date_time=$(date +%Y%m%d%H%M%S)

resource_group_name=Telephony.Automated.RG

az group create --name $resource_group_name --location eastus

tenant_id=$(az account show --query tenantId --output tsv)

# az identity create --resource-group $resource_group_name --name botIdentity

az deployment group create --resource-group $resource_group_name \
    --template-file ./iac/bot.json \
    --parameters appType=UserAssignedMSI displayName=relaybot botId=relaybot$current_date_time msAppId="" \
    tenantId="" msiResourceId=""




