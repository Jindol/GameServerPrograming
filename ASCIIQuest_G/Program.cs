// Program.cs
using System.Text;
using System.Runtime.InteropServices; 
using System; 
    
class Program
{
    // ... (P/Invoke 선언부는 변경 없음) ...
    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern IntPtr GetConsoleWindow();

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private const int SW_MAXIMIZE = 3; 
    // --- [끝] ---

    static void Main(string[] args)
    {
        // (윈도우 최대화 ... 변경 없음)
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            try
            {
                IntPtr consoleWindow = GetConsoleWindow();
                if (consoleWindow != IntPtr.Zero)
                {
                    ShowWindow(consoleWindow, SW_MAXIMIZE);
                }
            }
            catch (Exception) { }
        }
        
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        // --- [핵심 수정] ---
        // Game.Start()가 true를 반환하면 (재시작 선택)
        // 루프가 반복되어 게임이 재시작됩니다.
        Game asciiQuest = new Game();
        while (asciiQuest.Start())
        {
            // 게임이 끝나고 (false 반환) 루프가 종료되거나,
            // (true 반환) 이 루프가 다시 실행되어 Start()가 호출됩니다.
            Console.Clear(); // 재시작 전 화면 정리
        }
        // --- [끝] ---

        Console.ResetColor();
        Console.WriteLine("게임을 종료합니다. 아무 키나 누르세요...");
        Console.ReadKey();
    }
}