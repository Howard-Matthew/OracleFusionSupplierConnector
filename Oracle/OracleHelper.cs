using Microsoft.Graph.Models.ExternalConnectors;
using Microsoft.Graph.Models.ODataErrors;
using RestSharp;
using Newtonsoft.Json.Linq;
using OracleFusionSupplierConnector.Graph;
using OracleFusionSupplierConnector.Data;

namespace OracleFusionSupplierConnector.Oracle;

public static class OracleHelper
{
    private static string? oracleAccessTokenUrl;
    private static string? oracleClientId;
    private static string? oracleClientSecret;
    private static string? oracleScope;
    private static string? oracleSupplierUrl;
    private static string? tenantId;
    private static RestClient? oracleClient;

    public static void Initialize(Settings settings)
    {
        oracleAccessTokenUrl = settings.OracleAccessTokenUrl ?? throw new ArgumentException("OracleAccessTokenUrl is required");
        oracleClientId = settings.OracleClientId ?? throw new ArgumentException("OracleClientId is required");
        oracleClientSecret = settings.OracleClientSecret ?? throw new ArgumentException("OracleClientSecret is required");
        oracleScope = settings.OracleScope ?? throw new ArgumentException("OracleScope is required");
        oracleSupplierUrl = settings.OracleSupplierUrl ?? throw new ArgumentException("OracleSupplierUrl is required");
        tenantId = settings.TenantId ?? throw new ArgumentException("TenantId is required");
        oracleClient = new RestClient();
    }

    public static async Task<string?> GetAccessTokenAsync()
    {
        _ = oracleClient ?? throw new MemberAccessException("oracleClient is null");

        var request = new RestRequest(oracleAccessTokenUrl, Method.Post);
        request.AddHeader("Content-Type", "application/x-www-form-urlencoded");
        request.AddParameter("client_id", oracleClientId);
        request.AddParameter("client_secret", oracleClientSecret);
        request.AddParameter("grant_type", "client_credentials");
        request.AddParameter("scope", oracleScope);

        var response = await oracleClient.ExecuteAsync(request);
        if (response.IsSuccessful)
        {
            var jsonResponse = JObject.Parse(response.Content ?? "{}");
            return jsonResponse["access_token"]?.ToString();
        }

        return null;
    }

    public static async Task GetSupplierDataAsync(bool uploadModifiedOnly, DateTimeOffset? lastUploadTime, string? accessToken, ExternalConnection currentConnection)
    {
        _ = accessToken ?? throw new ArgumentNullException("accessToken cannot be null.");
        _ = currentConnection ?? throw new ArgumentNullException("currentConnection cannot be null.");

        _ = oracleClient ?? throw new MemberAccessException("oracleClient is null");

        var hasMoreResults = true;
        var offset = 0;
        // Optionally limit the number of suppliers to upload (for testing faster)
        var maxSuppliers = int.MaxValue;

        var request = new RestRequest(oracleSupplierUrl, Method.Get);
        request.AddHeader("Authorization", "Bearer " + accessToken);
        request.AddHeader("cache-control", "no-cache");
        // Iterate through 100 at a time
        request.AddParameter("limit", "100", ParameterType.QueryString);
        request.AddParameter("onlyData", "true", ParameterType.QueryString);
        request.AddParameter("totalResults", "true", ParameterType.QueryString);
        // Scope to only get the supplier profile attributes we want
        request.AddParameter("fields", "SupplierId,Supplier,Status,BusinessRelationship,TaxOrganizationType", ParameterType.QueryString);
        var suppliers = new List<Supplier>();

        // Go through all the results.
        while (hasMoreResults && offset < maxSuppliers)
        {
            // Incremental crawl
            if (uploadModifiedOnly)
            {
                // Get only suppliers that have been modified since the last upload time
                request.AddParameter("orderBy", "LastUpdateDate:desc", ParameterType.QueryString);
                request.AddOrUpdateParameter("fields", "SupplierId,Supplier,Status,BusinessRelationship,TaxOrganizationType,LastUpdateDate", ParameterType.QueryString);
            }
            request.AddOrUpdateParameter("offset", offset.ToString(), ParameterType.QueryString);
            Console.WriteLine($"Retrieving suppliers from Oracle Fusion, {offset} - {offset + 100}...");
            RestResponse? response = null;
            try
            {
                response = oracleClient.Execute(request);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Could not get supplier data from Oracle Fusion: " + ex.Message);
            }

            if (response == null)
            {
                Console.WriteLine("Response from Oracle Fusion is null.");
                hasMoreResults = false;
                break;
            }

            var json = JObject.Parse(response.Content ?? throw new Exception("Could not parse response from Oracle Fusion"));
            if (json["items"] == null)
            {
                hasMoreResults = false;
                break;
            }
            else
            {
                hasMoreResults = json["hasMore"]?.ToObject<bool>() ?? false;
                foreach (var supplier in json["items"])
                {
                    Supplier? supplierToAdd = null;
                    var supplierId = supplier["SupplierId"]?.ToObject<string>();
                    if (string.IsNullOrEmpty(supplierId))
                    {
                        // Skip this supplier if SupplierId is missing
                        continue;
                    }
                    if (uploadModifiedOnly)
                    {
                        var lastUpdateDate = supplier["LastUpdateDate"]?.ToObject<string>();
                        if (lastUpdateDate != null && DateTimeOffset.Parse(lastUpdateDate) > lastUploadTime)
                        {

                            supplierToAdd = new Supplier(
                                supplierId,
                                supplier["Supplier"]?.ToObject<string>() ?? string.Empty,
                                supplier["Status"]?.ToObject<string>() ?? string.Empty,
                                supplier["BusinessRelationship"]?.ToObject<string>() ?? string.Empty,
                                supplier["TaxOrganizationType"]?.ToObject<string>() ?? string.Empty);
                        }
                    }
                    else
                    {
                        supplierToAdd = new Supplier(
                            supplierId,
                            supplier["Supplier"]?.ToObject<string>() ?? string.Empty,
                            supplier["Status"]?.ToObject<string>() ?? string.Empty,
                            supplier["BusinessRelationship"]?.ToObject<string>() ?? string.Empty,
                            supplier["TaxOrganizationType"]?.ToObject<string>() ?? string.Empty);
                    }
                    // Get additional data from other tables.
                    if (supplierToAdd != null)
                    {
                        var content = $"# Supplemental information for {supplierToAdd.SupplierName}:\n";
                        Console.WriteLine($"Getting additional data for supplier {supplierToAdd.SupplierName}...");

                        // Add sites description/data
                        content += "## Supplier Sites Data: \n This table captures site-specific data for each supplier, allowing for detailed reasoning about supplier operations, geographic alignment, and procurement eligibility. It supports scenarios where a supplier operates across multiple locations, enabling filtering and analysis based on regional or business unit criteria. This data is especially useful for systems that need to evaluate supplier presence and compliance within specific operational contexts. Users want to know if a Supplier is eligible for procurement in their BU (e.g. Business Unit) as reflected in matching ProcurementBU field.  \n";
                        content += await GetSupplierRelatedData(accessToken, supplierToAdd.SupplierId, "Sites",
                            oracleSupplierUrl + "/" + supplierToAdd.SupplierId + "/child/sites",
                            new List<string> { "SupplierSiteId", "SupplierSite", "ProcurementBUId", "ProcurementBU", "SupplierAddressName", "Email", "PayGroup", "PaymentTerms" });

                        // Add DFF data
                        content += "## Descriptive Flexfields Data: \n This table captures third-party risk intelligence from Exiger, providing structured risk scores and metadata for each supplier. It enables reasoning about supplier risk exposure (e.g. exigerRiskLevel), compliance posture, and mitigation needs (e.g. exigerRelationshipStatus).\n";
                        content += await GetSupplierRelatedData(accessToken, supplierToAdd.SupplierId, "DFF",
                            oracleSupplierUrl + "/" + supplierToAdd.SupplierId + "/child/DFF",
                            new List<string> { "exigerRelationshipStatus", "exigerRiskLevel" });

                        // Add Global DFF data 
                        /*content += "## Global Descriptive Flexfields Data: \n This table contains information about the supplier's global DFF (Descriptive Flexfields), which are additional fields that can be used to capture more information about the supplier." +
                                   "The exigerRelationshipStatus field is a custom field that is used to capture the relationship status of the supplier with Exiger (a risk/compliance firm for vetting suppliers).\n";
                        content += await GetSupplierRelatedData(accessToken, supplierToAdd.SupplierId, "GlobalDFF",
                            oracleSupplierUrl + "/" + supplierToAdd.SupplierId + "/child/globalDFF",
                            new List<string> { "exigerRelationshipStatus" });*/

                        // Add Business Classification data
                        content += "## Business Classifications Data: \n This table contains information about the various business classifications of a supplier. It is used to track diversity certifications, their statuses, and the agencies that issued them. This supports compliance, reporting, and supplier diversity initiatives.\n";
                        content += await GetSupplierRelatedData(accessToken, supplierToAdd.SupplierId, "BusinessClassification",
                            oracleSupplierUrl + "/" + supplierToAdd.SupplierId + "/child/businessClassifications",
                            new List<string> { "Classification", "Subclassification", "Status", "CertifyingAgency", "CertificateExpirationDate", "Notes" });

                        // Add Contacts data
                        content += "## Contacts Data: \n This table contains information about the supplier's contacts, including the contact name (FirstName + LastName), email address, phone number, job title and contact status. This is useful for understanding who is the primary contact at the supplier for various purposes.\n";
                        content += await GetSupplierRelatedData(accessToken, supplierToAdd.SupplierId, "Contacts",
                            oracleSupplierUrl + "/" + supplierToAdd.SupplierId + "/child/contacts",
                            new List<string> { "FirstName", "LastName", "JobTitle", "PhoneNumber", "Email", "Status" });

                        // Add Products and Services data
                        content += "## Products and Services Data: \n This table captures structured information about the offerings of each supplier, enabling the LLM to reason about supplier capabilities, match offerings to business needs, and support procurement decisions. CategoryName identifies the category of Products and Services offered by the Supplier and serves as a semantic anchor for identifying and categorizing supplier capabilities. CategoryDescription is a detailed description of the product or services offered. \n";
                        content += await GetSupplierRelatedData(accessToken, supplierToAdd.SupplierId, "ProductsAndServices",
                            oracleSupplierUrl + "/" + supplierToAdd.SupplierId + "/child/productsAndServices",
                            new List<string> { "CategoryName", "CategoryDescription", "CategoryType" });

                        // Add Address data
                        content += "## Address Data: \n Contains structured location and contact data for each supplier, including address_line_1, address_line_2, city, state, postal_code, and country. This schema enables geolocation, regional compliance checks, and communication routing for supplier entities\n";
                        content += await GetSupplierRelatedData(accessToken, supplierToAdd.SupplierId, "Addresses",
                            oracleSupplierUrl + "/" + supplierToAdd.SupplierId + "/child/addresses",
                            new List<string> { "AddressName", "Country", "AddressLine1", "AddressLine2", "AddressLine3", "AddressLine4", "City", "State", "PostalCode", "Status", "AddressPurposeOrderingFlag", "AddressPurposeRemitToFlag", "AddressPurposeRFQOrBiddingFlag" });

                        supplierToAdd.Content = content;

                        suppliers.Add(supplierToAdd);
                    }
                }
                offset += 100;
            }
        }

        await UploadSuppliersToGraph(suppliers, currentConnection);
    }

    public static async Task<string?> GetSupplierRelatedData(string? accessToken, string? supplierId, string? tableName, string? url, List<string>? fields)
    {
        _ = accessToken ?? throw new ArgumentNullException("accessToken cannot be null.");
        _ = supplierId ?? throw new ArgumentNullException("supplierId cannot be null.");
        _ = fields ?? throw new ArgumentNullException("fields cannot be null.");
        _ = oracleClient ?? throw new MemberAccessException("oracleClient is null");

        var hasMoreResults = true;
        var offset = 0;

        var request = new RestRequest(url, Method.Get);
        request.AddHeader("Authorization", "Bearer " + accessToken);
        request.AddHeader("cache-control", "no-cache");
        // Iterate through 100 at a time
        request.AddParameter("limit", "100", ParameterType.QueryString);
        request.AddParameter("onlyData", "true", ParameterType.QueryString);
        request.AddParameter("totalResults", "true", ParameterType.QueryString);
        // Only get the fields we need
        var fieldsParameter = string.Join(",", fields);
        request.AddParameter("fields", fieldsParameter, ParameterType.QueryString);

        var data = "";
        var allItems = new List<JToken>();

        // retry if there are failures
        int maxRetries = 3;
        int delayMilliseconds = 2000;
        while (hasMoreResults)
        {
            request.AddOrUpdateParameter("offset", offset.ToString(), ParameterType.QueryString);
            int attempt = 0;
            RestResponse? response = null;
            while (attempt < maxRetries)
            {
                try
                {
                    response = await oracleClient.ExecuteAsync(request);
                    if (response.IsSuccessful)
                    {
                        break; // Exit the retry loop if the request was successful
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error getting {tableName} data for {supplierId}: {ex.Message}");
                }

                attempt++;
                if (attempt < maxRetries)
                {
                    Console.WriteLine($"Retrying in {delayMilliseconds} milliseconds...");
                    await Task.Delay(delayMilliseconds);
                }
            }

            var json = JObject.Parse(response.Content ?? throw new Exception("Could not parse response from Oracle Fusion"));
            if (json["items"] == null)
            {
                hasMoreResults = false;
                break;
            }
            else
            {
                hasMoreResults = json["hasMore"]?.ToObject<bool>() ?? false;
                allItems.AddRange(json["items"]);
                offset += 100;
            }
        }

        if (allItems.Count > 0)
        {
            data += ConvertJsonDataToMarkdown(allItems, fields);
        }

        return data;
    }

    public static string ConvertJsonDataToMarkdown(List<JToken> jsonData, List<string>? fields)
    {
        _ = fields ?? throw new ArgumentNullException("fields cannot be null.");
        var markdown = "| " + string.Join(" | ", fields) + " |\n";
        markdown += "| " + string.Join(" | ", Enumerable.Repeat("---", fields.Count)) + " |\n";

        foreach (var item in jsonData)
        {
            var row = "| ";
            foreach (var field in fields)
            {
                var fieldValue = item[field]?.ToObject<string>();
                if (fieldValue != null)
                {
                    row += fieldValue + " | ";
                }
            }
            markdown += row.TrimEnd(' ', '|') + "\n";
        }

        return markdown;
    }

    public static async Task UploadSuppliersToGraph(List<Supplier> suppliers, ExternalConnection currentConnection)
    {
        _ = suppliers ?? throw new ArgumentNullException("suppliers cannot be null.");
        _ = currentConnection ?? throw new ArgumentNullException("currentConnection cannot be null.");
        _ = oracleClient ?? throw new MemberAccessException("oracleClient is null");

        var success = true;
        var oracleSupplierUrl = OracleHelper.oracleSupplierUrl ?? throw new ArgumentException("OracleSupplierUrl is required");
    
        // Upload the suppliers to Microsoft Graph
        Console.WriteLine($"Uploading {suppliers.Count} suppliers to Microsoft Graph...");
        foreach (var supplier in suppliers)
        {
            var newItem = new ExternalItem
            {
                Id = supplier.SupplierId,

                Acl = new List<Acl>
                {
                    new Acl
                    {
                        AccessType = AccessType.Grant,
                        Type = AclType.Everyone,
                        Value = tenantId,
                    }
                },
                Properties = new Properties
                {
                    AdditionalData = new Dictionary<string, object>
                    {
                        { "supplierId", supplier.SupplierId },
                        { "supplier", supplier.SupplierName },
                        { "status", supplier.Status },
                        { "businessRelationship", supplier.BusinessRelationship },
                        { "taxOrganizationType", supplier.TaxOrganizationType },
                        // TODO:  Deep link to the supplier in Oracle Fusion ERP (not service level URL)
                        { "url", oracleSupplierUrl + "/" + supplier.SupplierId },
                        // TODO: replace with Oracle Fusion logo.
                        { "iconUrl", "https://img.icons8.com/?size=100&id=1349&format=png&color=000000"}
                    }
                },
                Content = new ExternalItemContent
                {
                    Type = ExternalItemContentType.Text,
                    Value = supplier.Content
                }
            };

            try
            {
                Console.Write($"Uploading supplier {supplier.SupplierName} with ID: {supplier.SupplierId}...");
                await GraphHelper.AddOrUpdateItemAsync(currentConnection.Id, newItem);
                Console.WriteLine("DONE");
            }
            catch (ODataError odataError)
            {
                success = false;
                Console.WriteLine("FAILED");
                Console.WriteLine($"Error: {odataError.ResponseStatusCode}: {odataError.Error?.Code} {odataError.Error?.Message} {odataError.Error?.Details}");
            }
        }
        ;
        if (success)
        {
            Console.WriteLine($"Successfully uploaded {suppliers.Count} suppliers to the Graph.");
        }
        else
        {
            Console.WriteLine("There were errors uploading suppliers from Oracle Fusion.");
        }
    }
}