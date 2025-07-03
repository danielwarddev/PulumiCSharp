using System;
using Pulumi;
using Pulumi.AzureNative.Resources;
using Pulumi.AzureNative.Storage;
using Pulumi.AzureNative.Storage.Inputs;
using System.Collections.Generic;
using System.IO;
using Pulumi.AzureNative.Insights;
using Pulumi.AzureNative.OperationalInsights;
using Pulumi.AzureNative.OperationalInsights.Inputs;
using Pulumi.AzureNative.Web;
using Pulumi.AzureNative.Web.Inputs;
using Pulumi.Command.Local;
using Kind = Pulumi.AzureNative.Storage.Kind;
using StorageAccountArgs = Pulumi.AzureNative.Storage.StorageAccountArgs;

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
    
    var appServicePlan = new AppServicePlan("appServicePlan", new AppServicePlanArgs
    {
        ResourceGroupName = resourceGroup.Name,
        Kind = "Linux",
        Sku = new SkuDescriptionArgs
        {
            Name = "Y1",
            Tier = "Dynamic"
        },
        Reserved = true,
        Location = "WestUS"
    });
    
    // Create a new blob container and blob to hold the ZIP file of the packaged app for the Azure Function
    var blobContainer = new BlobContainer("function-zip-container", new BlobContainerArgs
    {
        ResourceGroupName = resourceGroup.Name,
        AccountName = storageAccount.Name,
        PublicAccess = PublicAccess.None
    });

    var publishCommand = Run.Invoke(new()
    {
        Command = "dotnet publish --output publish",
        Dir = Path.GetFullPath(Path.Combine("..", "PulumiCSharp.ConsoleApp")),
        Environment = new Dictionary<string, string>
        {
            ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1"
        }
    });
    publishCommand.Apply(x =>
    {
        Console.WriteLine(x.Stdout);
        return x; // We don't care about this, but Apply<T>() forces you to return a T
    });
    
    var blob = new Blob("functionZipBlob", new BlobArgs
    {
        ResourceGroupName = resourceGroup.Name,
        AccountName = storageAccount.Name,
        ContainerName = blobContainer.Name,
        Type = BlobType.Block,
        Source = new FileArchive("../PulumiCSharp.ConsoleApp/publish") // This will create a .zip for us from the folder
    });

    // Get the signed URL for the blob by calling ListStorageAccountServiceSAS on Azure
    var sasToken = ListStorageAccountServiceSAS.Invoke(new ListStorageAccountServiceSASInvokeArgs
    {
        ResourceGroupName = resourceGroup.Name,
        AccountName = storageAccount.Name,
        Protocols = HttpProtocol.Https,
        SharedAccessStartTime = "2022-01-01",
        SharedAccessExpiryTime = "2030-01-01",
        Resource = SignedResource.C, // Container
        Permissions = Permissions.R, // Read
        CanonicalizedResource = Output.Format($"/blob/{storageAccount.Name}/{blobContainer.Name}")
    }).Apply(result => result.ServiceSasToken);
    
    var codeBlobUrl = Output.Format($"https://{storageAccount.Name}.blob.core.windows.net/{blobContainer.Name}/{blob.Name}?{sasToken}");

    // Create an Azure workspace and Application Insights inside of it
    var workspace = new Workspace("workspace", new()
    {
        ResourceGroupName = resourceGroup.Name,
        RetentionInDays = 30,
        Sku = new WorkspaceSkuArgs
        {
            Name = "PerGB2018",
        },
        Features = new WorkspaceFeaturesArgs
        {
            EnableDataExport = true
        }
    });

    var appInsights = new Component("appInsights", new()
    {
        ResourceGroupName = resourceGroup.Name,
        ApplicationType = "web",
        Kind = "web",
        WorkspaceResourceId = workspace.Id
    });

    // Use everything above to create the app
    var app = new WebApp("my-function-app", new()
    {
        ResourceGroupName = resourceGroup.Name,
        ServerFarmId = appServicePlan.Id,
        Kind = "FunctionApp",
        SiteConfig = new SiteConfigArgs
        {
            NetFrameworkVersion = "v8.0",
            LinuxFxVersion = "DOTNET-ISOLATED|8.0",
            DetailedErrorLoggingEnabled = true,
            HttpLoggingEnabled = true,
            AppSettings = new[]
            {
                new NameValuePairArgs
                {
                    Name = "FUNCTIONS_WORKER_RUNTIME",
                    Value = "dotnet-isolated",
                },
                new NameValuePairArgs
                {
                    Name = "FUNCTIONS_EXTENSION_VERSION",
                    Value = "~4",
                },
                new NameValuePairArgs
                {
                    Name = "WEBSITE_RUN_FROM_PACKAGE",
                    Value = codeBlobUrl,
                },
                new NameValuePairArgs
                {
                    Name = "APPINSIGHTS_INSTRUMENTATIONKEY",
                    Value = appInsights.InstrumentationKey
                }
            },
            Cors = new CorsSettingsArgs
            {
                AllowedOrigins = new[]
                {
                    "*",
                },
            },
        },
    });
    
    var storageAccountKeys = ListStorageAccountKeys.Invoke(new ListStorageAccountKeysInvokeArgs
    {
        ResourceGroupName = resourceGroup.Name,
        AccountName = storageAccount.Name
    });

    var primaryStorageKey = storageAccountKeys.Apply(accountKeys =>
    {
        var firstKey = accountKeys.Keys[0].Value;
        return Output.CreateSecret(firstKey);
    });
    
    return new Dictionary<string, object?>
    {
        ["primaryStorageKey"] = primaryStorageKey,
        ["apiUrl"] = app.DefaultHostName.Apply(x => $"https://{x}/api/MyCoolFunction"),
        // ["publishOutput"] = publishCommand.Stdout.Apply(x => x)
        /*["publishOutput"] = publishCommand.Stdout.Apply(output => 
        {
            if (string.IsNullOrEmpty(output)) return "No output";
            var lines = output.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            return string.Join(Environment.NewLine, lines.Select(line => $"  {line.Trim()}"));
        })*/
    };
});