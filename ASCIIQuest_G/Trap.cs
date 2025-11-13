// Trap.cs
using System;

// 함정의 종류를 정의합니다.
public enum TrapType
{
    Damage, // 밟으면 데미지를 입는 함정
    Battle  // 밟으면 몬스터와 전투가 시작되는 함정
}

public class Trap
{
    public int X { get; private set; }
    public int Y { get; private set; }
    public TrapType Type { get; private set; }
    public char Icon { get; private set; }
    public bool IsTriggered { get; private set; }

    public Trap(int x, int y, TrapType type, char icon)
    {
        X = x;
        Y = y;
        Type = type;
        Icon = icon;
        IsTriggered = false; // 처음엔 비활성화 상태
    }

    // 함정을 발동시키는 공용 메서드
    public void Trigger(Player player, Game game, Random rand, int currentStage)
    {
        if (IsTriggered) return; // 이미 발동된 함정은 무시

        IsTriggered = true; // 함정 발동!

        switch (Type)
        {
            case TrapType.Damage:
                int damage = 5;
                player.HP -= damage;
                game.AddLog($"날카로운 함정을 밟았다! (HP -{damage})");
                break;
            
            case TrapType.Battle:
                game.AddLog("함정이다! 숨어있던 몬스터가 공격한다!");
                // 0, 0 좌표는 임시값 (전투 화면에선 좌표가 무의미함)
                Monster trapMonster = MonsterDB.CreateRandomMonster(0, 0, rand, currentStage);                
                // [핵심 수정] 
                // StartBattle에 'isFromTrap: true' 플래그를 전달합니다.
                game.StartBattle(trapMonster, true); 
                break;
        }
        
        // Game 객체를 통해 맵의 시각적 타일도 변경
        game.UpdateMapTile(X, Y, '.');
    }
}