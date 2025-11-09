//
// ASCIIQuest_C/ASCIIQuest_C.cs (수정된 코드)
//
using System;
using System.IO; 
using System.Net.Sockets;
using System.Text;
using System.Threading;

class MUDClient
{
    private TcpClient client;
    private StreamWriter writer; 
    private StreamReader reader; 
    private bool connected = false;

    public MUDClient(string serverAddress, int port)
    {
        try
        {
            client = new TcpClient(serverAddress, port);
            NetworkStream stream = client.GetStream();
            writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };
            reader = new StreamReader(stream, Encoding.UTF8);
            connected = true;
            Console.WriteLine("서버에 연결되었습니다...");

            Console.CursorVisible = false;
            Console.Clear();

            StartReceivingData(); 
            StartSendingInput();  
        }
        catch (Exception e)
        {
            Console.WriteLine($"서버 연결 실패: {e.Message}");
            connected = false;
        }
    }

    // [핵심 수정] 서버로부터 화면 데이터를 받아 그리는 스레드
    private void StartReceivingData()
    {
        Thread thread = new Thread(() =>
        {
            try
            {
                while (connected)
                {
                    string fullScreenData = reader.ReadLine(); 
                    
                    if (fullScreenData != null)
                    {
                        // --- 1. 커서 보일지 결정 (닉네임 입력 중인지?) ---
                        bool isPrompt = fullScreenData.TrimEnd().EndsWith("> ");
                        if(isPrompt)
                            Console.CursorVisible = true;
                        else 
                            Console.CursorVisible = false;

                        // --- 2. 화면 다시 그리기 (G와 유사하게) ---
                        string[] lines = fullScreenData.Split('\n');
                        
                        for (int y = 0; y < lines.Length; y++)
                        {
                            string line = lines[y].TrimEnd('\r');
                            
                            // [수정] 0, Y 좌표로 이동해서 한 줄씩 쓴다 (WriteLine 아님)
                            Console.SetCursorPosition(0, y);
                            Console.Write(line);
                            
                            // [수정] 현재 줄의 나머지 공간을 지운다 (이전 잔상 제거)
                            Console.Write(new char[Math.Max(0, Console.WindowWidth - line.Length - 1)]);
                        }

                        // [수정] 화면의 나머지 아랫부분을 지운다 (이전 잔상 제거)
                        for (int y = lines.Length; y < Console.WindowHeight; y++)
                        {
                            Console.SetCursorPosition(0, y);
                            Console.Write(new char[Math.Max(0, Console.WindowWidth - 1)]);
                        }
                        
                        // --- 3. 커서 위치 조정 (닉네임 입력창) ---
                        if(isPrompt)
                        {
                            // 서버가 보낸 메시지는 "...닉네임을 입력하세요:\n> " 형태
                            // \n으로 쪼개면 마지막은 "", 마지막에서 두 번째가 "> "
                            if (lines.Length >= 2)
                            {
                                string promptLine = lines[lines.Length - 2]; // "> " 또는 "> NICKNAME"
                                int lineIndex = lines.Length - 2;
                                Console.SetCursorPosition(promptLine.Length, lineIndex);
                            }
                        }
                    }
                    else
                    {
                        connected = false;
                    }
                }
            }
            catch (Exception)
            {
                connected = false;
            }

            Console.Clear();
            Console.CursorVisible = true;
            Console.SetCursorPosition(0, 0);
            Console.WriteLine("서버와의 연결이 끊겼습니다. 아무 키나 눌러 종료합니다.");
        });
        thread.IsBackground = true;
        thread.Start();
    }

    // 키 입력을 즉시 서버로 보내는 스레드
    private void StartSendingInput()
    {
        try
        {
            while (connected)
            {
                ConsoleKeyInfo key = Console.ReadKey(true);
                SendMessage(key.Key.ToString());
            }
        }
        catch (Exception)
        {
            connected = false;
        }
    }

    private void SendMessage(string message)
    {
        if (!connected) return;
        try
        {
            writer.WriteLine(message); 
        }
        catch (Exception)
        {
            connected = false;
        }
    }

    public void Wait()
    {
        while (connected)
        {
            Thread.Sleep(100);
        }
    }
}

class Program
{
    static void Main()
    {
        string serverAddress = "127.0.0.1"; 
        int port = 12345;

        MUDClient client = new MUDClient(serverAddress, port);
        client.Wait(); 
    }
}