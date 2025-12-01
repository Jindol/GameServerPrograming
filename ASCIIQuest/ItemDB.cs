// ItemDB.cs
using System;
using System.Collections.Generic;
// System.Linq: .Where(), .Cast() 등 컬렉션 처리에 사용될 수 있습니다. (현재 이 파일에서는 직접 사용되지 않음)
using System.Linq; 

/// <summary>
/// [핵심] 게임의 모든 아이템 생성을 전담하는 정적(static) 'Factory(공장)' 클래스입니다.
/// 'ItemDB.GenerateAllDrops()' 호출 한 번으로 복잡한 확률 계산, 이름 조합, 스탯 생성을
/// 모두 처리하여 완성된 'Item' 객체를 반환합니다.
/// </summary>
public static class ItemDB
{
    // --- 1. 기본 확률 상수 정의 ---
    #region Drop_Rates
    // 아이템 드랍률의 '기본값'을 정의하는 상수들입니다.
    // 이 값들은 이후 스테이지(stage)에 따라 동적으로 보정됩니다.
    
    // (일반 몬스터) 장비 드랍 기본 확률 (15%)
    private const double BASE_EQUIPMENT_DROP_CHANCE = 0.15; // (스테이지당 +8%씩 증가)
    // (일반 몬스터) 소비템 드랍 기본 확률 (40%)
    private const double BASE_CONSUMABLE_DROP_CHANCE = 0.40; 
    
    // (장비) 희귀도별 기본 확률
    private const double BASE_EQ_COMMON_CHANCE = 0.60; // (스테이지당 -15%씩 감소)
    private const double BASE_EQ_RARE_CHANCE = 0.25;   // (스테이지당 +7%씩 증가)
    private const double BASE_EQ_UNIQUE_CHANCE = 0.10; // (스테이지당 +5%씩 증가)
    private const double BASE_EQ_LEGENDARY_CHANCE = 0.05;// (스테이지당 +3%씩 증가)
    // (소비템) 희귀도별 기본 확률
    private const double CON_COMMON_CHANCE = 0.70;    
    private const double CON_RARE_CHANCE = 0.20;      
    private const double CON_UNIQUE_CHANCE = 0.08;    
    private const double CON_LEGENDARY_CHANCE = 0.02; 
    #endregion

    // --- 2. 아이템 이름 데이터 정의 ---
    #region Base_Item_Names
    /// <summary>
    /// 아이템의 '기본 이름'을 정의하는 2중 딕셔너리입니다.
    /// [직업][부위] -> "이름" (예: [Warrior][Weapon] -> "검")
    /// </summary>
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

    /// <summary>
    /// [핵심] 절차적(Procedural) 아이템 이름 생성을 위한 데이터 및 로직 영역입니다.
    /// </summary>
    #region Name_Generators
    
    /// <summary>
    /// (장비용) 희귀도에 따라 붙는 '접두사' 목록입니다. (MUD/컴퓨터 세계관 반영)
    /// </summary>
    private static readonly Dictionary<ItemRarity, List<string>> rarityPrefixes = new()
    {
        { ItemRarity.Common, new() { "손상된", "임시", "오래된", "디버그용" } },
        { ItemRarity.Rare, new() { "안정화된", "수정된", "컴파일된", "최적화된" } },
        { ItemRarity.Unique, new() { "인증된", "해시된", "암호화된", "가상" } },
        { ItemRarity.Legendary, new() { "커널", "태초의", "관리자의", "아스키" } }
    };

    /// <summary>
    /// (장비용) 희귀도에 따라 붙는 '칭호' 목록입니다. (MUD/컴퓨터 세계관 반영)
    /// </summary>
    private static readonly Dictionary<ItemRarity, List<string>> rarityTitles = new()
    {
        { ItemRarity.Common, new() { "데이터", "파일", "스크립트", "비트" } },
        { ItemRarity.Rare, new() { "프로세스", "모듈", "라이브러리", "패킷" } },
        { ItemRarity.Unique, new() { "방화벽", "세마포어", "알고리즘", "함수" } },
        { ItemRarity.Legendary, new() { "수호자", "시스템", "오류", "미궁" } }
    };

    /// <summary>
    /// (소비 아이템용) 희귀도에 따른 고정 접두사입니다.
    /// </summary>
    private static readonly Dictionary<ItemRarity, string> consumablePrefixes = new()
    {
        { ItemRarity.Common, "저용량" },
        { ItemRarity.Rare, "표준" },
        { ItemRarity.Unique, "고용량" },
        { ItemRarity.Legendary, "압축된" }
    };

    /// <summary>
    /// 아이템 이름 생성의 핵심 헬퍼 메서드입니다.
    /// 기본 이름, 접두사, 칭호를 희귀도별 확률에 따라 조합합니다.
    /// </summary>
    /// <returns>예: "[Rare] 최적화된 프로세스 검"</returns>
    private static string GenerateEquipmentName(string baseName, ItemRarity rarity, Random rand)
    {
        string prefix = "";
        string title = "";

        // 희귀도별로 접두사/칭호가 붙을 확률을 정의합니다.
        double prefixChance = 0.0;
        double titleChance = 0.0;
        switch (rarity)
        {
            case ItemRarity.Common: prefixChance = 0.5; titleChance = 0.2; break; // 50%, 20%
            case ItemRarity.Rare:   prefixChance = 1.0; titleChance = 0.5; break; // 100%, 50%
            case ItemRarity.Unique: prefixChance = 1.0; titleChance = 1.0; break; // 100%, 100%
            case ItemRarity.Legendary: prefixChance = 1.0; titleChance = 1.0; break; // 100%, 100%
        }

       // 확률 검사를 통과하면, 해당 희귀도의 이름 목록에서 랜덤으로 하나를 선택합니다.
       if (rand.NextDouble() < prefixChance)
        {
            var pList = rarityPrefixes[rarity];
            prefix = pList[rand.Next(pList.Count)];
        }
        if (rand.NextDouble() < titleChance)
        {
            var tList = rarityTitles[rarity];
            title = tList[rand.Next(tList.Count)];
        }
        
        // 이름 조합: "[희귀도] (접두사) (칭호) 기본이름"
        string combinedName = baseName;
        if (!string.IsNullOrEmpty(title)) combinedName = $"{title} {combinedName}";
        if (!string.IsNullOrEmpty(prefix)) combinedName = $"{prefix} {combinedName}";

        return $"[{rarity}] {combinedName}";
    }
    
    #endregion

    // --- 3. 아이템 스탯 데이터 정의 ---
    /// <summary>
    /// '클래스 특화' 아이템을 생성하기 위해, 직업별로 아이템에 부여될 수 있는 스탯의 '종류'를 그룹화합니다.
    /// </summary>
    #region Stat_Pools
    // 직업별 주 스탯
    private static Dictionary<PlayerClass, List<StatType>> classPrimaryStats = new() {
        { PlayerClass.Warrior, new() { StatType.STR }},
        { PlayerClass.Wizard, new() { StatType.INT }},
        { PlayerClass.Rogue, new() { StatType.DEX }}
    };
    // 직업별 스킬 강화 스탯
    private static Dictionary<PlayerClass, List<StatType>> classSkillStats = new() {
        { PlayerClass.Warrior, new() { 
            StatType.PowerStrikeDamage, StatType.ShieldBashDamage, StatType.ExecutionDamage // [추가]
        }},
        { PlayerClass.Wizard, new() { 
            StatType.FireballDamage, StatType.MagicMissileDamage, StatType.HealAmount, StatType.MeteorDamage // [추가]
        }},
        { PlayerClass.Rogue, new() { 
            StatType.BackstabDamage, StatType.PoisonStabDamage, StatType.QuickAttackDamage, StatType.EviscerateDamage // [추가]
        }}
    };
    // 직업별 특수 스탯
    private static Dictionary<PlayerClass, List<StatType>> classSpecialStats = new() {
        { PlayerClass.Warrior, new() { StatType.DamageReflectChance, StatType.StunChance }},
        { PlayerClass.Wizard, new() { StatType.ManaRefundChance, StatType.ManaShieldConversion }},
        { PlayerClass.Rogue, new() { StatType.LifeStealPercent, StatType.BleedChance }}
    };
    // 공용 유틸리티 스탯
    private static List<StatType> commonUtilityStats = new() { 
        StatType.MP, StatType.EXPGain, StatType.ResourceCostReduction
    };
    // 무기 전용 스탯 (ATK)
    private static List<StatType> weaponOnlyStats = new() { 
        StatType.ATK
    };
    // 방어구(무기 외) 전용 스탯 (HP, DEF)
    private static List<StatType> armorOnlyStats = new() { 
        StatType.HP, StatType.DEF 
    };
    #endregion

    // --- 4. 아이템 생성 (Factory) 메인 메서드 ---

    /// <summary>
    /// (일반 몬스터) 사냥 시 호출되는 아이템 드랍 생성기입니다.
    /// </summary>
    /// <param name="playerClass">플레이어 직업 (클래스 특화 아이템 생성용)</param>
    /// <param name="rand">랜덤 객체</param>
    /// <param name="stage">현재 스테이지 (드랍률 및 아이템 품질 스케일링용)</param>
    /// <returns>드랍된 아이템(장비, 소비템) 리스트</returns>
    public static List<Item> GenerateAllDrops(PlayerClass playerClass, Random rand, int stage = 1)
    {
        List<Item> drops = new List<Item>();
        
        // 1. 스테이지별 장비 드랍률 계산 (기본 15% + 스테이지당 8% 증가)
        double equipmentDropChance = BASE_EQUIPMENT_DROP_CHANCE + ((stage - 1) * 0.08); // (15% -> 23% -> 31%)
        
        // 2. 장비 드랍 (다중 드랍 지원)
        // (예: 23% 확률로 1개 드랍, 11.5% 확률로 2개 드랍, 5.75% 확률로 3개 드랍...)
        while (rand.NextDouble() < equipmentDropChance)
        {
            drops.Add(GenerateRandomEquipment(playerClass, rand, false, stage));
            equipmentDropChance /= 2.0; // 다음 장비 드랍 확률은 절반으로 감소
        }
        
        // 3. 소비템 드랍 (고정 40%)
        if (rand.NextDouble() < BASE_CONSUMABLE_DROP_CHANCE)
        {
            drops.Add(CreateRandomConsumable(rand, false, stage));
        }
        return drops;
    }
    
    /// <summary>
    /// (메인 보스) 사냥 시 호출되는 아이템 드랍 생성기입니다. (더 좋은 보상 보장)
    /// </summary>
    public static List<Item> GenerateBossDrops(PlayerClass playerClass, Random rand, int stage = 1)
    {
        List<Item> drops = new List<Item>();
        // 최소 1개의 '보스 품질' 장비 보장
        drops.Add(GenerateRandomEquipment(playerClass, rand, true, stage)); 
        // 스테이지 수만큼 장비와 소비템을 추가로 드랍
        for(int i = 0; i < stage; i++)
        {
            drops.Add(GenerateRandomEquipment(playerClass, rand, true, stage));
            drops.Add(CreateRandomConsumable(rand, true, stage));
        }
        return drops;
    }

    /// <summary>
    /// 소비 아이템을 생성하는 'Factory' 메서드입니다.
    /// </summary>
    /// <param name="isBossDrop">보스 드랍 여부 (희귀도 보정용)</param>
    /// <param name="stage">현재 스테이지 (희귀도 및 품질 보정용)</param>
    /// <returns>생성된 Consumable 객체</returns>
   public static Consumable CreateRandomConsumable(Random rand, bool isBossDrop = false, int stage = 1)
    {
        // 1. 희귀도 결정 (보스 드랍/일반 드랍, 스테이지별로 확률 보정)
        ItemRarity rarity;
        if (isBossDrop)
            rarity = GetRandomConsumableRarity_Boss(rand, stage);
        else
            rarity = GetRandomConsumableRarity(rand, stage);

        // 2. 타입 결정 (HP 물약 또는 MP 물약, 50% 확률)
        ConsumableType type = (rand.Next(0, 2) == 0) ? ConsumableType.HealthPotion : ConsumableType.ManaPotion;
        
        // 3. '최소 회복량'(Value) 계산 (희귀도에 따라 증가)
        int baseValue = (type == ConsumableType.HealthPotion) ? 20 : 10;
        double multiplier = 1 + ((int)rarity * 0.75); // (Common *1, Rare *1.75, ...)
        int value = (int)(baseValue * multiplier); // (Common 20, Rare 35, Unique 50, Leg 65)

        // 4. 이름 생성 (예: "[Rare] 표준 HP 물약")
        string baseName = (type == ConsumableType.HealthPotion) ? "HP 물약" : "MP 물약"; 
        string prefix = consumablePrefixes[rarity];
        string name = $"[{rarity}] {prefix} {baseName}";

        // 5. Consumable 객체 생성 및 반환
        return new Consumable(name, rarity, type, value);
    }

    /// <summary>
    /// [핵심] 장비 아이템을 '조립'하는 가장 중요한 Factory 메서드입니다.
    /// </summary>
    /// <param name="playerClass">플레이어 직업 (스탯 풀, 이름 결정용)</param>
    /// <param name="isBossDrop">보스 드랍 여부 (희귀도 보정용)</param>
    /// <param name="stage">현재 스테이지 (희귀도 및 스탯 품질 보정용)</param>
    /// <returns>스탯 옵션이 모두 추가된 완성된 Equipment 객체</returns>
    public static Equipment GenerateRandomEquipment(PlayerClass playerClass, Random rand, bool isBossDrop = false, int stage = 1)
    {
        // 1. 희귀도 결정
        ItemRarity rarity;
        if (isBossDrop) rarity = GetRandomEquipmentRarity_Boss(rand, stage);
        else rarity = GetRandomEquipmentRarity(rand, stage);
        
        // 2. 장비 부위(Slot) 랜덤 결정
        Array slots = Enum.GetValues(typeof(EquipmentSlot));
        EquipmentSlot slot = (EquipmentSlot)slots.GetValue(rand.Next(slots.Length))!;
        bool isWeapon = (slot == EquipmentSlot.Weapon); 
        
        // 3. 이름 생성 (예: "[Rare] 최적화된 검")
        string baseName = baseItemNames[playerClass][slot];
        string name = GenerateEquipmentName(baseName, rarity, rand);
        
        // 4. 아이템 '껍데기(Shell)' 생성
        Equipment equip = new Equipment(name, rarity, slot, playerClass);
        
        // 5. '스탯 후보 목록(availablePool)' 생성
        // (직업별 주 스탯 + 스킬 스탯 + 특수 스탯 + 공용 스탯을 모두 합침)
        List<StatType> availablePool = new List<StatType>();
        availablePool.AddRange(classPrimaryStats[playerClass]);
        availablePool.AddRange(classSkillStats[playerClass]);  
        availablePool.AddRange(classSpecialStats[playerClass]);
        availablePool.AddRange(commonUtilityStats);            

        // (무기/방어구 전용 스탯 추가)
        if (isWeapon)
        {
            availablePool.AddRange(weaponOnlyStats);
        }
        else
        {
            availablePool.AddRange(armorOnlyStats);
        }

        // 6. 희귀도에 따른 '옵션 개수' 결정
        int modifierCount = 1;
        switch (rarity)
        {
            // (Common: 1~2개, Rare: 2~3개, Unique: 4~5개, Legendary: 6~7개)
            case ItemRarity.Common: modifierCount = rand.Next(1, 3); break; 
            case ItemRarity.Rare:   modifierCount = rand.Next(2, 4); break; 
            case ItemRarity.Unique: modifierCount = rand.Next(4, 6); break; 
            case ItemRarity.Legendary: modifierCount = rand.Next(6, 8); break; 
        }

        // 7. '보장 스탯' 추가 (무기는 ATK, 방어구는 DEF를 최소 1개 보장)
        List<StatType> addedModifiers = new List<StatType>();
        StatType guaranteedStat = isWeapon ? StatType.ATK : StatType.DEF;
        
        // [핵심] GetRandomModifier를 호출하여 스탯 '값'을 계산하고 아이템에 '주입'
        equip.AddModifier(GetRandomModifier(playerClass, guaranteedStat, rarity, rand, isWeapon));
        addedModifiers.Add(guaranteedStat);
        availablePool.Remove(guaranteedStat); // 후보 목록에서 제거 (중복 방지)

        // 8. '나머지 랜덤 스탯' 추가
        int remainingModifiers = modifierCount - addedModifiers.Count;
        for (int i = 0; i < remainingModifiers; i++)
        {
            if (availablePool.Count == 0) break; // (후보가 고갈되면 중단)
            
            // 후보 목록에서 랜덤으로 스탯 '종류'를 하나 뽑음
            StatType randomStat = availablePool[rand.Next(availablePool.Count)];
            availablePool.Remove(randomStat); // (중복 방지)
            
            // [핵심] 해당 스탯의 '값'을 계산하여 아이템에 '주입'
            equip.AddModifier(GetRandomModifier(playerClass, randomStat, rarity, rand, isWeapon));
        }

        // 9. 완성된 장비 반환
        return equip;
    }

    // --- 5. 확률 계산 헬퍼 메서드 ---
    /// <summary>
    /// 아이템 희귀도를 '결정'하는 확률 헬퍼 메서드들입니다.
    /// </summary>
    #region Rarity_Roll
    
    /// <summary>
    /// (일반 드랍) 스테이지에 따라 보정된 장비 희귀도를 랜덤으로 반환합니다.
    /// </summary>
    private static ItemRarity GetRandomEquipmentRarity(Random rand, int stage = 1)
    {
        // [핵심] 스테이지가 오를수록 Common 확률은 낮아지고(최소 20%), Rare 이상 확률은 높아집니다.
        // (예: Stage 1 = 60%, Stage 2 = 45%, Stage 3 = 30%)
        double commonChance = Math.Max(0.20, BASE_EQ_COMMON_CHANCE - ((stage - 1) * 0.15));
        // (예: Stage 1 = 25%, Stage 2 = 32%, Stage 3 = 39%)
        double rareChance = BASE_EQ_RARE_CHANCE + ((stage - 1) * 0.07);
        // (예: Stage 1 = 10%, Stage 2 = 15%, Stage 3 = 20%)
        double uniqueChance = BASE_EQ_UNIQUE_CHANCE + ((stage - 1) * 0.05);
        // (예: Stage 1 = 5%, Stage 2 = 8%, Stage 3 = 11%)
        double legendaryChance = BASE_EQ_LEGENDARY_CHANCE + ((stage - 1) * 0.03);

        // 0.0 ~ 1.0 사이의 난수를 굴려 희귀도를 결정합니다.
        double roll = rand.NextDouble();
        if (roll < legendaryChance) return ItemRarity.Legendary; // (0.0 ~ 0.05)
        if (roll < legendaryChance + uniqueChance) return ItemRarity.Unique; // (0.05 ~ 0.15)
        if (roll < legendaryChance + uniqueChance + rareChance) return ItemRarity.Rare; // (0.15 ~ 0.40)
        return ItemRarity.Common; // (0.40 ~ 1.0)
    }

    /// <summary>
    /// (일반 드랍) 스테이지에 따라 보정된 소비 아이템 희귀도를 랜덤으로 반환합니다.
    /// (장비 희귀도 계산 로직과 동일한 스케일링을 사용)
    /// </summary>
    private static ItemRarity GetRandomConsumableRarity(Random rand, int stage = 1)
    {
        double commonChance = Math.Max(0.30, BASE_EQ_COMMON_CHANCE - ((stage - 1) * 0.15));
        double rareChance = BASE_EQ_RARE_CHANCE + ((stage - 1) * 0.07);
        double uniqueChance = BASE_EQ_UNIQUE_CHANCE + ((stage - 1) * 0.05);
        double legendaryChance = BASE_EQ_LEGENDARY_CHANCE + ((stage - 1) * 0.03);

        double roll = rand.NextDouble();
        if (roll < legendaryChance) return ItemRarity.Legendary;
        if (roll < legendaryChance + uniqueChance) return ItemRarity.Unique;
        if (roll < legendaryChance + uniqueChance + rareChance) return ItemRarity.Rare;
        return ItemRarity.Common;
    }
    #endregion

    // --- 6. 스탯 수치 생성 헬퍼 메서드 ---
    /// <summary>
    /// 아이템에 붙을 '단일 스탯 옵션'의 '수치'를 계산하는 로직입니다.
    /// </summary>
    #region Stat_Modifier_Generation
    
    /// <summary>
    /// [핵심 2] 스탯의 실제 '값'을 생성하는 헬퍼 메서드입니다.
    /// (GenerateRandomEquipment에서 호출됨)
    /// </summary>
    /// <param name="playerClass">직업 (밸런싱 보정용)</param>
    /// <param name="statToBuff">값을 생성할 스탯의 '종류' (예: StatType.HP)</param>
    /// <param name="rarity">희귀도 (값 범위 설정용)</param>
    /// <param name="isWeapon">무기 여부 (밸런싱 보정용)</param>
    /// <returns>완성된 StatModifier 객체 (예: "HP +15")</returns>
   private static StatModifier GetRandomModifier(PlayerClass playerClass, StatType statToBuff, ItemRarity rarity, Random rand, bool isWeapon)
    {
        float value = 1;
        int min, max; 

        switch (statToBuff)
        {
            // --- 1. 기본 스탯 (직업별 밸런싱 적용) ---
            case StatType.HP:
                switch (rarity)
                {
                    case ItemRarity.Rare:      min = 16; max = 24; break;
                    case ItemRarity.Unique:    min = 28; max = 40; break; 
                    case ItemRarity.Legendary: min = 45; max = 65; break; 
                    default: min = 8; max = 15; break;
                }
                value = rand.Next(min, max + 1);
                
                float hpMultiplier = 1.0f;
                if (!isWeapon) 
                {
                    if (playerClass == PlayerClass.Warrior) hpMultiplier = 1.3f; 
                    else hpMultiplier = 0.8f; 
                }
                value = Math.Max(1.0f, value * hpMultiplier); 
                break;

            case StatType.MP:
                switch (rarity)
                {
                    case ItemRarity.Rare:      min = 11; max = 16; break;
                    case ItemRarity.Unique:    min = 18; max = 25; break; 
                    case ItemRarity.Legendary: min = 28; max = 40; break; 
                    default: min = 5; max = 10; break;
                }
                value = rand.Next(min, max + 1);

                // 도적 MP 상향, 전사 하향, 마법사 소폭 하향
                float manaMultiplier = 1.0f;
                if (playerClass == PlayerClass.Wizard) manaMultiplier = 0.8f; 
                else if (playerClass == PlayerClass.Rogue) manaMultiplier = 0.6f; 
                else manaMultiplier = 0.5f; 
                value = Math.Max(1.0f, value * manaMultiplier); 
                break;

            case StatType.STR:
            case StatType.INT:
            case StatType.DEX:
                switch (rarity)
                {
                    case ItemRarity.Rare:      min = 3; max = 4; break;
                    case ItemRarity.Unique:    min = 5; max = 7; break; 
                    case ItemRarity.Legendary: min = 8; max = 12; break; 
                    default: min = 1; max = 2; break;
                }
                value = rand.Next(min, max + 1);
                break;

            case StatType.ATK:
                 switch (rarity)
                {
                    case ItemRarity.Rare:      min = 5; max = 7; break;
                    case ItemRarity.Unique:    min = 9; max = 13; break; 
                    case ItemRarity.Legendary: min = 15; max = 22; break; 
                    default: min = 2; max = 4; break;
                }
                value = rand.Next(min, max + 1);
                break;

            case StatType.DEF:
                 switch (rarity)
                {
                    case ItemRarity.Rare:      min = 5; max = 7; break;
                    case ItemRarity.Unique:    min = 9; max = 13; break; 
                    case ItemRarity.Legendary: min = 15; max = 22; break; 
                    default: min = 2; max = 4; break;
                }
                value = rand.Next(min, max + 1);
                
                float defMultiplier = 1.0f;
                if (!isWeapon) 
                {
                    if (playerClass == PlayerClass.Warrior) defMultiplier = 1.3f; 
                    else defMultiplier = 0.8f; 
                }
                value = Math.Max(1.0f, value * defMultiplier); 
                break;

            // --- 2. 경험치 증가 (특수) ---
            case StatType.EXPGain:
                float expBase = rand.Next(5, 11);
                float expScale = (int)rarity * rand.Next(25, 31);
                float expValue = (expBase + expScale) * 0.01f;
                return new StatModifier(statToBuff, expValue, ModifierType.Percent);

            // --- 3. 스킬 데미지 % 증가 (모든 스킬 포함) ---
            case StatType.PowerStrikeDamage:
            case StatType.ShieldBashDamage:
            case StatType.ExecutionDamage:    // [신규]
            case StatType.FireballDamage:
            case StatType.HealAmount:
            case StatType.MagicMissileDamage:
            case StatType.MeteorDamage:       // [신규]
            case StatType.BackstabDamage:
            case StatType.PoisonStabDamage:
            case StatType.QuickAttackDamage:
            case StatType.EviscerateDamage:   // [신규]
                
                float skillBase = rand.Next(5, 11);
                float skillScale = (int)rarity * rand.Next(8, 13);
                float skillValue = (skillBase + skillScale) * 0.01f;
                
                // 무기에 붙으면 효과 1.5배
                if (isWeapon) { skillValue *= 1.5f; }
                
                return new StatModifier(statToBuff, skillValue, ModifierType.Percent);

            // --- 4. 직업별 특수 효과 % (등급별 상향 적용됨) ---
            
            case StatType.DamageReflectChance: // 전사: 피해 반사
                value = (rand.Next(4, 8) + ((int)rarity * (3.0f + (int)rarity))) * 0.01f;
                return new StatModifier(statToBuff, value, ModifierType.Percent);

            case StatType.StunChance:          // 전사: 기절
                value = (rand.Next(2, 5) + ((int)rarity * (2.0f + (int)rarity))) * 0.01f; 
                return new StatModifier(statToBuff, value, ModifierType.Percent);

            case StatType.ManaRefundChance:    // 마법사: 마나 환급
                value = (rand.Next(5, 8) + ((int)rarity * (5f + (int)rarity))) * 0.01f;
                return new StatModifier(statToBuff, value, ModifierType.Percent);

            case StatType.ManaShieldConversion:// 마법사: 마나 보호막
                value = (rand.Next(5, 11) + ((int)rarity * (5.0f + (int)rarity))) * 0.01f;
                return new StatModifier(statToBuff, value, ModifierType.Percent);

            case StatType.LifeStealPercent:    // 도적: 흡혈
                value = (rand.Next(1, 3) + ((int)rarity * (1.5f + (int)rarity))) * 0.01f;
                return new StatModifier(statToBuff, value, ModifierType.Percent);

            case StatType.BleedChance:         // 도적: 출혈
                value = (rand.Next(3, 6) + ((int)rarity * (2.5f + (int)rarity))) * 0.01f;
                return new StatModifier(statToBuff, value, ModifierType.Percent);

            case StatType.ResourceCostReduction: // 공용: 자원 소모 감소
                value = (rand.Next(1, 4) + ((int)rarity * (1.5f + (int)rarity))) * 0.01f; 
                return new StatModifier(statToBuff, value, ModifierType.Percent);
            
            // --- 그 외 (Fallback) ---
            default:
                 switch (rarity)
                {
                    case ItemRarity.Rare:      min = 3; max = 4; break;
                    case ItemRarity.Unique:    min = 5; max = 6; break;
                    case ItemRarity.Legendary: min = 7; max = 8; break;
                    default: min = 1; max = 2; break;
                }
                value = rand.Next(min, max + 1);
                break;
        }

        // 기본은 Flat(고정값) 반환 (위에서 return된 % 스탯 제외)
        return new StatModifier(statToBuff, (float)Math.Round(value), ModifierType.Flat);
    }
    
    /// <summary>
    /// (보스 드랍) 희귀도를 랜덤으로 반환합니다. (Rare 등급 이상 보장)
    /// </summary>
    private static ItemRarity GetRandomEquipmentRarity_Boss(Random rand, int stage = 1)
    {
        // (보스는 Rare 이상을 보장하며, 그 안에서 Rare/Unique/Legendary 확률을 계산)
        double rareChance = BASE_EQ_RARE_CHANCE + ((stage - 1) * 0.05);
        double uniqueChance = BASE_EQ_UNIQUE_CHANCE + ((stage - 1) * 0.03);
        double legendaryChance = BASE_EQ_LEGENDARY_CHANCE + ((stage - 1) * 0.02);
        double total = rareChance + uniqueChance + legendaryChance;

        double roll = rand.NextDouble();
        // (보정된 확률에 따라 추첨)
        if (roll < (legendaryChance / total)) return ItemRarity.Legendary;
        if (roll < ((legendaryChance + uniqueChance) / total)) return ItemRarity.Unique;
        return ItemRarity.Rare;
    }
    
    /// <summary>
    /// (보스 드랍) 소비 아이템 희귀도를 랜덤으로 반환합니다. (Rare 등급 이상 보장)
    /// </summary>
    private static ItemRarity GetRandomConsumableRarity_Boss(Random rand, int stage = 1)
    {
        // (장비 로직과 동일)
        double rareChance = BASE_EQ_RARE_CHANCE + ((stage - 1) * 0.05);
        double uniqueChance = BASE_EQ_UNIQUE_CHANCE + ((stage - 1) * 0.03);
        double legendaryChance = BASE_EQ_LEGENDARY_CHANCE + ((stage - 1) * 0.02);
        double total = rareChance + uniqueChance + legendaryChance;
        
        double roll = rand.NextDouble();
        if (roll < (legendaryChance / total)) return ItemRarity.Legendary;
        if (roll < ((legendaryChance + uniqueChance) / total)) return ItemRarity.Unique;
        return ItemRarity.Rare;
    }

    /// <summary>
    /// (필드 보스) 사냥 시 호출되는 아이템 드랍 생성기입니다.
    /// (일반 몬스터보다 드랍률이 높고, 다음 스테이지 수준의 아이템을 줍니다.)
    /// </summary>
    public static List<Item> GenerateFieldBossDrops(PlayerClass playerClass, Random rand, int stage = 1)
    {
        List<Item> drops = new List<Item>();
        // (장비 드랍률 +30% 보너스)
        double equipmentDropChance = BASE_EQUIPMENT_DROP_CHANCE + ((stage - 1) * 0.05) + 0.30; 
        // (아이템 품질은 (현재 스테이지 + 1) 수준으로 설정)
        int lootStage = stage + 1; 
        
        if (rand.NextDouble() < equipmentDropChance)
        {
            drops.Add(GenerateRandomEquipment(playerClass, rand, false, lootStage));
        }
        drops.Add(CreateRandomConsumable(rand, false, lootStage));
        return drops;
    }
    #endregion
}