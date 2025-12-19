using Pulumi;
using Pulumi.Command.Local;
using PulumiCSharp.Infrastructure.ConsoleApp;
using System.Collections.Generic;
using System.IO;
using System.Linq;

return await Pulumi.Deployment.RunAsync(() =>
{
    var config = new Config();

    var foundationOrgName = config.Require("foundationOrgName");
    var foundationProjectName = config.Require("foundationProjectName");
    var foundationStackName = config.Require("foundationStackName");

    var foundationStack = new StackReference($"{foundationOrgName}/{foundationProjectName}/{foundationStackName}");
    var foundationResourceGroupName = foundationStack.RequireOutput("resourceGroupName").Apply(x => (string)x);
    var foundationStorageAccountName = foundationStack.RequireOutput("storageAccountName").Apply(x => (string)x);

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

    var functionCount = config.RequireInt32("function-count");
    var functionApps = new List<AzureFunctionApp>();

    for (int i = 0; i < functionCount; i++)
    {
        var app = new AzureFunctionApp($"cool-function-{i + 1}", new()
        {
            ResourceGroupName = foundationResourceGroupName,
            StorageAccountName = foundationStorageAccountName,
            FunctionName = "MyCoolFunction",
            PublishPath = "../PulumiCSharp.ConsoleApp/publish",
            DotNetVersion = DotNetVersion.V8
        });
        functionApps.Add(app);
    }

    var functionUrls = Output
        .All(functionApps.Select(app => app.ApiUrl))
        .Apply(urls => urls.ToArray());

    return new Dictionary<string, object?>
    {
        ["functionAppApiUrls"] = functionUrls
    };
});


