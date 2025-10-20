using Pulumi.AzureNative.Resources;
using Pulumi.AzureNative.Storage;
using Pulumi.AzureNative.Storage.Inputs;
using System.Collections.Generic;
using System.IO;
using Pulumi.Command.Local;
using Kind = Pulumi.AzureNative.Storage.Kind;
using StorageAccountArgs = Pulumi.AzureNative.Storage.StorageAccountArgs;
using PulumiCSharp.Infrastructure;
using System.Linq;
using System;

return await Pulumi.Deployment.RunAsync(() =>
{
    var resourceGroup = new ResourceGroup("resourceGroup");

    var storageAccount = new StorageAccount("sa", new StorageAccountArgs
    {
        ResourceGroupName = resourceGroup.Name,
        Sku = new SkuArgs
        {
            Name = SkuName.Standard_LRS
        },
        Kind = Kind.StorageV2
    });

    var publishCommand = Run.Invoke(new()
    {
        Command = $"dotnet clean && dotnet publish --output publish",
        Dir = Path.GetFullPath(Path.Combine("..", "PulumiCSharp.ConsoleApp")),
        Environment = new Dictionary<string, string>
        {
            ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1"
        }
    });

    // Uncomment to get the typical non-error logs from dotnet publish
    /*publishCommand.Apply(x =>
    {
        Console.WriteLine(x.Stdout);
        return x; // We don't care about this, but Apply<T>() forces you to return a T
    });*/

    var config = new Pulumi.Config();
    var functionCount = config.RequireInt32("function-count");
    var functionApps = new List<AzureFunctionApp>();

    for (int i = 0; i < functionCount; i++)
    {
        var app = new AzureFunctionApp($"cool-function-{i + 1}", new()
        {
            ResourceGroupName = resourceGroup.Name,
            StorageAccountName = storageAccount.Name,
            FunctionName = "MyCoolFunction",
            PublishPath = "../PulumiCSharp.ConsoleApp/publish",
            DotNetVersion = DotNetVersion.V8
        });
        functionApps.Add(app);
    }

    return functionApps.ToDictionary(
        app => $"{app.GetResourceName()}-api-url",
        app => (object?)app.ApiUrl
    );
});

// Examples of using multiple Azure Native providers in a single Pulumi stack
/*
return await Pulumi.Deployment.RunAsync(() =>
{
    // As examples...
    // 1) You might want to manage resources in multiple subscriptions at once...
    var devWestProvider = new Pulumi.AzureNative.Provider("Dev-WestUS", new()
    {
        Location = "EastUS",
        SubscriptionId = "11111111-1111-1111-1111-111111111111"
    });
    var stagingCentralProvider = new Pulumi.AzureNative.Provider("Staging-CentralUS", new()
    {
        Location = "WestUS",
        SubscriptionId = "22222222-2222-2222-2222-222222222222"
    });

    var devResourceGroup = new ResourceGroup("devResourceGroup", options: new Pulumi.CustomResourceOptions()
    {
        Provider = devWestProvider
    });

    var stagingResourceGroup = new ResourceGroup("stagingResourceGroup", options: new Pulumi.CustomResourceOptions()
    {
        Provider = stagingCentralProvider
    });

    // 2) ...or maybe read existing resources from other subscriptions you don't own
    // Pulumi won't manage these resources! You'll just have access to them in your stack
    var subscriptionId = "33333333-3333-3333-3333-333333333333";
    var resourceGroupName = "other-teams-rg";
    var keyVaultName = "other-teams-vault";

    var otherTeamsAccountProvider = new Pulumi.AzureNative.Provider("some-other-account", new()
    {
        Location = "EastUS",
        SubscriptionId = subscriptionId
        // When accessing a subscription you don't own, you may need to specify the authentication info
        // eg. ClientId, ClientSecret, UseMsi, UseOidc, etc.
    });

    // You can also use the command, Pulumi.AzureNative.KeyVault.GetVault.Invoke
    var keyVault = Pulumi.AzureNative.KeyVault.Vault.Get(
        "other-team-vault",
        $"/subscriptions/{subscriptionId}/resourceGroups/{resourceGroupName}/providers/Microsoft.KeyVault/vaults/{keyVaultName}",
        new() { Provider = otherTeamsAccountProvider }
    );

    return new Dictionary<string, object?>();
});
*/