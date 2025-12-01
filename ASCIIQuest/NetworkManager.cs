using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Text;
using System.Collections.Generic;
using System.Text.Json;

public class NetworkManager
{
    private TcpListener server;
    private TcpClient client;
    private NetworkStream stream;
    private Thread receiveThread;
    private RoomInfo myHostingRoom;
    
    // [신규] LAN 방 검색을 위한 UDP 클라이언트
    private UdpClient udpBroadcaster;
    private UdpClient udpListener;
    private Thread broadcastThread;
    private Thread udpListenThread;
    private const int DISCOVERY_PORT = 47777; // 방 검색용 포트
    private bool isBroadcasting = false;
    private bool isUdpListening = false;
    public bool IsDirty { get; set; } = false;

    public bool IsHost { get; private set; }
    public bool IsConnected => client != null && client.Connected;

    public Queue<Packet> PacketQueue { get; private set; } = new Queue<Packet>();
    public object QueueLock = new object();

    // [신규] 검색된 방 목록 (Game.cs에서 가져감)
    public List<RoomInfo> DiscoveredRooms { get; private set; } = new List<RoomInfo>();
    public object RoomListLock = new object();

    public TcpClient GuestClient { get; private set; }

    private static NetworkManager instance;
    public static NetworkManager Instance => instance ??= new NetworkManager();

    // --- 1. 호스트 시작 (TCP 서버 + UDP 방 알림) ---
    public void StartHost(int port, RoomInfo roomInfo)
    {
        try
        {
            IsHost = true;
            server = new TcpListener(IPAddress.Any, port);
            server.Start();

            int assignedPort = ((IPEndPoint)server.LocalEndpoint).Port;
            roomInfo.Port = assignedPort;
            
            // [신규] 방 정보 저장 (인원수 관리를 위해)
            myHostingRoom = roomInfo;

            server.BeginAcceptTcpClient(OnClientConnected, null);
            
            // 저장된 myHostingRoom 객체를 브로드캐스트 (내용이 바뀌면 반영됨)
            StartRoomBroadcast(myHostingRoom);
        }
        catch (Exception e) 
        { 
            Console.WriteLine("Host Error: " + e.Message); 
            IsHost = false;
        }
    }

    private void StartRoomBroadcast(RoomInfo info)
    {
        isBroadcasting = true;
        udpBroadcaster = new UdpClient();
        udpBroadcaster.EnableBroadcast = true; // 브로드캐스트 활성화

        // 자신의 로컬 IP 설정
        string localIP = GetLocalIPAddress();
        info.IpAddress = localIP; 

        broadcastThread = new Thread(() =>
        {
            IPEndPoint target = new IPEndPoint(IPAddress.Broadcast, DISCOVERY_PORT);
            while (isBroadcasting)
            {
                try
                {
                    string json = RoomInfo.Serialize(info);
                    byte[] data = Encoding.UTF8.GetBytes("ROOM_DISCOVERY:" + json);
                    udpBroadcaster.Send(data, data.Length, target);
                    Thread.Sleep(1000); // 1초마다 "나 여기 있어" 알림
                }
                catch { break; }
            }
        });
        broadcastThread.IsBackground = true;
        broadcastThread.Start();
    }

    // --- 2. 방 검색 (UDP Listening) ---
    public void StartSearchingRooms()
    {
        StopSearchingRooms(); // 기존 검색 중지

        lock (RoomListLock) DiscoveredRooms.Clear();
        isUdpListening = true;

        try
        {
            udpListener = new UdpClient();
            
            // [핵심] 한 PC에서 여러 클라이언트가 동시에 방 검색(Listening)을 할 수 있게 설정
            udpListener.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            
            // 포트 바인딩
            udpListener.Client.Bind(new IPEndPoint(IPAddress.Any, DISCOVERY_PORT));

            udpListenThread = new Thread(() =>
            {
                // ... (이 내부 로직은 기존과 동일) ...
                IPEndPoint remoteEP = new IPEndPoint(IPAddress.Any, 0);
                while (isUdpListening)
                {
                    try
                    {
                        if (udpListener.Available > 0)
                        {
                            byte[] data = udpListener.Receive(ref remoteEP);
                            string msg = Encoding.UTF8.GetString(data);

                            if (msg.StartsWith("ROOM_DISCOVERY:"))
                            {
                                string json = msg.Substring("ROOM_DISCOVERY:".Length);
                                RoomInfo room = RoomInfo.Deserialize(json);

                                lock (RoomListLock)
                                {
                                    // [추가 수정] 로컬 테스트 시 닉네임/방제목이 같을 수 있으므로
                                    // 포트 번호까지 비교해서 서로 다른 방으로 인식하게 함
                                    int idx = DiscoveredRooms.FindIndex(r => 
                                        r.HostName == room.HostName && 
                                        r.Title == room.Title && 
                                        r.Port == room.Port); // 포트까지 비교

                                    if (idx != -1) DiscoveredRooms[idx] = room;
                                    else DiscoveredRooms.Add(room);
                                }
                                IsDirty = true;
                            }
                        }
                        else Thread.Sleep(100);
                    }
                    catch { break; }
                }
            });
            udpListenThread.IsBackground = true;
            udpListenThread.Start();
        }
        catch (Exception e)
        { 
            Console.WriteLine("Search Error: " + e.Message);
        }
    }

    public void StopSearchingRooms()
    {
        isUdpListening = false;
        udpListener?.Close();
        udpListener = null;
    }

    // --- 3. 접속 및 통신 ---
    private void OnClientConnected(IAsyncResult ar)
    {
        if (!IsHost) return;
        try 
        {
            // 기존 게스트가 있으면 끊기 (1:1 게임이므로)
            if (GuestClient != null && GuestClient.Connected)
            {
                GuestClient.Close();
            }

            GuestClient = server.EndAcceptTcpClient(ar);
            
            // [신규] 접속 성공 시 인원수 증가
            if (myHostingRoom != null) myHostingRoom.CurrentPlayers = 2;

            Thread clientThread = new Thread(() => ReceiveLoop(GuestClient));
            clientThread.IsBackground = true;
            clientThread.Start();
        }
        catch { }
    }
    // [신규] 게스트를 다시 받을 준비를 하는 메서드
    public void RestartListening()
    {
        if (IsHost && server != null)
        {
            try
            {
                // 다시 접속 대기 시작
                server.BeginAcceptTcpClient(OnClientConnected, null);
            }
            catch { }
        }
    }

    public bool ConnectToHost(string ip, int port)
    {
        try
        {
            IsHost = false;
            client = new TcpClient();
            // 연결 타임아웃 2초 설정 (너무 오래 걸리지 않게)
            var result = client.BeginConnect(ip, port, null, null);
            bool success = result.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(2));
            
            if (!success) return false;

            client.EndConnect(result);
            stream = client.GetStream();

            receiveThread = new Thread(() => ReceiveLoop(client));
            receiveThread.IsBackground = true;
            receiveThread.Start();
            return true;
        }
        catch { return false; }
    }

    // [수정] 안전한 수신 루프 (오류 발생 시 조용히 종료)
    private void ReceiveLoop(TcpClient socket)
    {
        NetworkStream netStream = socket.GetStream();
        byte[] buffer = new byte[4096];

        try
        {
            while (socket.Connected)
            {
                // 1. 데이터 읽기
                int bytesRead = netStream.Read(buffer, 0, buffer.Length);
                if (bytesRead == 0) throw new Exception("Disconnected"); // 0바이트면 정상 종료

                // 2. 유효한 데이터만 복사
                byte[] data = new byte[bytesRead];
                Array.Copy(buffer, data, bytesRead);

                // [핵심 수정] 패킷 해석 시 에러가 나도 연결을 끊지 않음
                try 
                {
                    Packet packet = Packet.Deserialize(data);
                    lock (QueueLock) PacketQueue.Enqueue(packet);
                    IsDirty = true;
                }
                catch (JsonException) 
                { 
                    // JSON 파싱 에러(패킷 뭉침 등)는 무시하고 넘어감
                    // (완벽한 해결을 위해선 패킷 길이 헤더 처리가 필요하지만, 약식으로 이렇게 처리)
                }
            }
        }
        catch (Exception)
        { 
            // 진짜 연결 문제(소켓 오류, 강제 종료)일 때만 Disconnect 처리
            lock (QueueLock)
            {
                PacketQueue.Enqueue(new Packet { Type = PacketType.Disconnect });
            }
            IsDirty = true;
        }
        finally
        {
            if (socket != null) socket.Close();
            if (IsHost && socket == GuestClient) GuestClient = null;
        }
    }

    public void Send(Packet packet)
    {
        try
        {
            if (!IsConnected && !IsHost) return;
            
            byte[] data = Packet.Serialize(packet);
            NetworkStream targetStream = null;

            if (IsHost && GuestClient != null) targetStream = GuestClient.GetStream();
            else if (!IsHost && stream != null) targetStream = stream;

            targetStream?.Write(data, 0, data.Length);
        }
        catch { }
    }

    // [수정] 안전한 종료 처리 (튕김 방지)
    public void Close()
    {
        // [핵심 수정] 상태 플래그 초기화
        IsHost = false; 
        IsDirty = false;

        // 1. UDP 관련 종료
        isBroadcasting = false;
        isUdpListening = false;
        try { udpBroadcaster?.Close(); } catch { }
        try { udpListener?.Close(); } catch { }
        udpBroadcaster = null;
        udpListener = null;

        // 2. TCP 관련 종료
        try { stream?.Close(); } catch { }
        try { client?.Close(); } catch { }
        try { server?.Stop(); } catch { }
        try { GuestClient?.Close(); } catch { }

        client = null;
        server = null;
        GuestClient = null;
        stream = null;

        // 큐 비우기 (잔여 패킷 제거)
        lock (QueueLock) PacketQueue.Clear();
        lock (RoomListLock) DiscoveredRooms.Clear();
    }

    private string GetLocalIPAddress()
    {
        var host = Dns.GetHostEntry(Dns.GetHostName());
        foreach (var ip in host.AddressList)
        {
            if (ip.AddressFamily == AddressFamily.InterNetwork) return ip.ToString();
        }
        return "127.0.0.1";
    }

    public void UpdateRoomPlayerCount(int count)
    {
        if (myHostingRoom != null)
        {
            myHostingRoom.CurrentPlayers = count;
        }
    }
}