using Pulumi;
using Pulumi.AzureNative.Insights;
using Pulumi.AzureNative.OperationalInsights;
using Pulumi.AzureNative.OperationalInsights.Inputs;
using Pulumi.AzureNative.Storage;
using Pulumi.AzureNative.Web;
using Pulumi.AzureNative.Web.Inputs;

namespace PulumiCSharp.Infrastructure;

public class DotNetVersion : ResourceArgs
{
    public static readonly DotNetVersion V8 = new DotNetVersion
    {
        NetFrameworkVersion = "v8.0",
        LinuxFxVersion = "DOTNET-ISOLATED|8.0"
    };
    public static readonly DotNetVersion V9 = new DotNetVersion
    {
        NetFrameworkVersion = "v9.0",
        LinuxFxVersion = "DOTNET-ISOLATED|9.0"
    };

    [Input("netFrameworkVersion", true)] public required Input<string> NetFrameworkVersion { get; set; }
    [Input("linuxFxVersion", true)] public required Input<string> LinuxFxVersion { get; set; }
}

public class AzureFunctionAppArgs : ResourceArgs
{
    [Input("region", true)] public required Input<string> Region { get; set; }
    [Input("resourceGroupName", true)] public required Input<string> ResourceGroupName { get; set; }
    [Input("storageAccountName", true)] public required Input<string> StorageAccountName { get; set; }
    [Input("functionName", true)] public required Input<string> FunctionName { get; set; }
    [Input("publishPath", true)] public required Input<string> PublishPath { get; set; }
    [Input("dotNetVersion", true)] public required Input<DotNetVersion> DotNetVersion { get; set; }
}

public class AzureFunctionApp : ComponentResource
{
    [Output] public Output<string> ApiUrl { get; set; }

    public AzureFunctionApp(string name, AzureFunctionAppArgs args, ComponentResourceOptions? options = null)
    : base("danielwarddev:PulumiCSharp:AzureFunctionApp", name, args, options)
    {
        // Create a new blob container and blob to hold the ZIP file of the packaged app for the Azure Function
        var blobContainer = new BlobContainer($"{name}-function-zip-container", new BlobContainerArgs
        {
            ResourceGroupName = args.ResourceGroupName,
            AccountName = args.StorageAccountName,
            PublicAccess = PublicAccess.None
        },
        new() { Parent = this });

        var blob = new Blob($"{name}-functionZipBlob", new BlobArgs
        {
            ResourceGroupName = args.ResourceGroupName,
            AccountName = args.StorageAccountName,
            ContainerName = blobContainer.Name,
            Type = BlobType.Block,
            Source = args.PublishPath.Apply(path => new FileArchive(path) as AssetOrArchive) // This will create a .zip for us from the folder
        },
        new() { Parent = this });

        // Get the signed URL for the blob by calling ListStorageAccountServiceSAS on Azure
        var sasToken = ListStorageAccountServiceSAS.Invoke(new ListStorageAccountServiceSASInvokeArgs
        {
            ResourceGroupName = args.ResourceGroupName,
            AccountName = args.StorageAccountName,
            Protocols = HttpProtocol.Https,
            SharedAccessStartTime = "2022-01-01",
            SharedAccessExpiryTime = "2030-01-01",
            Resource = SignedResource.C, // Container
            Permissions = Permissions.R, // Read
            CanonicalizedResource = Output.Format($"/blob/{args.StorageAccountName}/{blobContainer.Name}")
        },
        new() { Parent = this })
            .Apply(result => result.ServiceSasToken);

        var codeBlobUrl = Output.Format($"https://{args.StorageAccountName}.blob.core.windows.net/{blobContainer.Name}/{blob.Name}?{sasToken}");

        // Create an Azure workspace and Application Insights inside of it
        var workspace = new Workspace($"{name}-workspace", new()
        {
            ResourceGroupName = args.ResourceGroupName,
            RetentionInDays = 30,
            Sku = new WorkspaceSkuArgs
            {
                Name = "PerGB2018",
            },
            Features = new WorkspaceFeaturesArgs
            {
                EnableDataExport = true
            }
        },
        new() { Parent = this });

        var appInsights = new Component($"{name}-appInsights", new()
        {
            ResourceGroupName = args.ResourceGroupName,
            ApplicationType = "web",
            Kind = "web",
            WorkspaceResourceId = workspace.Id
        },
        new() { Parent = this });

        var appServicePlan = new AppServicePlan($"{name}-appServicePlan", new AppServicePlanArgs
        {
            ResourceGroupName = args.ResourceGroupName,
            Kind = "Linux",
            Sku = new SkuDescriptionArgs
            {
                Name = "Y1",
                Tier = "Dynamic"
            },
            Reserved = true,
            Location = args.Region
        },
        new() { Parent = this });

        // Use everything above to create the app
        var app = new WebApp($"{name}-my-function-app", new()
        {
            ResourceGroupName = args.ResourceGroupName,
            ServerFarmId = appServicePlan.Id,
            Kind = "FunctionApp",
            SiteConfig = new SiteConfigArgs
            {
                NetFrameworkVersion = args.DotNetVersion.Apply(x => x.NetFrameworkVersion),
                LinuxFxVersion = args.DotNetVersion.Apply(x => x.LinuxFxVersion),
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
                    AllowedOrigins = new[] { "*" }
                },
            },
        },
        new() { Parent = this });

        ApiUrl = Output.Format($"https://{app.DefaultHostName}/api/{args.FunctionName}");

        RegisterOutputs();
    }
}
