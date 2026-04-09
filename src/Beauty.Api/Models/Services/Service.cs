namespace Beauty.Api.Models;

public class Service
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public long CategoryId { get; set; }
    public decimal Price { get; set; }
    public int DurationMinutes { get; set; }
    public bool Active { get; set; } = true;
}
