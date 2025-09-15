using Pulumi.AzureNative.Resources;
using Pulumi.AzureNative.Storage;
using Pulumi.AzureNative.Storage.Inputs;
using System.Collections.Generic;
using System.IO;
using Pulumi.Command.Local;
using Kind = Pulumi.AzureNative.Storage.Kind;
using StorageAccountArgs = Pulumi.AzureNative.Storage.StorageAccountArgs;
using PulumiCSharp.Infrastructure;

return await Pulumi.Deployment.RunAsync(() =>
{
    var azureConfig = new Pulumi.Config("azure-native");

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

    var location = azureConfig.Require("location");

    var functionApp1 = new AzureFunctionApp("cool-function-1", new()
    {
        Region = azureConfig.Require("location"),
        ResourceGroupName = resourceGroup.Name,
        StorageAccountName = storageAccount.Name,
        FunctionName = "MyCoolFunction",
        PublishPath = "../PulumiCSharp.ConsoleApp/publish",
        DotNetVersion = DotNetVersion.V8
    });

    var functionApp2 = new AzureFunctionApp("cool-function-2", new()
    {
        Region = azureConfig.Require("location"),
        ResourceGroupName = resourceGroup.Name,
        StorageAccountName = storageAccount.Name,
        FunctionName = "MyCoolFunction",
        PublishPath = "../PulumiCSharp.ConsoleApp/publish",
        DotNetVersion = DotNetVersion.V8
    });

    return new Dictionary<string, object?>
    {
        ["coolApp1-apiUrl"] = functionApp1.ApiUrl,
        ["coolApp2-apiUrl"] = functionApp2.ApiUrl
    };
});