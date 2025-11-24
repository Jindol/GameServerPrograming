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
    private TcpClient? client;
    private StreamWriter? writer; 
    private StreamReader? reader; 
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

            // 콘솔 크기를 서버에 전송
            SendConsoleSize();

            StartReceivingData(); 
            StartSendingInput();  
        }
        catch (Exception e)
        {
            Console.WriteLine($"서버 연결 실패: {e.Message}");
            connected = false;
        }
    }

    private void SendConsoleSize()
    {
        try
        {
            int width = Console.WindowWidth;
            int height = Console.WindowHeight;
            SendMessage($"CONSOLESIZE:{width}:{height}");
        }
        catch (Exception)
        {
            // 기본값 사용
            SendMessage("CONSOLESIZE:120:30");
        }
    }

    // [핵심 수정] ASCIIQuest_G의 PrintBufferToConsole 방식과 유사하게 렌더링
    private void StartReceivingData()
    {
        Thread thread = new Thread(() =>
        {
            try
            {
                while (connected && reader != null)
                {
                    string? singleLineData = reader.ReadLine(); 
                    
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
                            
                            // ANSI 이스케이프 제거용 정규식
                            System.Text.RegularExpressions.Regex ansiRegex = new System.Text.RegularExpressions.Regex(@"\x1B\[[0-9;]*m");

                            // [핵심 수정] ASCIIQuest_G의 PrintBufferToConsole 방식과 유사하게 렌더링
                            // 먼저 화면 전체를 지우고, 각 줄을 출력
                            try
                            {
                                Console.SetCursorPosition(0, 0);
                            }
                            catch (ArgumentOutOfRangeException)
                            {
                                continue;
                            }
                            
                            int maxHeight = Math.Min(Console.WindowHeight, lines.Length);
                            
                            for (int y = 0; y < Console.WindowHeight; y++)
                            {
                                try
                                {
                                    Console.SetCursorPosition(0, y);
                                    
                                    if (y < lines.Length && y < maxHeight)
                                    {
                                        string line = lines[y].TrimEnd('\r');
                                        
                                        // 줄 출력
                                        Console.Write(line.Replace("\r", ""));
                                        
                                        // 줄의 나머지 부분을 공백으로 채우기 (잔상 제거)
                                        int visibleLen = ansiRegex.Replace(line, "").Length;
                                        int spacesToPad = Console.WindowWidth - visibleLen;
                                        if (spacesToPad > 0)
                                        {
                                            Console.Write(new string(' ', spacesToPad));
                                        }
                                    }
                                    else
                                    {
                                        // 추가 줄이 없으면 공백으로 채우기
                                        Console.Write(new string(' ', Console.WindowWidth));
                                    }
                                }
                                catch (ArgumentOutOfRangeException)
                                {
                                    break;
                                }
                            }

                            // 커서 설정
                            if (isPrompt)
                            {
                                Console.CursorVisible = true;
                                // 닉네임 입력 시 커서를 프롬프트 끝으로 이동
                                int lastLineY = Math.Min(maxHeight - 1, lines.Length - 1);
                                if (lastLineY >= 0)
                                {
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
                            }
                            else 
                            {
                                Console.CursorVisible = false;
                            }
                        }
                        catch (Exception)
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
        Thread sizeCheckThread = new Thread(() =>
        {
            int lastWidth = Console.WindowWidth;
            int lastHeight = Console.WindowHeight;
            while (connected)
            {
                Thread.Sleep(500); // 0.5초마다 체크
                try
                {
                    int currentWidth = Console.WindowWidth;
                    int currentHeight = Console.WindowHeight;
                    if (currentWidth != lastWidth || currentHeight != lastHeight)
                    {
                        lastWidth = currentWidth;
                        lastHeight = currentHeight;
                        SendConsoleSize();
                    }
                }
                catch (Exception)
                {
                    // 무시
                }
            }
        });
        sizeCheckThread.IsBackground = true;
        sizeCheckThread.Start();

        try
        {
            while (connected)
            {
                ConsoleKeyInfo key = Console.ReadKey(true);
                // [수정] 일반 문자 키(1, 2, 3 등)는 KeyChar로 보내고, 특수 키는 Key.ToString()으로 보냄
                if (key.KeyChar != '\0' && !char.IsControl(key.KeyChar))
                {
                    // 일반 문자 키 (1, 2, 3, a, b, c 등)
                    SendMessage(key.KeyChar.ToString().ToUpper());
                }
                else
                {
                    // 특수 키 (Enter, Backspace, Arrow keys 등)
                    SendMessage(key.Key.ToString());
                }
            }
        }
        catch (Exception)
        {
            connected = false;
        }
    }

    private void SendMessage(string message)
    {
        if (!connected || writer == null) return;
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
        string serverAddress = "10.60.22.85";
        
        // [참고] 만약 '소켓' 오류가 나면 12346으로 변경
        int port = 12345; 
        
        // [참고] '소켓' 오류가 나면 서버와 클라이언트 모두 12346으로 변경
        MUDClient client = new MUDClient(serverAddress, port);
        client.Wait(); 
    }
}