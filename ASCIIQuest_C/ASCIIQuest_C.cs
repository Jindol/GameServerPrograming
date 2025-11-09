//
// ASCIIQuest_C/ASCIIQuest_C.cs (깜빡임 해결 최종 코드)
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
            
            Console.Write("서버에 연결되었습니다..."); 
            Thread.Sleep(500); 

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

    // [핵심 수정] 화면 렌더링 방식을 SetCursorPosition(0, 0)으로 변경
    private void StartReceivingData()
    {
        Thread thread = new Thread(() =>
        {
            try
            {
                while (connected)
                {
                    string singleLineData = reader.ReadLine(); 
                    
                    if (singleLineData != null)
                    {
                        string[] lines = singleLineData.Split('|');

                        // 1. 닉네임 프롬프트 확인 (커서 표시용)
                        string lastMeaningfulLine = "";
                        for(int i = lines.Length - 1; i >= 0; i--)
                        {
                            if (!string.IsNullOrEmpty(lines[i]))
                            {
                                lastMeaningfulLine = lines[i];
                                break;
                            }
                        }
                        bool isPrompt = lastMeaningfulLine.TrimEnd().EndsWith("> ");
                        
                        // [핵심 수정] 2. Console.Clear() 대신 커서를 (0,0)으로 이동
                        Console.SetCursorPosition(0, 0); 
                        
                        int lineCount = 0;
                        for (int y = 0; y < lines.Length; y++)
                        {
                            // 마지막 빈 줄은 무시
                            if (y == lines.Length - 1 && string.IsNullOrEmpty(lines[y]))
                            {
                                continue;
                            }

                            string line = lines[y].TrimEnd('\r');

                            // [핵심 수정] 3. WriteLine() 대신 Write()로 덮어쓰기
                            Console.Write(line);

                            // [핵심 수정] 4. 현재 줄의 나머지 부분을 공백으로 지우기 (잔상 제거)
                            int spacesToPad = Console.WindowWidth - line.Length;
                            if (spacesToPad > 1) // 1칸 이상 여유 있을 때만
                            {
                                Console.Write(new string(' ', spacesToPad - 1));
                            }

                            // [핵심 수정] 5. 다음 줄로 수동 이동
                            if (y < Console.WindowHeight - 1)
                            {
                                Console.SetCursorPosition(0, y + 1);
                            }
                            lineCount++;
                        }

                        // [핵심 수정] 6. 화면의 나머지 아랫부분을 지우기 (잔상 제거)
                        for (int y = lineCount; y < Console.WindowHeight; y++)
                        {
                            Console.SetCursorPosition(0, y);
                            Console.Write(new string(' ', Console.WindowWidth - 1));
                        }

                        // 7. 커서 설정
                        if (isPrompt)
                        {
                            Console.CursorVisible = true;
                            // 닉네임 입력 시 커서를 프롬프트 끝으로 이동
                            Console.SetCursorPosition(lastMeaningfulLine.Length, lineCount - 1); 
                        }
                        else 
                        {
                            Console.CursorVisible = false;
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
        int port = 12345; // [참고] 만약 '소켓' 오류가 나면 12346으로 변경

        MUDClient client = new MUDClient(serverAddress, port);
        client.Wait(); 
    }
}