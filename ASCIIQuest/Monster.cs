// Monster.cs
using System.Collections.Generic;
using System;

public class Monster
{
    public string Name { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    
    private int _hp;
    public int HP 
    { 
        get => _hp; 
        set => _hp = Math.Max(0, Math.Min(value, MaxHP));
    }

    public int MaxHP { get; set; }
    
    public int ATK 
    { 
        get
        {
            if (StatusEffects.ContainsKey(StatType.AtkDebuff) && StatusEffects[StatType.AtkDebuff] > 0)
            {
                return Math.Max(1, OriginalATK / 2); 
            }
            return OriginalATK;
        }
        set => OriginalATK = value; 
    }

    public int DEF { get; set; } 
    public char Icon { get; set; } 
    public int EXPReward { get; set; }
    public int ArtOffsetY { get; set; }
    public int ArtOffsetX { get; set; }

    public int OriginalATK { get; set; } 
    public int OriginalMaxHP { get; private set; }
    public int OriginalDEF { get; private set; }
    public int OriginalEXPReward { get; private set; }

    public Dictionary<StatType, int> StatusEffects { get; private set; }
    
    // [신규] 상태이상 적용 순서를 기억하는 리스트 (UI 표시 및 우선순위용)
    public List<StatType> StatusOrder { get; private set; } = new List<StatType>();

    public int PoisonDamagePerTurn { get; set; } = 0;
    public int BleedDamagePerTurn { get; set; } = 0;
    public int StrongPoisonDamagePerTurn { get; set; } = 0; 

    public string MonsterId { get; private set; }

    public Monster(string name, int x, int y, int hp, int atk, int def, char icon, int expReward, int artOffsetY, int artOffsetX, string monsterId)
    {
        Name = name;
        X = x; Y = y;
        MaxHP = HP = hp;
        OriginalATK = atk;
        DEF = def;
        Icon = icon;
        EXPReward = expReward;
        ArtOffsetY = artOffsetY; 
        ArtOffsetX = artOffsetX; 
        MonsterId = monsterId;
        
        OriginalMaxHP = hp;
        OriginalDEF = def;
        OriginalEXPReward = expReward;
        
        StatusEffects = new Dictionary<StatType, int>();
    }

    // [신규] 상태이상을 적용하는 헬퍼 메서드 (Game.cs에서 직접 딕셔너리 수정하는 대신 사용)
    public void AddStatusEffect(StatType type, int duration)
    {
        StatusEffects[type] = duration;
        
        // 순서 리스트 관리: 이미 있으면 제거 후 맨 뒤로(최신) 이동
        if (StatusOrder.Contains(type)) StatusOrder.Remove(type);
        StatusOrder.Add(type);
    }

    // [신규] 만료된 상태이상 제거
    public void RemoveStatusEffect(StatType type)
    {
        if (StatusEffects.ContainsKey(type)) StatusEffects.Remove(type);
        if (StatusOrder.Contains(type)) StatusOrder.Remove(type);
    }
}