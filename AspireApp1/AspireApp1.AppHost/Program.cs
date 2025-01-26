var builder = DistributedApplication.CreateBuilder(args);

// https://github.com/SigNoz/signoz/blob/v0.68.0/deploy/docker/clickhouse-setup/docker-compose-minimal.yaml
var zookeeper = builder.AddContainer("zookeeper-1", "bitnami/zookeeper", "3.7.1")
    .WithEndpoint(2181, name: "zookeeper-2181", isProxied: false)
    .WithEndpoint(2888, name: "zookeeper-2888", isProxied: false)
    .WithEndpoint(3888, name: "zookeeper-3888", isProxied: false)
    .WithBindMount("Container/data/zookeeper", "/bitnami/zookeeper")
    .WithEnvironment("ALLOW_ANONYMOUS_LOGIN", "yes")
    .WithEnvironment("ZOO_SERVER_ID", "1")
    .WithEnvironment("ZOO_AUTOPURGE_INTERVAL", "1");

var clickhouse = builder.AddContainer("clickhouse", "clickhouse/clickhouse-server", "24.1.2-alpine")
    .WithEndpoint(8123, name: "clickhouse-8123", isProxied: false)
    .WithEndpoint(9000, name: "clickhouse-9000", isProxied: false)
    .WithEndpoint(9181, name: "clickhouse-9181", isProxied: false)
    .WithBindMount("Container/config/clickhouse/clickhouse-config.xml", "/etc/clickhouse-server/config.xml")
    .WithBindMount("Container/config/clickhouse/clickhouse-users.xml", "/etc/clickhouse-server/users.xml")
    .WithBindMount("Container/config/clickhouse/custom-function.xml", "/etc/clickhouse-server/custom-function.xml")
    .WithBindMount("Container/config/clickhouse/clickhouse-cluster.xml", "/etc/clickhouse-server/config.d/cluster.xml")
    .WithBindMount("Container/config/clickhouse/user_scripts/", "/var/lib/clickhouse/user_scripts/")
    .WithBindMount("Container/data/clickhouse/", "/var/lib/clickhouse/")
    .WaitFor(zookeeper);

var otelMigratorSync = builder.AddContainer("otel-collector-migrator-sync", "signoz/signoz-schema-migrator", "0.111.23")
    .WithArgs("sync", "--dsn=tcp://clickhouse:9000", "--up=")
    .WithReference(clickhouse.GetEndpoint("clickhouse-9000"))
    .WaitFor(clickhouse);

var otelMigratorAsync = builder.AddContainer("otel-collector-migrator-async", "signoz/signoz-schema-migrator", "0.111.23")
    .WithArgs("async", "--dsn=tcp://clickhouse:9000", "--up=")
    .WithReference(clickhouse.GetEndpoint("clickhouse-9000"))
    .WaitForCompletion(otelMigratorSync, 0);

var queryService = builder.AddContainer("query-service", "signoz/query-service", "0.68.0")
    .WithArgs("-config=/root/config/prometheus.yml", "--use-logs-new-schema=true",
        "--use-trace-new-schema=true")
    .WithBindMount("Container/config/queryservice/prometheus.yml", "/root/config/prometheus.yml")
    .WithBindMount("Container/config/queryservice/dashboards", "/root/config/dashboards")
    .WithBindMount("Container/data/queryservice/", "/var/lib/signoz/")
    .WithEnvironment("ClickHouseUrl", "tcp://clickhouse:9000")
    .WithEnvironment("ALERTMANAGER_API_PREFIX", "http://alertmanager:9093/api/")
    .WithEnvironment("SIGNOZ_LOCAL_DB_PATH", "/var/lib/signoz/signoz.db")
    .WithEnvironment("DASHBOARDS_PATH", "/root/config/dashboards")
    .WithEnvironment("STORAGE", "clickhouse")
    .WithEnvironment("GODEBUG", "netdns=go")
    .WithEnvironment("TELEMETRY_ENABLED", "true")
    .WithEnvironment("DEPLOYMENT_TYPE", "docker-standalone-amd")
    .WithHttpEndpoint(8085, name: "query-service-8085", isProxied: false)
    .WithReference(clickhouse.GetEndpoint("clickhouse-9000"))
    .WaitFor(clickhouse)
    .WaitFor(otelMigratorSync);

var frontend = builder.AddContainer("frontend", "signoz/frontend", "0.68.0")
    .WithHttpEndpoint(3301, name: "frontend-3301", isProxied: false)
    .WithBindMount("Container/config/frontend/nginx-config.conf", "/etc/nginx/conf.d/default.conf")
    .WithReference(queryService.GetEndpoint("query-service-8085"))
    .WaitFor(queryService);

var alertManager = builder.AddContainer("alertmanger", "signoz/alertmanager", "0.23.7")
    .WithArgs("--queryService.url=http://query-service:8085", "--storage.path=/data")
    .WithEndpoint(9093, name: "alertmanager-9093", isProxied: false)
    .WithBindMount("Container/data/alertmanager", "/data")
    .WithReference(queryService.GetEndpoint("query-service-8085"))
    .WaitFor(queryService)
    .WaitFor(clickhouse);

queryService.WithReference(alertManager.GetEndpoint("alertmanager-9093"));

var otelCollector = builder.AddContainer("otel-collector", "signoz/signoz-otel-collector", "0.111.23")
    .WithContainerRuntimeArgs("--user=0")
    .WithArgs("--config=/etc/otel-collector-config.yaml", "--manager-config=/etc/manager-config.yaml",
        "--copy-path=/var/tmp/collector-config.yaml", "--feature-gates=-pkg.translator.prometheus.NormalizeName")
    .WithBindMount("Container/config/otel-collector/otel-collector-config.yaml", "/etc/otel-collector-config.yaml")
    .WithBindMount("Container/config/otel-collector/otel-collector-opamp-config.yaml", "/etc/manager-config.yaml")
    .WithBindMount("/var/lib/docker/containers", "/var/lib/docker/containers", true)
    .WithBindMount("/", "/hostfs", true)
    .WithEnvironment("OTEL_RESOURCE_ATTRIBUTES", "host.name=signoz-host,os.type=linux")
    .WithEnvironment("LOW_CARDINAL_EXCEPTION_GROUPING", "false")
    .WithEndpoint(4317, name: "otel-collector-4317", isProxied: false)
    .WithHttpEndpoint(4318, name: "otel-collector-4318", isProxied: false)
    .WithReference(clickhouse.GetEndpoint("clickhouse-9000"))
    .WaitFor(clickhouse)
    .WaitFor(queryService)
    .WaitForCompletion(otelMigratorSync, 0);

var apiService = builder.AddProject<Projects.AspireApp1_ApiService>("apiservice")
    .WithReference(otelCollector.GetEndpoint("otel-collector-4318"))
    .WithEnvironment("OTEL_SERVICE_NAME", "api-service")
    .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", "http://otel-collector:4318")
    .WaitFor(otelCollector);

builder.AddProject<Projects.AspireApp1_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithReference(apiService)
    .WithReference(otelCollector.GetEndpoint("otel-collector-4318"))
    .WithEnvironment("OTEL_SERVICE_NAME", "webfrontend")
    .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", "http://otel-collector:4318")
    .WaitFor(apiService)
    .WaitFor(frontend)
    .WaitFor(otelCollector);

builder.Build().Run();