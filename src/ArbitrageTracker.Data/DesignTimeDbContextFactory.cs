using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ArbitrageTracker.Data;

public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ArbitrageDbContext>
{
    public ArbitrageDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ArbitrageDbContext>()
            .UseSqlite("Data Source=data/arbitrage.db")
            .Options;
        return new ArbitrageDbContext(options);
    }
}
