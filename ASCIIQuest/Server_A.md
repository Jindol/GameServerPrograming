# 서버 관련 코드 정리 문서 (Server_A)

## 핵심 구조

### 1. NetworkManager.cs
**역할**: TCP/UDP 네트워크 통신 관리

**주요 메서드:**
- `StartHost(port, roomInfo)`: TCP 서버 시작 + UDP 방 알림 (1초마다 브로드캐스트)
- `StartSearchingRooms()`: UDP 포트 47777에서 방 검색
- `ConnectToHost(ip, port)`: TCP 연결 (타임아웃 2초)
- `Send(packet)`: 패킷 전송 (길이 헤더 4바이트 + JSON 본문)
- `Close()`: 모든 네트워크 리소스 종료

**주요 속성:**
- `IsHost`, `IsConnected`, `PacketQueue`, `DiscoveredRooms`

---

### 2. MultiplayerBattleManager.cs
**역할**: 멀티플레이어 전투 동기화

**로컬 플레이어 행동:**
- `PerformLocalAttack()`: 공격 → 데미지 계산 → 패킷 전송
- `PerformLocalSkill()`: 스킬 → 공격/버프/힐 처리 → 패킷 전송
- `PerformLocalItem()`: 아이템 사용 → 패킷 전송

**상대방 행동 수신:**
- `OnReceiveBattleAction()`: 패킷 수신 → 몬스터/동료 상태 업데이트

**몬스터 턴 (호스트 전용):**
- `ProcessMonsterTurn_Host()`: 맹독(10) → 독(11) → 출혈(12) → 기절(13) → 공격력 회복(14) → 기본 공격(3)
- `OnReceiveEnemyAction()`: 호스트가 보낸 적 행동 패킷 처리

---

### 3. Packet.cs
**패킷 타입:**
- **로비**: `PlayerInfo`, `Chat`, `Disconnect`
- **인게임**: `GameStart`, `ClassSelect`, `MapInit`, `MapMove`, `BattleStart`, `BattleAction`, `EnemyAction`, `BattleEnd`, `ChestUpdate`, `TrapUpdate`, `MonsterUpdate`

**주요 데이터 클래스:**
```csharp
// 방 정보
RoomInfo { Title, HostName, IpAddress, Port, CurrentPlayers, MaxPlayers }

// 플레이어 정보
PlayerInfoData { PlayerId, Nickname, IsHost, PlayerClass, HP, MaxHP, DEF, DEX }

// 전투 행동
BattleActionData { 
    ActionType,  // 0:공격, 1:스킬, 2:아이템, 3:적공격, 10:맹독, 11:독, 12:출혈, 13:기절, 14:공격력회복
    Damage,      // 음수면 힐/버프
    IsCrit, 
    SkillName, 
    IsTargetHost 
}
```

**직렬화**: JSON (UTF-8)

---

### 4. 네트워크 프로토콜 및 통신 상세

#### TCP 통신 (게임 데이터 전송)

**연결 과정:**

1. **호스트 시작:**
   ```
   StartHost(port, roomInfo)
   ├─ TcpListener 생성 (IPAddress.Any, port)
   ├─ 서버 시작 (server.Start())
   ├─ 비동기 클라이언트 수락 대기 (BeginAcceptTcpClient)
   └─ UDP 브로드캐스트 시작
   ```

2. **클라이언트 연결:**
   ```
   ConnectToHost(ip, port)
   ├─ TcpClient 생성
   ├─ 비동기 연결 시도 (BeginConnect)
   ├─ 2초 타임아웃 대기
   ├─ 연결 완료 (EndConnect)
   └─ 수신 스레드 시작 (ReceiveLoop)
   ```

3. **호스트의 클라이언트 수락:**
   ```
   OnClientConnected (콜백)
   ├─ 기존 게스트 연결 종료 (1:1 게임)
   ├─ 새 클라이언트 수락 (EndAcceptTcpClient)
   ├─ 인원수 업데이트 (CurrentPlayers = 2)
   └─ 수신 스레드 시작 (ReceiveLoop)
   ```

**패킷 송수신 메커니즘:**

**송신 과정 (`Send` 메서드):**
```csharp
1. 패킷 → JSON 직렬화 (Packet.Serialize)
2. JSON → UTF-8 바이트 배열
3. 길이 헤더 생성 (4바이트, int)
4. 최종 패킷 구성: [4바이트 길이] + [JSON 본문]
5. NetworkStream.Write()로 전송
```

**수신 과정 (`ReceiveLoop` 메서드):**
```csharp
// 별도 스레드에서 실행 (백그라운드)
while (연결 유지)
{
    Step 1: 길이 헤더 읽기 (4바이트)
    ├─ int dataLength = BitConverter.ToInt32(lengthBuffer)
    └─ 부분 읽기 처리 (totalRead < 4까지 반복)
    
    Step 2: 본문 데이터 읽기 (dataLength 바이트)
    ├─ byte[] dataBuffer = new byte[dataLength]
    └─ 부분 읽기 처리 (totalRead < dataLength까지 반복)
    
    Step 3: 역직렬화 및 큐 저장
    ├─ Packet packet = Packet.Deserialize(dataBuffer)
    ├─ lock (QueueLock) PacketQueue.Enqueue(packet)
    └─ IsDirty = true (렌더링 필요 신호)
}
```

**패킷 형식:**
```
[길이 헤더: 4바이트 (int)] + [JSON 본문: UTF-8]
예: [0x00, 0x00, 0x00, 0x64] + {"Type":1,"Data":"{...}"}
    (길이 100바이트)
```

**에러 처리:**
- 연결 끊김: `Disconnect` 패킷을 큐에 추가
- JSON 파싱 오류: 해당 패킷만 스킵하고 다음 패킷 처리 계속
- 네트워크 오류: 조용히 처리하고 연결 종료

#### UDP 통신 (LAN 방 검색)

**호스트 브로드캐스트:**
```
StartRoomBroadcast()
├─ UdpClient 생성 (EnableBroadcast = true)
├─ 로컬 IP 주소 획득 (GetLocalIPAddress)
├─ 백그라운드 스레드 시작
└─ 1초마다 반복:
    ├─ RoomInfo → JSON 직렬화
    ├─ "ROOM_DISCOVERY:" + JSON → UTF-8 바이트
    └─ IPAddress.Broadcast:47777로 전송
```

**클라이언트 리스닝:**
```
StartSearchingRooms()
├─ UdpClient 생성
├─ ReuseAddress 옵션 설정 (같은 PC 여러 클라이언트 지원)
├─ 포트 47777 바인딩
├─ 백그라운드 스레드 시작
└─ 반복:
    ├─ UDP 메시지 수신 대기
    ├─ "ROOM_DISCOVERY:" 접두사 확인
    ├─ JSON 파싱 → RoomInfo
    └─ DiscoveredRooms 업데이트 (중복 체크: HostName+Title+Port)
```

**UDP 메시지 형식:**
```
"ROOM_DISCOVERY:" + JSON(RoomInfo)
예: "ROOM_DISCOVERY:{"Title":"테스트방","HostName":"플레이어1",...}"
```

#### 스레드 구조

**호스트:**
- 메인 스레드: 게임 로직, 패킷 처리
- TCP 수신 스레드: GuestClient로부터 패킷 수신
- UDP 브로드캐스트 스레드: 방 정보 주기적 전송

**클라이언트:**
- 메인 스레드: 게임 로직, 패킷 처리
- TCP 수신 스레드: 호스트로부터 패킷 수신
- UDP 리스닝 스레드: 방 검색 메시지 수신

**스레드 안전성:**
- `PacketQueue`: `QueueLock`으로 동기화
- `DiscoveredRooms`: `RoomListLock`으로 동기화
- 모든 스레드는 백그라운드로 실행

#### 패킷 처리 흐름

**송신 → 수신 → 처리:**
```
[송신자]
Game.cs → NetworkManager.Send()
  └─ TCP 스트림으로 전송

[수신자]
TCP 스트림 → ReceiveLoop() (별도 스레드)
  └─ PacketQueue.Enqueue()
  └─ IsDirty = true

[처리]
Game.RunGameLoop() (메인 스레드)
  └─ ProcessNetworkPackets()
      └─ PacketQueue.Dequeue()
      └─ 패킷 타입별 핸들러 호출
```

**동기화 전략:**
1. **호스트 권한**: 몬스터 턴, 맵 생성은 호스트만 담당
2. **이벤트 기반**: 플레이어 행동을 즉시 패킷으로 전송
3. **턴 기반 전투**: 각 플레이어 턴 종료 후 다음 플레이어
4. **상태 동기화**: 주기적으로 PlayerInfo 패킷 전송
5. **Watchdog**: 3초 이상 행동 없으면 호스트가 자동 진행

---

### 5. Game.cs 통합

**패킷 처리:**
- `ProcessNetworkPackets()`: 매 프레임 패킷 큐 처리
- 각 패킷 타입별 핸들러 호출

**전투 동기화:**
- `ProcessMultiplayerMonsterTurn()`: 호스트가 몬스터 턴 처리
- Watchdog: 3초 이상 행동 없으면 자동 진행

---

## 통신 흐름 상세 예시

### 시나리오 1: 방 생성 및 접속

```
[호스트]
1. StartHost(7777, roomInfo)
   └─ TCP 서버 시작 (포트 7777)
   └─ UDP 브로드캐스트 시작 (1초마다)

[클라이언트]
2. StartSearchingRooms()
   └─ UDP 포트 47777 리스닝 시작
   └─ DiscoveredRooms에 방 정보 수집

3. ConnectToHost("192.168.0.1", 7777)
   └─ TCP 연결 시도 (타임아웃 2초)
   └─ 연결 성공 → ReceiveLoop 시작

[호스트]
4. OnClientConnected (콜백)
   └─ GuestClient = 새 클라이언트
   └─ ReceiveLoop(GuestClient) 시작
```

### 시나리오 2: 전투 행동 동기화

```
[플레이어 A - 공격]
1. MultiplayerBattleManager.PerformLocalAttack()
   ├─ 데미지 계산 (로컬)
   ├─ 몬스터 HP 감소 (로컬)
   └─ 패킷 전송:
       {
         Type: BattleAction,
         Data: {
           ActionType: 0,
           Damage: 50,
           IsCrit: false
         }
       }

[NetworkManager]
2. Send(packet)
   ├─ JSON 직렬화
   ├─ 길이 헤더 추가
   └─ TCP 스트림으로 전송

[플레이어 B - 수신]
3. ReceiveLoop() (별도 스레드)
   ├─ 길이 헤더 읽기 (4바이트)
   ├─ 본문 데이터 읽기
   ├─ JSON 역직렬화
   └─ PacketQueue.Enqueue()

4. ProcessNetworkPackets() (메인 스레드)
   ├─ PacketQueue.Dequeue()
   ├─ HandleBattleAction() 호출
   └─ MultiplayerBattleManager.OnReceiveBattleAction()
       └─ 몬스터 HP 감소 (동기화)
```

### 시나리오 3: 몬스터 턴 처리

```
[호스트]
1. ProcessMonsterTurn_Host()
   ├─ 맹독 처리 → 패킷 전송 (ActionType: 10)
   ├─ 독 처리 → 패킷 전송 (ActionType: 11)
   ├─ 출혈 처리 → 패킷 전송 (ActionType: 12)
   ├─ 기절 체크 → 패킷 전송 (ActionType: 13)
   └─ 기본 공격 → 패킷 전송 (ActionType: 3)

[각 패킷 전송 후 50ms 지연]
- TCP 패킷 뭉침 방지

[게스트]
2. ReceiveLoop() → PacketQueue
3. ProcessNetworkPackets()
   └─ HandleEnemyAction()
       └─ OnReceiveEnemyAction()
           └─ 상태이상 턴 차감 및 데미지 적용
```

### 시나리오 4: 연결 끊김 처리

```
[연결 끊김 발생]
1. ReceiveLoop()에서 예외 발생
   └─ read == 0 또는 네트워크 오류

2. catch 블록 실행
   └─ PacketQueue.Enqueue(Disconnect 패킷)
   └─ IsDirty = true

3. ProcessNetworkPackets()
   └─ HandleDisconnection()
       ├─ 연결 상태 초기화
       └─ 게임 상태 변경
```

---

## 패킷 타입별 상세 설명

### 로비 단계 패킷

**PlayerInfo:**
- **용도**: 플레이어 정보 동기화
- **전송 시점**: 접속 시, 스탯 변경 시
- **데이터**: 닉네임, 직업, HP, DEF, DEX 등

**Chat:**
- **용도**: 채팅 메시지
- **전송 시점**: 플레이어가 메시지 입력 시
- **데이터**: 닉네임, 메시지 내용

### 인게임 패킷

**MapInit:**
- **용도**: 맵 초기화 (호스트 → 게스트)
- **전송 시점**: 게임 시작 시
- **데이터**: 시드, 스테이지, 맵 크기, 호스트 시작 위치

**MapMove:**
- **용도**: 플레이어 이동 동기화
- **전송 시점**: 플레이어 이동 시
- **데이터**: X, Y 좌표

**BattleStart:**
- **용도**: 전투 시작 알림
- **전송 시점**: 몬스터와 조우 시
- **데이터**: 몬스터 ID, 스탯, 위치

**BattleAction:**
- **용도**: 전투 행동 (공격/스킬/아이템)
- **전송 시점**: 플레이어 행동 시
- **데이터**: 행동 타입, 데미지, 스킬명 등

**EnemyAction:**
- **용도**: 적 행동 (호스트 → 게스트)
- **전송 시점**: 몬스터 턴 처리 시
- **데이터**: 행동 타입, 데미지, 타겟 정보

**ChestUpdate / TrapUpdate:**
- **용도**: 상자/함정 상태 동기화
- **전송 시점**: 상자 열림/함정 발동 시
- **데이터**: 위치, 상태 (열림/발동 여부)

---

## 통신 최적화 및 주의사항

### 성능 최적화

1. **비동기 I/O**
   - `BeginAcceptTcpClient`: 비동기 클라이언트 수락
   - `BeginConnect`: 비동기 연결 시도
   - 메인 스레드 블로킹 방지

2. **백그라운드 스레드**
   - 모든 수신 스레드는 `IsBackground = true`
   - 애플리케이션 종료 시 자동 종료

3. **패킷 뭉침 방지**
   - 연속 패킷 전송 시 50ms 지연
   - TCP Nagle 알고리즘과의 충돌 방지

4. **부분 읽기 처리**
   - `Read()`는 전체 데이터를 한 번에 읽지 못할 수 있음
   - `totalRead`로 누적 읽기 보장

### 주요 주의사항

1. **스레드 안전성**
   - `PacketQueue`: `QueueLock`으로 동기화 필수
   - `DiscoveredRooms`: `RoomListLock`으로 동기화 필수
   - 큐 접근 시 항상 락 사용

2. **호스트 권한**
   - 몬스터 턴 처리는 호스트만 가능
   - 맵 생성 및 초기화는 호스트 담당
   - 게스트는 수신만 가능

3. **연결 관리**
   - 연결 타임아웃: 2초 (너무 길면 UX 저하)
   - 연결 끊김 시 `Disconnect` 패킷 큐에 추가
   - `Close()` 호출로 모든 리소스 정리 필수

4. **에러 처리**
   - 네트워크 오류는 조용히 처리 (예외 전파 방지)
   - JSON 파싱 오류는 해당 패킷만 스킵
   - 연결 끊김은 정상 종료로 처리

5. **리소스 관리**
   - `Close()` 호출 시 모든 스레드 종료
   - UDP/TCP 소켓 모두 닫기
   - 큐 및 리스트 초기화

### 잠재적 문제점 및 해결책

1. **패킷 순서 보장**
   - TCP는 순서 보장하지만, 여러 스레드에서 전송 시 주의
   - 해결: 단일 스레드에서 전송 또는 시퀀스 번호 추가

2. **패킷 손실**
   - 네트워크 오류 시 패킷 손실 가능
   - 해결: 중요 패킷은 ACK 메커니즘 추가 고려

3. **지연 처리**
   - 네트워크 지연 시 동기화 어려움
   - 해결: 타임스탬프 추가 및 보간 처리 고려

4. **동시 접속**
   - 현재는 1:1 게임 (호스트:게스트)
   - 여러 게스트 지원 시 구조 변경 필요

---

## 핵심 버그 수정

- 더블 어택 방지: 로컬 처리 시 즉시 패킷 전송
- 상태이상 턴 동기화: 게스트도 정확히 차감
- 방 검색 중복 방지: 포트 번호까지 비교
- 안전한 종료: 모든 리소스 정리
