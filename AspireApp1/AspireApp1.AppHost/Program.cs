var builder = DistributedApplication.CreateBuilder(args);
var apiService = builder.AddProject<Projects.AspireApp1_ApiService>("apiservice");

// https://github.com/SigNoz/signoz/blob/v0.68.0/deploy/docker/clickhouse-setup/docker-compose-minimal.yaml
var zookeeper = builder.AddContainer("signoz-zookeeper", "bitnami/zookeeper", "3.7.1")
    .WithEndpoint(2181, name: "zookeeper-2181", isProxied: false)
    .WithEndpoint(2888, name: "zookeeper-2888", isProxied: false)
    .WithEndpoint(3888, name: "zookeeper-3888", isProxied: false)
    .WithBindMount("Container/data/zookeeper", "/bitnami/zookeeper")
    .WithEnvironment("ALLOW_ANONYMOUS_LOGIN", "yes")
    .WithEnvironment("ZOO_SERVER_ID", "1")
    .WithEnvironment("ZOO_AUTOPURGE_INTERVAL", "1");

var clickhouse = builder.AddContainer("signoz-clickhouse", "clickhouse/clickhouse-server", "24.1.2-alpine")
    .WithEndpoint(8123, 8123)
    .WithEndpoint(9000, 9000)
    .WithEndpoint(9181, 9181)
    .WithBindMount("Config/clickhouse-config.xml", "/etc/clickhouse-server/config.xml")
    .WithBindMount("Config/clickhouse-users.xml", "/etc/clickhouse-server/users.xml")
    .WithBindMount("Config/custom-function.xml", "/etc/clickhouse-server/custom-function.xml")
    .WithBindMount("Config/clickhouse-cluster.xml", "/etc/clickhouse-server/config.d/cluster.xml")
    .WithBindMount("Config/data/clickhouse/", "/var/lib/clickhouse/")
    .WithBindMount("Config/user_scripts", "/var/lib/clickhouse/user_scripts/")
    .WaitFor(zookeeper);
builder.Build().Run();
