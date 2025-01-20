var builder = DistributedApplication.CreateBuilder(args);

var apiService = builder.AddProject<Projects.AspireApp1_ApiService>("apiservice");

builder.AddProject<Projects.AspireApp1_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithReference(apiService)
    .WaitFor(apiService);

// https://github.com/SigNoz/signoz/blob/v0.68.0/deploy/docker/clickhouse-setup/docker-compose-minimal.yaml

builder.AddContainer("clickhouse", "clickhouse/clickhouse-server", "24.1.2-alpine")
    .WithEndpoint(8123, 8123)
    .WithEndpoint(9000, 9000)
    .WithEndpoint(9181, 9181);

    

builder.AddContainer("zookeeper", "bitnami/zookeepe", "3.7.1")
    .WithEndpoint(2181, 2181)
    .WithEndpoint(2888, 2888)
    .WithEndpoint(3888, 3888)
    .WithVolume("/data/zookeeper", "/bitnami/zookeeper")
    .WithEnvironment("ALLOW_ANONYMOUS_LOGIN", "yes")
    .WithEnvironment("ZOO_SERVER_ID", "1")
    .WithEnvironment("ZOO_AUTOPURGE_INTERVAL", "1");

builder.Build().Run();
