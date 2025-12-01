// Equipment.cs

// List<T> 컬렉션(예: List<StatModifier>)을 사용하기 위해 필요합니다.
using System.Collections.Generic;

/// <summary>
/// 장비 아이템이 착용될 수 있는 부위(슬롯)를 정의합니다.
/// </summary>
public enum EquipmentSlot
{
    Weapon, // 무기
    Head,   // 머리
    Armor,  // 갑옷
    Gloves, // 장갑
    Boots   // 신발
}

/// <summary>
/// 'Item' 클래스를 상속받아, '장비' 아이템 고유의 속성을 정의합니다.
/// 이 클래스는 장비의 뼈대(슬롯, 착용 직업)와 스탯 옵션 목록(Modifiers)을 가집니다.
/// </summary>
public class Equipment : Item
{
    /// <summary>
    /// 이 장비가 착용되는 부위(슬롯)입니다. (예: Weapon, Armor)
    /// </summary>
    public EquipmentSlot Slot { get; private set; }

    /// <summary>
    /// 이 장비를 착용할 수 있는 직업입니다.
    /// (ItemDB에서 아이템 생성 시 플레이어의 직업에 맞춰 설정됩니다.)
    /// </summary>
    public PlayerClass RequiredClass { get; private set; }
    
    /// <summary>
    /// [핵심] 이 장비가 제공하는 모든 스탯 옵션(효과)들을 담는 리스트입니다.
    /// (예: "HP +10", "STR +2", "공격력 +5%" 등)
    /// 이 리스트 덕분에 아이템 등급에 따라 1~8개까지 다양한 개수의 옵션을 가질 수 있습니다.
    /// </summary>
    public List<StatModifier> Modifiers { get; private set; }

    /// <summary>
    /// 새 장비 아이템 인스턴스를 생성합니다.
    /// </summary>
    /// <param name="name">아이템 이름 (예: "[Rare] 컴파일된 검")</param>
    /// <param name="rarity">아이템 희귀도 (Common, Rare 등)</param>
    /// <param name="slot">착용 부위 (Weapon, Armor 등)</param>
    /// <param name="requiredClass">착용 가능 직업 (Warrior, Wizard 등)</param>
    public Equipment(string name, ItemRarity rarity, EquipmentSlot slot, PlayerClass requiredClass) 
        // 부모 클래스(Item)의 생성자를 호출하여 기본 속성(Name, Rarity, Type)을 초기화합니다.
        : base(name, rarity, ItemType.Equipment)
    {
        Slot = slot;
        RequiredClass = requiredClass;
        
        // Modifiers 리스트를 비어있는 새 리스트로 초기화합니다.
        // (이후 AddModifier를 통해 스탯 옵션이 추가됩니다.)
        Modifiers = new List<StatModifier>();
    }

    /// <summary>
    /// ItemDB가 아이템을 생성(조립)하는 과정에서 호출됩니다.
    /// 이 장비에 스탯 옵션(StatModifier)을 추가합니다.
    /// </summary>
    /// <param name="modifier">추가할 스탯 옵션 객체 (예: "HP +10")</param>
    public void AddModifier(StatModifier modifier)
    {
        Modifiers.Add(modifier);
    }
}