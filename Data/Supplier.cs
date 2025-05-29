namespace OracleFusionSupplierConnector.Data;

public class Supplier
{
    public string SupplierId { get; set; }
    public string SupplierName { get; set; }
    public string Status { get; set; }
    public string Content { get; set; }
    public string BusinessRelationship { get; set; }
    public string TaxOrganizationType { get; set; }

    public Supplier(string id, string name, string status, string businessRelationship, string taxOrganizationType)
    {
        SupplierId = id;
        SupplierName = name;
        Status = status;
        Content = "";
        BusinessRelationship = businessRelationship;
        TaxOrganizationType = taxOrganizationType;
    }
}