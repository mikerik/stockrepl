using Microsoft.EntityFrameworkCore;
using StockReplenishment.Api.Domain;
using StockReplenishment.Contracts.Requests;

namespace StockReplenishment.Api.Data;

/// <summary>
/// Seeds a realistic data set covering every workflow state. Requests are built through the domain methods,
/// so the seeded history and stock check data are consistent with what the application itself produces.
/// </summary>
public static class DemoDataSeeder
{
    public static readonly string[] Workers = ["Anna Lindqvist", "Johan Berg", "Maria Nilsson", "Peter Svensson"];

    public static readonly string[] Reviewers = ["Erik Johansson", "Sara Holm"];

    private enum SeedTarget
    {
        Draft,
        AwaitingCheck,
        CheckCompleted,
        CheckFailed,
        Approved,
        Rejected,
        Fulfilled,
    }

    // Two requests are left "AwaitingCheck": the background worker picks them up on startup.
    private static readonly SeedTarget[] Targets =
    [
        SeedTarget.Fulfilled, SeedTarget.Fulfilled, SeedTarget.Rejected, SeedTarget.Fulfilled, SeedTarget.Approved,
        SeedTarget.Fulfilled, SeedTarget.Rejected, SeedTarget.Fulfilled, SeedTarget.Approved, SeedTarget.Fulfilled,
        SeedTarget.Rejected, SeedTarget.Approved, SeedTarget.CheckCompleted, SeedTarget.Fulfilled, SeedTarget.CheckFailed,
        SeedTarget.Approved, SeedTarget.Rejected, SeedTarget.CheckCompleted, SeedTarget.Draft, SeedTarget.CheckCompleted,
        SeedTarget.Approved, SeedTarget.CheckFailed, SeedTarget.Draft, SeedTarget.CheckCompleted, SeedTarget.Draft,
        SeedTarget.CheckCompleted, SeedTarget.AwaitingCheck, SeedTarget.Draft, SeedTarget.AwaitingCheck, SeedTarget.Draft,
    ];

    private static readonly string[] RejectionReasons =
    [
        "Duplicate of an open request for the same station.",
        "Quantity exceeds the station's max stock level; please reduce.",
        "Material is being phased out - use the replacement article.",
        "Station is scheduled for a line changeover this week.",
    ];

    public static async Task SeedAsync(AppDbContext db, TimeProvider timeProvider, CancellationToken cancellationToken = default)
    {
        if (await db.Locations.AnyAsync(cancellationToken))
        {
            return;
        }

        var random = new Random(20261016);
        var now = timeProvider.GetUtcNow().UtcDateTime;

        var locations = CreateLocations();
        var materials = CreateMaterials();
        db.Locations.AddRange(locations);
        db.Materials.AddRange(materials);

        var requests = new List<(ReplenishmentRequest Request, SeedTarget Target, DateTime CreatedAt)>();
        for (var i = 0; i < Targets.Length; i++)
        {
            var createdAt = now.AddHours(-(Targets.Length - i) * 9).AddMinutes(-random.Next(0, 300));
            var lines = materials
                .OrderBy(_ => random.Next())
                .Take(random.Next(1, 5))
                .Select(m => new RequestLineSpec(m, m.UnitOfMeasure is "kg" or "L" ? random.Next(5, 60) : random.Next(1, 40) * 10))
                .ToList();

            var request = ReplenishmentRequest.CreateDraft(
                locations[random.Next(locations.Count)],
                (RequestPriority)random.Next(0, 3),
                i % 4 == 0 ? "Running low before the next shift." : null,
                lines,
                Workers[random.Next(Workers.Length)],
                createdAt);

            db.Requests.Add(request);
            requests.Add((request, Targets[i], createdAt));
        }

        // Line ids are needed for fulfilment, so the drafts are persisted first.
        await db.SaveChangesAsync(cancellationToken);

        foreach (var (request, target, createdAt) in requests)
        {
            AdvanceTo(request, target, createdAt, random);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static void AdvanceTo(ReplenishmentRequest request, SeedTarget target, DateTime createdAt, Random random)
    {
        if (target == SeedTarget.Draft)
        {
            return;
        }

        var reviewer = Reviewers[random.Next(Reviewers.Length)];
        var submittedAt = createdAt.AddMinutes(random.Next(5, 60));
        request.Submit(request.RequestedBy, submittedAt);

        if (target == SeedTarget.AwaitingCheck)
        {
            return;
        }

        var checkedAt = submittedAt.AddSeconds(random.Next(3, 9));
        if (target == SeedTarget.CheckFailed)
        {
            request.FailStockCheck("Stock check failed after 3 attempt(s): The warehouse stock service returned 503 Service Unavailable.", checkedAt);
            return;
        }

        // Approved/fulfilled requests were (mostly) fully available; the open ones show a mix.
        var mostlyAvailable = target is SeedTarget.Approved or SeedTarget.Fulfilled;
        var availability = request.Lines.ToDictionary(
            l => l.Material.ArticleNumber,
            l => mostlyAvailable || random.Next(3) > 0 ? l.RequestedQuantity * random.Next(2, 10) : Math.Floor(l.RequestedQuantity / 2));
        request.CompleteStockCheck(availability, checkedAt);

        if (target == SeedTarget.CheckCompleted)
        {
            return;
        }

        var reviewedAt = checkedAt.AddMinutes(random.Next(10, 240));
        if (target == SeedTarget.Rejected)
        {
            request.Reject(reviewer, RejectionReasons[random.Next(RejectionReasons.Length)], reviewedAt);
            return;
        }

        request.Approve(reviewer, random.Next(2) == 0 ? "OK, deliver with the next milk run." : null, reviewedAt);

        if (target == SeedTarget.Fulfilled)
        {
            var fulfilled = request.Lines.ToDictionary(
                l => l.Id,
                l => random.Next(5) == 0 ? Math.Floor(l.RequestedQuantity * 0.8m) : l.RequestedQuantity);
            request.Fulfill("Warehouse", fulfilled, reviewedAt.AddHours(random.Next(1, 6)));
        }
    }

    private static List<StockLocation> CreateLocations() =>
    [
        new() { Code = "ASM1-ST01", Name = "Assembly Line 1 - Station 1", Area = "Assembly" },
        new() { Code = "ASM1-ST04", Name = "Assembly Line 1 - Station 4", Area = "Assembly" },
        new() { Code = "ASM2-ST02", Name = "Assembly Line 2 - Station 2", Area = "Assembly" },
        new() { Code = "WLD-C03", Name = "Welding Cell 3", Area = "Welding" },
        new() { Code = "PNT-B01", Name = "Paint Booth 1", Area = "Paint Shop" },
        new() { Code = "PKG-01", Name = "Packaging Station 1", Area = "Packaging" },
        new() { Code = "QA-LAB", Name = "Quality Lab", Area = "Quality" },
    ];

    private static List<Material> CreateMaterials() =>
    [
        new() { ArticleNumber = "100-2045", Description = "Hex bolt M8x30, zinc plated", UnitOfMeasure = "pcs" },
        new() { ArticleNumber = "100-2046", Description = "Hex nut M8, zinc plated", UnitOfMeasure = "pcs" },
        new() { ArticleNumber = "100-3310", Description = "Washer M8 DIN 125", UnitOfMeasure = "pcs" },
        new() { ArticleNumber = "220-1001", Description = "Cable tie 200 mm, black", UnitOfMeasure = "pcs" },
        new() { ArticleNumber = "220-1450", Description = "Wire harness WH-450", UnitOfMeasure = "pcs" },
        new() { ArticleNumber = "310-0007", Description = "Welding wire SG2 1.0 mm", UnitOfMeasure = "kg" },
        new() { ArticleNumber = "310-0110", Description = "Shielding gas cartridge Ar/CO2", UnitOfMeasure = "pcs" },
        new() { ArticleNumber = "405-8800", Description = "Primer, grey", UnitOfMeasure = "L" },
        new() { ArticleNumber = "405-8812", Description = "Top coat RAL 5010", UnitOfMeasure = "L" },
        new() { ArticleNumber = "510-0042", Description = "Corrugated box 600x400x400", UnitOfMeasure = "pcs" },
        new() { ArticleNumber = "510-0050", Description = "Stretch film 500 mm", UnitOfMeasure = "roll" },
        new() { ArticleNumber = "610-7781", Description = "Hydraulic hose 3/8\" 1 m", UnitOfMeasure = "pcs" },
        new() { ArticleNumber = "700-1200", Description = "Sealing ring 40x52x7", UnitOfMeasure = "pcs" },
        new() { ArticleNumber = "820-0001", Description = "Nitrile gloves, size L", UnitOfMeasure = "box" },
        new() { ArticleNumber = "900-3003", Description = "Isopropyl alcohol 1 L", UnitOfMeasure = "pcs" },
    ];
}
