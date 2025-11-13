// ItemDB.cs
using System;
using System.Collections.Generic;
using System.Linq; 

public static class ItemDB
{
    // (드랍률 설정... 변경 없음)
    #region Drop_Rates
    private const double BASE_EQUIPMENT_DROP_CHANCE = 0.15; // (스테이지당 +5%)
    private const double BASE_CONSUMABLE_DROP_CHANCE = 0.40; 
    
    private const double BASE_EQ_COMMON_CHANCE = 0.60; // (스테이지당 -10%)
    private const double BASE_EQ_RARE_CHANCE = 0.25;   // (스테이지당 +5%)
    private const double BASE_EQ_UNIQUE_CHANCE = 0.10; // (스테이지당 +3%)
    private const double BASE_EQ_LEGENDARY_CHANCE = 0.05;// (스테이지당 +2%)
    private const double CON_COMMON_CHANCE = 0.70;    
    private const double CON_RARE_CHANCE = 0.20;      
    private const double CON_UNIQUE_CHANCE = 0.08;    
    private const double CON_LEGENDARY_CHANCE = 0.02; 
    #endregion

    // (아이템 이름 템플릿... 변경 없음)
    #region Base_Item_Names
    private static Dictionary<PlayerClass, Dictionary<EquipmentSlot, string>> baseItemNames = new() {
        { PlayerClass.Warrior, new() {
            { EquipmentSlot.Weapon, "검" }, { EquipmentSlot.Head, "투구" }, { EquipmentSlot.Armor, "갑옷" },
            { EquipmentSlot.Gloves, "건틀릿" }, { EquipmentSlot.Boots, "장화" }
        }},
        { PlayerClass.Wizard, new() {
            { EquipmentSlot.Weapon, "지팡이" }, { EquipmentSlot.Head, "모자" }, { EquipmentSlot.Armor, "로브" },
            { EquipmentSlot.Gloves, "장갑" }, { EquipmentSlot.Boots, "신발" }
        }},
        { PlayerClass.Rogue, new() {
            { EquipmentSlot.Weapon, "단검" }, { EquipmentSlot.Head, "두건" }, { EquipmentSlot.Armor, "경갑" },
            { EquipmentSlot.Gloves, "가죽 장갑" }, { EquipmentSlot.Boots, "부츠" }
        }}
    };
    #endregion

    // --- [핵심 수정] 3번 요청 (아이템 이름 생성 규칙 변경) ---
    #region Name_Generators
    
    // [수정] 장비용 접두사 (예: "낡은")
    private static readonly Dictionary<ItemRarity, List<string>> rarityPrefixes = new()
    {
        { ItemRarity.Common, new() { "낡은", "조잡한", "평범한", "오래된" } },
        { ItemRarity.Rare, new() { "쓸만한", "견고한", "날카로운", "단단한", "수습생의" } },
        { ItemRarity.Unique, new() { "숙련자의", "빛나는", "정교한", "강화된", "필멸의" } },
        { ItemRarity.Legendary, new() { "전설적인", "타락한", "신성한", "파멸의", "영웅의", "고대의" } }
    };

    // [신규] 장비용 '칭호' (예: "영웅")
    private static readonly Dictionary<ItemRarity, List<string>> rarityTitles = new()
    {
        { ItemRarity.Common, new() { "모험가", "마을", "초보자" } }, // (예: 낡은 모험가 검)
        { ItemRarity.Rare, new() { "병사", "수련자", "바위" } },
        { ItemRarity.Unique, new() { "정복자", "달인", "유령", "왕" } },
        { ItemRarity.Legendary, new() { "영웅", "신", "타이탄", "고대신" } }
    };

    // [삭제] Suffixes (접미사)는 더 이상 사용하지 않음
    // private static readonly Dictionary<ItemRarity, List<string>> raritySuffixes = ...

    // [신규] 소비 아이템용 고정 접두사
    private static readonly Dictionary<ItemRarity, string> consumablePrefixes = new()
    {
        { ItemRarity.Common, "조악한" },
        { ItemRarity.Rare, "쓸만한" },
        { ItemRarity.Unique, "정교한" },
        { ItemRarity.Legendary, "신비로운" }
    };

    // [수정] 장비 이름 생성 헬퍼 (칭호 추가, 접미사 제거)
    // (GenerateItemName -> GenerateEquipmentName으로 이름 변경)
    private static string GenerateEquipmentName(string baseName, ItemRarity rarity, Random rand)
    {
        string prefix = "";
        string title = ""; // [신규]

        // 등급별 접두사/칭호 확률
        double prefixChance = 0.0;
        double titleChance = 0.0; // [신규]

        switch (rarity)
        {
            case ItemRarity.Common:
                prefixChance = 0.5; // 50%
                titleChance = 0.2; // 20%
                break;
            case ItemRarity.Rare:
                prefixChance = 1.0; // 100%
                titleChance = 0.5; // 50%
                break;
            case ItemRarity.Unique:
                prefixChance = 1.0; // 100%
                titleChance = 1.0; // 100%
                break;
            case ItemRarity.Legendary:
                prefixChance = 1.0; // 100%
                titleChance = 1.0; // 100%
                break;
        }

        // 접두사 결정 (예: "낡은")
        if (rand.NextDouble() < prefixChance)
        {
            var pList = rarityPrefixes[rarity];
            prefix = pList[rand.Next(pList.Count)];
        }

        // 칭호 결정 (예: "모험가")
        if (rand.NextDouble() < titleChance)
        {
            var tList = rarityTitles[rarity];
            title = tList[rand.Next(tList.Count)];
        }
        
        // 이름 조합
        // [Rarity] {Prefix} {Title} {BaseName}
        string combinedName = baseName;
        if (!string.IsNullOrEmpty(title))
        {
            combinedName = $"{title} {combinedName}"; // (예: "모험가 검")
        }
        if (!string.IsNullOrEmpty(prefix))
        {
            combinedName = $"{prefix} {combinedName}"; // (예: "낡은 모험가 검")
        }

        return $"[{rarity}] {combinedName}";
    }
    
    #endregion

    // (스탯 풀... 변경 없음)
    #region Stat_Pools
    private static Dictionary<PlayerClass, List<StatType>> classPrimaryStats = new() {
        { PlayerClass.Warrior, new() { StatType.STR }},
        { PlayerClass.Wizard, new() { StatType.INT }},
        { PlayerClass.Rogue, new() { StatType.DEX }}
    };
    private static Dictionary<PlayerClass, List<StatType>> classSkillStats = new() {
        { PlayerClass.Warrior, new() { StatType.PowerStrikeDamage, StatType.ShieldBashDamage }},
        { PlayerClass.Wizard, new() { StatType.FireballDamage, StatType.MagicMissileDamage, StatType.HealAmount }},
        { PlayerClass.Rogue, new() { StatType.BackstabDamage, StatType.PoisonStabDamage, StatType.QuickAttackDamage }}
    };
    private static Dictionary<PlayerClass, List<StatType>> classSpecialStats = new() {
        { PlayerClass.Warrior, new() { StatType.DamageReflectChance, StatType.StunChance }},
        { PlayerClass.Wizard, new() { StatType.ManaRefundChance, StatType.ManaShieldConversion }},
        { PlayerClass.Rogue, new() { StatType.LifeStealPercent, StatType.BleedChance }}
    };
    private static List<StatType> commonUtilityStats = new() { 
        StatType.MP, StatType.EXPGain, StatType.ResourceCostReduction
    };

    // [수정] 2번 요청 (크리티컬 확률 제거)
    private static List<StatType> weaponOnlyStats = new() { 
        StatType.ATK // [삭제] StatType.CritChance 
    };
    private static List<StatType> armorOnlyStats = new() { 
        StatType.HP, StatType.DEF 
    };
    #endregion

    // --- [수정] 3번 요청 (이름 생성 로직 호출) ---
    public static List<Item> GenerateAllDrops(PlayerClass playerClass, Random rand, int stage = 1)
    {
        List<Item> drops = new List<Item>();
        
        // 스테이지 보너스 (스테이지당 장비 드랍 +5%)
        double equipmentDropChance = BASE_EQUIPMENT_DROP_CHANCE + ((stage - 1) * 0.05);

        while (rand.NextDouble() < equipmentDropChance)
        {
            drops.Add(GenerateRandomEquipment(playerClass, rand, false, stage));
            equipmentDropChance /= 2.0; // (연속 드랍 페널티)
        }
        
        // (소비템 드랍률은 스테이지 영향 X, 단 희귀도는 영향 O)
        if (rand.NextDouble() < BASE_CONSUMABLE_DROP_CHANCE)
        {
            drops.Add(CreateRandomConsumable(rand, false, stage));
        }

        return drops;
    }
    
    public static List<Item> GenerateBossDrops(PlayerClass playerClass, Random rand, int stage = 1)
    {
        List<Item> drops = new List<Item>();
        
        // 보스는 '현재 스테이지' 기준으로 Rare+ 장비 1개 보장
        drops.Add(GenerateRandomEquipment(playerClass, rand, true, stage)); 
        
        // 추가 아이템 (스테이지별로 보상 증가)
        for(int i = 0; i < stage; i++)
        {
            drops.Add(GenerateRandomEquipment(playerClass, rand, true, stage));
            drops.Add(CreateRandomConsumable(rand, true, stage));
        }
        
        return drops;
    }

    // [수정] 소비 아이템 이름 생성 로직 변경 (3번 요청)
    public static Consumable CreateRandomConsumable(Random rand, bool isBossDrop = false, int stage = 1)    
    {
        // 1. 희귀도, 타입, 값 결정
        ItemRarity rarity;
        if (isBossDrop)
            rarity = GetRandomConsumableRarity_Boss(rand, stage);
        else
            rarity = GetRandomConsumableRarity(rand, stage);

        ConsumableType type = (rand.Next(0, 2) == 0) ? ConsumableType.HealthPotion : ConsumableType.ManaPotion;
        // ... (이하 동일) ...
        int baseValue = (type == ConsumableType.HealthPotion) ? 20 : 10;
        double multiplier = 1 + ((int)rarity * 0.75); 
        int value = (int)(baseValue * multiplier);
        string baseName = (type == ConsumableType.HealthPotion) ? "HP 물약" : "MP 물약";
        string prefix = consumablePrefixes[rarity]; 
        string name = $"[{rarity}] {prefix} {baseName}";
        return new Consumable(name, rarity, type, value);
    }

    // [수정] 장비 이름 생성 로직 변경 (3번 요청)
    public static Equipment GenerateRandomEquipment(PlayerClass playerClass, Random rand, bool isBossDrop = false, int stage = 1)
    {
        ItemRarity rarity;
        if (isBossDrop)
            rarity = GetRandomEquipmentRarity_Boss(rand, stage); 
        else
            rarity = GetRandomEquipmentRarity(rand, stage);
        Array slots = Enum.GetValues(typeof(EquipmentSlot));
        EquipmentSlot slot = (EquipmentSlot)slots.GetValue(rand.Next(slots.Length))!;
        
        bool isWeapon = (slot == EquipmentSlot.Weapon); 

        // 2. [수정] 이름 생성
        string baseName = baseItemNames[playerClass][slot];
        // [수정] GenerateEquipmentName 호출
        string name = GenerateEquipmentName(baseName, rarity, rand); 
        
        Equipment equip = new Equipment(name, rarity, slot, playerClass);
        
        // 3. 스탯 풀 생성
        List<StatType> availablePool = new List<StatType>();
        availablePool.AddRange(classPrimaryStats[playerClass]); 
        availablePool.AddRange(classSkillStats[playerClass]);   
        availablePool.AddRange(classSpecialStats[playerClass]); 
        availablePool.AddRange(commonUtilityStats);             

        if (isWeapon)
        {
            availablePool.AddRange(weaponOnlyStats);
        }
        else
        {
            availablePool.AddRange(armorOnlyStats);
        }

        // 4. 효과 개수 결정
        int modifierCount = 1;
        switch (rarity)
        {
            case ItemRarity.Common: modifierCount = rand.Next(1, 3); break; 
            case ItemRarity.Rare:   modifierCount = rand.Next(2, 4); break; 
            case ItemRarity.Unique: modifierCount = rand.Next(3, 5); break; 
            case ItemRarity.Legendary: modifierCount = rand.Next(4, 6); break; 
        }

        // 5. 보장 스탯 추가
        List<StatType> addedModifiers = new List<StatType>();
        StatType guaranteedStat = isWeapon ? StatType.ATK : StatType.DEF;
        
        equip.AddModifier(GetRandomModifier(guaranteedStat, rarity, rand, isWeapon));
        addedModifiers.Add(guaranteedStat);
        availablePool.Remove(guaranteedStat); 

        // 6. 나머지 랜덤 스탯 추가
        int remainingModifiers = modifierCount - addedModifiers.Count;
        for (int i = 0; i < remainingModifiers; i++)
        {
            if (availablePool.Count == 0) break; 
            StatType randomStat = availablePool[rand.Next(availablePool.Count)];
            availablePool.Remove(randomStat);
            
            equip.AddModifier(GetRandomModifier(randomStat, rarity, rand, isWeapon));
        }

        return equip;
    }

    // (희귀도 뽑기... 변경 없음)
    #region Rarity_Roll
    private static ItemRarity GetRandomEquipmentRarity(Random rand, int stage = 1)
    {
        // 스테이지당 Common 10% 감소, Rare/Unique/Legendary 증가
        double commonChance = Math.Max(0.20, BASE_EQ_COMMON_CHANCE - ((stage - 1) * 0.10)); // (60% -> 50% -> 40%)
        double rareChance = BASE_EQ_RARE_CHANCE + ((stage - 1) * 0.05);   // (25% -> 30% -> 35%)
        double uniqueChance = BASE_EQ_UNIQUE_CHANCE + ((stage - 1) * 0.03); // (10% -> 13% -> 16%)
        double legendaryChance = BASE_EQ_LEGENDARY_CHANCE + ((stage - 1) * 0.02); // (5% -> 7% -> 9%)

        double roll = rand.NextDouble();
        if (roll < legendaryChance) return ItemRarity.Legendary;
        if (roll < legendaryChance + uniqueChance) return ItemRarity.Unique;
        if (roll < legendaryChance + uniqueChance + rareChance) return ItemRarity.Rare;
        return ItemRarity.Common;
    }

    private static ItemRarity GetRandomConsumableRarity(Random rand, int stage = 1)
    {
        // (장비와 동일한 로직 적용)
        double commonChance = Math.Max(0.30, BASE_EQ_COMMON_CHANCE - ((stage - 1) * 0.10));
        double rareChance = BASE_EQ_RARE_CHANCE + ((stage - 1) * 0.05);
        double uniqueChance = BASE_EQ_UNIQUE_CHANCE + ((stage - 1) * 0.03);
        double legendaryChance = BASE_EQ_LEGENDARY_CHANCE + ((stage - 1) * 0.02);

        double roll = rand.NextDouble();
        if (roll < legendaryChance) return ItemRarity.Legendary;
        if (roll < legendaryChance + uniqueChance) return ItemRarity.Unique;
        if (roll < legendaryChance + uniqueChance + rareChance) return ItemRarity.Rare;
        return ItemRarity.Common;
    }
    #endregion

    // (스탯 값 생성... 변경 없음)
    #region Stat_Modifier_Generation
    private static StatModifier GetRandomModifier(StatType statToBuff, ItemRarity rarity, Random rand, bool isWeapon)
    {
        float rarityMultiplier = 1.0f + ((int)rarity * 0.5f);
        float value = 1;

        switch (statToBuff)
        {
            case StatType.HP:
                value = rand.Next(8, 16) * rarityMultiplier;
                break;
            case StatType.MP:
                value = rand.Next(5, 11) * rarityMultiplier;
                break;
            case StatType.STR:
            case StatType.INT:
            case StatType.DEX:
                value = rand.Next(1, 4) * rarityMultiplier;
                break;
            case StatType.ATK:
            case StatType.DEF:
                value = rand.Next(2, 5) * rarityMultiplier;
                break;

            case StatType.EXPGain:
                float expBase = rand.Next(5, 11);
                float expScale = (int)rarity * rand.Next(25, 31);
                float expValue = (expBase + expScale) * 0.01f;
                return new StatModifier(statToBuff, expValue, ModifierType.Percent);

            case StatType.PowerStrikeDamage:
            case StatType.FireballDamage:
            case StatType.HealAmount:
            case StatType.BackstabDamage:
            case StatType.PoisonStabDamage:
            case StatType.QuickAttackDamage:
            case StatType.ShieldBashDamage:
            case StatType.MagicMissileDamage:
                float skillBase = rand.Next(5, 11);
                float skillScale = (int)rarity * rand.Next(8, 13);
                float skillValue = (skillBase + skillScale) * 0.01f;
                if (isWeapon) { skillValue *= 1.5f; }
                return new StatModifier(statToBuff, skillValue, ModifierType.Percent);

            case StatType.DamageReflectChance:
                value = (rand.Next(4, 8) + ((int)rarity * 3.0f)) * 0.01f;
                return new StatModifier(statToBuff, value, ModifierType.Percent);

            case StatType.ManaRefundChance:
                value = (rand.Next(5, 8) + ((int)rarity * 5f)) * 0.01f;
                return new StatModifier(statToBuff, value, ModifierType.Percent);

            case StatType.LifeStealPercent:
                value = (rand.Next(1, 3) + ((int)rarity * 1.5f)) * 0.01f;
                return new StatModifier(statToBuff, value, ModifierType.Percent);

            case StatType.ResourceCostReduction: 
                value = (rand.Next(1, 4) + ((int)rarity * 1.5f)) * 0.01f; // (예: 1% ~ 7%)
                return new StatModifier(statToBuff, value, ModifierType.Percent);
            
            case StatType.StunChance: 
                value = (rand.Next(2, 5) + ((int)rarity * 2.0f)) * 0.01f; 
                return new StatModifier(statToBuff, value, ModifierType.Percent);

            case StatType.ManaShieldConversion: 
                // 마법사는 방어구가 아니어도 이 옵션을 얻을 수 있으므로 가치를 높게 설정
                value = (rand.Next(5, 11) + ((int)rarity * 5.0f)) * 0.01f; // (예: 5% ~ 25%)
                return new StatModifier(statToBuff, value, ModifierType.Percent);
            
            case StatType.BleedChance: 
                value = (rand.Next(3, 6) + ((int)rarity * 2.5f)) * 0.01f; // (예: 3% ~ 13%)
                return new StatModifier(statToBuff, value, ModifierType.Percent);

            default:
                value = rand.Next(1, 3) * rarityMultiplier;
                break;
        }

        return new StatModifier(statToBuff, (float)Math.Round(value), ModifierType.Flat);
    }
    
    private static ItemRarity GetRandomEquipmentRarity_Boss(Random rand, int stage = 1)
    {
        // (보스는 Rare 이상 보장 + 스테이지 보너스)
        double rareChance = BASE_EQ_RARE_CHANCE + ((stage - 1) * 0.05);
        double uniqueChance = BASE_EQ_UNIQUE_CHANCE + ((stage - 1) * 0.03);
        double legendaryChance = BASE_EQ_LEGENDARY_CHANCE + ((stage - 1) * 0.02);
        double total = rareChance + uniqueChance + legendaryChance;

        double roll = rand.NextDouble();
        if (roll < (legendaryChance / total)) return ItemRarity.Legendary;
        if (roll < ((legendaryChance + uniqueChance) / total)) return ItemRarity.Unique;
        return ItemRarity.Rare;
    }
    
    // [신규] 보스 드랍용 소비템 희귀도 (Rare 이상)
    private static ItemRarity GetRandomConsumableRarity_Boss(Random rand, int stage = 1)
    {
        // (보스는 Rare 이상 보장 + 스테이지 보너스)
        double rareChance = BASE_EQ_RARE_CHANCE + ((stage - 1) * 0.05);
        double uniqueChance = BASE_EQ_UNIQUE_CHANCE + ((stage - 1) * 0.03);
        double legendaryChance = BASE_EQ_LEGENDARY_CHANCE + ((stage - 1) * 0.02);
        double total = rareChance + uniqueChance + legendaryChance;
        
        double roll = rand.NextDouble();
        if (roll < (legendaryChance / total)) return ItemRarity.Legendary;
        if (roll < ((legendaryChance + uniqueChance) / total)) return ItemRarity.Unique;
        return ItemRarity.Rare;
    }
    #endregion
}