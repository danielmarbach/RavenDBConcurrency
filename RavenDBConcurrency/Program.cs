// See https://aka.ms/new-console-template for more information

using Raven.Client.Documents;
using Raven.Client.Documents.Session;
using Raven.Client.ServerWide;
using Raven.Client.ServerWide.Operations;

bool useClusterWideTransactions = false;
string urls = useClusterWideTransactions
    ? Environment.GetEnvironmentVariable("CommaSeparatedRavenClusterUrls") ?? "http://localhost:8081,http://localhost:8082,http://localhost:8083"
    : Environment.GetEnvironmentVariable("RavenSingleNodeUrl") ?? "http://localhost:8080";

var dbName = Path.GetFileNameWithoutExtension(Path.GetTempFileName());
Console.WriteLine(dbName);
var documentStore = new DocumentStore
{
    Urls = urls.Split(','),
    Database = dbName
};
documentStore.Initialize();
var dbRecord = new DatabaseRecord(dbName);
await documentStore.Maintenance.Server.SendAsync(new CreateDatabaseOperation(dbRecord));

var someDataId = Guid.NewGuid();
var id = $"ToStore/{someDataId}";
using (var saveSession = documentStore.OpenAsyncSession())
{
    var someId = Guid.NewGuid().ToString();

    var someData = new SomeData
    {
        Id = someDataId,
        SomeId = someId,
        DateTimeProperty = DateTime.UtcNow,
    };
    var toStore = new ToStore
    {
        Id = id,
        IdentityDocId = Guid.NewGuid().ToString(),
        Data = someData
    };
    await saveSession.StoreAsync(toStore);
    await saveSession.StoreAsync(new UniqueIdentity
    {
        Id = toStore.IdentityDocId,
        SagaId = someData.Id,
        UniqueValue = someId,
        SagaDocId = toStore.Id
    }, id: toStore.IdentityDocId);
    var metadata = saveSession.Advanced.GetMetadataFor(toStore);
    metadata["SchemaVersion"] = " 1.0.0";
    await saveSession.SaveChangesAsync();
}

IAsyncDocumentSession loosingSession;
ToStore loosingSessionData;
using (var winningSession = documentStore.OpenAsyncSession())
{
    winningSession.Advanced.UseOptimisticConcurrency = true;
    
    loosingSession = documentStore.OpenAsyncSession();
    loosingSession.Advanced.UseOptimisticConcurrency = true;
    
    var winningSessionData = await winningSession.LoadAsync<ToStore>(id);
    var winningSessionDataMetadata = winningSession.Advanced.GetMetadataFor(winningSessionData);
    winningSessionDataMetadata["SchemaVersion"] = "1.0.0";
    loosingSessionData = await loosingSession.LoadAsync<ToStore>(id);
    var loosingSessionDataMetadata = loosingSession.Advanced.GetMetadataFor(loosingSessionData);
    loosingSessionDataMetadata["SchemaVersion"] = "1.0.0";
    winningSessionData.Data.DateTimeProperty = DateTime.UtcNow.AddHours(1);
    await winningSession.SaveChangesAsync();
}

// loosingSessionData.Data.DateTimeProperty = DateTime.UtcNow.AddHours(1);
await loosingSession.SaveChangesAsync();
loosingSession.Dispose();

class ToStore
{
    public string Id { get; set; }
    public string IdentityDocId { get; set; }

    public SomeData Data { get; set; }
}

class SomeData
{
    public string SomeId { get; set; } = "Test";

    public DateTime DateTimeProperty { get; set; }
    public Guid Id { get; set; }
}

class UniqueIdentity
{
    public string Id { get; set; }
    public Guid SagaId { get; set; }
    public object UniqueValue { get; set; }
    public string SagaDocId { get; set; }
}