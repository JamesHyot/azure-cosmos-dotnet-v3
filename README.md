# Microsoft Azure Cosmos DB .NET SDK Version 3.0

This client library enables client applications to connect to Azure Cosmos via the SQL API. Azure Cosmos is a globally distributed, multi-model database service. For more information, refer to https://azure.microsoft.com/services/cosmos-db/.

```csharp
CosmosClient client = new CosmosClient("https://mycosmosaccount.documents.azure.com:443/", "mysupersecretkey");
Database database = await client.CreateDatabaseIfNotExistsAsync("MyDatabaseName");
Container container = await database.CreateContainerIfNotExistsAsync(
    "MyContainerName",
    "/partitionKeyPath",
    400);

dynamic testItem = new { id = "MyTestItemId", partitionKeyPath = "MyTestPkValue", details = "it's working" };
ItemResponse<dynamic> response = await container.CreateItemAsync(testItem);
```

## Install via [Nuget.org](https://www.nuget.org/packages/Microsoft.Azure.Cosmos/)

`Install-Package Microsoft.Azure.Cosmos`

## Useful links

- [Get Started APP](https://docs.microsoft.com/azure/cosmos-db/sql-api-get-started)
- [Github samples](https://github.com/Azure/azure-cosmos-dotnet-v3/tree/master/Microsoft.Azure.Cosmos.Samples/CodeSamples)
- [MultiMaster samples](https://github.com/markjbrown/azure-cosmosdb-dotnet/tree/master/samples/MultiMaster)
- [Resource Model of Azure Cosmos DB Service](https://docs.microsoft.com/azure/cosmos-db/sql-api-resources)
- [Cosmos DB Resource URI](https://docs.microsoft.com/rest/api/documentdb/documentdb-resource-uri-syntax-for-rest)
- [Partitioning](https://docs.microsoft.com/azure/cosmos-db/partition-data)
- [Introduction to SQL API of Azure Cosmos DB Service](https://docs.microsoft.com/azure/cosmos-db/sql-api-sql-query)
- [SDK API](https://docs.microsoft.com/dotnet/api/microsoft.azure.cosmos?view=azure-dotnet)
- [Using emulator](https://github.com/Azure/azure-documentdb-dotnet/blob/master/docs/documentdb-nosql-local-emulator.md)
- [Capture traces](https://github.com/Azure/azure-documentdb-dotnet/blob/master/docs/documentdb-sdk_capture_etl.md)
- [Release notes](https://github.com/Azure/azure-cosmos-dotnet-v3/blob/master/changelog.md)
- [Diagnose and troubleshooting](https://docs.microsoft.com/azure/cosmos-db/troubleshoot-dot-net-sdk)

## Contributing

This project welcomes contributions and suggestions.  Most contributions require you to agree to a
Contributor License Agreement (CLA) declaring that you have the right to, and actually do, grant us
the rights to use your contribution. For details, visit https://cla.microsoft.com.

When you submit a pull request, a CLA-bot will automatically determine whether you need to provide
a CLA and decorate the PR appropriately (e.g., label, comment). Simply follow the instructions
provided by the bot. You will only need to do this once across all repos using our CLA.

This project has adopted the [Microsoft Open Source Code of Conduct](https://opensource.microsoft.com/codeofconduct/).
For more information see the [Code of Conduct FAQ](https://opensource.microsoft.com/codeofconduct/faq/) or
contact [opencode@microsoft.com](mailto:opencode@microsoft.com) with any additional questions or comments.

