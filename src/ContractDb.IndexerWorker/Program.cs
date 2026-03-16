using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using ContractDb.IndexerWorker.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices((context, services) =>
    {

        var config = context.Configuration;
        var kvUri = config["KeyVaultUri"];
        if (!string.IsNullOrEmpty(kvUri))
        {
            var secretClient = new SecretClient(new Uri(kvUri), new DefaultAzureCredential());
            var searchEndpoint = secretClient.GetSecret("SearchEndpoint").Value.Value;
            var searchAdminKey = secretClient.GetSecret("SearchAdminKey").Value.Value;
            services.AddSingleton(new SearchConfig(searchEndpoint, searchAdminKey));
        }
        else
        {
            services.AddSingleton(new SearchConfig(
                config["SearchEndpoint"] ?? "",
                config["SearchAdminKey"] ?? ""));
        }

        services.AddSingleton<SearchIndexerClient>();
        services.AddSingleton<SearchVerificationClient>();
        services.AddSingleton<JobStoreClient>();
        services.AddSingleton<RetryPolicy>();
        services.AddHttpClient("SearchAdmin");
    })
    .Build();

await host.RunAsync();

public record SearchConfig(string Endpoint, string AdminKey);
