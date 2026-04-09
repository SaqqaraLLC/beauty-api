namespace Beauty.Api.Models;

public class Location
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string Address1 { get; set; } = "";
    public string City { get; set; } = "";
    public string State { get; set; } = "";
    public string PostalCode { get; set; } = "";
    public string Phone { get; set; } = "";
}
