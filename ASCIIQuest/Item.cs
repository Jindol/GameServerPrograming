// Item.cs

/// <summary>
/// 아이템의 희귀도(등급)를 정의합니다.
/// (예: Common은 0, Legendary는 3)
/// </summary>
public enum ItemRarity
{
    Common,     // 1. 흔함
    Rare,       // 2. 레어
    Unique,     // 3. 유니크
    Legendary   // 4. 레전더리
}

/// <summary>
/// 아이템의 기본 타입을 정의합니다. (장비인지, 소비템인지)
/// </summary>
public enum ItemType
{
    Equipment,  // 장비
    Consumable  // 소비 아이템
}

/// <summary>
/// [핵심] 모든 아이템(장비, 소비템)의 기반이 되는 '추상 부모 클래스'입니다.
/// 'abstract' 키워드가 붙어있으므로, Item 클래스 자체로는 인스턴스(객체)를 만들 수 없습니다.
/// 오직 Equipment나 Consumable 같은 자식 클래스를 통해서만 구현될 수 있습니다.
/// </summary>
public abstract class Item
{
    /// <summary>
    /// 아이템의 이름 (예: "[Common] HP 물약", "[Legendary] 커널의 검")
    /// 'protected set'이므로 자식 클래스(Equipment, Consumable)에서만 이 값을 설정할 수 있습니다.
    /// </summary>
    public string Name { get; protected set; }
    
    /// <summary>
    /// 아이템의 희귀도 (Common, Rare, Unique, Legendary)
    /// </summary>
    public ItemRarity Rarity { get; protected set; }
    
    /// <summary>
    /// 아이템의 타입 (Equipment, Consumable)
    /// </summary>
    public ItemType Type { get; protected set; }

    /// <summary>
    /// Item의 기본 생성자입니다.
    /// Equipment, Consumable 등 이 클래스를 '상속'받는 자식 클래스들이
    /// 'base(name, rarity, type)' 형태로 호출하여 공통 속성을 초기화합니다.
    /// </summary>
    /// <param name="name">아이템의 이름</param>
    /// <param name="rarity">아이템의 희귀도</param>
    /// <param name="type">아이템의 타입</param>
    public Item(string name, ItemRarity rarity, ItemType type)
    {
        Name = name;
        Rarity = rarity;
        Type = type;
    }
}