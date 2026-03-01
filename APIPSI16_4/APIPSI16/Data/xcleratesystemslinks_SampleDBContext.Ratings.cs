using Microsoft.EntityFrameworkCore;
using APIPSI16.Models;

namespace APIPSI16.Data
{
    public partial class xcleratesystemslinks_SampleDBContext
    {
        public virtual DbSet<Rating> Ratings { get; set; }
    }
}
