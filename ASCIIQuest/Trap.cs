// Trap.cs
using System;

/// <summary>
/// 함정의 종류를 정의합니다.
/// </summary>
public enum TrapType
{
    /// <summary>
    /// 밟으면 즉시 데미지를 입는 함정입니다.
    /// </summary>
    Damage, 
    
    /// <summary>
    /// 밟으면 몬스터와 강제 전투가 시작되는 함정입니다.
    /// </summary>
    Battle  
}

/// <summary>
/// 맵에 배치되는 '함정' 오브젝트를 정의하는 클래스입니다.
/// 플레이어가 밟았을 때(Trigger) 정해진 효과(데미지 또는 전투)를 발동시킵니다.
/// </summary>
public class Trap
{
    // --- 함정의 속성(Properties) ---

    /// <summary>
    /// 함정의 맵 X좌표
    /// </summary>
    public int X { get; private set; }
    
    /// <summary>
    /// 함정의 맵 Y좌표
    /// </summary>
    public int Y { get; private set; }
    
    /// <summary>
    /// 이 함정의 종류 (Damage 또는 Battle)
    /// </summary>
    public TrapType Type { get; private set; }
    
    /// <summary>
    /// 맵에 표시될 아이콘 (예: '^' 또는 '*')
    /// </summary>
    public char Icon { get; private set; }
    
    /// <summary>
    /// 함정이 이미 발동되었는지 여부를 나타내는 플래그 (중복 발동 방지용)
    /// </summary>
    public bool IsTriggered { get; private set; }

    /// <summary>
    /// 지정된 좌표(x, y)와 종류(type)를 가진 새 함정 인스턴스를 생성합니다.
    /// </summary>
    /// <param name="x">맵 X좌표</param>
    /// <param name="y">맵 Y좌표</param>
    /// <param name="type">함정 종류 (Damage, Battle)</param>
    /// <param name="icon">맵에 표시할 아이콘</param>
    public Trap(int x, int y, TrapType type, char icon)
    {
        X = x;
        Y = y;
        Type = type;
        Icon = icon;
        IsTriggered = false; // 생성 시에는 항상 비활성화(발동 전) 상태
    }

    /// <summary>
    /// [핵심] 플레이어가 이 함정을 밟았을 때 호출되는 메인 로직입니다.
    /// (Game.cs의 ProcessWorldInput에서 호출됨)
    /// </summary>
    /// <param name="player">함정 효과를 적용받을 Player 객체</param>
    /// <param name="game">로그 출력(AddLog), 전투 시작(StartBattle) 등을 처리할 Game 엔진</param>
    /// <param name="rand">전투 함정의 몬스터 생성에 사용할 Random 객체</param>
    /// <param name="currentStage">현재 스테이지 (스폰될 몬스터의 레벨 결정용)</param>
    public void Trigger(Player player, Game game, Random rand, int currentStage)
    {
        // 1. 이미 발동된 함정 무시
        if (IsTriggered) return; 

        // 2. 발동 처리 및 맵 갱신
        IsTriggered = true; 
        game.UpdateMapTile(X, Y, '.'); // 함정 제거 시각화

        // 3. 함정 종류에 따른 분기
        switch (Type)
        {
            // [Case A] 데미지 함정 (가시)
            case TrapType.Damage:
                // [핵심 수정 1] 최대 체력의 20% 데미지 (최소 1)
                int damage = (int)(player.MaxHP * 0.20f); 
                damage = Math.Max(1, damage);

                player.HP -= damage;
                if (player.HP < 0) player.HP = 0;

                game.AddLog($"날카로운 가시 함정을 밟았다! (HP -{damage})");
                
                // [핵심 수정 2] 함정으로 인한 체력 변화를 즉시 동기화
                // (이 메서드가 없으면 Game.cs에 추가해야 함 -> 아래 Game.cs 수정 참조)
                game.OnTrapDamageTaken(); 
                break;
            
            // [Case B] 전투 함정 (몬스터 매복)
            case TrapType.Battle:
                game.AddLog("함정이다! 숨어있던 몬스터가 공격한다!");
                
                // (미믹 대신 일반 랜덤 몬스터 생성 - 기존 로직 유지)
                Monster trapMonster = MonsterDB.CreateRandomMonster(0, 0, rand, currentStage);                
                
                trapMonster.X = this.X;
                trapMonster.Y = this.Y;
                
                // 전투 시작 (isFromTrap: true)
                game.StartBattle(trapMonster, true); 
                break;
        }
        
        // 4. 멀티플레이라면 함정 발동 상태 동기화 (다른 사람 맵에서도 함정 지우기)
        // (Game.cs의 SendTrapUpdate 헬퍼 사용)
        game.SendTrapUpdatePacket(this); 
    }

    // [신규] 네트워크 동기화용 강제 발동 메서드
    public void ForceTrigger(Game game)
    {
        if (IsTriggered) return;

        IsTriggered = true;
        
        // 맵 타일 변경 (함정 제거 시각화)
        game.UpdateMapTile(X, Y, '.');
    }
}