// Skill.cs
using System;

public abstract class Skill
{
    public string Name { get; protected set; }
    public int MpCost { get; protected set; }
    public Skill(string name, int mpCost) { Name = name; MpCost = mpCost; }

    // [수정] 스킬 로직이 Game.cs로 이전됨
    // 이제 스킬은 'int' (데미지)를 반환합니다.
    // (데미지를 주지 않는 스킬은 0 또는 -1을 반환)
    public abstract int CalculateDamage(Player caster, Monster target);

    // [신규] 스킬이 데미지를 입히는 스킬인지 확인
    public virtual bool IsDamagingSkill => true;
    
    // [신규] 스킬이 자신에게 이로운 효과를 주는지 확인
    public virtual bool IsBuffSkill => false;
}

// --- 전사 스킬 ---
public class PowerStrike : Skill
{
    public PowerStrike() : base("파워 스트라이크", 5) { }
    public override int CalculateDamage(Player caster, Monster target)
    {
        float bonusPwr = caster.GetStatBonus(StatType.PowerStrikeDamage, ModifierType.Percent); 
        
        // [수정] "- target.DEF" 제거
        int damage = (caster.ATK + caster.STR) * 2; 
        
        damage = Math.Max(1, damage);
        return (int)Math.Round(damage * (1.0f + bonusPwr)); 
    }
}

public class ShieldBash : Skill
{
    public ShieldBash() : base("방패 치기", 3) { }
    public override int CalculateDamage(Player caster, Monster target)
    {
        float bonusShield = caster.GetStatBonus(StatType.ShieldBashDamage, ModifierType.Percent); 
        
        // [수정] "- target.DEF" 제거
        int damage = caster.DEF * 2; 
        
        damage = Math.Max(1, damage);
        return (int)Math.Round(damage * (1.0f + bonusShield)); 
    }
}

public class MoraleBoost : Skill
{
    public MoraleBoost() : base("사기 진작", 8) { }
    public override bool IsDamagingSkill => false; // 데미지 스킬 아님
    public override bool IsBuffSkill => true;    // 버프 스킬임

    public override int CalculateDamage(Player caster, Monster target)
    {
        caster.baseATK += 2; // 효과는 즉시 적용
        return -1; // (데미지 없음)
    }
}

// --- 마법사 스킬 ---
public class Fireball : Skill
{
    // (마나 소모 12)
    public Fireball() : base("파이어볼", 12) { }
    public override int CalculateDamage(Player caster, Monster target)
    {
        float bonusFire = caster.GetStatBonus(StatType.FireballDamage, ModifierType.Percent);
        // (방어 무시, 데미지 상향)
        int damage = (caster.INT * 4) + (caster.ATK / 2); 
        damage = Math.Max(1, damage);
        return (int)Math.Round(damage * (1.0f + bonusFire)); 
    }
}

public class Heal : Skill
{
    public Heal() : base("힐", 10) { }
    public override bool IsDamagingSkill => false; 
    public override bool IsBuffSkill => true;

    public override int CalculateDamage(Player caster, Monster target)
    {
        float bonusHeal = caster.GetStatBonus(StatType.HealAmount, ModifierType.Percent);
        int heal = caster.INT * 2;
        heal = (int)Math.Round(heal * (1.0f + bonusHeal)); 
        caster.HP = Math.Min(caster.MaxHP, caster.HP + heal);
        return heal; // (힐량 반환)
    }
}

public class MagicMissile : Skill
{
    // (마나 소모 7)
    public MagicMissile() : base("매직 미사일", 7) { }
    public override int CalculateDamage(Player caster, Monster target)
    {
        float bonusMissile = caster.GetStatBonus(StatType.MagicMissileDamage, ModifierType.Percent);
        // (방어 무시, 데미지 상향)
        int damage = (caster.INT * 2) + (caster.ATK / 2) + 5; 
        damage = Math.Max(1, damage);
        return (int)Math.Round(damage * (1.0f + bonusMissile)); 
    }
}

// --- 도적 스킬 ---
public class Backstab : Skill
{
    public Backstab() : base("백스탭", 7) { }
    public override int CalculateDamage(Player caster, Monster target)
    {
        float bonusBackstab = caster.GetStatBonus(StatType.BackstabDamage, ModifierType.Percent);
        
        // [수정] "- target.DEF" 제거
        int damage = (caster.ATK + caster.DEX) * 2; 
        
        damage = Math.Max(1, damage);
        return (int)Math.Round(damage * (1.0f + bonusBackstab));
    }
}

public class PoisonStab : Skill
{
    public PoisonStab() : base("독 찌르기", 4) { }
    public override int CalculateDamage(Player caster, Monster target)
    {
        int duration = 5; 
        float bonusPoison = caster.GetStatBonus(StatType.PoisonStabDamage, ModifierType.Percent);
        int damage = caster.DEX / 2; // 턴당 데미지
        damage = Math.Max(1, damage);
        damage = (int)Math.Round(damage * (1.0f + bonusPoison)); 
        
        // (상태이상 적용은 즉시)
        target.PoisonDamagePerTurn = damage;
        target.StatusEffects[StatType.PoisonStabDamage] = duration;
        
        return damage; // (로그 표시를 위해 턴당 데미지 반환)
    }
}

public class QuickAttack : Skill
{
    public QuickAttack() : base("퀵 어택", 3) { }
    public override int CalculateDamage(Player caster, Monster target)
    {
        float bonusQuick = caster.GetStatBonus(StatType.QuickAttackDamage, ModifierType.Percent);
        
        // [수정] "- target.DEF" 제거
        int damage = caster.ATK + caster.DEX; 
        
        damage = Math.Max(1, damage);
        return (int)Math.Round(damage * (1.0f + bonusQuick));
    }
}

public class Execution : Skill
{
    public Execution() : base("처형", 15) { }
    public override int CalculateDamage(Player caster, Monster target)
    {
        // 자신의 STR과 DEF를 합산하여 강력한 방어 무시 데미지를 줍니다.
        int damage = (caster.STR + caster.DEF) * 2;
        damage = Math.Max(1, damage);
        return damage; 
    }
}

// --- [신규] 마법사 필살기 ---
public class Meteor : Skill
{
    public Meteor() : base("메테오", 20) { }
    public override int CalculateDamage(Player caster, Monster target)
    {
        // INT 기반의 막대한 방어 무시 데미지를 줍니다.
        int damage = (caster.INT * 6) + 10; 
        damage = Math.Max(1, damage);
        return damage; 
    }
}

// --- [신규] 도적 필살기 ---
public class Eviscerate : Skill
{
    public Eviscerate() : base("파열", 14) { }
    public override int CalculateDamage(Player caster, Monster target)
    {
        float damage = (caster.ATK + caster.DEX) * 2.5f;
        if (target.StatusEffects.GetValueOrDefault(StatType.PoisonStabDamage, 0) > 0 ||
            target.StatusEffects.GetValueOrDefault(StatType.BleedChance, 0) > 0)
        {
            damage *= 1.5f;
        }
        
        // [수정] "damage -= target.DEF;" 제거
        
        return Math.Max(1, (int)damage);
    }
}