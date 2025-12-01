// Skill.cs
using System;

/// <summary>
/// [핵심] 모든 스킬의 '기반'이 되는 '추상 클래스(abstract class)'입니다.
/// '전략 패턴'의 '전략(Strategy)' 인터페이스 역할을 합니다.
/// Game.cs는 이 'Skill' 타입만 알고 있으면, 어떤 자식 스킬이든 동일한 방식으로 호출할 수 있습니다.
/// </summary>
public abstract class Skill
{
    /// <summary>
    /// 스킬의 이름 (예: "파워 스트라이크")
    /// </summary>
    public string Name { get; protected set; }
    
    /// <summary>
    /// 스킬 사용에 필요한 기본 MP 소모량
    /// </summary>
    public int MpCost { get; protected set; }

    /// <summary>
    /// 모든 자식 스킬 클래스들의 '부모 생성자'입니다.
    /// 'base(name, mpCost)' 형태로 호출되어 스킬의 이름과 MP 소모량을 설정합니다.
    /// </summary>
    public Skill(string name, int mpCost) { Name = name; MpCost = mpCost; }

    public int Cooldown { get; protected set; } = 0;       // 스킬의 기본 쿨타임 (턴)
    public int CurrentCooldown { get; set; } = 0;

    public virtual bool IsUltimate => false;

    /// <summary>
    /// [핵심] 모든 자식 스킬이 '반드시' 구현해야 하는 '추상 메서드'입니다.
    /// 이 메서드는 스킬의 '방어력 무시 원본 데미지(Raw Damage)' 또는 '버프/힐 수치'를 계산하여 반환합니다.
    /// (실제 방어력 적용 및 HP 차감은 Game.cs에서 이 값을 받아 처리합니다.)
    /// </summary>
    /// <param name="caster">스킬 시전자(Player)</param>
    /// <param name="target">스킬 대상(Monster)</param>
    /// <returns>계산된 '원본 데미지' 또는 '버프/힐 수치'</returns>
    public abstract int CalculateDamage(Player caster, Monster target);

    /// <summary>
    /// [타입 태그 1] 이 스킬이 '데미지를 입히는' 스킬인지 여부를 반환합니다.
    /// 'virtual'이므로, 자식 스킬이 이 값을 재정의(override)할 수 있습니다.
    /// (기본값: true)
    /// </summary>
    public virtual bool IsDamagingSkill => true;
    
    /// <summary>
    /// [타입 태그 2] 이 스킬이 '아군 대상 버프/힐' 스킬인지 여부를 반환합니다.
    /// (기본값: false)
    /// </summary>
    public virtual bool IsBuffSkill => false;
}

// --- 1. 전사 스킬 (Warrior) ---

/// <summary>
/// (전사) 파워 스트라이크: (ATK + STR) * 2 기반의 강력한 물리 데미지
/// </summary>
public class PowerStrike : Skill
{
    // "파워 스트라이크" 이름과 MP 5 소모로 부모 생성자 호출
    public PowerStrike() : base("파워 스트라이크", 5) { }
    
    /// <summary>
    /// 스킬 데미지를 계산합니다. (방어력 적용은 Game.cs에서 처리)
    /// </summary>
    public override int CalculateDamage(Player caster, Monster target)
    {
        // 1. 장비 옵션에서 "파워 스트라이크 데미지 %" 보너스를 가져옵니다.
        float bonusPwr = caster.GetStatBonus(StatType.PowerStrikeDamage, ModifierType.Percent); 
        
        // 2. '원본 데미지'를 계산합니다. (ATK + STR) * 2
        int damage = (caster.ATK + caster.STR) * 2; 
        
        // 3. 최소 1 데미지를 보장하고, % 보너스를 적용하여 반환합니다.
        damage = Math.Max(1, damage);
        return (int)Math.Round(damage * (1.0f + bonusPwr)); 
    }
}

/// <summary>
/// (전사) 방패 치기: DEF * 2 기반의 물리 데미지
/// </summary>
public class ShieldBash : Skill
{
    public ShieldBash() : base("방패 치기", 3) { }
    public override int CalculateDamage(Player caster, Monster target)
    {
        // 1. 장비 옵션에서 "방패 치기 데미지 %" 보너스를 가져옵니다.
        float bonusShield = caster.GetStatBonus(StatType.ShieldBashDamage, ModifierType.Percent); 
        
        // 2. '원본 데미지'를 계산합니다. (방어력 * 2)
        int damage = caster.DEF * 2; 
        
        // 3. 최소 1 데미지를 보장하고, % 보너스를 적용하여 반환합니다.
        damage = Math.Max(1, damage);
        return (int)Math.Round(damage * (1.0f + bonusShield)); 
    }
}

/// <summary>
/// (전사) 강철의 의지: 5턴간 방어력을 (기본 DEF * 0.5) + 5 만큼 증가시키는 '버프' 스킬
/// </summary>
public class IronWill : Skill
{
    public IronWill() : base("강철의 의지", 10) { } // MP 10 소모
    
    // 이 스킬은 데미지를 주지 않음 (false)
    public override bool IsDamagingSkill => false;
    // 이 스킬은 버프 스킬임 (true)
    public override bool IsBuffSkill => true;

    /// <summary>
    /// 버프를 '즉시 적용'하고, UI 표시에 사용할 '방어력 증가량'을 반환합니다.
    /// </summary>
    public override int CalculateDamage(Player caster, Monster target)
    {
        int duration = 5; // 5턴간 지속
        
        // 1. 방어력 증가량 계산: (순수 기본 방어력의 50%) + 고정 5
        int defGain = (int)(caster.baseDEF * 0.5) + 5; 
        
        // 2. [즉시 적용] 플레이어의 '상태 효과' 딕셔너리에 버프 턴 수를 기록합니다.
        // (Game.cs가 매 턴 이 수치를 1씩 감소시킵니다.)
        caster.StatusEffects[StatType.DEF] = duration; 
        
        // 3. [즉시 적용] 플레이어의 '임시 방어력' 변수에 실제 증가량을 저장합니다.
        // (Player.DEF 속성은 이 값을 자동으로 합산합니다.)
        caster.TempDefBuff = defGain; 
        
        // 4. Game.cs의 로그 표출을 위해 '방어력 증가량'을 반환합니다.
        return defGain;
    }
}

/// <summary>
/// (전사) 처형: (STR + DEF) * 2 기반의 '방어 무시' 물리 데미지
/// (Game.cs에서 이 스킬은 ApplyDefense를 호출하지 않습니다.)
/// </summary>
public class Execution : Skill
{
    // [수정] 쿨타임 5턴 설정
    public Execution() : base("처형", 15) { Cooldown = 5; } 
    public override bool IsUltimate => true; // 궁극기임

    public override int CalculateDamage(Player caster, Monster target)
    {
        // [신규] 아이템 보너스 적용
        float bonus = caster.GetStatBonus(StatType.ExecutionDamage, ModifierType.Percent);

        // [수정] 데미지 대폭 상향: (STR + DEF) * 2 -> * 5
        int damage = (caster.STR + caster.DEF) * 5;
        
        damage = Math.Max(1, damage);
        return (int)Math.Round(damage * (1.0f + bonus)); 
    }
}


// --- 2. 마법사 스킬 (Wizard) ---

/// <summary>
/// (마법사) 파이어볼: (INT * 5) + (ATK / 2) 기반의 '방어 무시' 마법 데미지
/// </summary>
public class Fireball : Skill
{
    public Fireball() : base("파이어볼", 12) { }
    public override int CalculateDamage(Player caster, Monster target)
    {
        float bonusFire = caster.GetStatBonus(StatType.FireballDamage, ModifierType.Percent);
        
        // '원본 데미지' 계산: (INT * 5) + (ATK / 2)
        int damage = (caster.INT * 5) + (caster.ATK / 2);

        damage = Math.Max(1, damage);
        return (int)Math.Round(damage * (1.0f + bonusFire));
    }
}

/// <summary>
/// (마법사) 힐: (INT * 2.5) 기반의 '회복' 스킬
/// </summary>
public class Heal : Skill
{
    public Heal() : base("힐", 10) { }
    // 데미지를 주지 않음 (false)
    public override bool IsDamagingSkill => false;
    // 버프(회복) 스킬임 (true)
    public override bool IsBuffSkill => true;

    /// <summary>
    /// HP를 '즉시 회복'시키고, UI 표시에 사용할 '힐량'을 반환합니다.
    /// </summary>
    public override int CalculateDamage(Player caster, Monster target)
    {
        float bonusHeal = caster.GetStatBonus(StatType.HealAmount, ModifierType.Percent);
        
        // 1. '원본 힐량' 계산: (INT * 2.5)
        int heal = (int)(caster.INT * 2.5f);

        // 2. 장비 보너스를 적용합니다.
        heal = (int)Math.Round(heal * (1.0f + bonusHeal));
        
        // 3. [즉시 적용] 플레이어의 HP를 회복시킵니다. (최대 HP를 넘지 않도록)
        caster.HP = Math.Min(caster.MaxHP, caster.HP + heal);
        
        // 4. Game.cs의 로그 표출을 위해 '최종 힐량'을 반환합니다.
        return heal;
    }
}

/// <summary>
/// (마법사) 매직 미사일: (INT * 3) + (ATK / 2) + 5 기반의 '방어 무시' 마법 데미지
/// </summary>
public class MagicMissile : Skill
{
    public MagicMissile() : base("매직 미사일", 7) { }
    public override int CalculateDamage(Player caster, Monster target)
    {
        float bonusMissile = caster.GetStatBonus(StatType.MagicMissileDamage, ModifierType.Percent);
        
        // '원본 데미지' 계산: (INT * 3) + (ATK / 2) + 5
        int damage = (caster.INT * 3) + (caster.ATK / 2) + 5;

        damage = Math.Max(1, damage);
        return (int)Math.Round(damage * (1.0f + bonusMissile));
    }
}

/// <summary>
/// (마법사) 메테오: (INT * 8) + 10 기반의 강력한 '방어 무시' 마법 데미지 (필살기)
/// </summary>
public class Meteor : Skill
{
    // [수정] 쿨타임 5턴 설정
    public Meteor() : base("메테오", 20) { Cooldown = 5; }
    public override bool IsUltimate => true;

    public override int CalculateDamage(Player caster, Monster target)
    {
        float bonus = caster.GetStatBonus(StatType.MeteorDamage, ModifierType.Percent);

        // [수정] 데미지 대폭 상향: (INT * 8) -> (INT * 15) + 고정 50
        int damage = (caster.INT * 15) + 50; 
        
        damage = Math.Max(1, damage);
        return (int)Math.Round(damage * (1.0f + bonus)); 
    }
}


// --- 3. 도적 스킬 (Rogue) ---

/// <summary>
/// (도적) 백스탭: (ATK + DEX) * 2 기반의 물리 데미지
/// </summary>
public class Backstab : Skill
{
    public Backstab() : base("백스탭", 7) { }
    public override int CalculateDamage(Player caster, Monster target)
    {
        float bonusBackstab = caster.GetStatBonus(StatType.BackstabDamage, ModifierType.Percent);
        
        // '원본 데미지' 계산: (ATK + DEX) * 2
        int damage = (caster.ATK + caster.DEX) * 2; 
        
        damage = Math.Max(1, damage);
        return (int)Math.Round(damage * (1.0f + bonusBackstab));
    }
}

/// <summary>
/// (도적) 독 찌르기: (DEX / 2) 기반의 '지속 피해(DoT)'를 5턴간 적용
/// </summary>
public class PoisonStab : Skill
{
    public PoisonStab() : base("독 찌르기", 4) { }
    
    /// <summary>
    /// 대상에게 '즉시 독 상태이상'을 적용하고, UI 표시에 사용할 '턴당 데미지'를 반환합니다.
    /// (이 스킬의 반환값(데미지)은 Game.cs에서 즉시 HP를 깎는 데 사용되지 않습니다.)
    /// </summary>
    public override int CalculateDamage(Player caster, Monster target)
    {
        int duration = 5; // 5턴간 지속
        float bonusPoison = caster.GetStatBonus(StatType.PoisonStabDamage, ModifierType.Percent);
        
        // 1. '턴당 데미지' 계산: (DEX / 2)
        int damage = caster.DEX / 2; 
        damage = Math.Max(1, damage);
        damage = (int)Math.Round(damage * (1.0f + bonusPoison)); 
        
        // 2. [즉시 적용] 몬스터의 'PoisonDamagePerTurn' 변수에 턴당 데미지를 저장합니다.
        target.PoisonDamagePerTurn = damage;
        // 3. [즉시 적용] 몬스터의 '상태 효과' 딕셔너리에 지속 턴(5)을 기록합니다.
        target.StatusEffects[StatType.PoisonStabDamage] = duration;
        
        // 4. Game.cs의 로그 표출을 위해 '턴당 데미지'를 반환합니다.
        return damage;
    }
}

/// <summary>
/// (도적) 퀵 어택: (ATK + DEX) 기반의 가벼운 물리 데미지
/// </summary>
public class QuickAttack : Skill
{
    public QuickAttack() : base("퀵 어택", 3) { }
    public override int CalculateDamage(Player caster, Monster target)
    {
        float bonusQuick = caster.GetStatBonus(StatType.QuickAttackDamage, ModifierType.Percent);
        
        // '원본 데미지' 계산: ATK + DEX
        int damage = caster.ATK + caster.DEX; 
        
        damage = Math.Max(1, damage);
        return (int)Math.Round(damage * (1.0f + bonusQuick));
    }
}

/// <summary>
/// (도적) 파열: (ATK + DEX) * 2.5 기반의 물리 데미지 (필살기)
/// 대상이 '독' 또는 '출혈' 상태일 경우 1.5배의 추가 데미지
/// </summary>
public class Eviscerate : Skill
{
    // [수정] 쿨타임 5턴 설정
    public Eviscerate() : base("파열", 14) { Cooldown = 5; }
    public override bool IsUltimate => true;

    public override int CalculateDamage(Player caster, Monster target)
    {
        float bonus = caster.GetStatBonus(StatType.EviscerateDamage, ModifierType.Percent);

        // [수정] 데미지 대폭 상향: (ATK + DEX) * 2.5 -> * 6
        // (상태이상 추가 데미지 로직은 제거하고 기본 깡뎀을 높임, 대신 확정 크리티컬 적용 예정)
        float damage = (caster.ATK + caster.DEX) * 6.0f;
        
        return (int)Math.Round(Math.Max(1, damage) * (1.0f + bonus));
    }
}