using System;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text.Json; 

public enum PacketType
{
    None,
    // [Phase 1: 로비]
    RoomListRequest, RoomListResponse, PlayerInfo, Chat, Disconnect,
    
    // [Phase 2: 인게임 신규 추가]
    GameStart,          // "게임 시작하자!" (인트로 진입)
    ClassSelect,        // "나 전사 골랐어"
    MapMove,            // "나 (5, 10)으로 이동했어"
    BattleStart,        // "야생의 몬스터와 전투 시작!"
    BattleAction,       // "나 공격했어 (데미지 50)"
    BattleTurnEnd,       // "내 턴 끝났어, 네 차례야"
    MapInit,
    MonsterUpdate,
    // [신규]
    EnemyAction, // 적이 공격했을 때
    FleeRequest, // 후퇴 요청
    BattleEnd,    // 전투 종료 (승리/도주/패배)
    ChestUpdate, 
    TrapUpdate,
    MonsterDead,
    // [신규] 전투 결과창(루팅/레벨업) 종료 확인 패킷
    BattleResultFinished,
    PortalEnter,
    RoomInfoRequest,
    RoomInfoResponse
}

[Serializable]
public class Packet
{
    public PacketType Type { get; set; }
    public string Data { get; set; } 

    public static byte[] Serialize(Packet packet)
    {
        string json = JsonSerializer.Serialize(packet);
        return System.Text.Encoding.UTF8.GetBytes(json);
    }

    public static Packet Deserialize(byte[] data)
    {
        string json = System.Text.Encoding.UTF8.GetString(data).Trim('\0');
        return JsonSerializer.Deserialize<Packet>(json);
    }
}

// --- 데이터 클래스들 ---

public class MapInitData
{
    public int Seed { get; set; }      // 랜덤 시드
    public int Stage { get; set; }     // 스테이지 번호
    public int HostX { get; set; }     // 호스트 시작 위치 X
    public int HostY { get; set; }     // 호스트 시작 위치 Y
    public int MapWidth { get; set; }
    public int MapHeight { get; set; }
}

// [신규] 상자 상태 동기화 데이터
public class ChestUpdateData
{
    public int X { get; set; }
    public int Y { get; set; }
    public bool IsOpen { get; set; }
}

// [신규] 함정 상태 동기화 데이터
public class TrapUpdateData
{
    public int X { get; set; }
    public int Y { get; set; }
    public bool IsTriggered { get; set; }
}

public class MonsterUpdateData
{
    // (몬스터 리스트의 인덱스 순서대로 좌표를 보냄)
    public List<int> XPositions { get; set; }
    public List<int> YPositions { get; set; }
}

public class ChatData
{
    public string Nickname { get; set; } 
    public string Message { get; set; }  
}

public class PlayerInfoData
{
    public string PlayerId { get; set; } 
    public string Nickname { get; set; }
    public bool IsHost { get; set; } 
    public int PlayerClass { get; set; } // [신규] 선택한 직업 (enum int값)
    public bool IsReady { get; set; }    // [신규] 준비 완료 여부 (직업 선택 완료)
    
    // [신규] 전투 동기화용 스탯
    public int DEX { get; set; } 
    public int HP { get; set; }
    public int MaxHP { get; set; }

    public int DEF { get; set; }

    public bool IsWaitingAtPortal { get; set; }
}

[Serializable]
public class RoomInfo
{
    public string Title { get; set; }
    public string HostName { get; set; }
    public bool IsPrivate { get; set; }
    public string Password { get; set; }
    public int CurrentPlayers { get; set; }
    public int MaxPlayers { get; set; } = 2;
    public string IpAddress { get; set; }
    public int Port { get; set; }

    public static string Serialize(RoomInfo info) => JsonSerializer.Serialize(info);
    public static RoomInfo Deserialize(string json) => JsonSerializer.Deserialize<RoomInfo>(json);
}

// 직업 선택 정보
public class ClassSelectData
{
    public PlayerClass SelectedClass { get; set; }
}

// 맵 이동 정보 (좌표)
public class MapMoveData
{
    public int X { get; set; }
    public int Y { get; set; }
}

// 전투 시작 정보 (몬스터 ID)
public class BattleStartData
{
    public string MonsterId { get; set; }
    public bool IsFromTrap { get; set; }

    // [신규] 맵 상의 몬스터 좌표 (원본 식별용)
    public int MapX { get; set; }
    public int MapY { get; set; }

    public int CurrentHP { get; set; } = -1;

    // [신규] 몬스터 스탯 동기화용 필드
    public int MaxHP { get; set; } = -1;
    public int ATK { get; set; } = -1;
    public int DEF { get; set; } = -1;
    public int EXPReward { get; set; } = -1;
}

public class MonsterDeadData
{
    public int X { get; set; }
    public int Y { get; set; }
}

// 전투 행동 정보 (무엇을 했는지, 데미지는 얼마인지)
public class BattleActionData
{
    public int ActionType { get; set; } // { get; set; } 필수!
    public int Damage { get; set; }
    public bool IsCrit { get; set; }
    public string SkillName { get; set; }
    public bool IsTargetHost { get; set; }
}