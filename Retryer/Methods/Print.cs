using System;

namespace Retryer.Methods
{
    internal class Print
    {
        private static bool NeedsColor()
        {
            return !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NEEDS_COLOR"));
        }

        // 正常输出直接
        // Console.WriteLine("");

        public static void PrintInfo(string message)
        {
            if (NeedsColor())
            {
                Console.ForegroundColor = ConsoleColor.Gray;
            }
            // 循环每个行，为每个行添加前缀
            foreach (string line in message.Split('\n'))
            {
                Console.WriteLine($"[INFO] {line}");
            }
            if (NeedsColor())
            {
                Console.ResetColor();
            }
        }

        public static void PrintWarning(string message)
        {
            if (NeedsColor())
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
            }
            // 循环每个行，为每个行添加前缀
            foreach (string line in message.Split('\n'))
            {
                Console.WriteLine($"[WARNING] {line}");
            }
            if (NeedsColor())
            {
                Console.ResetColor();
            }
        }

        public static void PrintError(string message)
        {
            if (NeedsColor())
            {
                Console.ForegroundColor = ConsoleColor.Red;
            }
            Environment.ExitCode = 1;
            // 循环每个行，为每个行添加前缀
            foreach (string line in message.Split('\n'))
            {
                Console.WriteLine($"[ERROR] {line}");
            }
            if (NeedsColor())
            {
                Console.ResetColor();
            }
        }

        public static void PrintDebug(string message)
        {
#if DEBUG
            if (NeedsColor())
            {
                Console.ForegroundColor = ConsoleColor.Magenta;
            }
            // 循环每个行，为每个行添加前缀
            foreach (string line in message.Split('\n'))
            {
                Console.WriteLine($"[Debug] {line}");
            }
            if (NeedsColor())
            {
                Console.ResetColor();
            }
#endif
        }

        public static void PrintHint(string message)
        {
            if (NeedsColor())
            {
                Console.ForegroundColor = ConsoleColor.DarkYellow;
            }
            // 循环每个行，为每个行添加前缀
            foreach (string line in message.Split('\n'))
            {
                Console.WriteLine($"[Hint] {line}");
            }
            if (NeedsColor())
            {
                Console.ResetColor();
            }
        }
    }
}
