using System.Collections.Generic;
using System.Threading.Tasks;


namespace Beauty.Api.Email
{
    public class EmailOptions
    {
        public string? Provider { get; set; }
        public string? From { get; set; }
        public string? TenantId { get; set; }
        public string? ClientId { get; set; }
        public string? ClientSecret { get; set; }
    }
}
