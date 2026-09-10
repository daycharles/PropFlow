using Microsoft.EntityFrameworkCore;
using PropFlow.Domain.Assets;
using PropFlow.Domain.People;
using PropFlow.Domain.Properties;
using PropFlow.Domain.Work;
using PropFlow.Infrastructure.Persistence;

namespace PropFlow.Admin;

/// <summary>
/// The realistic Tidewater Residential Management dataset (PF-4.10): three properties, 10
/// buildings, ~80 spaces, ~70 residents with mixed consent, 6 vendors, 5 employees, ~70 assets
/// and 100 work items spanning every status and priority, with ~20 pest-control requests and a
/// mix of overdue / upcoming / completed. Deterministic — a fixed RNG seed makes every run
/// identical, and the caller only invokes it when the organization has no work items yet.
/// </summary>
internal static class TidewaterSeed
{
    private static readonly string[] FirstNames =
    [
        "Dana", "Sam", "Alex", "Jordan", "Casey", "Riley", "Morgan", "Taylor", "Jamie", "Avery",
        "Quinn", "Parker", "Drew", "Reese", "Skyler", "Rowan", "Emerson", "Finley", "Harper", "Kai",
    ];
    private static readonly string[] LastNames =
    [
        "Reyes", "Okafor", "Nguyen", "Patel", "Garcia", "Kim", "Johnson", "Brooks", "Delgado",
        "Fischer", "Osei", "Romano", "Haddad", "Larsson", "Petrov", "Mensah", "Cohen", "Ali",
    ];
    private static readonly (string Name, string Trade)[] Vendors =
    [
        ("Tidewater Pest Services", "Pest control"),
        ("Bayfront Plumbing Co.", "Plumbing"),
        ("Coastline Electric", "Electrical"),
        ("Harbor HVAC & Refrigeration", "HVAC"),
        ("Summit Roofing", "Roofing"),
        ("All-Trades Facility Group", "General maintenance"),
    ];
    private static readonly (string Name, int Sort)[] Categories =
    [
        ("Maintenance", 0), ("Pest control", 1), ("Plumbing", 2),
        ("Electrical", 3), ("HVAC", 4), ("Turnover", 5),
    ];
    private static readonly (string Property, string Tz, string[] Buildings)[] Layout =
    [
        ("Harbor View Apartments", "America/New_York", ["Building A", "Building B", "Building C", "Building D"]),
        ("Maple Court", "America/New_York", ["North Wing", "South Wing", "Garden House"]),
        ("Riverside Commons", "America/Chicago", ["River Building", "Oak Building", "Willow Building"]),
    ];
    private static readonly (string Title, string Category, WorkPriority Priority)[] WorkTemplates =
    [
        ("Ant swarm in the trash room", "Pest control", WorkPriority.High),
        ("Quarterly pest treatment", "Pest control", WorkPriority.Normal),
        ("Roach sighting in kitchen", "Pest control", WorkPriority.Normal),
        ("Rodent bait station refresh", "Pest control", WorkPriority.Normal),
        ("Wasp nest on the balcony", "Pest control", WorkPriority.High),
        ("Bed bug inspection request", "Pest control", WorkPriority.High),
        ("Leaking kitchen sink", "Plumbing", WorkPriority.Normal),
        ("Running toilet", "Plumbing", WorkPriority.Low),
        ("No hot water", "Plumbing", WorkPriority.High),
        ("Clogged bathtub drain", "Plumbing", WorkPriority.Normal),
        ("Broken entry light", "Electrical", WorkPriority.Normal),
        ("Outlet sparks when used", "Electrical", WorkPriority.Critical),
        ("Hallway smoke detector chirping", "Electrical", WorkPriority.Normal),
        ("AC not cooling", "HVAC", WorkPriority.High),
        ("Furnace makes a loud noise", "HVAC", WorkPriority.Normal),
        ("Thermostat unresponsive", "HVAC", WorkPriority.Low),
        ("Repaint the stairwell", "Maintenance", WorkPriority.Low),
        ("Replace lobby entry mats", "Maintenance", WorkPriority.Low),
        ("Gate motor jammed", "Maintenance", WorkPriority.Normal),
        ("Roof leak above the top floor", "Maintenance", WorkPriority.High),
        ("Gas odor investigation", "Maintenance", WorkPriority.Critical),
        ("Unit turn: paint and clean", "Turnover", WorkPriority.Normal),
        ("Unit turn: replace carpet", "Turnover", WorkPriority.Normal),
    ];

    public static async Task SeedAsync(string adminConnection, Guid organizationId, Guid creatorId)
    {
        await using var store = DatabaseProvisioner.CreateOperationsStore(adminConnection, organizationId);
        if (await store.WorkItems.AnyAsync()) return; // idempotent; user/demo edits are preserved

        var rng = new Random(20260410);
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var now = DateTimeOffset.UtcNow;

        // --- portfolio, properties, buildings, spaces --------------------------------------
        var portfolioId = Guid.NewGuid();
        store.Portfolios.Add(new Portfolio(organizationId, portfolioId, "Tidewater Portfolio"));

        var spaces = new List<(Guid Id, Guid PropertyId, Guid BuildingId, string Code)>();
        var propertyIds = new List<Guid>();
        foreach (var (propertyName, tz, buildings) in Layout)
        {
            var propertyId = Guid.NewGuid();
            propertyIds.Add(propertyId);
            store.Properties.Add(new Property(organizationId, propertyId, portfolioId, propertyName, tz));
            foreach (var buildingName in buildings)
            {
                var buildingId = Guid.NewGuid();
                store.Buildings.Add(new Building(organizationId, buildingId, propertyId, buildingName));
                var unitsPerBuilding = rng.Next(7, 10);
                for (var unit = 1; unit <= unitsPerBuilding; unit++)
                {
                    var code = $"{rng.Next(1, 4)}{unit:D2}";
                    var spaceId = Guid.NewGuid();
                    store.Spaces.Add(new Space(organizationId, spaceId, propertyId, buildingId, code));
                    spaces.Add((spaceId, propertyId, buildingId, code));
                }
            }
        }

        // --- vendors, employees, categories ----------------------------------------------
        var vendorIds = new List<Guid>();
        foreach (var (name, trade) in Vendors)
        {
            var vendorId = Guid.NewGuid();
            vendorIds.Add(vendorId);
            var vendor = new Vendor(organizationId, vendorId, name);
            vendor.UpdateContact($"dispatch@{Slug(name)}.example.test", $"555-01{vendorIds.Count:D2}", trade, trade);
            store.Vendors.Add(vendor);
        }

        var employeeIds = new List<Guid>();
        foreach (var display in new[] { "Jordan Lee", "Priya Anand", "Marcus Bell", "Sofia Torres", "Wes Carroll" })
        {
            var employeeId = Guid.NewGuid();
            employeeIds.Add(employeeId);
            store.Employees.Add(new Employee(organizationId, employeeId, display,
                $"{Slug(display)}@tidewater.example.test", $"555-02{employeeIds.Count:D2}"));
        }

        var categoryIds = new Dictionary<string, Guid>();
        foreach (var (name, sort) in Categories)
        {
            var id = Guid.NewGuid();
            categoryIds[name] = id;
            store.Categories.Add(new WorkCategory(organizationId, id, name, sort));
        }

        // --- residents + occupancies (70 of the ~80 spaces occupied) --------------------
        var occupiedSpaces = spaces.OrderBy(_ => rng.Next()).Take(Math.Min(70, spaces.Count - 8)).ToList();
        var residents = new List<(Guid Id, Guid SpaceId)>();
        foreach (var space in occupiedSpaces)
        {
            var residentId = Guid.NewGuid();
            var fullName = $"{FirstNames[rng.Next(FirstNames.Length)]} {LastNames[rng.Next(LastNames.Length)]}";
            var handle = $"{Slug(fullName)}{rng.Next(10, 99)}";
            var resident = new Resident(organizationId, residentId, fullName,
                $"{handle}@residents.example.test", $"+1555{rng.Next(1000000, 9999999)}");
            // Mixed consent: ~55% both, ~20% sms only, ~10% email only, ~15% none.
            var roll = rng.Next(100);
            if (roll < 75) resident.SetConsent(PropFlow.Domain.Communications.MessageChannel.Sms, granted: true, now);
            if (roll is >= 55 and < 85) resident.SetConsent(PropFlow.Domain.Communications.MessageChannel.Email, granted: true, now);
            store.Residents.Add(resident);

            var movedIn = today.AddDays(-rng.Next(60, 1400));
            var occupancy = new Occupancy(organizationId, Guid.NewGuid(), residentId, space.Id, movedIn);
            store.Occupancies.Add(occupancy);
            residents.Add((residentId, space.Id));
        }
        // A handful of prior tenancies that have ended, so occupancy history is not all-current.
        foreach (var space in occupiedSpaces.Take(6))
        {
            var priorId = Guid.NewGuid();
            store.Residents.Add(new Resident(organizationId, priorId,
                $"{FirstNames[rng.Next(FirstNames.Length)]} {LastNames[rng.Next(LastNames.Length)]}", null, $"+1555{rng.Next(1000000, 9999999)}"));
            var start = today.AddDays(-rng.Next(1500, 2500));
            var priorOccupancy = new Occupancy(organizationId, Guid.NewGuid(), priorId, space.Id, start);
            priorOccupancy.EndOn(start.AddDays(rng.Next(200, 900)));
            store.Occupancies.Add(priorOccupancy);
        }

        // --- assets (~60): HVAC per building, water heaters + appliances per space ------
        var assetsByProperty = spaces.Select(s => s.PropertyId).Distinct().ToList();
        var buildingsSeen = new HashSet<Guid>();
        var assetCount = 0;
        foreach (var space in spaces)
        {
            if (buildingsSeen.Add(space.BuildingId))
            {
                AddAsset(store, organizationId, space.PropertyId, null, AssetKind.Hvac, $"Rooftop HVAC — {space.Code[..1]} wing", rng, today);
                assetCount++;
            }
            if (assetCount < 60 && rng.Next(100) < 55)
            {
                AddAsset(store, organizationId, space.PropertyId, space.Id, AssetKind.WaterHeater, $"Water heater — unit {space.Code}", rng, today);
                assetCount++;
            }
            if (assetCount < 60 && rng.Next(100) < 30)
            {
                AddAsset(store, organizationId, space.PropertyId, space.Id, AssetKind.Appliance, $"Refrigerator — unit {space.Code}", rng, today);
                assetCount++;
            }
        }
        foreach (var propertyId in assetsByProperty)
        {
            AddAsset(store, organizationId, propertyId, null, AssetKind.Roof, "Main roof membrane", rng, today);
            AddAsset(store, organizationId, propertyId, null, AssetKind.ElectricalPanel, "Main electrical panel", rng, today);
            AddAsset(store, organizationId, propertyId, null, AssetKind.Generator, "Emergency generator", rng, today);
        }

        // --- work items (~100), every status + priority, 18+ pest control ----------------
        var statusPlan = BuildStatusPlan();
        var templateOrder = Enumerable.Range(0, 100).Select(i => WorkTemplates[i % WorkTemplates.Length]).ToList();
        // Guarantee ≥ 20 pest-control items by swapping non-pest templates for pest ones.
        var pestTemplates = WorkTemplates.Where(t => t.Category == "Pest control").ToArray();
        for (var i = 0; templateOrder.Count(t => t.Category == "Pest control") < 20; i += 3)
            templateOrder[i % templateOrder.Count] = pestTemplates[i % pestTemplates.Length];

        for (var i = 0; i < 100; i++)
        {
            var (title, categoryName, priority) = templateOrder[i];
            var status = statusPlan[i];
            var space = spaces[rng.Next(spaces.Count)];
            var resident = residents.FirstOrDefault(r => r.SpaceId == space.Id);

            var work = new WorkItem(organizationId, Guid.NewGuid(), title, space.PropertyId, creatorId);
            work.Edit(title, "Reported by the resident.", categoryIds[categoryName], priority);
            work.SetLocation(space.PropertyId, space.BuildingId, space.Id, resident.Id == Guid.Empty ? null : resident.Id);

            // Overdue for a third of the still-open items; upcoming otherwise.
            var overdue = status is not (WorkStatus.Completed or WorkStatus.Cancelled) && rng.Next(3) == 0;
            work.SetDueDate(now.AddDays(overdue ? -rng.Next(1, 14) : rng.Next(1, 21)));
            if (rng.Next(4) == 0) work.SetCost(rng.Next(75, 900));

            if (status is not WorkStatus.Draft)
            {
                work.Publish(now.AddDays(-rng.Next(1, 30)));
                switch (status)
                {
                    case WorkStatus.Assigned:
                        work.AssignVendor(vendorIds[rng.Next(vendorIds.Count)]);
                        break;
                    case WorkStatus.Scheduled:
                        work.AssignVendor(vendorIds[rng.Next(vendorIds.Count)]);
                        var slot = now.AddDays(rng.Next(1, 6));
                        work.Schedule(slot, slot.AddHours(2));
                        break;
                    case WorkStatus.InProgress:
                        work.AssignEmployee(employeeIds[rng.Next(employeeIds.Count)]);
                        work.ChangeStatus(WorkStatus.InProgress, now);
                        break;
                    case WorkStatus.OnHold:
                        work.ChangeStatus(WorkStatus.OnHold, now);
                        break;
                    case WorkStatus.Completed:
                        work.AssignVendor(vendorIds[rng.Next(vendorIds.Count)]);
                        work.ChangeStatus(WorkStatus.Completed, now.AddDays(-rng.Next(1, 20)));
                        break;
                    case WorkStatus.Cancelled:
                        work.ChangeStatus(WorkStatus.Cancelled, now);
                        break;
                }
            }
            store.WorkItems.Add(work);
        }

        await store.SaveChangesAsync();
    }

    private static void AddAsset(OperationsStore store, Guid org, Guid propertyId, Guid? spaceId,
        AssetKind kind, string name, Random rng, DateOnly today)
    {
        var asset = new Asset(org, Guid.NewGuid(), propertyId, spaceId, kind, name);
        var installed = today.AddDays(-rng.Next(200, 5500));
        var life = kind switch { AssetKind.WaterHeater => 12, AssetKind.Hvac => 15, AssetKind.Roof => 25, AssetKind.Generator => 20, _ => 10 };
        asset.SetLifecycle(installed, installed.AddYears(life is > 12 ? 10 : 8), life);
        var age = today.Year - installed.Year;
        asset.RecordCondition(age switch { < 2 => AssetCondition.New, < 6 => AssetCondition.Good, < 12 => AssetCondition.Fair, < 18 => AssetCondition.Poor, _ => AssetCondition.EndOfLife });
        asset.SetReplacementCost(kind switch { AssetKind.Hvac => 8500, AssetKind.WaterHeater => 1600, AssetKind.Roof => 45000, AssetKind.Generator => 22000, AssetKind.Appliance => 1200, _ => 900 });
        store.Assets.Add(asset);
    }

    // 100 work items across all eight statuses, weighted toward the active middle of the pipeline.
    private static List<WorkStatus> BuildStatusPlan()
    {
        var counts = new (WorkStatus Status, int Count)[]
        {
            (WorkStatus.Draft, 5), (WorkStatus.New, 16), (WorkStatus.Assigned, 15), (WorkStatus.Scheduled, 12),
            (WorkStatus.InProgress, 12), (WorkStatus.OnHold, 8), (WorkStatus.Completed, 22), (WorkStatus.Cancelled, 10),
        };
        var plan = counts.SelectMany(c => Enumerable.Repeat(c.Status, c.Count)).ToList();
        var rng = new Random(11);
        for (var i = plan.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (plan[i], plan[j]) = (plan[j], plan[i]);
        }
        return plan;
    }

    private static string Slug(string value)
    {
        var chars = value.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray();
        return new string(chars).Trim('-').Replace("--", "-");
    }
}
