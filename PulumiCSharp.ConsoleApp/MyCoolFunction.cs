using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace PulumiCSharp.ConsoleApp;

public class MyCoolFunction
{
    private readonly ILogger<MyCoolFunction> _logger;

    public MyCoolFunction(ILogger<MyCoolFunction> logger)
    {
        _logger = logger;
    }

    [Function("MyCoolFunction")]
    public IActionResult Run([HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequest req)
    {
        _logger.LogInformation("Function called!");
        return new OkObjectResult("Function succeeded!");
    }
}