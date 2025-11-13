// Player.cs
using System.Collections.Generic;
using System.Linq;

public enum PlayerClass
{
    Warrior,
    Wizard,
    Rogue
}

public class Player
{
    // --- Y좌표 상수 ---
    private const int WARRIOR_ART_Y = 5;
    private const int WIZARD_ART_Y = 4;
    private const int ROGUE_ART_Y = 6;

    // [신규] X좌표 오프셋 상수 (기본 0)
    private const int WARRIOR_ART_X = 0;
    private const int WIZARD_ART_X = 10;
    private const int ROGUE_ART_X = -10;

    public int ArtOffsetY
    {
        get
        {
            switch (Class)
            {
                case PlayerClass.Warrior: return WARRIOR_ART_Y;
                case PlayerClass.Wizard: return WIZARD_ART_Y;
                case PlayerClass.Rogue: return ROGUE_ART_Y;
                default: return 2;
            }
        }
    }

    public int ArtOffsetX
    {
        get
        {
            switch (Class)
            {
                case PlayerClass.Warrior: return WARRIOR_ART_X;
                case PlayerClass.Wizard: return WIZARD_ART_X;
                case PlayerClass.Rogue: return ROGUE_ART_X;
                default: return 0;
            }
        }
    }
    

    // 위치
    public int X { get; set; }
    public int Y { get; set; }

    // 성장 스탯
    public int Level { get; private set; }
    public int EXP { get; private set; }
    public int EXPNext { get; private set; }

    // --- 스탯 로직 ---
    public int baseMaxHP, baseMaxMP, baseATK, baseDEF, baseSTR, baseINT, baseDEX;

    // 현재 체력
    public int HP { get; set; }
    public int MP { get; set; }

    // 장비를 포함한 최종 스탯 (계산된 속성)
    public int MaxHP => baseMaxHP + (int)GetStatBonus(StatType.HP, ModifierType.Flat);
    public int MaxMP => baseMaxMP + (int)GetStatBonus(StatType.MP, ModifierType.Flat);
    public int ATK => baseATK + (int)GetStatBonus(StatType.ATK, ModifierType.Flat);
    public int DEF => baseDEF + (int)GetStatBonus(StatType.DEF, ModifierType.Flat);
    public int STR => baseSTR + (int)GetStatBonus(StatType.STR, ModifierType.Flat);
    public int INT => baseINT + (int)GetStatBonus(StatType.INT, ModifierType.Flat);
    public int DEX => baseDEX + (int)GetStatBonus(StatType.DEX, ModifierType.Flat);
    public float CritChance => DEX * 0.005f;
    public Dictionary<EquipmentSlot, Equipment> EquippedGear { get; private set; }
    public List<Consumable> ConsumableInventory { get; private set; }

    public PlayerClass Class { get; private set; }
    public List<Skill> Skills { get; private set; }

    public Dictionary<StatType, int> StatusEffects { get; private set; }
    public int PoisonDamagePerTurn { get; set; } = 0;

    public Dictionary<string, (int old, int @new)> LastLevelUpStats { get; private set; } = new Dictionary<string, (int, int)>();
    public int LevelsGainedThisTurn { get; private set; } = 0;

    // 생성자
    public Player(PlayerClass playerClass)
    {
        Class = playerClass;
        Skills = new List<Skill>();
        EquippedGear = new Dictionary<EquipmentSlot, Equipment>();
        ConsumableInventory = new List<Consumable>();
        StatusEffects = new Dictionary<StatType, int>();
        SetInitialStats();
    }

    // 초기 스탯 설정
    private void SetInitialStats()
    {
        Level = 1;
        EXP = 0;
        
        // [수정] 100 또는 80 대신, 새 계산식으로 초기화
        EXPNext = CalculateEXPForNextLevel(1); // (73이 됨)

        Skills.Clear(); 
        EquippedGear.Clear();
        ConsumableInventory.Clear(); 
        
        AddConsumable(new Consumable("[Common] 조악한 HP 물약", ItemRarity.Common, ConsumableType.HealthPotion, 20));
        AddConsumable(new Consumable("[Common] 조악한 HP 물약", ItemRarity.Common, ConsumableType.HealthPotion, 20));
        AddConsumable(new Consumable("[Common] 조악한 MP 물약", ItemRarity.Common, ConsumableType.ManaPotion, 10));

       switch (Class)
        {
            case PlayerClass.Warrior:
                baseMaxHP = 30; baseMaxMP = 15;
                baseATK = 4; baseDEF = 4; baseSTR = 8; baseINT = 2; baseDEX = 2;
                Skills.Add(new PowerStrike());
                Skills.Add(new ShieldBash());
                Skills.Add(new MoraleBoost());
                Skills.Add(new Execution()); // [신규]
                break;
            case PlayerClass.Wizard:
                baseMaxHP = 20; baseMaxMP = 30;
                baseATK = 2; baseDEF = 2; baseSTR = 2; baseINT = 12; baseDEX = 2;
                Skills.Add(new Fireball());
                Skills.Add(new Heal());
                Skills.Add(new MagicMissile());
                Skills.Add(new Meteor()); // [신규]
                break;
            case PlayerClass.Rogue:
                baseMaxHP = 25; baseMaxMP = 15;
                baseATK = 3; baseDEF = 3; baseSTR = 2; baseINT = 2; baseDEX = 13;
                Skills.Add(new Backstab());
                Skills.Add(new PoisonStab());
                Skills.Add(new QuickAttack());
                Skills.Add(new Eviscerate()); // [신규]
                break;
        }
        
        HP = MaxHP;
        MP = MaxMP;
    }

    // [수정] 장비 장착
    public Equipment? EquipItem(Equipment newItem)
    {
        // 1. 장착 전 스탯 저장
        int oldMaxHP = MaxHP;
        int oldMaxMP = MaxMP;

        // 2. 장비 교체
        Equipment? oldItem = null;
        EquippedGear.TryGetValue(newItem.Slot, out oldItem);
        EquippedGear[newItem.Slot] = newItem;

        // 3. 증가량 계산
        int hpGain = MaxHP - oldMaxHP;
        int mpGain = MaxMP - oldMaxMP;

        // 4. 현재 체력/마나에 증가량 반영 (감소 시에는 적용 안 함)
        if (hpGain > 0)
        {
            HP += hpGain;
        }
        if (mpGain > 0)
        {
            MP += mpGain;
        }

        // 5. 스탯 최종 보정 (최대치 넘지 않도록)
        RecalculateStats();
        
        return oldItem; // 버려질 아이템 반환
    }

    // 스탯 재계산 (HP/MP 보정)
    private void RecalculateStats()
    {
        // 최대치가 변했으므로 현재 HP/MP를 최대치에 맞게 보정
        HP = Math.Min(HP, MaxHP);
        MP = Math.Min(MP, MaxMP);
    }
    
    // 장비로 인한 스탯 보너스 합산
    public float GetStatBonus(StatType stat, ModifierType type)
    {
        float bonus = 0;
        foreach (var equip in EquippedGear.Values)
        {
            foreach (var mod in equip.Modifiers)
            {
                if (mod.Stat == stat && mod.Type == type)
                {
                    bonus += mod.Value;
                }
            }
        }
        return bonus;
    }

    // 소비 아이템 추가
    public void AddConsumable(Consumable item)
    {
        ConsumableInventory.Add(item);
    }

    // [수정] 소비 아이템 사용 (타입 + "희귀도"로 찾아 사용)
    public bool UseConsumable(ConsumableType cType, ItemRarity rarity, Game game)
    {
        Consumable? itemToUse = ConsumableInventory.FirstOrDefault(item => item.CType == cType && item.Rarity == rarity);

        if (itemToUse == null)
        {
            game.AddLog("아이템이 없습니다.");
            return false; // 사용 실패
        }

        bool success = itemToUse.Use(this, game);
        if (success)
        {
            ConsumableInventory.Remove(itemToUse); // 사용했으면 리스트에서 제거
        }
        return success;
    }

    // 경험치 획득 및 레벨 업 처리
    public bool AddExperience(int expAmount)
    {
        EXP += expAmount;
        bool leveledUp = false;

        // [수정] 레벨 업 루프가 시작되기 전, '이전' 스탯을 저장합니다.
        LevelsGainedThisTurn = 0; // 초기화
        int oldBaseMaxHP = this.baseMaxHP;
        int oldBaseMaxMP = this.baseMaxMP;
        int oldBaseATK = this.baseATK;
        int oldBaseDEF = this.baseDEF;
        int oldBaseSTR = this.baseSTR;
        int oldBaseINT = this.baseINT;
        int oldBaseDEX = this.baseDEX;

        while (EXP >= EXPNext)
        {
            EXP -= EXPNext;
            Level++;
            leveledUp = true;
            LevelsGainedThisTurn++; 
            LevelUpStats(); // 스탯 상승 적용
            
            // [핵심 수정] (기존: EXPNext = (int)(EXPNext * 1.6);)
            // 새 계산식으로 다음 레벨의 요구 경험치를 갱신합니다.
            EXPNext = CalculateEXPForNextLevel(Level); 
        }

        // [신규] 레벨 업이 발생했다면, 변경 사항을 딕셔너리에 기록
        if (leveledUp)
        {
            LastLevelUpStats.Clear(); // 이전 기록 삭제

            // '이전' 스탯과 '현재' 스탯을 비교하여 기록
            if (baseMaxHP > oldBaseMaxHP) LastLevelUpStats["최대 HP"] = (oldBaseMaxHP, baseMaxHP);
            if (baseMaxMP > oldBaseMaxMP) LastLevelUpStats["최대 MP"] = (oldBaseMaxMP, baseMaxMP);
            if (baseATK > oldBaseATK) LastLevelUpStats["공격력"] = (oldBaseATK, baseATK);
            if (baseDEF > oldBaseDEF) LastLevelUpStats["방어력"] = (oldBaseDEF, baseDEF);
            if (baseSTR > oldBaseSTR) LastLevelUpStats["STR"] = (oldBaseSTR, baseSTR);
            if (baseINT > oldBaseINT) LastLevelUpStats["INT"] = (oldBaseINT, baseINT);
            if (baseDEX > oldBaseDEX) LastLevelUpStats["DEX"] = (oldBaseDEX, baseDEX);
            
            // (참고: Player.cs의 LevelUpStats 오타 수정)
            // HP = MaxHP;
            // MP = MaxMP; // (기존 코드에 MaxHP로 되어있던 오타 수정)
        }
        
        return leveledUp;
    }

    // 레벨 업 시 직업별 스탯 성장
    private void LevelUpStats()
    {
        switch (Class)
        {
            case PlayerClass.Warrior:
                baseMaxHP += 10; baseMaxMP += 5; baseSTR += 2; baseDEF += 1;
                break;
            case PlayerClass.Wizard:
                baseMaxHP += 4; baseMaxMP += 10; baseINT += 2;
                break;
            case PlayerClass.Rogue:
                baseMaxHP += 6; baseMaxMP += 5; baseATK += 1; baseDEX += 2;
                break;
        }
        HP = MaxHP;
        MP = MaxMP;
    }

    // [신규] 레벨별 요구 경험치 계산식 (다항식)
    private int CalculateEXPForNextLevel(int level)
    {
        // 1. 기본 요구치 (Lvl 1->2)
        int baseExp = 80;
        
        // 2. 레벨당 선형 증가 (Lvl * 10)
        int linearGrowth = 10 * level;
        
        // 3. 레벨 제곱에 비례한 증가 (Lvl^2 * 3)
        // (이것이 고레벨로 갈수록 급격히 어려워지는 핵심)
        int polynomialGrowth = (int)(4.0 * Math.Pow(level, 2));
        
        // 최종 요구 경험치 = 기본 + 선형 + 제곱
        return baseExp + linearGrowth + polynomialGrowth;
        
    }
}