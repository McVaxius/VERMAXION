// Adapted from erdelf/AutoDuty, commit 7e0ede4be688980d217faf6889ffdac53e18e30f. Private local port.
using ECommons.GameHelpers;
using System;
using System.Collections.Generic;

using System.Linq;
using ECommons.DalamudServices;
using Lumina.Excel;
using Lumina.Excel.Sheets.Experimental;
#pragma warning disable PendingExcelSchema

namespace VERMAXION.Beastmaster;

public static class CrucibleItemData
{
    public static readonly uint[] ShopHealing =
    [
        140, // Beast Potion Kit
        79,  // G4 Beast Potion
        78,  // G3 Beast Potion
        77,  // G2 Beast Potion
        76,  // G1 Beast Potion
        82,  // G3 Crucible Ash
        81,  // G2 Crucible Ash
        80   // G1 Crucible Ash
    ];

    public static readonly uint[] ShopGear =
    [
        23, // Angel Robe
        3,  // Ring of Curing
        73, // Empress Hairpin
        2,  // Ring of Sacrifice
        27, // Hero's Crown
        24, // Master Shield
        15, // Earth Shield
        49, // Wind Armor
        21, // Genji Armor
        47, // Wind Shield
        25, // Mystic Veil
        16, // Water Shield
        35, // Astral Mantle
        44, // Ninja Suit
        9,  // Power Armlet
        11, // Green Beret
        51, // Mythril Armor
        58, // Thief's Garb
        72, // Demonic Armor
        32, // Umbral Mantle
        62, // Thunder Armor
        48, // Wind Mask
        28, // Mirage Vest
        14, // Ice Shield
        54, // Beast Mask
        26, // Black Cowl
        66, // Flame Shield
        52, // Mythril Gloves
        70, // Spirited Ring
        34, // Umbral Wristlet
        13, // Ring of Protection
        69, // Merchant's Shoes
        29, // Soulreaper's Armor
        1,  // Belt of Constitution
        20, // Genji Greatshield
        12, // Mystic Boots
        31, // Coward's Knife
        37, // Astral Wristlet
        53, // Mythril Greaves
        10, // Twisted Headband
        56, // Beast Earring
        63, // Warded Shield
        17, // Force Shield
        33, // Umbral Band
        36, // Astral Band
        75, // Warrior's Buckler
        6,  // Steel Armor
        5,  // Hexed Hat
        68, // Merchant's Garb
        60, // Thief's Boots
        61, // Thunder Axe
        50, // Heavy Axe
        71, // Demonic Helm
        65, // Flame Knife
        19, // Genji Gloves
        57, // Thief's Knife
        7,  // Crown of the Wild
        8,  // Staff of the Wise
        30, // Briar Armor
        38, // Flame-wreathed Axe
        39, // Icebitten Axe
        40, // Thunderstruck Axe
        41, // Earthcrushed Axe
        42, // Deepdrowned Axe
        43, // Windblown Axe
        64, // Gold Hairpin
        22, // Silver Specs
        46, // Ninja Eyepatch
        55, // Beastly Knife
        74, // Haste Belt
        45, // Ninja Gloves
        59, // Thief's Gloves
        67, // Merchant's Cap
        18, // Crimson Ribbon
        4   // Chemist's Satchel
    ];

    public static readonly uint[] ElemntalAxes =
    [
        38, // Flame-wreathed Axe
        39, // Icebitten Axe
        40, // Thunderstruck Axe
        41, // Earthcrushed Axe
        42, // Deepdrowned Axe
        43  // Windblown Axe
    ];

    public static readonly uint[] ShopFeed =
    [
        144, // G1 Primafodder
        145, // G2 Primafodder
        149, // Crab Ball Simular
        153, // Yellow Egg Simular
        155, // Milk Simular
        163, // Magnum Water
        165, // Noble Blood Simular
        174, // Lugworm Simular
        185, // Cream Cheese Simular
        188, // Cornbread Simular
        191, // Belladonna Simular
        202, // Cottage Cheese Simular
        203, // Lassi Simular
        148, // Honey Simular
        154, // Tomato Simular
        157, // Porcini Simular
        158, // Morel Simular
        159, // Black Truffle Simular
        160, // White Truffle Simular
        161, // Mushroom Simular
        166, // Lily Simular
        169, // Black Scorpion Simular
        171, // Angelfish Simular
        175, // Crucible Tonic
        176, // Carrot Simular
        177, // Onion Simular
        178, // Lettuce Simular
        179, // Lemon Simular
        180, // Untaming Oil
        181, // Mucus Simular
        182, // Sap Simular
        183, // Salt Simular
        184, // Syrup Simular
        187, // Toast Simular
        194, // Herbal Tea Simular
        195, // Honeycomb Simular
        197, // Grape Juice Simular
        146, // Meat Simular
        147, // Hydrolixer
        150, // Banana Simular
        151, // Berry Simular
        152, // Egg Simular
        156, // Rolanberry Cheese Simular
        162, // Sole Simular
        164, // Blood Simular
        167, // Flounder Simular
        170, // Herring Simular
        172, // Grape Simular
        173, // Orange Simular
        186, // Red Egg Simular
        189, // Mandrake Simular
        190, // Tarantula Simular
        192, // Steak Simular
        193, // Roe Simular
        196, // Orange Juice Simular
        198, // Skewer Simular
        199, // Pineapple Juice Simular
        200, // Oyster Simular
        201, // Blue Cheese Simular
        168  // White Scorpion Simular
    ];

    public static readonly uint[] FightHealingItems =
    [
        140, // Beast Potion Kit
        79,  // G4 Beast Potion
        78,  // G3 Beast Potion
        77,  // G2 Beast Potion
        76,  // G1 Beast Potion
        82,  // G3 Crucible Ash
        81,  // G2 Crucible Ash
        80,  // G1 Crucible Ash
        112, // Potion of Tempered Constitution
        102, // Crucible Tannin
        137, // Tome of the Impervious
        135  // Vampiric Essence
    ];

    public static readonly uint[] BoardHealingItems =
    [
        79, // G4 Beast Potion
        78, // G3 Beast Potion
        77, // G2 Beast Potion
        76, // G1 Beast Potion
        82, // G3 Crucible Ash
        81, // G2 Crucible Ash
        80  // G1 Crucible Ash
    ];

    public static readonly uint[][] StatusItems =
    [
        [138, 4870], // Temporal Sands
        [99, 4846],  // G2 Reraiser
        [98, 4846],  // G1 Reraiser
        [101, 4856], // Merchant's Eye
        [100, 4855]  // Thief's Eye
    ];

    public static readonly uint[] CombatDamageItems =
    [
        128, // Fang of Fire
        129, // Fang of Ice
        130, // Fang of Water
        131, // Fang of Lightning
        132, // Fang of Earth
        133, // Fang of Wind
        134, // Vampiric Fang
    ];

    public static uint[] ShopItems => ShopHealing.Concat(StatusItems.Select(x => x[0])).Concat(CombatDamageItems).Concat(ShopGear).Concat(ShopFeed).Concat(Items.Where(item => item.Type.RowId != 0).Select(item => item.RowId)).Distinct().ToArray();

    public static BeastmasterShopList ActiveShopList
    {
        get
        {
            BeastmasterSettings crucible = BeastmasterRuntime.Settings;
            if (crucible.ShopLists.Count == 0)
            {
                crucible.ShopLists.Add(new BeastmasterShopList
                                       {
                                           Order = crucible.ShopGearOrder.Select<uint, BeastmasterShopList.Entry>(id => id).ToList()
                                       }.CompleteList());
                crucible.ShopGearOrder = [];
            }

            crucible.ShopListIndex = Math.Clamp(crucible.ShopListIndex, 0, crucible.ShopLists.Count - 1);
            return crucible.ShopLists[crucible.ShopListIndex];
        }
    }

    public static List<BeastmasterShopList.Entry> ShopOrder => ActiveShopList.CompleteList().Order;
    public static List<BeastmasterShopList.Entry> ShopOrderActive => ShopOrder.Where(e => !e.Blocked).ToList();

    private static ExcelSheet<XBMItem> Items => field ??= Svc.Data.GetExcelSheet<XBMItem>();

    public static readonly Dictionary<uint, uint[]> GearRequires = new()
    {
        [71] = [29], // Demonic Helm - Soulreaper Armor
        [61] = [34]  // Thunder Axe - Umbral Wristlet
    };

    public static bool BlockedGear(uint row, HashSet<uint> ownedGear)
    {
        BeastmasterSettings crucible = BeastmasterRuntime.Settings;

        bool secondElementalAxe = ElemntalAxes.Contains(row) && ownedGear.Any(ElemntalAxes.Contains);
        bool missingRequirement = crucible.RespectGearRequirements && GearRequires.TryGetValue(row, out uint[]? required) && !ownedGear.Any(required.Contains);

        return secondElementalAxe || missingRequirement;
    }

    public static string NameOf(uint row) =>
        Items.TryGetRow(row, out XBMItem item) && item.Name.ExtractText() is { Length: > 0 } name ? name : $"item {row}";

    public readonly record struct ItemInfo(string Name, string Type, string Summary, string Description, uint Icon);

    public static ItemInfo? InfoOf(uint row)
    {
        if (!Items.TryGetRow(row, out XBMItem item))
            return null;

        string type = item.Type.ValueNullable?.Name.ExtractText() ?? string.Empty;
        return new ItemInfo(NameOf(row), type, item.ShortDescription.ExtractText(), item.Description.ExtractText(), item.Icon);
    }

    public static uint ItemIn(string text) =>
        Items.Where(x => x.RowId > 0)
             .Select(x => (x.RowId, Name: x.Name.ExtractText()))
             .Where(x => x.Name.Length > 0 && text.Contains(x.Name, StringComparison.OrdinalIgnoreCase))
             .OrderByDescending(x => x.Name.Length)
             .Select(x => x.RowId)
             .FirstOrDefault();

    public static int TreasureRank(uint row)
    {
        int index = ShopOrderActive.FindIndex(e => e.ID == row);
        return index < 0 ? int.MaxValue : index;
    }

    public enum CrucibleItemType
    {
        None,
        BeastGear = 1,
        CrucibleItem = 2,
        Feed = 3
    }

    [Flags]
    public enum CrucibleItemCategory
    {
        None       = 0,
        Gear       = 1 << 0,
        HealItem   = 1 << 1,
        DamageItem = 1 << 2,
        OtherItem  = 1 << 3,
        Item       = HealItem | DamageItem | OtherItem,
        Feed       = 1 << 4
    }

    public static CrucibleItemCategory GetCategoryOf(uint row)
    {
        if (!Items.TryGetRow(row, out XBMItem item) || !item.Type.IsValid)
            return CrucibleItemCategory.None;

        CrucibleItemType type = (CrucibleItemType) item.Type.RowId;

        return type switch
        {
            CrucibleItemType.BeastGear => CrucibleItemCategory.Gear,
            CrucibleItemType.CrucibleItem when ShopHealing.Contains(row) => CrucibleItemCategory.HealItem,
            CrucibleItemType.CrucibleItem when CombatDamageItems.Contains(row) => CrucibleItemCategory.DamageItem,
            CrucibleItemType.CrucibleItem => CrucibleItemCategory.OtherItem,
            CrucibleItemType.Feed => CrucibleItemCategory.Feed,
            _ => CrucibleItemCategory.None
        };
    }
}   

public partial class BeastmasterShopList
{
    public BeastmasterShopList CompleteList()
    {
        foreach (var id in CrucibleItemData.ShopItems)
            if (Order.All(e => e.ID != id)) Order.Add(new Entry { ID = id });
        return this;
    }
    [Newtonsoft.Json.JsonIgnore] public bool IsCompleted => CrucibleItemData.ShopItems.All(id => Order.Any(e => e.ID == id));
    [Newtonsoft.Json.JsonIgnore] public bool OrderChanged => !Order.Select(e => e.ID).SequenceEqual(CrucibleItemData.ShopItems);
    public partial class Entry
    {
        [Newtonsoft.Json.JsonIgnore] public CrucibleItemData.CrucibleItemCategory GetCategory => CrucibleItemData.GetCategoryOf(ID);
    }
}
