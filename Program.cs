using System.Text.Json;
using Microsoft.Graph;
using Microsoft.Graph.Models.ExternalConnectors;
using Microsoft.Graph.Models.ODataErrors;
using OracleFusionSupplierConnector;
using OracleFusionSupplierConnector.Graph;
using OracleFusionSupplierConnector.Oracle;

Console.WriteLine("Oracle Fusion Supplier Copilot Connector\n");

var settings = Settings.LoadSettings();

InitializeGraph(settings);

ExternalConnection? currentConnection = null;
int choice = -1;

while (choice != 0)
{
    Console.WriteLine($"Current connection: {(currentConnection == null ? "NONE" : currentConnection.Name)}\n");
    Console.WriteLine("Please choose one of the following options:");
    Console.WriteLine("0. Exit");
    Console.WriteLine("1. Create a connection");
    Console.WriteLine("2. Select an existing connection");
    Console.WriteLine("3. Delete current connection");
    Console.WriteLine("4. Register schema for current connection");
    Console.WriteLine("5. View schema for current connection");
    Console.WriteLine("6. Push updated items to current connection");
    Console.WriteLine("7. Push ALL items to current connection");
    Console.Write("Selection: ");

    try
    {
        choice = int.Parse(Console.ReadLine() ?? string.Empty);
    }
    catch (FormatException)
    {
        // Set to invalid value
        choice = -1;
    }

    switch(choice)
    {
        case 0:
            // Exit the program
            Console.WriteLine("Goodbye...");
            break;
        case 1:
            currentConnection = await CreateConnectionAsync();
            break;
        case 2:
            currentConnection = await SelectExistingConnectionAsync();
            break;
        case 3:
            await DeleteCurrentConnectionAsync(currentConnection);
            currentConnection = null;
            break;
        case 4:
            await RegisterSchemaAsync();
            break;
        case 5:
            await GetSchemaAsync();
            break;
        case 6:
            await UpdateItemsFromOracleAsync(true);
            break;
        case 7:
            await UpdateItemsFromOracleAsync(false);
            break;
        default:
            Console.WriteLine("Invalid choice! Please try again.");
            break;
    }
}

static string? PromptForInput(string prompt, bool valueRequired)
{
    string? response;

    do
    {
        Console.WriteLine($"{prompt}:");
        response = Console.ReadLine();
        if (valueRequired && string.IsNullOrEmpty(response))
        {
            Console.WriteLine("You must provide a value");
        }
    } while (valueRequired && string.IsNullOrEmpty(response));

    return response;
}

static DateTimeOffset GetLastUploadTime()
{
    if (File.Exists("lastuploadtime.bin"))
    {
        return DateTimeOffset.Parse(File.ReadAllText("lastuploadtime.bin"));
    }

    return DateTime.MinValue;
}

static void SaveLastUploadTime(DateTimeOffset uploadTime)
{
    File.WriteAllText("lastuploadtime.bin", uploadTime.ToString("o"));
}

void InitializeGraph(Settings settings)
{
    try
    {
        GraphHelper.Initialize(settings);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error initializing Graph: {ex.Message}");
    }
}

async Task<ExternalConnection?> CreateConnectionAsync()
{
    var connectionId = PromptForInput(
        "Enter a unique ID for the new connection (3-32 characters)", true) ?? "ConnectionId";
    var connectionName = PromptForInput(
        "Enter a name for the new connection", true) ?? "ConnectionName";
    var connectionDescription = "Structured supplier entity schema from Oracle Supplier Management, encompassing core profile attributes, business classifications, " +
        "contact hierarchies, and procurement-relevant metadata, optimized for AI reasoning and role-based access access. This schema reflects core profile attributes, " +
        "business classifications, contact hierarchies, and procurement-relevant metadata, formatted in a modular and structured way to support intelligent operations in Copilot. ";

    try
    {
        // Create the connection
        var connection = await GraphHelper.CreateConnectionAsync(
            connectionId, connectionName, connectionDescription);
        Console.WriteLine($"New connection created - Name: {connection?.Name}, Id: {connection?.Id}");
        return connection;
    }
    catch (ODataError odataError)
    {
        Console.WriteLine($"Error creating connection: {odataError.ResponseStatusCode}: {odataError.Error?.Code} {odataError.Error?.Message}");
        return null;
    }
}

async Task<ExternalConnection?> SelectExistingConnectionAsync()
{
    Console.WriteLine("Getting existing connections...");
    try
    {
        var response = await GraphHelper.GetExistingConnectionsAsync();
        var connections = response?.Value ?? new List<ExternalConnection>();
        if (connections.Count <= 0)
        {
            Console.WriteLine("No connections exist. Please create a new connection");
            return null;
        }

        // Display connections
        Console.WriteLine("Choose one of the following connections:");
        var menuNumber = 1;
        foreach(var connection in connections)
        {
            Console.WriteLine($"{menuNumber++}. {connection.Name}");
        }

        ExternalConnection? selection = null;

        do
        {
            try
            {
                Console.Write("Selection: ");
                var choice = int.Parse(Console.ReadLine() ?? string.Empty);
                if (choice > 0 && choice <= connections.Count)
                {
                    selection = connections[choice - 1];
                }
                else
                {
                    Console.WriteLine("Invalid choice.");
                }
            }
            catch (FormatException)
            {
                Console.WriteLine("Invalid choice.");
            }
        } while (selection == null);

        return selection;
    }
    catch (ODataError odataError)
    {
        Console.WriteLine($"Error getting connections: {odataError.ResponseStatusCode}: {odataError.Error?.Code} {odataError.Error?.Message}");
        return null;
    }
}

async Task DeleteCurrentConnectionAsync(ExternalConnection? connection)
{
    if (connection == null)
    {
        Console.WriteLine(
            "No connection selected. Please create a new connection or select an existing connection.");
        return;
    }

    try
    {
        await GraphHelper.DeleteConnectionAsync(connection.Id);
        Console.WriteLine($"{connection.Name} deleted successfully.");
    }
    catch (ODataError odataError)
    {
        Console.WriteLine($"Error deleting connection: {odataError.ResponseStatusCode}: {odataError.Error?.Code} {odataError.Error?.Message}");
    }
}

async Task RegisterSchemaAsync()
{
    if (currentConnection == null)
    {
        Console.WriteLine("No connection selected. Please create a new connection or select an existing connection.");
        return;
    }

    Console.WriteLine("Registering schema, this may take a moment...");

    try
    {
        // Create the schema
        var schema = new Schema
        {
            BaseType = "microsoft.graph.externalItem",
            Properties = new List<Property>
            {
                // Supplier profile properties
                new Property { Name = "supplierId", Type = PropertyType.String, IsQueryable = true, IsSearchable = true, IsRetrievable = true, IsRefinable = false },
                new Property { Name = "supplier", Type = PropertyType.String, IsQueryable = true, IsSearchable = true, IsRetrievable = true, IsRefinable = false, Labels = new List<Label?>() { Label.Title }},
                new Property { Name = "status", Type = PropertyType.String, IsQueryable = true, IsSearchable = true, IsRetrievable = true, IsRefinable = false },
                new Property { Name = "businessRelationship", Type = PropertyType.String, IsQueryable = true, IsSearchable = true, IsRetrievable = true, IsRefinable = false },
                new Property { Name = "taxOrganizationType", Type = PropertyType.String, IsQueryable = true, IsSearchable = true, IsRetrievable = true, IsRefinable = false },
                // Reference properties
                new Property { Name = "url", Type = PropertyType.String, IsQueryable = false, IsSearchable = false, IsRetrievable = true, IsRefinable = false, Labels = new List<Label?>() { Label.Url } },
                new Property { Name = "iconUrl", Type = PropertyType.String, IsQueryable = false, IsSearchable = false, IsRetrievable = true, IsRefinable = false, Labels = new List<Label?>() { Label.IconUrl } },
            },
        };

        await GraphHelper.RegisterSchemaAsync(currentConnection.Id, schema);
        Console.WriteLine("Schema registered successfully");
    }
    catch (ServiceException serviceException)
    {
        Console.WriteLine($"Error registering schema: {serviceException.ResponseStatusCode} {serviceException.Message}");
    }
    catch (ODataError odataError)
    {
        Console.WriteLine($"Error registering schema: {odataError.ResponseStatusCode}: {odataError.Error?.Code} {odataError.Error?.Message}");
    }
}

async Task GetSchemaAsync()
{
    if (currentConnection == null)
    {
        Console.WriteLine("No connection selected. Please create a new connection or select an existing connection.");
        return;
    }

    try
    {
        var schema = await GraphHelper.GetSchemaAsync(currentConnection.Id);
        Console.WriteLine(JsonSerializer.Serialize(schema));

    }
    catch (ODataError odataError)
    {
        Console.WriteLine($"Error getting schema: {odataError.ResponseStatusCode}: {odataError.Error?.Code} {odataError.Error?.Message}");
    }
}

async Task UpdateItemsFromOracleAsync(bool uploadModifiedOnly)
{
    if (currentConnection == null)
    {
        Console.WriteLine("No connection selected. Please create a new connection or select an existing connection.");
        return;
    }

    try
    {
        // Create Oracle client
        Console.WriteLine("Creating Oracle client...");
        OracleHelper.Initialize(settings);

        // Get OAuth2 access token
        Console.WriteLine("Getting OAuth2 access token...");
        var accessToken = await OracleHelper.GetAccessTokenAsync() ?? throw new Exception("Could not get access token from Oracle Fusion");

        // Get suppliers from Oracle Fusion
        Console.WriteLine("Getting suppliers from Oracle Fusion...");

        var newUploadTime = DateTimeOffset.UtcNow.ToString("o");
        var lastUploadTime = GetLastUploadTime();
        Console.WriteLine("New upload time: " + newUploadTime);
        await OracleHelper.GetSupplierDataAsync(uploadModifiedOnly, lastUploadTime, accessToken, currentConnection);
        SaveLastUploadTime(DateTimeOffset.Parse(newUploadTime));
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error calling Oracle: {ex.Message}");
    }
}

