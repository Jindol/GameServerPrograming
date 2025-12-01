// Program.cs
using System;
using System.Runtime.InteropServices;
using System.Threading; // 딜레이를 위해 필요

class Program
{
    // --- [키보드 입력 시뮬레이션을 위한 WinAPI] ---
    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    // 가상 키 코드
    private const byte VK_MENU = 0x12;   // Alt 키
    private const byte VK_RETURN = 0x0D; // Enter 키
    private const uint KEYEVENTF_KEYUP = 0x0002; // 키 떼기

static void Main(string[] args)
    {
        // 1. 기본 콘솔 설정
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.CursorVisible = false;
        Console.Title = "ASCII Quest";

        // 2. [핵심] 전체 화면 전환 (Alt + Enter 시뮬레이션)
        Thread.Sleep(100); 
        SetFullScreen();

        // [!!!] --- 버그 수정: 입력 버퍼 비우기 --- [!!!]
        // Alt+Enter를 누를 때 발생한 'Enter' 키 입력이 남아있다면 싹 비웁니다.
        // (약간의 딜레이를 주어 운영체제가 키 입력을 처리할 시간을 줍니다)
        Thread.Sleep(100); 
        while (Console.KeyAvailable)
        {
            Console.ReadKey(true); // 찌꺼기 입력(Enter)을 읽어서 버림
        }
        // [!!!] --- 수정 끝 --- [!!!]

        // 3. 게임 루프 시작
        Game asciiQuest = new Game();
        while (asciiQuest.Start())
        {
            Console.Clear();
        }

        // 4. 종료 처리
        Console.ResetColor();
        Console.Clear();
        Console.WriteLine("게임을 종료합니다. 아무 키나 누르세요...");
        Console.ReadKey();
    }
    // Alt + Enter를 눌러 전체 화면으로 전환하는 메서드
    private static void SetFullScreen()
    {
        // 1. Alt 키 누름
        keybd_event(VK_MENU, 0, 0, UIntPtr.Zero);
        
        // 2. Enter 키 누름
        keybd_event(VK_RETURN, 0, 0, UIntPtr.Zero);
        
        // 3. Enter 키 뗌
        keybd_event(VK_RETURN, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        
        // 4. Alt 키 뗌
        keybd_event(VK_MENU, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }
}