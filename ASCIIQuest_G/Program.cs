// Program.cs
// using System.Runtime.Versioning; // 없어도 됨

class Program
{
    // [SupportedOSPlatform("windows")] // 없어도 됨
    static void Main(string[] args)
    {
        Game asciiQuest = new Game();
        asciiQuest.Start();

        Console.ResetColor();
        Console.WriteLine("게임을 종료합니다. 아무 키나 누르세요...");
        Console.ReadKey();
    }
}