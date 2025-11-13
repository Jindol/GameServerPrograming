// Equipment.cs
using System.Collections.Generic;

// 장비 부위
public enum EquipmentSlot
{
    Weapon,
    Head,
    Armor,
    Gloves,
    Boots
}

public class Equipment : Item
{
    public EquipmentSlot Slot { get; private set; }
    public PlayerClass RequiredClass { get; private set; }
    
    // 이 장비가 제공하는 모든 스탯 효과들
    public List<StatModifier> Modifiers { get; private set; }

    public Equipment(string name, ItemRarity rarity, EquipmentSlot slot, PlayerClass requiredClass) 
        : base(name, rarity, ItemType.Equipment)
    {
        Slot = slot;
        RequiredClass = requiredClass;
        Modifiers = new List<StatModifier>();
    }

    // 아이템 생성 시 스탯을 추가하는 메서드
    public void AddModifier(StatModifier modifier)
    {
        Modifiers.Add(modifier);
    }
}