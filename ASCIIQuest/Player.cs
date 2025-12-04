// Player.cs

// System.Collections.Generic: List<T>, Dictionary<TKey, TValue> 사용을 위해 필요
using System.Collections.Generic;
// System.Linq: .FirstOrDefault() 등 LINQ 메서드 사용을 위해 필요
using System.Linq;

/// <summary>
/// 플레이어의 3가지 기본 직업을 정의합니다.
/// </summary>
public enum PlayerClass
{
    Warrior,
    Wizard,
    Rogue
}

/// <summary>
/// [핵심] 플레이어의 모든 데이터를 저장하고 관리하는 'Actor' 클래스입니다.
/// 레벨, 경험치, 스탯, 장비, 인벤토리, 스킬 등 플레이어와 관련된
/// 모든 정보를 캡슐화합니다.
/// </summary>
public class Player
{
    // --- 1. 아트 오프셋 상수 및 속성 ---
    
    // (전투 화면) 직업별 아트를 세로(Y)로 보정하기 위한 상수
    private const int WARRIOR_ART_Y = 0;
    private const int WIZARD_ART_Y = 0;
    private const int ROGUE_ART_Y = 0;
    
    // (전투 화면) 직업별 아트를 가로(X)로 보정하기 위한 상수
    private const int WARRIOR_ART_X = 0;
    private const int WIZARD_ART_X = 10;
    private const int ROGUE_ART_X = -10;

    public bool IsDead => HP <= 0;

    /// <summary>
    /// (전투 화면) 현재 직업에 맞는 '세로(Y) 아트 보정값'을 반환하는 계산된 속성입니다.
    /// </summary>
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

    /// <summary>
    /// (전투 화면) 현재 직업에 맞는 '가로(X) 아트 보정값'을 반환하는 계산된 속성입니다.
    /// </summary>
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
    
    // --- 2. 위치 및 성장 데이터 ---
    
    /// <summary>
    /// (월드 탐험) 플레이어의 현재 맵 X좌표
    /// </summary>
    public int X { get; set; }
    
    /// <summary>
    /// (월드 탐험) 플레이어의 현재 맵 Y좌표
    /// </summary>
    public int Y { get; set; }

    /// <summary>
    /// 플레이어의 현재 레벨
    /// </summary>
    public int Level { get; private set; }
    
    /// <summary>
    /// 현재 레벨에서 쌓인 경험치
    /// </summary>
    public int EXP { get; private set; }
    
    /// <summary>
    /// 다음 레벨 업에 필요한 총 경험치
    /// </summary>
    public int EXPNext { get; private set; }

    // --- 3. 스탯 (Base vs Calculated) ---
    
    /// <summary>
    /// '순수 기본 스탯' 변수들입니다. (오직 레벨 업을 통해서만 증가합니다.)
    /// </summary>
    public int baseMaxHP, baseMaxMP, baseATK, baseDEF, baseSTR, baseINT, baseDEX;

    /// <summary>
    /// 플레이어의 '현재' 체력
    /// </summary>
    // [수정] 실제 값을 저장할 private 변수
    private int _hp;

    // [수정] HP 속성에 로직 추가 (0 ~ MaxHP 범위 제한)
    public int HP 
    { 
        get => _hp; 
        set 
        {
            // 들어오는 값(value)을 0과 MaxHP 사이로 제한
            _hp = Math.Max(0, Math.Min(value, MaxHP));
        }
    }    
    /// <summary>
    /// 플레이어의 '현재' 마나
    /// </summary>
    public int MP { get; set; }

    // --- [핵심] 계산된 속성 (Calculated Properties) ---
    // 'player.MaxHP'처럼 호출 시, '기본 스탯'과 '장비 보너스 스탯'을 실시간으로 합산하여 반환합니다.
    
    /// <summary>
    /// (기본 MaxHP + 장비의 모든 HP 보너스)를 합산한 '최종 최대 HP'
    /// </summary>
    public int MaxHP => baseMaxHP + (int)GetStatBonus(StatType.HP, ModifierType.Flat);
    
    /// <summary>
    /// (기본 MaxMP + 장비의 모든 MP 보너스)를 합산한 '최종 최대 MP'
    /// </summary>
    public int MaxMP => baseMaxMP + (int)GetStatBonus(StatType.MP, ModifierType.Flat);
    
    /// <summary>
    /// (기본 ATK + 장비의 모든 ATK 보너스)를 합산한 '최종 공격력'
    /// </summary>
    public int ATK => baseATK + (int)GetStatBonus(StatType.ATK, ModifierType.Flat);
    
    /// <summary>
    /// (기본 DEF + 임시 버프 + 장비 보너스)를 합산한 '최종 방어력'
    /// </summary>
    public int DEF => baseDEF + TempDefBuff + (int)GetStatBonus(StatType.DEF, ModifierType.Flat);
    
    /// <summary>
    /// (기본 STR + 장비 보너스)를 합산한 '최종 STR'
    /// </summary>
    public int STR => baseSTR + (int)GetStatBonus(StatType.STR, ModifierType.Flat);
    
    /// <summary>
    /// (기본 INT + 장비 보너스)를 합산한 '최종 INT'
    /// </summary>
    public int INT => baseINT + (int)GetStatBonus(StatType.INT, ModifierType.Flat);
    
    /// <summary>
    /// (기본 DEX + 장비 보너스)를 합산한 '최종 DEX'
    /// </summary>
    public int DEX => baseDEX + (int)GetStatBonus(StatType.DEX, ModifierType.Flat);
    
    /// <summary>
    /// (최종 DEX * 0.01)을 합산한 '최종 크리티컬 확률' (예: DEX 10 -> 0.1, 즉 10%)
    /// </summary>
    public float CritChance => DEX * 0.01f;
    
    // --- 4. 인벤토리 및 스킬 ---
    
    /// <summary>
    /// 현재 '착용 중인' 장비 딕셔너리입니다.
    /// (Key: EquipmentSlot.Weapon, Value: Equipment 객체)
    /// </summary>
    public Dictionary<EquipmentSlot, Equipment> EquippedGear { get; private set; }
    
    /// <summary>
    /// '보유 중인' 모든 소비 아이템 리스트입니다. (중복 아이템 포함)
    /// </summary>
    public List<Consumable> ConsumableInventory { get; private set; }

    /// <summary>
    /// 현재 플레이어의 직업 (Warrior, Wizard, Rogue)
    /// </summary>
    public PlayerClass Class { get; private set; }
    
    /// <summary>
    /// 플레이어가 보유한 스킬 리스트입니다.
    /// </summary>
    public List<Skill> Skills { get; private set; }

    // --- 5. 상태 효과 및 UI ---
    
    /// <summary>
    /// 플레이어에게 적용 중인 '버프'/'디버프'와 남은 턴 수를 저장합니다.
    /// (예: [StatType.DEF, 5] -> 방어력 버프 5턴 남음)
    /// </summary>
    public Dictionary<StatType, int> StatusEffects { get; private set; }
    
    /// <summary>
    /// (아직 이 코드에서는 사용되지 않음 - 몬스터용)
    /// </summary>
    public int PoisonDamagePerTurn { get; set; } = 0;
    
    /// <summary>
    /// 스킬(예: IronWill)로 인해 '임시로' 증가한 방어력 수치입니다.
    /// (버프가 만료되면 0이 됩니다.)
    /// </summary>
    public int TempDefBuff { get; set; } = 0;

    /// <summary>
    /// (UI 표시용) 레벨 업 시, "오르기 전" 스탯과 "오른 후" 스탯을 기록합니다.
    /// (Key: "최대 HP", Value: (이전 값, 새 값))
    /// </summary>
    public Dictionary<string, (int old, int @new)> LastLevelUpStats { get; private set; } = new Dictionary<string, (int, int)>();
    
    /// <summary>
    /// (UI 표시용) 한 번에 몇 레벨이 올랐는지 기록합니다. (예: 2레벨 동시 상승)
    /// </summary>
    public int LevelsGainedThisTurn { get; private set; } = 0;

    /// <summary>
    /// 새 플레이어 인스턴스를 생성하고, `SetInitialStats`를 호출하여 초기화합니다.
    /// </summary>
    public Player(PlayerClass playerClass)
    {
        Class = playerClass;
        // 리스트와 딕셔너리들을 비어있는 상태로 초기화합니다.
        Skills = new List<Skill>();
        EquippedGear = new Dictionary<EquipmentSlot, Equipment>();
        ConsumableInventory = new List<Consumable>();
        StatusEffects = new Dictionary<StatType, int>();
        // 직업에 맞는 초기 스탯, 스킬, 아이템을 설정합니다.
        SetInitialStats();
    }

    /// <summary>
    /// [핵심] 플레이어의 모든 데이터를 '직업별 초기 상태'로 설정(또는 리셋)합니다.
    /// </summary>
   private void SetInitialStats()
    {
        Level = 1;
        EXP = 0;
        EXPNext = CalculateEXPForNextLevel(1);

        Skills.Clear();
        EquippedGear.Clear();
        ConsumableInventory.Clear();
        
        StatusEffects.Clear();
        TempDefBuff = 0;
        
        AddConsumable(new Consumable("[Common] 저용량 HP 물약", ItemRarity.Common, ConsumableType.HealthPotion, 20));
        AddConsumable(new Consumable("[Common] 저용량 HP 물약", ItemRarity.Common, ConsumableType.HealthPotion, 20));
        AddConsumable(new Consumable("[Common] 저용량 MP 물약", ItemRarity.Common, ConsumableType.ManaPotion, 10));

       // [신규] 초기 장비 변수 선언 (모든 부위)
       Equipment starterWeapon, starterHead, starterArmor, starterGloves, starterBoots;

       switch (Class)
        {
            case PlayerClass.Warrior:
                baseMaxHP = 50; baseMaxMP = 15;
                baseATK = 4; baseDEF = 4; baseSTR = 8; baseINT = 2; baseDEX = 2;
                Skills.Add(new PowerStrike());
                Skills.Add(new ShieldBash());
                Skills.Add(new IronWill()); 
                Skills.Add(new Execution());
                
                // [전사] 초기 장비 생성
                starterWeapon = new Equipment("수련용 검", ItemRarity.Common, EquipmentSlot.Weapon, PlayerClass.Warrior);
                starterWeapon.AddModifier(new StatModifier(StatType.ATK, 2, ModifierType.Flat)); 

                starterHead = new Equipment("수련용 투구", ItemRarity.Common, EquipmentSlot.Head, PlayerClass.Warrior);
                starterHead.AddModifier(new StatModifier(StatType.DEF, 1, ModifierType.Flat));

                starterArmor = new Equipment("수련용 갑옷", ItemRarity.Common, EquipmentSlot.Armor, PlayerClass.Warrior);
                starterArmor.AddModifier(new StatModifier(StatType.DEF, 2, ModifierType.Flat)); 

                starterGloves = new Equipment("수련용 건틀릿", ItemRarity.Common, EquipmentSlot.Gloves, PlayerClass.Warrior);
                starterGloves.AddModifier(new StatModifier(StatType.DEF, 1, ModifierType.Flat));

                starterBoots = new Equipment("수련용 장화", ItemRarity.Common, EquipmentSlot.Boots, PlayerClass.Warrior);
                starterBoots.AddModifier(new StatModifier(StatType.DEF, 1, ModifierType.Flat));
                break;

            case PlayerClass.Wizard:
                baseMaxHP = 30; baseMaxMP = 30;
                baseATK = 2; baseDEF = 2; baseSTR = 2; baseINT = 12; baseDEX = 2;
                Skills.Add(new Fireball());
                Skills.Add(new Heal());
                Skills.Add(new MagicMissile());
                Skills.Add(new Meteor());
                
                // [마법사] 초기 장비 생성
                starterWeapon = new Equipment("수련용 지팡이", ItemRarity.Common, EquipmentSlot.Weapon, PlayerClass.Wizard);
                starterWeapon.AddModifier(new StatModifier(StatType.ATK, 1, ModifierType.Flat)); 

                starterHead = new Equipment("수련용 모자", ItemRarity.Common, EquipmentSlot.Head, PlayerClass.Wizard);
                starterHead.AddModifier(new StatModifier(StatType.DEF, 1, ModifierType.Flat));

                starterArmor = new Equipment("수련용 로브", ItemRarity.Common, EquipmentSlot.Armor, PlayerClass.Wizard);
                starterArmor.AddModifier(new StatModifier(StatType.DEF, 1, ModifierType.Flat)); 

                starterGloves = new Equipment("수련용 장갑", ItemRarity.Common, EquipmentSlot.Gloves, PlayerClass.Wizard);
                starterGloves.AddModifier(new StatModifier(StatType.DEF, 1, ModifierType.Flat));

                starterBoots = new Equipment("수련용 신발", ItemRarity.Common, EquipmentSlot.Boots, PlayerClass.Wizard);
                starterBoots.AddModifier(new StatModifier(StatType.DEF, 1, ModifierType.Flat));
                break;

            case PlayerClass.Rogue:
                baseMaxHP = 35; baseMaxMP = 15;
                baseATK = 3; baseDEF = 3; baseSTR = 2; baseINT = 2; baseDEX = 13;
                Skills.Add(new Backstab());
                Skills.Add(new PoisonStab());
                Skills.Add(new QuickAttack());
                Skills.Add(new Eviscerate());
                
                // [도적] 초기 장비 생성
                starterWeapon = new Equipment("수련용 단검", ItemRarity.Common, EquipmentSlot.Weapon, PlayerClass.Rogue);
                starterWeapon.AddModifier(new StatModifier(StatType.ATK, 2, ModifierType.Flat)); 

                starterHead = new Equipment("수련용 두건", ItemRarity.Common, EquipmentSlot.Head, PlayerClass.Rogue);
                starterHead.AddModifier(new StatModifier(StatType.DEF, 1, ModifierType.Flat));

                starterArmor = new Equipment("수련용 경갑", ItemRarity.Common, EquipmentSlot.Armor, PlayerClass.Rogue);
                starterArmor.AddModifier(new StatModifier(StatType.DEF, 1, ModifierType.Flat)); 

                starterGloves = new Equipment("수련용 가죽 장갑", ItemRarity.Common, EquipmentSlot.Gloves, PlayerClass.Rogue);
                starterGloves.AddModifier(new StatModifier(StatType.DEF, 1, ModifierType.Flat));

                starterBoots = new Equipment("수련용 부츠", ItemRarity.Common, EquipmentSlot.Boots, PlayerClass.Rogue);
                starterBoots.AddModifier(new StatModifier(StatType.DEF, 1, ModifierType.Flat));
                break;
            
            default:
                // (Fallback)
                starterWeapon = new Equipment("Unknown", ItemRarity.Common, EquipmentSlot.Weapon, PlayerClass.Warrior);
                starterHead = new Equipment("Unknown", ItemRarity.Common, EquipmentSlot.Head, PlayerClass.Warrior);
                starterArmor = new Equipment("Unknown", ItemRarity.Common, EquipmentSlot.Armor, PlayerClass.Warrior);
                starterGloves = new Equipment("Unknown", ItemRarity.Common, EquipmentSlot.Gloves, PlayerClass.Warrior);
                starterBoots = new Equipment("Unknown", ItemRarity.Common, EquipmentSlot.Boots, PlayerClass.Warrior);
                break;
        }
        
        // [신규] 모든 부위 장비 자동 장착
        EquipItem(starterWeapon);
        EquipItem(starterHead);
        EquipItem(starterArmor);
        EquipItem(starterGloves);
        EquipItem(starterBoots);

        // 스탯 최종 계산 (장비 스탯 반영)
        HP = MaxHP;
        MP = MaxMP;
    }
    /// <summary>
    /// [핵심] 장비 아이템을 착용합니다.
    /// </summary>
    /// <param name="newItem">새로 착용할 장비</param>
    /// <returns>교체되어 벗겨진 '이전 장비' (없으면 null)</returns>
    public Equipment? EquipItem(Equipment newItem)
    {
        // 1. 장착 전 '이전' 스탯(MaxHP/MP)을 임시 저장합니다.
        int oldMaxHP = MaxHP;
        int oldMaxMP = MaxMP;

        // 2. 딕셔너리에서 교체될 '이전 장비'를 찾아(TryGetValue) oldItem 변수에 저장합니다.
        Equipment? oldItem = null;
        EquippedGear.TryGetValue(newItem.Slot, out oldItem);
        
        // 3. 딕셔너리의 해당 슬롯에 '새 장비(newItem)'를 덮어씌웁니다.
        EquippedGear[newItem.Slot] = newItem;

        // 4. '이후' 스탯(MaxHP/MP)과 '이전' 스탯을 비교하여 '증가량'을 계산합니다.
        // (MaxHP 속성은 이 시점에 이미 새 장비의 보너스를 포함하여 자동 계산됩니다.)
        int hpGain = MaxHP - oldMaxHP;
        int mpGain = MaxMP - oldMaxMP;

        // 5. HP/MP가 '증가'했다면, 현재 HP/MP에도 증가량을 반영합니다.
        // (장비 교체로 최대치가 100->120이 되면, 현재 HP도 100->120이 됨)
        if (hpGain > 0)
        {
            HP += hpGain;
        }
        if (mpGain > 0)
        {
            MP += mpGain;
        }

        // 6. 스탯 최종 보정 (HP/MP가 최대치를 넘지 않도록)
        RecalculateStats();
        
        // 7. 인벤토리에서 제거할 수 있도록 '이전 장비(oldItem)'를 반환합니다.
        return oldItem;
    }

    /// <summary>
    /// 장비 교체 등으로 인해 HP/MP가 MaxHP/MaxMP를 초과했을 경우, 최대치에 맞게 보정합니다.
    /// </summary>
    private void RecalculateStats()
    {
        HP = Math.Min(HP, MaxHP);
        MP = Math.Min(MP, MaxMP);
    }
    
    /// <summary>
    /// [핵심] 현재 착용한 모든 장비(EquippedGear)를 순회하며,
    /// 특정 스탯(stat)의 보너스 '총합'을 계산하여 반환합니다.
    /// (이 메서드는 MaxHP, ATK 등 '계산된 속성'에서 내부적으로 호출됩니다.)
    /// </summary>
    /// <param name="stat">보너스 총합을 계산할 스탯 종류 (예: StatType.HP)</param>
    /// <param name="type">계산할 타입 (예: ModifierType.Flat)</param>
    /// <returns>해당 스탯의 보너스 총합</returns>
    public float GetStatBonus(StatType stat, ModifierType type)
    {
        float bonus = 0;
        // 1. 딕셔너리에 저장된 '모든 장비'(Values)를 순회합니다.
        foreach (var equip in EquippedGear.Values)
        {
            // 2. 각 장비가 가진 '모든 스탯 옵션'(Modifiers)을 순회합니다.
            foreach (var mod in equip.Modifiers)
            {
                // 3. 찾으려는 스탯(stat) 및 타입(type)과 일치하는 옵션인지 확인합니다.
                if (mod.Stat == stat && mod.Type == type)
                {
                    // 4. 일치하면 보너스 총합(bonus)에 더합니다.
                    bonus += mod.Value;
                }
            }
        }
        // 5. 최종 합산된 보너스 값을 반환합니다.
        return bonus;
    }

    /// <summary>
    /// '보유 중인' 소비 아이템 리스트에 아이템을 추가합니다.
    /// </summary>
    public void AddConsumable(Consumable item)
    {
        ConsumableInventory.Add(item);
    }

    /// <summary>
    /// [핵심] 소비 아이템을 '사용'합니다.
    /// '종류(cType)'와 '희귀도(rarity)'가 모두 일치하는 첫 번째 아이템을 찾아 사용합니다.
    /// </summary>
    /// <param name="cType">사용할 아이템 종류 (예: HealthPotion)</param>
    /// <param name="rarity">사용할 아이템 희귀도 (예: Common)</param>
    /// <param name="game">로그 출력(AddLog)을 위한 Game 엔진</param>
    /// <returns>사용 성공 시 true, 실패(아이템 없음, HP/MP 가득 참) 시 false</returns>
    public bool UseConsumable(ConsumableType cType, ItemRarity rarity, Game game)
    {
        // 1. 인벤토리 리스트에서 cType과 rarity가 모두 일치하는 '첫 번째' 아이템을 찾습니다.
        Consumable? itemToUse = ConsumableInventory.FirstOrDefault(item => item.CType == cType && item.Rarity == rarity);

        // 2. 아이템을 찾지 못한 경우
        if (itemToUse == null)
        {
            game.AddLog("아이템이 없습니다.");
            return false; // 사용 실패
        }

        // 3. 아이템을 찾은 경우, Consumable.cs의 Use() 메서드를 호출하여 실제 효과를 적용합니다.
        bool success = itemToUse.Use(this, game);
        
        // 4. Use()가 성공(true)을 반환한 경우 (예: HP 회복 성공)
        if (success)
        {
            // 인벤토리 리스트에서 사용한 아이템을 '제거'합니다.
            ConsumableInventory.Remove(itemToUse);
        }
        return success; // (성공/실패 여부 반환)
    }

    /// <summary>
    /// [핵심] 경험치를 획득하고, 필요 시 '반복적'으로 레벨 업을 처리합니다.
    /// </summary>
    /// <param name="expAmount">획득한 경험치 양</param>
    /// <returns>레벨 업을 '한 번이라도' 했으면 true, 아니면 false</returns>
    public bool AddExperience(int expAmount)
    {
        EXP += expAmount;
        bool leveledUp = false;

        // (UI 표시용) 레벨 업 루프가 시작되기 전, '이전' 스탯을 저장합니다.
        LevelsGainedThisTurn = 0; // 초기화
        int oldBaseMaxHP = this.baseMaxHP;
        int oldBaseMaxMP = this.baseMaxMP;
        int oldBaseATK = this.baseATK;
        int oldBaseDEF = this.baseDEF;
        int oldBaseSTR = this.baseSTR;
        int oldBaseINT = this.baseINT;
        int oldBaseDEX = this.baseDEX;

        // [핵심] 획득한 경험치(EXP)가 요구 경험치(EXPNext)보다 많은 동안 '계속' 반복합니다.
        // (한 번에 2~3 레벨 이상 오를 수 있음을 의미합니다.)
        while (EXP >= EXPNext)
        {
            EXP -= EXPNext; // 요구 경험치 차감
            Level++; // 레벨 1 상승
            leveledUp = true;
            LevelsGainedThisTurn++; 
            LevelUpStats(); // 스탯 상승 적용
            
            // 새 레벨에 맞는 '다음' 요구 경험치를 다시 계산하여 갱신합니다.
            EXPNext = CalculateEXPForNextLevel(Level); 
        }

        // [UI 표시용] 레벨 업이 발생했다면, 변경 사항을 LastLevelUpStats 딕셔너리에 기록합니다.
        if (leveledUp)
        {
            LastLevelUpStats.Clear(); // 이전 기록 삭제

            // '이전' 스탯과 '현재' 스탯을 비교하여 기록합니다.
            if (baseMaxHP > oldBaseMaxHP) LastLevelUpStats["최대 HP"] = (oldBaseMaxHP, baseMaxHP);
            if (baseMaxMP > oldBaseMaxMP) LastLevelUpStats["최대 MP"] = (oldBaseMaxMP, baseMaxMP);
            if (baseATK > oldBaseATK) LastLevelUpStats["공격력"] = (oldBaseATK, baseATK);
            if (baseDEF > oldBaseDEF) LastLevelUpStats["방어력"] = (oldBaseDEF, baseDEF);
            if (baseSTR > oldBaseSTR) LastLevelUpStats["STR"] = (oldBaseSTR, baseSTR);
            if (baseINT > oldBaseINT) LastLevelUpStats["INT"] = (oldBaseINT, baseINT);
            if (baseDEX > oldBaseDEX) LastLevelUpStats["DEX"] = (oldBaseDEX, baseDEX);
            
            // 레벨 업 시 HP/MP를 완전히 회복시킵니다.
            HP = MaxHP;
            MP = MaxMP;
        }
        
        return leveledUp;
    }

    /// <summary>
    /// 레벨 업 시 호출되어, 직업(Class)에 맞는 '기본 스탯'을 상승시킵니다.
    /// </summary>
    private void LevelUpStats()
    {
        switch (Class)
        {
            case PlayerClass.Warrior:
                // (전사: HP/STR/DEF 위주, MP는 소량)
                baseMaxHP += 10; baseMaxMP += 2; baseSTR += 2; baseDEF += 1;
                break;
            case PlayerClass.Wizard:
                // (마법사: MP/INT 위주)
                baseMaxHP += 4; baseMaxMP += 7; baseINT += 2;
                break;
            case PlayerClass.Rogue:
                // (도적: HP/ATK/DEX 위주)
                baseMaxHP += 6; baseMaxMP += 3; baseATK += 1; baseDEX += 2;
                break;
        }
        // (레벨 업 보상으로 HP/MP를 즉시 가득 채웁니다.)
        // (이 코드가 AddExperience의 HP/MP 회복 로직보다 먼저 실행되지만,
        //  AddExperience에서 한 번 더 덮어쓰므로 최종 결과는 '완전 회복'이 보장됩니다.)
        HP = MaxHP;
        MP = MaxMP;
    }

    /// <summary>
    /// [핵심] JRPG 스타일의 '요구 경험치 곡선'을 계산합니다.
    /// (기본값 + 선형 증가 + 다항(제곱) 증가)
    /// </summary>
    /// <param name="level">'다음 레벨'이 아닌 '현재 레벨'</param>
    /// <returns>현재 레벨(level)에서 다음 레벨로 가는 데 필요한 총 경험치</returns>
    private int CalculateEXPForNextLevel(int level)
    {
        // 1. 기본 요구치 (Lvl 1->2 기준)
        int baseExp = 80;
        
        // 2. 레벨당 선형(1차 함수) 증가 (Lvl * 10)
        int linearGrowth = 10 * level;
        
        // 3. 레벨 제곱에 비례한(2차 함수) 증가 (Lvl^2 * 3)
        // (고레벨로 갈수록 급격히 어려워지는 핵심 공식)
        int polynomialGrowth = (int)(3.0 * Math.Pow(level, 2));
        
        // 최종 요구 경험치 = 기본(80) + (Level * 10) + (Level^2 * 3)
        return baseExp + linearGrowth + polynomialGrowth;
    }
}