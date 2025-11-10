//
// ASCIIQuest_C/ASCIIQuest_C.cs (깜빡임 현상 최종 수정 코드)
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
                        try
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
                            try
                            {
                                Console.SetCursorPosition(0, 0);
                            }
                            catch (ArgumentOutOfRangeException)
                            {
                                // 화면 크기가 변경되었을 수 있음
                                continue;
                            }
                            
                        // ANSI 이스케이프 제거용 정규식
                        System.Text.RegularExpressions.Regex ansiRegex = new System.Text.RegularExpressions.Regex(@"\x1B\[[0-9;]*m");

                        int screenY = 0; // 실제 화면 Y 좌표
                            int maxHeight = Math.Min(Console.WindowHeight, lines.Length);
                            
                            for (int i = 0; i < lines.Length && screenY < maxHeight; i++)
                            {
                                // 마지막 빈 줄은 무시
                                if (i == lines.Length - 1 && string.IsNullOrEmpty(lines[i]))
                                {
                                    continue;
                                }

                                string line = lines[i].TrimEnd('\r');
                                
                                if (string.IsNullOrEmpty(line) && i < lines.Length - 1)
                                {
                                    // 빈 줄도 표시 (단, 마지막 빈 줄은 제외)
                                    try
                                    {
                                        Console.SetCursorPosition(0, screenY);
                                        Console.Write(new string(' ', Console.WindowWidth));
                                    }
                                    catch (ArgumentOutOfRangeException)
                                    {
                                        break;
                                    }
                                    screenY++;
                                    continue;
                                }

                                try
                                {
                                    // [핵심 수정] 3. 현재 화면 위치에 줄 표시
                                    Console.SetCursorPosition(0, screenY);
                                Console.Write(line.Replace("\r", "")); // [추가] 혹시 남은 CR 제거

                                    // [핵심 수정] 4. 현재 줄의 나머지 부분을 공백으로 지우기 (잔상 제거)
                                int visibleLen = ansiRegex.Replace(line, "").Length;
                                int spacesToPad = Console.WindowWidth - visibleLen;
                                    if (spacesToPad > 0)
                                    {
                                        Console.Write(new string(' ', spacesToPad));
                                    }
                                }
                                catch (ArgumentOutOfRangeException)
                                {
                                    break;
                                }

                                screenY++;
                            }

                            // [핵심 수정] 5. 화면의 나머지 아랫부분을 지우기 (잔상 제거)
                            for (int y = screenY; y < Console.WindowHeight; y++)
                            {
                                try
                                {
                                    Console.SetCursorPosition(0, y);
                                    Console.Write(new string(' ', Console.WindowWidth));
                                }
                                catch (ArgumentOutOfRangeException)
                                {
                                    break;
                                }
                            }

                            // 6. 커서 설정
                            if (isPrompt)
                            {
                                Console.CursorVisible = true;
                                // 닉네임 입력 시 커서를 프롬프트 끝으로 이동
                                int lastLineY = screenY > 0 ? screenY - 1 : 0;
                                try
                                {
                                    int promptVisibleLen = ansiRegex.Replace(lastMeaningfulLine, "").Length;
                                    Console.SetCursorPosition(Math.Min(promptVisibleLen, Console.WindowWidth - 1), lastLineY);
                                }
                                catch (ArgumentOutOfRangeException)
                                {
                                    // 무시
                                }
                            }
                            else 
                            {
                                Console.CursorVisible = false;
                            }
                        }
                        catch (Exception ex)
                        {
                            // 디버깅용 (나중에 제거 가능)
                            // Console.SetCursorPosition(0, 0);
                            // Console.Write($"Error: {ex.Message}");
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
        
        // [참고] 만약 '소켓' 오류가 나면 12346으로 변경
        int port = 12345; 
        
        // [참고] '소켓' 오류가 나면 서버와 클라이언트 모두 12346으로 변경
        MUDClient client = new MUDClient(serverAddress, port);
        client.Wait(); 
    }
}