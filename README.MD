# Oracle Fusion Supplier Custom Copilot Connector
This sample shows how to set up and use the Oracle Fusion Custom Connector for supplier data. The connector enables you to integrate data from Oracle Fusion with your Microsoft 365 environment, allowing it to be searchable and available in Microsoft 365 experiences, such as Copilot. 

On each run, the connector connects to Oracle Fusion through its REST API, retrieves the active suppliers and related tables, and then formats that data into items that Microsoft Graph can index. Graph APIs are then called to push these items into your search index. 

It brings together data from multiple tables from Oracle Fusion listed below.

- Suppliers
- Sites
- DFF (Descriptive FlexFields)
- Global DFF
- Business Classification
- Contacts
- Products and Services
- Address

[Oracle API Reference](https://docs.oracle.com/en/cloud/saas/procurement/25a/fapra/op-suppliers-get.html)

## Schema
The custom connector has the following [schema](https://learn.microsoft.com/en-us/graph/connecting-external-content-manage-schema).

| Property | Type | Searchable | Queryable | Retrievable | Refineable | Labels |
|----------|------|------------|-----------|-------------|------------|--------|
|SupplierId|String|Y|Y|Y|N||
|Supplier|String|Y|Y|Y|N|Title|
|Status|String|Y|Y|Y|N||
|BusinessRelationship|String|Y|Y|Y|N||
|TaxOrganizationType|String|Y|Y|Y|N||
|Url|String|N|N|Y|N|Url|
|IconUrl|String|N|N|Y|N|IconUrl|

### Content
Of particular importance is the [Content](https://learn.microsoft.com/en-us/microsoft-365-copilot/extensibility/overview-copilot-connector#copilot-connector-semantic-indexing) field, which along with the 'Title' property, are semantically indexed by Copilot. As Copilot Connectors have a maximum number of 128 properties in the flat schema, it was decided that the tables related to the main Supplier profile table will be concatenated into Content.

For each external item (supplier), the content is formatted as markdown, with a table for each of the related entities. The properties that are currently indexed can be seen below.
1. Sites
    - SupplierSiteId, SupplierSite, ProcurementBUId, ProcurementBU, SupplierAddressName, Email, PayGroup, PaymentTerms
1. DFF
    - ExigerRelationshipStatus, ExigerRiskLevel
1. Global DFF
    - TBD
1. Business Classification
    - Classification, Subclassification, Status, CertifyingAgency, CertificateExpirationDate, Notes
1. Contacts
    - FirstName, LastName, JobTitle, PhoneNumber, Email, Status
1. Products and Services
    - CategoryName, CategoryDescription, CategoryType
1. Address
    - AddressName, Country, AddressLine1-4, City, State, PostalCode, Status, AddressPurposeOrderingFlag, AddressPurposeRemitToFlag, AddressPurposeRFQOrBiddingFlag

## Setup Instructions
These instructions are for how to quickly set up the Oracle Fusion Supplier custom connector.

### Prerequisites
- Oracle Fusion Access. This sample uses OAuth2, so you will need a service account with permissions to read the above tables. You'll need the following later:
    - Oracle Access Token URL: to get the Bearer token
    - Oracle Client ID: service account for reading data
    - Oracle Client Secret: secret for service account
    - Oracle Client Scope: permissions for service account
    - Oracle Supplier URL: url for the suppliers table in your instance
- The .NET SDK installed on your development machine
- You should have a Microsoft work or school account with the Global administrator role.

### Create an App Registration for the Connector
- Sign in to the [Microsoft Entra admin center](entra.microsoft.com)
- Expand **Identity** menu > select **Applications** > **App Registrations** > **New Registration**
- Enter a name for your application, for example, "Oracle Supplier Connector"
- Set Supported account types to **Accounts in this organizational directory only**
- Leave Redirect URI empty
- Select **Register**. On the application's Overview page, copy the value of the **Application (client) ID** and **Directory (tenant) ID** and save them. You'll need these values later.
- Select **API permissions** under **Manage**
- Remove the default ```User.Read``` permission under Configured permissions
- Select **Add a permission**, then **Microsoft Graph**
- Select **Application permissions**
- Select ```ExternalConnection.ReadWrite.OwnedBy``` and ```ExternalItem.ReadWrite.OwnedBy```

### Setup the project
- Clone the project
- Load application secrets and settings using the values from above. 

1. Initialize the Secrets Manager
```
dotnet user-secrets init
```

2. Set the Microsoft Graph settings

```
dotnet user-secrets set settings:clientId <client-id>
dotnet user-secrets set settings:tenantId <tenant-id>
dotnet user-secrets set settings:clientSecret <client-secret>
```
3. Set the Oracle Fusion Settings

```
dotnet user-secrets set settings:oracleAccessTokenUrl <oracleAccessTokenUrl>
dotnet user-secrets set settings:oracleClientId <oracleClientId>
dotnet user-secrets set settings:oracleClientSecret <oracleClientSecret>
dotnet user-secrets set settings:oracleScope <oracleScope>
dotnet user-secrets set settings:oracleSupplierUrl <oracleSupplierUrl>
```

### Run the application
In this step, you'll build and run the sample. This code sample creates a new connection, registers the schema, and then pushes items from your Oracle Fusion endpoint into that connection.
1. Open your command-line interface (CLI) in the OracleFusionSupplierConnector directory.
2. Use the command ```dotnet build``` to build the sample.
3. Use the command ```dotnet run``` to run the sample.
4. **Select 1. Create a connection.** Enter a unique identifier and name for the connection (description is contained in the code and can be easily modified)
5. **Select 4. Register schema for current connection,** and then wait for the operation to complete.
6. **Select 7. Push ALL items to current connection**

## TODO / Next Steps
1. Replace the Url for external items to something that Copilot should reference (directly to supplier profile in Oracle?)
1. Replace the iconUrl with something stable (Oracle Fusion logo?)
1. Improve error handling/retry logic on API calls to Oracle/Microsoft Graph
1. Increase ingestion speeds by parallelizing requests to Oracle/Microsoft Graph
1. Deploy custom connector so not just running locally
    - Make sure to move secrets to a secure storage
1. Testing, testing, testing. This is a POC and additional schema refinements may need to be made before deploying to production.

## Contributors
 - [Matt Howard](https://github.com/Howard-Matthew)
 - Scott Trathen

 ## Version History
 |Version|Date|Comment|
 |--|--|--|
 |1.0|May 29, 2025|Initial Release|


 ## Disclaimer
 **THIS CODE IS PROVIDED *AS IS* WITHOUT WARRANTY OF ANY KIND, EITHER EXPRESS OR IMPLIED, INCLUDING ANY IMPLIED WARRANTIES OF FITNESS FOR A PARTICULAR PURPOSE, MERCHANTABILITY, OR NON-INFRINGEMENT.**
