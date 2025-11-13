// Item.cs

// 아이템 희귀도
public enum ItemRarity
{
    Common,     // 1. 흔함
    Rare,       // 2. 레어
    Unique,     // 3. 유니크
    Legendary   // 4. 레전더리
}

// 아이템 기본 타입
public enum ItemType
{
    Equipment,
    Consumable
}

public abstract class Item
{
    public string Name { get; protected set; }
    public ItemRarity Rarity { get; protected set; }
    public ItemType Type { get; protected set; }

    public Item(string name, ItemRarity rarity, ItemType type)
    {
        Name = name;
        Rarity = rarity;
        Type = type;
    }
}